using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/*
		Server-only sync/CPU reductions inspired by ValheimTune (MIT) and LeanNet:
		dirty-set CreateSyncList, ZDO relay throttle, Top-K send sort, skip heightmap
		render mesh, defer UnloadUnusedAssets, and motion/revision cull.

		Does not change ownership, wire format, or require client mods. Do not also run
		ValheimTune / SkadiNet / LeanNet on the same dedicated server.
	*/
	public class Sync : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.syncDirtySets.Value
				|| Configuration.syncRelayMinIntervalMs.Value > 0
				|| Configuration.syncTopKSort.Value
				|| Configuration.skipRenderMesh.Value
				|| Configuration.deferAssetUnload.Value
				|| Configuration.motionCullEnabled.Value;
		}

		internal static void Tick()
		{
			AssetUnload.RunIfDue();
			DirtySync.WatchdogTick();
		}

		[HarmonyPatch(typeof(ZDO), nameof(ZDO.DataRevision), MethodType.Setter)]
		public static class ZDO_DataRevision_Mark
		{
			static void Postfix(ZDO __instance) => DirtySync.Mark(__instance.m_uid);
		}

		[HarmonyPatch(typeof(ZDO), nameof(ZDO.OwnerRevision), MethodType.Setter)]
		public static class ZDO_OwnerRevision_Mark
		{
			static void Postfix(ZDO __instance) => DirtySync.Mark(__instance.m_uid);
		}

		[HarmonyPatch(typeof(ZDO), nameof(ZDO.Deserialize))]
		public static class ZDO_Deserialize_Watchdog
		{
			static void Postfix() => DirtySync.NoteRecv();
		}

		[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateSyncList))]
		public static class ZDOMan_CreateSyncList_Dirty
		{
			static bool Prefix(ZDOMan __instance, ZDOMan.ZDOPeer peer, List<ZDO> toSync, out bool __state)
			{
				__state = false;
				if (!DirtySync.Active) return true;

				ZDOID lastId = ZDOID.None;
				ZDO lastZdo = null;
				ZDO Lookup(ZDOID id)
				{
					if (id != lastId)
					{
						lastZdo = __instance.GetZDO(id);
						lastId = id;
					}
					return lastZdo;
				}

				Vector3 refPos = peer.m_peer.GetRefPos();
				Vector2s zone = ZoneSystem.GetZone(refPos);
				DirtyPeerState st = DirtySync.StateFor(peer);
				bool needsFull = st.NeedsFullScan((zone.x, zone.y), Time.time, Configuration.syncReconcileSeconds.Value, active: true);
				if (needsFull)
				{
					__state = true;
					return true;
				}

				SimulationDistance sd = peer.m_peer.m_simulationDistance;
				List<ZDOID> ids = DirtySync.ScratchIds;
				ids.Clear();
				int relayMs = Configuration.syncRelayMinIntervalMs.Value;
				st.Drain(ids,
					exists: id => Lookup(id) != null,
					inArea: id =>
					{
						ZDO z = Lookup(id);
						Vector2s s = z.GetSector();
						return DirtySync.InNear(zone, s, sd) || (z.Distant && DirtySync.InDistant(zone, s, sd));
					},
					shouldSend: id => peer.ShouldSend(Lookup(id)),
					deferSend: id => DirtySync.RelayThrottled(peer, Lookup(id), relayMs));

				int nearCount = 0;
				for (int i = 0; i < ids.Count; i++)
				{
					ZDO z = __instance.GetZDO(ids[i]);
					if (DirtySync.InNear(zone, z.GetSector(), sd))
					{
						toSync.Add(z);
						nearCount++;
					}
				}
				if (nearCount < 10)
				{
					for (int i = 0; i < ids.Count; i++)
					{
						ZDO z = __instance.GetZDO(ids[i]);
						if (!DirtySync.InNear(zone, z.GetSector(), sd)) toSync.Add(z);
					}
				}

				__instance.ServerSortSendZDOS(toSync, refPos, peer);
				__instance.AddForceSendZdos(peer, toSync);
				return false;
			}

			static void Postfix(ZDOMan.ZDOPeer peer, List<ZDO> toSync, bool __state)
			{
				if (!__state || !DirtySync.Active) return;
				DirtyPeerState st = DirtySync.StateFor(peer);
				for (int i = 0; i < toSync.Count; i++) st.Pending.Add(toSync[i].m_uid);
			}
		}

		[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.ServerSortSendZDOS))]
		public static class ZDOMan_ServerSortSendZDOS_TopK
		{
			static bool Prefix(List<ZDO> objects, Vector3 refPos, ZDOMan.ZDOPeer peer)
			{
				if (!Configuration.syncTopKSort.Value || ZNet.instance == null || !ZNet.instance.IsServer()) return true;
				float time = Time.time;
				long receiver = peer.m_peer.m_uid;
				var zdos = peer.m_zdos;
				int k = Configuration.syncTopK.Value;
				if (k <= 0) k = Math.Max(64, Networking.QueueSize() / 64);
				TopK.Select(objects, z =>
				{
					bool priorityOther = z.Type == ZDO.ObjectType.Prioritized && z.HasOwner() && z.GetOwner() != receiver;
					int bucket = priorityOther ? 0 : 1 + (3 - (int)z.Type);
					float stale = 100f;
					if (zdos.TryGetValue(z.m_uid, out var info)) stale = Mathf.Clamp(time - info.m_syncTime, 0f, 100f);
					double sortValue = Vector3.Distance(z.GetPosition(), refPos) - stale * 1.5f;
					return bucket * 100000.0 + sortValue;
				}, k);
				return false;
			}
		}

		[HarmonyPatch(typeof(Heightmap), nameof(Heightmap.RebuildRenderMesh))]
		public static class Heightmap_RebuildRenderMesh_Skip
		{
			static bool Prefix()
			{
				if (!Configuration.skipRenderMesh.Value) return true;
				if (ZNet.instance == null || !ZNet.instance.IsDedicated()) return true;
				return false;
			}
		}

		[HarmonyPatch(typeof(Game), nameof(Game.CollectResources))]
		public static class Game_CollectResources_Defer
		{
			static bool Prefix()
			{
				return !AssetUnload.ShouldDefer();
			}
		}

		[HarmonyPatch(typeof(ZDO), "Set", new Type[] { typeof(int), typeof(Vector3) })]
		public static class ZDO_Set_Vec3_Cull
		{
			static bool Prefix(ZDO __instance, int hash, Vector3 value)
			{
				if (!MotionCull.Active || MotionCull.IsForcing) return true;
				if (__instance.GetVec3(hash, out Vector3 cur))
				{
					if ((cur - value).sqrMagnitude < MotionCull.Vec3CullSq) return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(ZDO), "Set", new Type[] { typeof(int), typeof(Quaternion) })]
		public static class ZDO_Set_Quat_Cull
		{
			static bool Prefix(ZDO __instance, int hash, Quaternion value)
			{
				if (!MotionCull.Active || MotionCull.IsForcing) return true;
				if (__instance.GetQuaternion(hash, out Quaternion cur))
				{
					if (Quaternion.Dot(cur, value) > 0.98f) return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(ZDO), nameof(ZDO.IncreaseDataRevision))]
		public static class ZDO_IncreaseDataRevision_Freeze
		{
			// TreeLog SetPosition/SetRotation call IncreaseDataRevision — blocking it leaves
			// the server pose updated but DataRevision unchanged, so clients never catch up.
			static bool Prefix(ZDO __instance)
			{
				if (!MotionCull.IsFreezing) return true;
				if (MotionCull.IsTreeLogPrefab(__instance.GetPrefab())) return true;
				return false;
			}
		}

		[HarmonyPatch(typeof(ZSyncTransform), nameof(ZSyncTransform.CustomLateUpdate))]
		public static class ZSyncTransform_MotionCull
		{
			private static bool freezing;
			private static bool forcing;

			static void Prefix(ZSyncTransform __instance, ZNetView ___m_nview)
			{
				freezing = false;
				forcing = false;
				if (!MotionCull.Active || ___m_nview == null || !___m_nview.IsValid()) return;
				ZDO zdo = ___m_nview.GetZDO();
				if (zdo.GetFloat(ZDOVars.s_rudder, out _)) return;
				// Falling TreeLog / tumbling debris: bypass rate-limit AND Vec3/Quat culls.
				if (MotionCull.IsHotPhysics(__instance, ___m_nview))
				{
					forcing = true;
					MotionCull.Force++;
					MotionCull.ForceSendHot(___m_nview);
					return;
				}
				float rate = Mathf.Max(4f, Configuration.motionCullPhysicsHz.Value);
				forcing = MotionCull.ShouldUpdate(zdo, 0.5f);
				freezing = !forcing && !MotionCull.ShouldUpdate(zdo, rate);
				if (forcing) MotionCull.Force++;
				if (freezing) MotionCull.Freeze++;
			}

			static void Postfix()
			{
				if (freezing) { freezing = false; MotionCull.Freeze--; }
				if (forcing) { forcing = false; MotionCull.Force--; }
			}
		}

		/// <summary>Promote falling logs to Prioritized so TopK / relay treat them like creatures.</summary>
		[HarmonyPatch(typeof(TreeLog), "Awake")]
		public static class TreeLog_Prioritize
		{
			static void Postfix(TreeLog __instance, ZNetView ___m_nview)
			{
				if (___m_nview == null || !___m_nview.IsValid()) return;
				ZDO zdo = ___m_nview.GetZDO();
				if (zdo == null || !zdo.IsValid() || !zdo.IsOwner()) return;
				if (zdo.Type != ZDO.ObjectType.Prioritized)
					zdo.SetType(ZDO.ObjectType.Prioritized);
			}
		}

		[HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
		public static class Character_MotionCull
		{
			private static bool freezing;
			private static bool forcing;

			static void Prefix(Character __instance, ZNetView ___m_nview)
			{
				freezing = false;
				forcing = false;
				if (!MotionCull.Active || ___m_nview == null || !___m_nview.IsValid()) return;
				if (__instance.IsPlayer()) return;
				ZDO zdo = ___m_nview.GetZDO();
				float rate = Mathf.Max(4f, Configuration.motionCullNpcHz.Value);
				forcing = MotionCull.ShouldUpdate(zdo, 0.5f);
				freezing = !forcing && !MotionCull.ShouldUpdate(zdo, rate);
				if (forcing) MotionCull.Force++;
				if (freezing) MotionCull.Freeze++;
			}

			static void Postfix()
			{
				if (freezing) { freezing = false; MotionCull.Freeze--; }
				if (forcing) { forcing = false; MotionCull.Force--; }
			}
		}

		[HarmonyPatch(typeof(Character), nameof(Character.UpdateGroundTilt))]
		public static class Character_UpdateGroundTilt_Cull
		{
			static void Prefix()
			{
				if (MotionCull.Active) MotionCull.Freeze++;
			}

			static void Postfix()
			{
				if (MotionCull.Active) MotionCull.Freeze--;
			}
		}

		[HarmonyPatch(typeof(Character), nameof(Character.SyncVelocity))]
		public static class Character_SyncVelocity_Cull
		{
			static bool Prefix(Rigidbody ___m_body, ref Vector3 ___m_bodyVelocityCached)
			{
				if (!MotionCull.Active || MotionCull.IsForcing) return true;
#pragma warning disable CS0618 // Unity marks velocity obsolete; Valheim still uses it on Character.
				Vector3 delta = ___m_body.velocity - ___m_bodyVelocityCached;
#pragma warning restore CS0618
				return delta.sqrMagnitude > MotionCull.Vec3CullSq;
			}
		}

		[HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.FixedUpdate))]
		public static class MonoUpdaters_FixedUpdate_MotionTime
		{
			static void Prefix()
			{
				if (!MotionCull.Active) return;
				MotionCull.Advance(Time.fixedDeltaTime);
			}
		}

		[HarmonyPatch(typeof(MonoUpdaters), nameof(MonoUpdaters.LateUpdate))]
		public static class MonoUpdaters_LateUpdate_MotionTime
		{
			static void Prefix()
			{
				if (!MotionCull.Active) return;
				MotionCull.Advance(Time.deltaTime);
			}
		}

		internal static class DirtySync
		{
			private static readonly ConditionalWeakTable<ZDOMan.ZDOPeer, DirtyPeerState> states =
				new ConditionalWeakTable<ZDOMan.ZDOPeer, DirtyPeerState>();
			internal static readonly List<ZDOID> ScratchIds = new List<ZDOID>(256);

			private static long marksWindow;
			private static long recvWindow;
			private static float windowStart = -1f;
			private static bool disabled;

			internal static bool Active =>
				Configuration.syncDirtySets.Value
				&& !disabled
				&& ZNet.instance != null
				&& ZNet.instance.IsServer();

			internal static DirtyPeerState StateFor(ZDOMan.ZDOPeer peer) =>
				states.GetValue(peer, _ => new DirtyPeerState());

			internal static void Mark(ZDOID id)
			{
				if (id == ZDOID.None) return;
				marksWindow++;
				if (!Active) return;
				ZDOMan man = ZDOMan.instance;
				if (man == null) return;
				List<ZDOMan.ZDOPeer> peers = man.m_peers;
				for (int i = 0; i < peers.Count; i++) StateFor(peers[i]).Pending.Add(id);
			}

			internal static void NoteRecv() => recvWindow++;

			internal static void WatchdogTick()
			{
				if (!Configuration.syncDirtySets.Value) return;
				float now = Time.realtimeSinceStartup;
				if (windowStart < 0f) windowStart = now;
				if (now - windowStart < 10f) return;

				if (!disabled && recvWindow > 0 && marksWindow == 0)
				{
					disabled = true;
					ServersidePlugin.logger.LogWarning("Sync DirtySets: change hooks silent while receiving ZDOs; falling back to vanilla CreateSyncList until marks resume.");
				}
				else if (disabled && marksWindow > 0)
				{
					disabled = false;
					ServersidePlugin.logger.LogInfo("Sync DirtySets: change hooks alive again; dirty sets re-armed.");
				}

				marksWindow = 0;
				recvWindow = 0;
				windowStart = now;
			}

			internal static int ZoneChebyshev(int ax, int ay, int bx, int by)
			{
				int dx = ax - bx; if (dx < 0) dx = -dx;
				int dy = ay - by; if (dy < 0) dy = -dy;
				return dx > dy ? dx : dy;
			}

			internal static bool InNear(Vector2s centre, Vector2s candidate, SimulationDistance sd)
			{
				if (ZoneChebyshev(centre.x, centre.y, candidate.x, candidate.y) > sd.NearSimulationDistance) return false;
				return sd.IsClassic || (ZoneSystem.instance != null && ZoneSystem.instance.ZonesWithinRadius(centre, candidate, sd.NearSimulationDistance));
			}

			internal static bool InDistant(Vector2s centre, Vector2s candidate, SimulationDistance sd)
			{
				if (ZoneChebyshev(centre.x, centre.y, candidate.x, candidate.y) > sd.TotalSimulationDistance) return false;
				return sd.IsClassic || (ZoneSystem.instance != null && ZoneSystem.instance.ZonesWithinRadius(centre, candidate, sd.TotalSimulationDistance, ghostZone: true));
			}

			internal static bool RelayThrottled(ZDOMan.ZDOPeer peer, ZDO z, int minMs)
			{
				if (minMs <= 0 || z == null) return false;
				if (z.Type == ZDO.ObjectType.Prioritized) return false;
				if (z.GetOwner() == peer.m_peer.m_uid) return false;
				// Prefab check — no FindInstance required (works the frame a log is spawned).
				if (MotionCull.IsTreeLogPrefab(z.GetPrefab())) return false;
				if (MotionCull.IsHotZdo(z)) return false;
				if (!peer.m_zdos.TryGetValue(z.m_uid, out var info)) return false;
				return (Time.time - info.m_syncTime) * 1000f < minMs;
			}
		}

		internal static class AssetUnload
		{
			private static float deferredSince = -1f;

			internal static bool ShouldDefer()
			{
				if (!Configuration.deferAssetUnload.Value) return false;
				if (ZNet.instance == null || !ZNet.instance.IsDedicated()) return false;

				int peers = ZNet.instance.GetPeerConnections();
				float now = Time.realtimeSinceStartup;
				double held = deferredSince < 0f ? 0.0 : now - deferredSince;
				double maxDefer = Configuration.assetUnloadMaxDeferMinutes.Value * 60.0;

				if (peers > 0 && held < maxDefer)
				{
					if (deferredSince < 0f)
					{
						deferredSince = now;
						ServersidePlugin.logger.LogInfo($"Sync: asset unload deferred ({peers} player(s) online; backstop {Configuration.assetUnloadMaxDeferMinutes.Value} min).");
					}
					return true;
				}

				if (deferredSince >= 0f)
				{
					ServersidePlugin.logger.LogInfo($"Sync: asset unload running after {held:F0}s deferred ({peers} player(s) online).");
					deferredSince = -1f;
				}
				return false;
			}

			internal static void RunIfDue()
			{
				if (deferredSince < 0f || !Configuration.deferAssetUnload.Value) return;
				ZNet znet = ZNet.instance;
				if (znet == null) return;
				double held = Time.realtimeSinceStartup - deferredSince;
				double maxDefer = Configuration.assetUnloadMaxDeferMinutes.Value * 60.0;
				if (znet.GetPeerConnections() > 0 && held < maxDefer) return;
				Game.instance?.CollectResources(false);
			}
		}

		internal static class MotionCull
		{
			private static double netTime;
			private static float lastDt = 0.01f;
			private static float nextForceSend;
			private static readonly Dictionary<int, bool> treeLogPrefabCache = new Dictionary<int, bool>();
			internal static int Freeze;
			internal static int Force;

			internal static bool Active =>
				Configuration.motionCullEnabled.Value
				&& ZNet.instance != null
				&& ZNet.instance.IsServer();

			internal static bool IsFreezing => Freeze > 0 && Force <= 0;
			internal static bool IsForcing => Force > 0;

			internal static float Vec3CullSq
			{
				get
				{
					float m = Mathf.Clamp(Configuration.motionCullVec3Meters.Value, 0.01f, 0.2f);
					return m * m;
				}
			}

			internal static void Advance(float dt)
			{
				lastDt = dt > 0f ? dt : lastDt;
				netTime += lastDt;
				if (dt > 100f) netTime -= dt;
			}

			internal static bool ShouldUpdate(ZDO zdo, float rateHz)
			{
				double baseT = netTime + 0.023 * (zdo.m_uid.ID & 4095);
				double next = baseT + lastDt;
				return Mathf.RoundToInt((float)(baseT * rateHz)) != Mathf.RoundToInt((float)(next * rateHz));
			}

			internal static bool IsTreeLogPrefab(int prefabHash)
			{
				if (prefabHash == 0 || !ZNetScene.instance) return false;
				if (treeLogPrefabCache.TryGetValue(prefabHash, out bool cached)) return cached;
				GameObject go = ZNetScene.instance.GetPrefab(prefabHash);
				bool isLog = go && go.GetComponent<TreeLog>() != null;
				treeLogPrefabCache[prefabHash] = isLog;
				return isLog;
			}

			/// <summary>
			/// Non-kinematic rigidbodies that are still moving (TreeLog fall, timber, ore chunks).
			/// Skip MotionCull / relay throttle so clients see smooth physics.
			/// </summary>
			internal static bool IsActivelyTumbling(ZSyncTransform sync)
			{
				if (!sync) return false;
				Rigidbody body = sync.GetComponent<Rigidbody>();
				if (!body || body.isKinematic || body.IsSleeping()) return false;
				const float linSq = 0.01f;  // 0.1 m/s
				const float angSq = 0.04f;  // ~0.2 rad/s
				return body.linearVelocity.sqrMagnitude > linSq
					|| body.angularVelocity.sqrMagnitude > angSq;
			}

			internal static bool IsHotPhysics(ZSyncTransform sync, ZNetView view)
			{
				if (view && view.IsValid())
				{
					// Any TreeLog with a live non-kinematic body — don't wait on velocity thresholds.
					if (view.GetComponent<TreeLog>() || IsTreeLogPrefab(view.GetZDO().GetPrefab()))
					{
						Rigidbody body = view.GetComponent<Rigidbody>();
						if (body && !body.isKinematic && !body.IsSleeping()) return true;
					}
				}
				return IsActivelyTumbling(sync);
			}

			internal static bool IsHotZdo(ZDO z)
			{
				if (z == null) return false;
				if (IsTreeLogPrefab(z.GetPrefab())) return true;
				if (!ZNetScene.instance) return false;
				ZNetView view = ZNetScene.instance.FindInstance(z);
				if (!view) return false;
				return IsHotPhysics(view.GetComponent<ZSyncTransform>(), view);
			}

			/// <summary>Push tumbling logs into the next send even if DirtySets/TopK would delay them.</summary>
			internal static void ForceSendHot(ZNetView view)
			{
				if (view == null || !view.IsValid() || ZDOMan.instance == null || ZNet.instance == null) return;
				float now = Time.time;
				if (now < nextForceSend) return;
				nextForceSend = now + 0.05f; // 20 Hz force-send budget shared across hot logs
				ZDO zdo = view.GetZDO();
				if (zdo == null || !zdo.IsValid()) return;
				ZDOMan.instance.ForceSendZDO(zdo.m_uid);
				List<ZNetPeer> peers = ZNet.instance.GetPeers();
				if (peers == null) return;
				for (int i = 0; i < peers.Count; i++)
				{
					ZNetPeer peer = peers[i];
					if (peer == null || !peer.IsReady()) continue;
					ZDOMan.instance.ForceSendZDO(peer.m_uid, zdo.m_uid);
				}
			}
		}
	}
}
