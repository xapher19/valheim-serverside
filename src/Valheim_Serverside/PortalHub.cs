using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	// Independent portal-hub for dedicated servers. Inspired by the public behaviour of
	// ArgusMagnus ServersideQoL AutoPortalHub (pair unpaired portal tags via a generated hub)
	// but does not include or depend on that mod's source. Remove AutoPortalHub / ServersideQoL
	// portal packages when using this; running both will fight over hub objects.
	//
	// Vanilla clients have no RPC that opens a destination dropdown. Custom RPCs are ignored
	// unless the client registered them. The selectable UI is therefore vanilla pieces the
	// server can spawn: a hall of portals with signs. Walking into an untagged home portal
	// arrives there. Typed tags remain an optional shortcut on that home portal.
	internal static class PortalHub
	{
		internal const string LobbyTag = "Home";
		internal static bool Installed;
		private static readonly int HubMarker = "nw_portal_hub".GetStableHashCode();
		private static readonly int LobbyMarker = "nw_portal_lobby".GetStableHashCode();
		private static readonly List<ZDOID> hubObjects = new List<ZDOID>();
		private static readonly List<ZDOID> gatewayIds = new List<ZDOID>();
		private static readonly Dictionary<long, double> lastEnter = new Dictionary<long, double>();
		private static readonly HashSet<int> portalPrefabs = new HashSet<int>();
		private static readonly List<string> portalPrefabNames = new List<string>();
		private static MethodInfo getPortalsMethod;
		private static MethodInfo getPortalListMethod;
		private static MethodInfo getAllPrefabsMethod;
		private static FieldInfo portalObjectsField;
		private static FieldInfo outsideSectorField;
		private static FieldInfo tagHashField;
		private static bool portalApisResolved;
		private static string lastSignature = "";
		private static double nextScan;
		private static Regex includeRegex, excludeRegex;
		private static Vector3 hubOrigin;
		private static bool originReady;
		private static ZDOID hubLobbyId;
		internal static bool Enabled => Installed && Configuration.portalHubEnabled.Value;
		internal static string Status => !Enabled ? "portal hub inactive"
			: lastSignature.StartsWith("hall|", StringComparison.Ordinal)
				? $"portal destination hall {hubObjects.Count} pieces"
				: $"portal hub {hubObjects.Count} pieces; last plan [{lastSignature}]";

		internal static void Tick()
		{
			if (!Enabled || ZDOMan.instance == null || !ZNetScene.instance || !ZNet.instance || !ZNet.instance.IsServer())
				return;
			if (!ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated) return;
			TryEnterGateways();
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextScan) return;
			nextScan = now + 2;
			EnsureOrigin();
			if (hubObjects.Count > 0)
			{
				EnsureHubZone();
				MaintainHubPieces();
			}
			RebuildFilters();
			RefreshPortalPrefabIndex();
			ResolvePortalApis();
			Reconcile();
		}

		private static void EnsureOrigin()
		{
			if (originReady) return;
			// Well inside the playable map, high enough to miss builds. The previous origin sat
			// on the world-edge kill ring, so vanilla clients that did arrive were already dying.
			hubOrigin = new Vector3(40f * 64f, 1800f, 40f * 64f);
			originReady = true;
		}

		private static void EnsureHubZone()
		{
			if (!ZoneSystem.instance) return;
			ZoneSystem.instance.PokeLocalZone(ZoneSystem.GetZone(hubOrigin));
		}

		private static void RebuildFilters()
		{
			includeRegex = Wildcard(Configuration.portalHubInclude.Value, matchAllIfStar: true);
			excludeRegex = Wildcard(Configuration.portalHubExclude.Value, matchAllIfStar: false);
		}

		private static Regex Wildcard(string pattern, bool matchAllIfStar)
		{
			if (string.IsNullOrWhiteSpace(pattern)) return matchAllIfStar ? new Regex("^.*$") : null;
			string trimmed = pattern.Trim();
			if (matchAllIfStar && trimmed == "*") return new Regex("^.*$");
			if (!matchAllIfStar && trimmed.Length == 0) return null;
			string escaped = Regex.Escape(trimmed).Replace("\\*", ".*").Replace("\\?", ".");
			return new Regex("^" + escaped + "$", RegexOptions.CultureInvariant);
		}

		private static bool AllowedTag(string tag)
		{
			if (string.IsNullOrEmpty(tag)) return false;
			if (string.Equals(tag, LobbyTag, StringComparison.OrdinalIgnoreCase)) return false;
			if (includeRegex != null && !includeRegex.IsMatch(tag)) return false;
			if (excludeRegex != null && excludeRegex.IsMatch(tag)) return false;
			return true;
		}

		private static bool IsHubObject(ZDO zdo) => zdo != null && zdo.GetLong(HubMarker, 0L) != 0L;

		private static bool IsHubLobby(ZDO zdo) => IsHubObject(zdo) && zdo.GetLong(LobbyMarker, 0L) != 0L;

		private static void RefreshPortalPrefabIndex()
		{
			portalPrefabs.Clear();
			portalPrefabNames.Clear();
			if (ZNetScene.instance == null || ZNetScene.instance.m_namedPrefabs == null) return;
			foreach (var pair in ZNetScene.instance.m_namedPrefabs)
			{
				if (!pair.Value || pair.Value.GetComponent<TeleportWorld>() == null) continue;
				portalPrefabs.Add(pair.Key);
				if (!string.IsNullOrEmpty(pair.Value.name) && !portalPrefabNames.Contains(pair.Value.name))
					portalPrefabNames.Add(pair.Value.name);
			}
		}

		private static void ResolvePortalApis()
		{
			if (portalApisResolved) return;
			portalApisResolved = true;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			getPortalsMethod = typeof(ZDOMan).GetMethod("GetPortals", flags, null, Type.EmptyTypes, null);
			getPortalListMethod = typeof(ZDOMan).GetMethod("GetPortalList", flags, null, Type.EmptyTypes, null);
			getAllPrefabsMethod = typeof(ZDOMan).GetMethod("GetAllZDOsWithPrefab", flags, null, new[] { typeof(string), typeof(List<ZDO>) }, null);
			portalObjectsField = typeof(ZDOMan).GetField("m_portalObjects", flags);
			outsideSectorField = typeof(ZDOMan).GetField("m_objectsByOutsideSector", flags);
			tagHashField = typeof(ZDOVars).GetField("s_tagHash", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		}

		private static List<ZDO> WorldPortals()
		{
			var list = new List<ZDO>();
			var seen = new HashSet<ZDOID>();
			CollectPortals(zdo =>
			{
				if (zdo == null || !zdo.IsValid() || IsHubObject(zdo)) return;
				if (portalPrefabs.Count > 0 && !portalPrefabs.Contains(zdo.GetPrefab())) return;
				if (!seen.Add(zdo.m_uid)) return;
				list.Add(zdo);
			});
			return list;
		}

		private static void CollectPortals(Action<ZDO> take)
		{
			if (ZDOMan.instance == null || take == null) return;
			// 1.0 GetPortals() is Dictionary<SectorIndex, List<ZDO>>, not IEnumerable<ZDO>.
			if (getPortalListMethod != null)
				TakePortalResult(getPortalListMethod.Invoke(ZDOMan.instance, null), take);
			if (getPortalsMethod != null)
				TakePortalResult(getPortalsMethod.Invoke(ZDOMan.instance, null), take);
			if (portalObjectsField != null)
				TakePortalResult(portalObjectsField.GetValue(ZDOMan.instance), take);
			if (getAllPrefabsMethod != null)
			{
				foreach (string name in portalPrefabNames)
				{
					var batch = new List<ZDO>();
					getAllPrefabsMethod.Invoke(ZDOMan.instance, new object[] { name, batch });
					foreach (ZDO zdo in batch) take(zdo);
				}
			}
			CollectSectorBuckets(ZDOMan.instance.m_objectsBySector, take);
			if (outsideSectorField != null)
				TakePortalResult(outsideSectorField.GetValue(ZDOMan.instance), take);
		}

		private static void TakePortalResult(object result, Action<ZDO> take)
		{
			if (result == null || take == null) return;
			if (result is IEnumerable<ZDO> found)
			{
				foreach (ZDO zdo in found) take(zdo);
				return;
			}
			if (result is IDictionary map)
			{
				foreach (object value in map.Values)
				{
					if (value is IEnumerable<ZDO> bucket)
						foreach (ZDO zdo in bucket) take(zdo);
					else if (value is ZDO zdo)
						take(zdo);
				}
			}
		}

		private static void CollectSectorBuckets(List<ZDO>[] sectors, Action<ZDO> take)
		{
			if (sectors == null) return;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++) take(bucket[i]);
			}
		}

		private static void MaybeAutoName(ZDO zdo)
		{
			if (!Configuration.portalHubAutoName.Value) return;
			if (!string.IsNullOrEmpty(TagOf(zdo))) return;
			Heightmap.Biome biome = WorldGenerator.instance != null
				? WorldGenerator.instance.GetBiome(zdo.GetPosition())
				: Heightmap.Biome.None;
			string biomeName = biome == Heightmap.Biome.None ? "Portal" : biome.ToString();
			var used = new HashSet<string>(WorldPortals().Select(TagOf));
			for (int i = 1; i <= 1000; i++)
			{
				string candidate = string.Format(Configuration.portalHubAutoNameFormat.Value, biomeName, i);
				if (used.Contains(candidate)) continue;
				SetTag(zdo, candidate);
				return;
			}
		}

		private static string TagOf(ZDO zdo) => zdo == null ? "" : (zdo.GetString(ZDOVars.s_tag, "") ?? "").Trim();

		private static void SetTag(ZDO zdo, string tag)
		{
			if (zdo == null) return;
			string text = tag ?? "";
			zdo.Set(ZDOVars.s_tag, text);
			if (tagHashField != null)
				zdo.Set((int)tagHashField.GetValue(null), text.Length == 0 ? 0 : text.GetStableHashCode());
		}

		private static bool IsGateway(ZDO zdo) => zdo != null && zdo.IsValid() && string.IsNullOrEmpty(TagOf(zdo));

		internal static List<string> UnpairedTags(IEnumerable<string> tags)
		{
			var byTag = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (string tag in tags)
			{
				if (string.IsNullOrEmpty(tag) || !AllowedTag(tag)) continue;
				byTag.TryGetValue(tag, out int n);
				byTag[tag] = n + 1;
			}
			return byTag.Where(kv => kv.Value % 2 != 0).Select(kv => kv.Key).OrderBy(t => t, StringComparer.Ordinal).ToList();
		}

		private static void Reconcile()
		{
			var portals = WorldPortals();
			foreach (ZDO zdo in portals) MaybeAutoName(zdo);
			int gateways = 0;
			foreach (ZDO zdo in portals)
				if (IsGateway(zdo)) gateways++;

			var unpaired = UnpairedTags(portals.Select(TagOf));
			if (gateways > 0)
			{
				EnsureHubZone();
				if (FindHubLobby() == null && !HasValidHubPiece())
					lastSignature = "";
				string signature = "hall|" + gateways + "|" + string.Join("|", unpaired);
				if (signature != lastSignature)
				{
					ClearHub();
					BuildHall(unpaired);
					lastSignature = signature;
					ServersidePlugin.logger.LogInfo(
						$"Portal hall: {gateways} untagged home portal(s), destinations [{string.Join(", ", unpaired)}]. "
						+ "Walk through an untagged home portal to pick a labeled destination. Tagged world portals return home.");
					NotifyAll(unpaired.Count == 0
						? "Home portal is ready. Name an outpost portal to add a destination, then walk through the untagged home portal."
						: "Home portal is ready. Walk through it to pick a destination.");
					Connect();
				}
				else if (FindHubLobby() == null)
				{
					// Lobby missing but other hall pieces remain — rebuild without waiting for wear to finish.
					ClearHub();
					BuildHall(unpaired);
					Connect();
				}
				WireHall();
				return;
			}

			string hubSignature = string.Join("|", unpaired);
			if (hubSignature == lastSignature && (unpaired.Count == 0 ? hubObjects.Count == 0 : hubObjects.Count > 0))
				return;

			ClearHub();
			lastSignature = hubSignature;
			if (unpaired.Count == 0)
			{
				Connect();
				return;
			}

			BuildHub(unpaired);
			Connect();
			ServersidePlugin.logger.LogInfo($"Portal hub: paired {unpaired.Count} unpaired tag(s)");
		}

		internal static void WireHall()
		{
			if (!Enabled || ZDOMan.instance == null) return;
			var portals = WorldPortals();
			var gateways = new List<ZDO>();
			foreach (ZDO zdo in portals)
				if (IsGateway(zdo)) gateways.Add(zdo);
			if (gateways.Count == 0)
			{
				gatewayIds.Clear();
				return;
			}
			gateways.Sort((a, b) =>
			{
				Vector3 pa = a.GetPosition(), pb = b.GetPosition();
				int c = pa.x.CompareTo(pb.x);
				return c != 0 ? c : pa.z.CompareTo(pb.z);
			});
			RememberGateways(gateways);
			ZDO home = gateways[0];
			ZDO lobby = FindHubLobby();
			if (lobby == null || !lobby.IsValid())
				return;

			// Only dirty / force-send when a link actually changes. Re-applying the same
			// connection every scan makes vanilla clients replay the portal activate VFX.
			bool changed = false;
			foreach (ZDO gateway in gateways)
				changed |= SetPortalConnection(gateway, lobby.m_uid);
			changed |= SetPortalConnection(lobby, home.m_uid);

			foreach (ZDO zdo in portals)
			{
				if (IsGateway(zdo) || !AllowedTag(TagOf(zdo))) continue;
				ZDOID connected = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
				ZDO other = connected.IsNone() ? null : ZDOMan.instance.GetZDO(connected);
				if (other != null && other.IsValid() && IsHubObject(other))
					changed |= SetPortalConnection(zdo, home.m_uid);
			}
			if (changed)
				PublishPortal(lobby, gateways, portals);
		}

		private static bool SetPortalConnection(ZDO zdo, ZDOID target)
		{
			if (zdo == null || !zdo.IsValid()) return false;
			ZDOID current = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
			if (current.Equals(target)) return false;
			if (Game.instance)
			{
				Game.instance.ForceSetConnection(zdo, target);
				return true;
			}
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.SetConnection(ZDOExtraData.ConnectionType.Portal, target);
			return true;
		}

		internal static bool TryInterceptTeleport(TeleportWorld portal, Player player)
		{
			if (!Enabled || !portal || !player || !portal.m_nview || !portal.m_nview.IsValid()) return false;
			ZDO zdo = portal.m_nview.GetZDO();
			if (zdo == null || IsHubObject(zdo) || !IsGateway(zdo)) return false;
			ZDO lobby = FindHubLobby();
			if (lobby == null || !lobby.IsValid()) return false;
			ZNetPeer peer = PeerOfPlayer(player);
			if (peer == null) return false;
			if (!CanPortalTravel(player, portal.m_allowAllItems, peer)) return true;
			lastEnter[peer.m_uid] = Time.realtimeSinceStartupAsDouble;
			PublishToPeer(peer, lobby);
			TeleportPeer(peer, lobby);
			Notify(peer, "Choose a destination.");
			return true;
		}

		private static ZNetPeer PeerOfPlayer(Player player)
		{
			if (player == null || !player.m_nview || !player.m_nview.IsValid() || ZNet.instance == null) return null;
			ZDO zdo = player.m_nview.GetZDO();
			if (zdo == null) return null;
			ZDOID id = zdo.m_uid;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				if (peer != null && peer.m_characterID.Equals(id)) return peer;
			return null;
		}

		private static Player PlayerOfPeer(ZNetPeer peer)
		{
			if (peer == null || peer.m_characterID.IsNone()) return null;
			foreach (Player player in Player.GetAllPlayers())
			{
				if (!player || !player.m_nview || !player.m_nview.IsValid()) continue;
				ZDO zdo = player.m_nview.GetZDO();
				if (zdo != null && zdo.IsValid() && zdo.m_uid.Equals(peer.m_characterID)) return player;
			}
			return null;
		}

		// Match vanilla TeleportWorld: ores / non-teleportable cargo cannot use portals.
		private static bool CanPortalTravel(Player player, bool allowAllItems, ZNetPeer peer)
		{
			if (player == null)
			{
				Notify(peer, "Cannot teleport right now.");
				return false;
			}
			if (player.IsTeleportable(allowAllItems)) return true;
			Notify(peer, "Cannot teleport with those items.");
			try
			{
				player.Message(MessageHud.MessageType.Center, "$msg_noteleport", 0, null, false);
			}
			catch (Exception) { }
			return false;
		}

		private static void RememberGateways(List<ZDO> gateways)
		{
			gatewayIds.Clear();
			if (gateways == null) return;
			foreach (ZDO zdo in gateways)
				if (zdo != null && zdo.IsValid()) gatewayIds.Add(zdo.m_uid);
		}

		private static void PublishPortal(ZDO lobby, List<ZDO> gateways, List<ZDO> world)
		{
			if (ZNet.instance == null || ZDOMan.instance == null) return;
			var ids = new List<ZDOID>();
			if (lobby != null && lobby.IsValid()) ids.Add(lobby.m_uid);
			if (gateways != null)
				foreach (ZDO gateway in gateways)
					if (gateway != null && gateway.IsValid()) ids.Add(gateway.m_uid);
			foreach (ZDOID id in hubObjects)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid()) ids.Add(zdo.m_uid);
			}
			if (world != null)
				foreach (ZDO zdo in world)
					if (zdo != null && zdo.IsValid() && !IsGateway(zdo) && AllowedTag(TagOf(zdo)))
						ids.Add(zdo.m_uid);
			ForceSendAll(ids);
		}

		private static void ForceSendAll(List<ZDOID> ids)
		{
			if (ids == null || ids.Count == 0 || ZNet.instance == null || ZDOMan.instance == null) return;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer == null || !peer.IsReady()) continue;
				for (int i = 0; i < ids.Count; i++)
					ZDOMan.instance.ForceSendZDO(peer.m_uid, ids[i]);
			}
		}

		private static void PublishToPeer(ZNetPeer peer, ZDO dest)
		{
			if (peer == null || !peer.IsReady() || ZDOMan.instance == null) return;
			if (dest != null && dest.IsValid())
				ZDOMan.instance.ForceSendZDO(peer.m_uid, dest.m_uid);
			for (int i = 0; i < hubObjects.Count; i++)
				ZDOMan.instance.ForceSendZDO(peer.m_uid, hubObjects[i]);
			for (int i = 0; i < gatewayIds.Count; i++)
				ZDOMan.instance.ForceSendZDO(peer.m_uid, gatewayIds[i]);
		}

		private static bool NearPortal(Vector3 pos, Vector3 portal, float radiusSq)
		{
			float dx = portal.x - pos.x, dz = portal.z - pos.z;
			return dx * dx + dz * dz <= radiusSq && Math.Abs(portal.y - pos.y) <= 2.5f;
		}

		private static void TryEnterGateways()
		{
			if (ZNet.instance == null || gatewayIds.Count == 0) return;
			ZDO lobby = FindHubLobby();
			if (lobby == null || !lobby.IsValid()) return;
			double now = Time.realtimeSinceStartupAsDouble;
			// Tight walk-into trigger; 4 m was large enough to catch nearby builds / standing still.
			const float radiusSq = 1.5f * 1.5f;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer == null || !peer.IsReady()) continue;
				if (lastEnter.TryGetValue(peer.m_uid, out double at) && now - at < 2.5) continue;
				Vector3 refPos = peer.GetRefPos();
				ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
				Vector3 body = character != null && character.IsValid() ? character.GetPosition() : refPos;
				for (int i = 0; i < gatewayIds.Count; i++)
				{
					ZDO gateway = ZDOMan.instance.GetZDO(gatewayIds[i]);
					if (!IsGateway(gateway)) continue;
					Vector3 portal = gateway.GetPosition();
					if (!NearPortal(refPos, portal, radiusSq) && !NearPortal(body, portal, radiusSq)) continue;
					Player traveler = PlayerOfPeer(peer);
					if (!CanPortalTravel(traveler, allowAllItems: false, peer))
					{
						lastEnter[peer.m_uid] = now;
						break;
					}
					lastEnter[peer.m_uid] = now;
					PublishToPeer(peer, lobby);
					TeleportPeer(peer, lobby);
					Notify(peer, "Choose a destination.");
					break;
				}
			}
		}

		internal static void HandleGatewayTag(TeleportWorld portal, string requested)
		{
			if (!Enabled || !portal || !portal.m_nview || !portal.m_nview.IsValid()) return;
			ZDO zdo = portal.m_nview.GetZDO();
			if (zdo == null || IsHubObject(zdo)) return;
			FarmingSupport.CreatorAt(zdo.GetPosition(), out ZNetPeer peer);
			if (string.IsNullOrWhiteSpace(requested))
			{
				Notify(peer, "Walk through this portal to pick a destination.");
				return;
			}
			ZDO dest = FindDestination(requested.Trim());
			if (dest == null)
			{
				Notify(peer, "No destination '" + requested.Trim() + "'. This portal is now tagged " + requested.Trim() + ".");
				return;
			}
			Player traveler = PlayerOfPeer(peer);
			if (!CanPortalTravel(traveler, portal.m_allowAllItems, peer)) return;
			SetTag(zdo, "");
			TeleportPeer(peer, dest);
			Notify(peer, "Traveling to " + TagOf(dest) + ".");
		}

		private static ZDO FindDestination(string tag)
		{
			ZDO exact = null, ignoreCase = null;
			foreach (ZDO zdo in WorldPortals())
			{
				string name = TagOf(zdo);
				if (name.Length == 0) continue;
				if (string.Equals(name, tag, StringComparison.Ordinal)) { exact = zdo; break; }
				if (ignoreCase == null && string.Equals(name, tag, StringComparison.OrdinalIgnoreCase)) ignoreCase = zdo;
			}
			return exact ?? ignoreCase;
		}

		private static void TeleportPeer(ZNetPeer peer, ZDO dest)
		{
			if (peer == null || dest == null || !dest.IsValid()) return;
			if (ZDOMan.instance != null)
			{
				ZDOMan.instance.ForceSendZDO(dest.m_uid);
				ZDOMan.instance.ForceSendZDO(peer.m_uid, dest.m_uid);
			}
			Quaternion rot = dest.GetRotation();
			Vector3 pos = dest.GetPosition() + rot * Vector3.forward * 1.5f + Vector3.up * 0.5f;
			ZDO character = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
			if (character != null && character.IsValid()) character.SetPosition(pos);
			try
			{
				if (Chat.instance)
					Chat.instance.TeleportPlayer(peer.m_uid, pos, rot, true);
				else if (ZRoutedRpc.instance != null)
					ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "RPC_TeleportPlayer", pos, rot, true);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning("Portal teleport failed: " + e.GetType().Name);
			}
		}

		private static void NotifyAll(string text)
		{
			if (ZNet.instance == null) return;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				Notify(peer, text);
		}

		private static void Notify(ZNetPeer peer, string text)
		{
			if (string.IsNullOrEmpty(text)) return;
			try
			{
				if (ZRoutedRpc.instance != null && peer != null)
					ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.TopLeft, "Northwatch: " + text);
			}
			catch (Exception) { }
			if (peer != null && peer.m_rpc != null)
			{
				try { peer.m_rpc.Invoke("RemotePrint", "[server] " + text); }
				catch (Exception) { }
			}
		}

		private static void ClearHub()
		{
			hubLobbyId = ZDOID.None;
			foreach (ZDOID id in hubObjects)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid()) ZDOMan.instance.DestroyZDO(zdo);
			}
			hubObjects.Clear();
			var orphans = new List<ZDO>();
			var seen = new HashSet<ZDOID>();
			CollectPortals(zdo =>
			{
				if (zdo == null || !zdo.IsValid() || !IsHubObject(zdo)) return;
				if (!seen.Add(zdo.m_uid)) return;
				orphans.Add(zdo);
			});
			CollectSectorBuckets(ZDOMan.instance.m_objectsBySector, zdo =>
			{
				if (zdo == null || !zdo.IsValid() || !IsHubObject(zdo)) return;
				if (!seen.Add(zdo.m_uid)) return;
				orphans.Add(zdo);
			});
			foreach (ZDO zdo in orphans) ZDOMan.instance.DestroyZDO(zdo);
		}

		private static ZDO FindHubLobby()
		{
			if (!hubLobbyId.IsNone())
			{
				ZDO known = ZDOMan.instance.GetZDO(hubLobbyId);
				if (known != null && known.IsValid() && IsHubLobby(known)) return known;
			}
			foreach (ZDOID id in hubObjects)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid() && IsHubLobby(zdo)) return zdo;
			}
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return null;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				foreach (ZDO zdo in bucket)
					if (zdo != null && zdo.IsValid() && IsHubLobby(zdo)) return zdo;
			}
			return null;
		}

		private static bool HasValidHubPiece()
		{
			for (int i = 0; i < hubObjects.Count; i++)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(hubObjects[i]);
				if (zdo != null && zdo.IsValid() && IsHubObject(zdo)) return true;
			}
			return false;
		}

		private static void MaintainHubPieces()
		{
			for (int i = hubObjects.Count - 1; i >= 0; i--)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(hubObjects[i]);
				if (zdo == null || !zdo.IsValid())
				{
					hubObjects.RemoveAt(i);
					continue;
				}
				zdo.Persistent = true;
				zdo.Distant = true;
				HardenHubZdo(zdo);
				if (ZNetScene.instance)
				{
					ZNetView view = ZNetScene.instance.FindInstance(zdo);
					if (view) HardenHubWear(view.GetComponent<WearNTear>());
				}
			}
		}

		private static void HardenHubZdo(ZDO zdo)
		{
			if (zdo == null) return;
			float health = zdo.GetFloat(ZDOVars.s_health, 0f);
			if (health > 0f && health < 1e8f)
				zdo.Set(ZDOVars.s_health, Math.Max(health, 1e8f));
			else if (health <= 0f)
				zdo.Set(ZDOVars.s_health, 1e8f);
			zdo.Set(ZDOVars.s_support, 1e9f);
		}

		private static void HardenHubWear(WearNTear wear)
		{
			if (!wear) return;
			wear.m_noSupportWear = true;
			wear.m_noRoofWear = true;
			wear.m_support = Math.Max(wear.m_support, wear.GetMaxSupport());
			if (wear.m_health < 1e8f) wear.m_health = 1e8f;
		}

		private static void BuildHall(List<string> tags)
		{
			EnsureHubZone();
			Quaternion faceDestinations = Quaternion.LookRotation(Vector3.forward);
			Quaternion faceHome = Quaternion.LookRotation(Vector3.back);
			const float spacing = 6f;
			const float destZ = 10f;
			int n = Math.Max(1, tags.Count);
			int cols = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(n)));
			int rows = Math.Max(1, (int)Math.Ceiling(tags.Count / (float)cols));
			PlacePlatform(cols, rows, spacing, destZ);

			ZDO lobby = PlacePrefab(ResolvePortalPrefab(), hubOrigin, faceDestinations);
			if (lobby != null)
			{
				SetTag(lobby, LobbyTag);
				lobby.Set(LobbyMarker, 1L);
				hubLobbyId = lobby.m_uid;
			}
			PlaceSign(hubOrigin + new Vector3(-2.4f, 2.2f, 1.2f), faceDestinations, "<color=yellow>Home");
			PlaceSign(hubOrigin + new Vector3(2.4f, 2.2f, 1.2f), faceDestinations,
				tags.Count == 0
					? "<color=white>Name an outpost portal"
					: "<color=white>Walk into a portal");

			for (int i = 0; i < tags.Count; i++)
			{
				int row = i / cols, col = i % cols;
				Vector3 pos = hubOrigin + new Vector3((col - (cols - 1) / 2f) * spacing, 0f, destZ + row * spacing);
				ZDO portal = PlacePrefab(ResolvePortalPrefab(), pos, faceHome);
				if (portal == null) continue;
				SetTag(portal, tags[i]);
				PlaceSign(pos + new Vector3(0f, 2.2f, -1.2f), faceHome, "<color=white>" + tags[i]);
			}
		}

		private static void PlacePlatform(int cols, int rows, float spacing, float destZ)
		{
			float minX = -((cols - 1) / 2f) * spacing - 4f;
			float maxX = ((cols - 1) / 2f) * spacing + 4f;
			float minZ = -4f;
			float maxZ = destZ + Math.Max(0, rows - 1) * spacing + 4f;
			for (float x = minX; x <= maxX + 0.01f; x += 4f)
				for (float z = minZ; z <= maxZ + 0.01f; z += 4f)
					PlaceFloor(hubOrigin + new Vector3(x, -0.1f, z));
		}

		private static void BuildHub(List<string> tags)
		{
			int n = tags.Count;
			int cols = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(n)));
			float spacing = 4f;
			for (int i = 0; i < n; i++)
			{
				int row = i / cols, col = i % cols;
				Vector3 pos = hubOrigin + new Vector3((col - cols / 2f) * spacing, 0f, (row - cols / 2f) * spacing);
				PlaceFloor(pos + new Vector3(0f, -0.1f, 0f));
				ZDO portal = PlacePrefab(ResolvePortalPrefab(), pos, Quaternion.identity);
				if (portal == null) continue;
				SetTag(portal, tags[i]);
				PlaceSign(pos + new Vector3(0f, 2f, -0.6f), Quaternion.identity, "<color=white>" + tags[i]);
			}
		}

		private static string ResolvePortalPrefab()
		{
			if (ZNetScene.instance.GetPrefab("portal_wood")) return "portal_wood";
			if (ZNetScene.instance.GetPrefab("portal")) return "portal";
			return "portal_wood";
		}

		private static void PlaceFloor(Vector3 pos)
		{
			foreach (string name in new[] { "Piece_grausten_floor_4x4", "wood_floor_4x4", "wood_floor" })
			{
				if (!ZNetScene.instance.GetPrefab(name)) continue;
				PlacePrefab(name, pos, Quaternion.identity);
				return;
			}
		}

		private static void PlaceSign(Vector3 pos, Quaternion rot, string text)
		{
			ZDO sign = PlacePrefab("sign", pos, rot);
			if (sign != null) sign.Set(ZDOVars.s_text, text);
		}

		private static ZDO PlacePrefab(string prefabName, Vector3 pos, Quaternion rot)
		{
			GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
			if (!prefab) return null;
			int hash = prefab.name.GetStableHashCode();
			ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
			zdo.SetPrefab(hash);
			zdo.SetRotation(rot);
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set(HubMarker, 1L);
			zdo.Persistent = true;
			zdo.Distant = true;
			HardenHubZdo(zdo);
			GameObject go = ZNetScene.instance.CreateObject(zdo);
			zdo.Persistent = true;
			zdo.Distant = true;
			HardenHubZdo(zdo);
			if (go) HardenHubWear(go.GetComponent<WearNTear>());
			ZDOMan.instance.SetDirtySector(zdo);
			hubObjects.Add(zdo.m_uid);
			return zdo;
		}

		private static void Connect()
		{
			if (Game.instance) Game.instance.ConnectPortals();
		}
	}
}
