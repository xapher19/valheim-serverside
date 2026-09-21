using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Server-side networking limits (BetterNetworking-style raised queue/rates) plus long-haul
		improvements inspired by NetworkPerformanceSystem, without latency-aware ownership:

		- Per-peer BDP send window from Steam RTT (capped by QueueSizeKB)
		- Raised ZRpc / Steam connection timeouts
		- Ghost-owner reclaim to the *server* (keeps serverside simulation)
		- Station insert RPC re-address to current owner
		- Filtered Everybody routed-RPC relays

		Clients stay vanilla. Do not also run BetterNetworking or NetworkPerformanceSystem.
	*/
	public class Networking : IFeature
	{
		private const int VanillaQueueSize = 10240;
		private static ZDOMan.ZDOPeer s_sendPeer;

		public bool FeatureEnabled()
		{
			return Configuration.networkingEnabled.Value;
		}

		public static int QueueSize()
		{
			return Mathf.Clamp(Configuration.networkQueueSizeKB.Value, 10, 80) * 1024;
		}

		public static int QueueSizeFor(ZDOMan.ZDOPeer peer)
		{
			return PeerWindow.BytesFor(peer);
		}

		private static int QueueSizeForCurrentPeer()
		{
			return PeerWindow.BytesFor(s_sendPeer);
		}

		[HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
		public static class ZDOMan_SendZDOs_Patch
		{
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
			{
				MethodInfo queueSize = AccessTools.Method(typeof(Networking), nameof(QueueSizeForCurrentPeer));
				List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
				int replaced = 0;
				for (int i = 0; i < codes.Count; i++)
				{
					if (codes[i].LoadsConstant(VanillaQueueSize))
					{
						CodeInstruction call = new CodeInstruction(OpCodes.Call, queueSize);
						call.labels.AddRange(codes[i].labels);
						call.blocks.AddRange(codes[i].blocks);
						codes[i] = call;
						replaced++;
					}
				}
				if (replaced != 2)
				{
					ServersidePlugin.logger.LogWarning($"{__originalMethod.DeclaringType.Name}.{__originalMethod.Name}: replaced {replaced} send queue limit(s), expected 2. The game changed this method; the patch needs reviewing.");
				}
				return codes;
			}

			static void Prefix(ZDOMan.ZDOPeer peer, bool flush)
			{
				s_sendPeer = peer;
				if (peer?.m_peer != null)
				{
					PeerRtt.Sample(peer.m_peer);
				}
				Stats.Record(peer?.m_peer, flush, PeerWindow.BytesFor(peer));
			}

			static void Finalizer()
			{
				s_sendPeer = null;
			}
		}

		[HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
		public static class ZSteamSocket_RegisterGlobalCallbacks_Patch
		{
			static void Postfix()
			{
				int max = Configuration.networkSendRateMaxKB.Value * 1024;
				int min = Math.Min(Configuration.networkSendRateMinKB.Value * 1024, max);
				SteamNet.SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, min);
				SteamNet.SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, max);
				Timeouts.ApplySteam("RegisterGlobalCallbacks");
				Timeouts.ApplyRpc(loading: false, "RegisterGlobalCallbacks");
				ServersidePlugin.logger.LogInfo(
					$"Networking: Steam send rate {min / 1024}-{max / 1024} KB/s, queue cap {QueueSize() / 1024} KB"
					+ (Configuration.networkBdpWindowEnabled.Value ? ", BDP window on" : "")
					+ (Configuration.networkTimeoutEnabled.Value ? $", timeouts {Configuration.networkConnectionTimeoutSeconds.Value}/{Configuration.networkLoadingTimeoutSeconds.Value}s" : "")
					+ (Configuration.networkGhostEvictEnabled.Value ? $", ghost reclaim {Configuration.networkGhostEvictSeconds.Value:0}s" : "")
					+ (Configuration.networkStationRoutingEnabled.Value ? ", station routing on" : "")
					+ (Configuration.networkRelayFilterEnabled.Value ? ", relay filter on" : ""));
			}
		}

		[HarmonyPatch(typeof(ZRpc), nameof(ZRpc.SetLongTimeout))]
		public static class ZRpc_SetLongTimeout_Patch
		{
			static void Postfix(bool enable)
			{
				Timeouts.ApplyRpc(loading: enable, "SetLongTimeout");
			}
		}

		[HarmonyPatch(typeof(ZNet), "Update")]
		public static class ZNet_Update_NetworkingTick
		{
			static void Postfix()
			{
				if (!ZNet.instance || !ZNet.instance.IsServer()) return;
				PeerLiveness.Evaluate();
				GhostOwners.Tick();
			}
		}

		[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
		public static class ZRoutedRpc_RouteRPC_Networking
		{
			static bool Prefix(ZRoutedRpc __instance, ZRoutedRpc.RoutedRPCData rpcData)
			{
				if (!__instance.m_server || rpcData == null) return true;
				if (StationRouting.TryHandle(__instance, rpcData)) return false;
				if (RelayFilter.TryRelay(__instance, rpcData)) return false;
				return true;
			}
		}

		internal static class SteamNet
		{
			internal static void SetInt(ESteamNetworkingConfigValue key, int value)
			{
				GCHandle handle = GCHandle.Alloc(value, GCHandleType.Pinned);
				try
				{
					SteamGameServerNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, handle.AddrOfPinnedObject());
				}
				finally
				{
					handle.Free();
				}
			}
		}

		internal static class Timeouts
		{
			internal static void ApplyRpc(bool loading, string reason)
			{
				if (!Configuration.networkTimeoutEnabled.Value) return;
				int playing = Mathf.Clamp(Configuration.networkConnectionTimeoutSeconds.Value, 30, 300);
				int load = Mathf.Max(playing, Mathf.Clamp(Configuration.networkLoadingTimeoutSeconds.Value, 60, 600));
				float seconds = loading ? load : playing;
				if (!Mathf.Approximately(ZRpc.m_timeout, seconds))
				{
					ServersidePlugin.logger.LogInfo($"Networking timeout ({reason}): ZRpc ping timeout {ZRpc.m_timeout:0}s -> {seconds:0}s{(loading ? " [loading]" : "")}");
				}
				ZRpc.m_timeout = seconds;
			}

			internal static void ApplySteam(string reason)
			{
				if (!Configuration.networkTimeoutEnabled.Value) return;
				int playing = Mathf.Clamp(Configuration.networkConnectionTimeoutSeconds.Value, 30, 300);
				int load = Mathf.Max(playing, Mathf.Clamp(Configuration.networkLoadingTimeoutSeconds.Value, 60, 600));
				SteamNet.SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutInitial, load * 1000);
				SteamNet.SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected, playing * 1000);
				ServersidePlugin.logger.LogInfo($"Networking timeout ({reason}): Steam TimeoutInitial {load}s, TimeoutConnected {playing}s");
			}
		}

		internal static class PeerRtt
		{
			private static readonly Dictionary<long, int> s_rttMs = new Dictionary<long, int>();
			private static readonly Dictionary<long, float> s_sampledAt = new Dictionary<long, float>();
			private static bool s_loggedUnavailable;

			internal static void Sample(ZNetPeer peer)
			{
				if (peer?.m_socket == null) return;
				float now = Time.realtimeSinceStartup;
				if (s_sampledAt.TryGetValue(peer.m_uid, out float at) && now - at < 1f) return;
				s_sampledAt[peer.m_uid] = now;
				if (!(peer.m_socket is ZSteamSocket steam) || !steam.IsConnected()) return;
				if (!TryPing(steam, out int ping) || ping <= 0) return;
				s_rttMs[peer.m_uid] = ping;
			}

			internal static bool TryGet(long uid, out int rttMs) => s_rttMs.TryGetValue(uid, out rttMs);

			internal static void Forget(long uid)
			{
				s_rttMs.Remove(uid);
				s_sampledAt.Remove(uid);
			}

			private static bool TryPing(ZSteamSocket steam, out int ping)
			{
				ping = 0;
				try
				{
					SteamNetConnectionRealTimeStatus_t status = default;
					SteamNetConnectionRealTimeLaneStatus_t lane = default;
					EResult result = SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(steam.m_con, ref status, 0, ref lane);
					if (result == EResult.k_EResultOK && status.m_nPing > 0)
					{
						ping = status.m_nPing;
						return true;
					}
				}
				catch (Exception e)
				{
					if (!s_loggedUnavailable)
					{
						s_loggedUnavailable = true;
						ServersidePlugin.logger.LogInfo($"Networking RTT: Steam GameServer status unavailable ({e.GetType().Name}); BDP window falls back to QueueSizeKB for Steam peers without a measurement.");
					}
				}
				try
				{
					steam.GetConnectionQuality(out _, out _, out ping, out _, out _);
					return ping > 0;
				}
				catch
				{
					ping = 0;
					return false;
				}
			}
		}

		internal static class PeerWindow
		{
			internal static int BytesFor(ZDOMan.ZDOPeer peer)
			{
				int cap = QueueSize();
				if (!Configuration.networkBdpWindowEnabled.Value || peer?.m_peer == null)
				{
					return cap;
				}
				if (!PeerRtt.TryGet(peer.m_peer.m_uid, out int rttMs) || rttMs <= 0)
				{
					return cap;
				}
				float rttSec = rttMs / 1000f;
				float target = Configuration.networkBdpTargetRateKBps.Value * 1024f;
				float factor = Configuration.networkBdpFactor.Value;
				int sized = Mathf.RoundToInt(target * rttSec * factor);
				return Mathf.Clamp(sized, VanillaQueueSize, cap);
			}
		}

		internal static class PeerLiveness
		{
			private class State
			{
				public float SilentFor;
				public bool Ghost;
			}

			private static readonly Dictionary<long, State> s_peers = new Dictionary<long, State>();
			private static readonly HashSet<long> s_ghosts = new HashSet<long>();
			private static float s_lastTick = -1f;
			private static bool s_localFault;

			internal static bool IsGhost(long uid) => !s_localFault && s_ghosts.Contains(uid);

			internal static void Evaluate()
			{
				if (!Configuration.networkGhostEvictEnabled.Value || ZNet.instance == null) return;
				float now = Time.realtimeSinceStartup;
				float dt = s_lastTick < 0f ? 0f : Mathf.Min(now - s_lastTick, 2f);
				s_lastTick = now;
				if (dt <= 0f) return;

				float evictAfter = Mathf.Min(
					Configuration.networkGhostEvictSeconds.Value,
					Mathf.Max(3f, Configuration.networkConnectionTimeoutSeconds.Value - 1f));

				List<ZNetPeer> peers = ZNet.instance.GetPeers();
				int quiet = 0;
				int ready = 0;
				HashSet<long> seen = new HashSet<long>();
				foreach (ZNetPeer peer in peers)
				{
					if (peer == null || !peer.IsReady() || peer.m_server) continue;
					ready++;
					seen.Add(peer.m_uid);
					if (!s_peers.TryGetValue(peer.m_uid, out State state))
					{
						state = new State();
						s_peers[peer.m_uid] = state;
					}

					bool deadTransport = peer.m_socket == null || !peer.m_socket.IsConnected();
					float silence = peer.m_rpc != null ? peer.m_rpc.m_timeSinceLastPing : state.SilentFor + dt;
					state.SilentFor = deadTransport ? Mathf.Max(state.SilentFor + dt, evictAfter) : silence;

					bool shouldGhost = deadTransport || state.SilentFor >= evictAfter;
					if (shouldGhost) quiet++;
					if (shouldGhost && !state.Ghost)
					{
						state.Ghost = true;
						s_ghosts.Add(peer.m_uid);
						ServersidePlugin.logger.LogInfo($"Networking: peer {peer.m_uid} quiet ({state.SilentFor:0.0}s{(deadTransport ? ", transport dead" : "")}); reclaiming owned objects to the server, slot kept.");
					}
					else if (!shouldGhost && state.Ghost)
					{
						ServersidePlugin.logger.LogInfo($"Networking: peer {peer.m_uid} answering again after being quiet; eligible to own objects again.");
						state.Ghost = false;
						s_ghosts.Remove(peer.m_uid);
					}
				}

				if (ready > 0 && quiet == ready)
				{
					if (!s_localFault)
					{
						s_localFault = true;
						ServersidePlugin.logger.LogInfo($"Networking: all {ready} peers went quiet at once — treating as a local fault; not reclaiming ownership.");
					}
				}
				else if (s_localFault && quiet < ready)
				{
					s_localFault = false;
					ServersidePlugin.logger.LogInfo("Networking: peers answering again; ghost reclaim back in force.");
				}

				List<long> gone = null;
				foreach (long uid in s_peers.Keys)
				{
					if (seen.Contains(uid)) continue;
					if (gone == null) gone = new List<long>();
					gone.Add(uid);
				}
				if (gone != null)
				{
					foreach (long uid in gone)
					{
						s_peers.Remove(uid);
						s_ghosts.Remove(uid);
						PeerRtt.Forget(uid);
					}
				}
			}

			internal static IEnumerable<long> GhostIds()
			{
				if (s_localFault) yield break;
				foreach (long id in s_ghosts) yield return id;
			}
		}

		internal static class GhostOwners
		{
			private static float s_next;

			internal static void Tick()
			{
				if (!Configuration.networkGhostEvictEnabled.Value || ZDOMan.instance == null || ZNet.instance == null) return;
				float now = Time.realtimeSinceStartup;
				if (now < s_next) return;
				s_next = now + 1f;

				List<long> ghosts = null;
				foreach (long id in PeerLiveness.GhostIds())
				{
					if (ghosts == null) ghosts = new List<long>();
					ghosts.Add(id);
				}
				if (ghosts == null || ghosts.Count == 0) return;

				long server = ZNet.GetUID();
				HashSet<long> ghostSet = new HashSet<long>(ghosts);
				List<ZDO> near = ZDOMan.instance.m_tempNearObjects;
				SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
				SimulationDistance nearOnly = new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic);
				int reclaimed = 0;

				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					if (peer == null || !peer.IsReady()) continue;
					near.Clear();
					ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(peer.GetRefPos()), nearOnly, near);
					foreach (ZDO zdo in near)
					{
						if (zdo == null || !zdo.Persistent) continue;
						long owner = zdo.GetOwner();
						if (!ghostSet.Contains(owner) || owner == server) continue;
						zdo.SetOwner(server);
						reclaimed++;
					}
				}

				if (reclaimed > 0)
				{
					ServersidePlugin.logger.LogInfo($"Networking: reclaimed {reclaimed} ghost-owned object(s) to the server.");
				}
			}
		}

		internal static class StationRouting
		{
			private static readonly HashSet<int> RequestHashes = new HashSet<int>
			{
				"RPC_AddItem".GetStableHashCode(),
				"RPC_Tap".GetStableHashCode(),
				"RPC_AddOre".GetStableHashCode(),
				"RPC_AddFuel".GetStableHashCode(),
				"RPC_EmptyProcessed".GetStableHashCode(),
				"RPC_AddFuelAmount".GetStableHashCode(),
				"RPC_ToggleOn".GetStableHashCode(),
				"RPC_AddAmmo".GetStableHashCode(),
			};

			private static readonly Dictionary<int, bool> PrefabIsStation = new Dictionary<int, bool>();

			internal static bool TryHandle(ZRoutedRpc routed, ZRoutedRpc.RoutedRPCData data)
			{
				if (!Configuration.networkStationRoutingEnabled.Value) return false;
				if (data.m_targetZDO.IsNone() || !RequestHashes.Contains(data.m_methodHash)) return false;
				if (ZDOMan.instance == null || ZNet.instance == null) return false;

				ZDO zdo = ZDOMan.instance.GetZDO(data.m_targetZDO);
				if (zdo == null || !IsStation(zdo)) return false;

				long server = ZNet.GetUID();
				long owner = zdo.GetOwner();
				ZNetPeer ownerPeer = owner != 0L && owner != server ? ZNet.instance.GetPeer(owner) : null;
				bool ownerPresent = owner == server
					|| (ownerPeer != null && ownerPeer.IsReady() && ownerPeer.m_socket != null && ownerPeer.m_socket.IsConnected() && !PeerLiveness.IsGhost(owner));

				if (!ownerPresent)
				{
					zdo.SetOwner(server);
					owner = server;
					if (data.m_senderPeerID != 0L)
					{
						ZDOMan.instance.ForceSendZDO(data.m_senderPeerID, zdo.m_uid);
					}
				}

				if (owner == server)
				{
					data.m_targetPeerID = server;
					routed.HandleRoutedRPC(data);
					return true;
				}

				data.m_targetPeerID = owner;
				return false;
			}

			private static bool IsStation(ZDO zdo)
			{
				int prefab = zdo.GetPrefab();
				if (PrefabIsStation.TryGetValue(prefab, out bool known)) return known;
				bool station = false;
				if (ZNetScene.instance)
				{
					GameObject go = ZNetScene.instance.GetPrefab(prefab);
					if (go)
					{
						station = go.GetComponent<Fermenter>()
							|| go.GetComponent<Smelter>()
							|| go.GetComponent<CookingStation>()
							|| go.GetComponent<Fireplace>()
							|| go.GetComponent<ShieldGenerator>()
							|| go.GetComponent<Turret>();
					}
				}
				PrefabIsStation[prefab] = station;
				return station;
			}
		}

		internal static class RelayFilter
		{
			internal static bool TryRelay(ZRoutedRpc routed, ZRoutedRpc.RoutedRPCData data)
			{
				if (!Configuration.networkRelayFilterEnabled.Value) return false;
				if (data.m_targetPeerID != 0L) return false;
				if (data.m_targetZDO.IsNone()) return false;
				if (ZDOMan.instance == null) return false;

				ZDO zdo = ZDOMan.instance.GetZDO(data.m_targetZDO);
				ZPackage pkg = new ZPackage();
				data.Serialize(pkg);
				bool limitDistance = Configuration.networkRelayLimitByDistance.Value;
				bool distant = zdo != null && zdo.Distant;

				foreach (ZNetPeer peer in routed.m_peers)
				{
					if (peer == null || !peer.IsReady() || peer.m_uid == data.m_senderPeerID) continue;
					if (!ShouldReceive(peer, data.m_targetZDO, zdo, distant, limitDistance)) continue;
					peer.m_rpc.Invoke("RoutedRPC", pkg);
				}
				return true;
			}

			private static bool ShouldReceive(ZNetPeer peer, ZDOID id, ZDO zdo, bool distant, bool limitDistance)
			{
				ZDOMan.ZDOPeer zdoPeer = ZDOMan.instance.GetPeer(peer.m_uid);
				bool known = zdoPeer != null && zdoPeer.m_zdos != null && zdoPeer.m_zdos.ContainsKey(id);
				if (limitDistance)
				{
					if (zdo == null) return known;
					return ZNetScene.InActiveArea(zdo.GetPosition(), ZoneSystem.GetZone(peer.GetRefPos()));
				}
				if (known) return true;
				if (zdo == null) return false;
				if (distant) return true;
				return ZNetScene.InActiveArea(zdo.GetPosition(), ZoneSystem.GetZone(peer.GetRefPos()));
			}
		}

		private static class Stats
		{
			private class PeerStats
			{
				public string name;
				public string transport;
				public int ticks;
				public int full;
				public int maxQueued;
				public int window;
				public int rttMs;
			}

			private static readonly Dictionary<long, PeerStats> s_peers = new Dictionary<long, PeerStats>();
			private static float s_nextReport = -1f;

			public static void Record(ZNetPeer peer, bool flush, int window)
			{
				int minutes = Configuration.networkStatsMinutes.Value;
				if (minutes <= 0 || flush || peer == null || peer.m_socket == null)
				{
					return;
				}
				int queued = peer.m_socket.GetSendQueueSize();
				if (!s_peers.TryGetValue(peer.m_uid, out PeerStats stats))
				{
					stats = new PeerStats();
					s_peers[peer.m_uid] = stats;
				}
				stats.name = DiagnosticRuntime.Clean(peer.m_playerName);
				stats.transport = peer.m_socket.GetType().Name;
				stats.window = window;
				if (PeerRtt.TryGet(peer.m_uid, out int rtt)) stats.rttMs = rtt;
				stats.ticks++;
				if (window - queued < 2048)
				{
					stats.full++;
				}
				stats.maxQueued = Math.Max(stats.maxQueued, queued);

				float now = Time.realtimeSinceStartup;
				if (s_nextReport < 0f)
				{
					s_nextReport = now + minutes * 60f;
				}
				else if (now >= s_nextReport)
				{
					Report();
					s_nextReport = now + minutes * 60f;
				}
			}

			private static void Report()
			{
				foreach (PeerStats stats in s_peers.Values)
				{
					string rtt = stats.rttMs > 0 ? $", rtt {stats.rttMs} ms" : "";
					ServersidePlugin.logger.LogInfo(
						$"Networking {stats.name} [{stats.transport}]: send queue full in {100f * stats.full / stats.ticks:0.0}% of {stats.ticks} send ticks, at most {stats.maxQueued / 1024} of {stats.window / 1024} KiB window{rtt}"
						+ (stats.transport == "ZPlayFabSocket" ? " (PlayFab; BDP uses QueueSizeKB)" : ""));
				}
				s_peers.Clear();
			}
		}
	}
}
