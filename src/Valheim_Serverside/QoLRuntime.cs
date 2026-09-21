using System;
using System.Collections.Generic;
using System.Reflection;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	/// <summary>
	/// Server-forced QoL for vanilla/console clients: magnet pickup, instant loot at
	/// killer feet, structure auto-repair near crafting stations, and (via PeerWorldKeys)
	/// carry-weight / near-bench durability spoofing.
	/// </summary>
	internal static class QoLRuntime
	{
		internal static bool Installed;
		private static double nextMagnet;
		private static double nextRepair;
		private static FieldInfo stationsField;
		private static int magnetMoves;
		private static int repairs;
		private static readonly List<ZDO> magnetCandidates = new List<ZDO>();

		internal static bool Enabled => Installed && Configuration.qolEnabled.Value;
		internal static string Status
		{
			get
			{
				if (!Enabled) return "qol inactive";
				string extra = ContainerExpand.Status;
				if (!string.IsNullOrEmpty(BackpackRuntime.Status)) extra += " " + BackpackRuntime.Status;
				if (!string.IsNullOrEmpty(CraftFromChests.Status)) extra += " " + CraftFromChests.Status;
				return $"qol magnet {magnetMoves} repairs {repairs}" + (string.IsNullOrEmpty(extra) ? "" : " " + extra);
			}
		}

		internal static void Tick()
		{
			if (!Enabled || ZDOMan.instance == null || !ZNet.instance || !ZNet.instance.IsServer())
				return;
			if (!ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated) return;

			PeerWorldKeys.Tick();

			double now = Time.realtimeSinceStartupAsDouble;
			if (Configuration.qolMagnetPickup.Value && now >= nextMagnet)
			{
				nextMagnet = now + 0.25;
				TickMagnet();
			}
			if (Configuration.qolStructureRepair.Value && now >= nextRepair)
			{
				nextRepair = now + 2;
				TickStructureRepair();
			}

			ContainerExpand.Tick();
			BackpackRuntime.Tick();
			CraftFromChests.Tick();
		}

		private static void TickMagnet()
		{
			float radius = Mathf.Max(2.5f, Configuration.qolMagnetRadius.Value);
			float radiusSq = radius * radius;
			const float vanillaPickup = 2f;
			float vanillaSq = vanillaPickup * vanillaPickup;
			// Soft pull — large steps + SetOwner fights look like low-FPS falling loot.
			float step = Mathf.Clamp(Configuration.qolMagnetStep.Value, 0.25f, 1.5f);

			List<ZNetPeer> peers = ZNet.instance.GetPeers();
			if (peers == null || peers.Count == 0) return;

			magnetCandidates.Clear();
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return;

			var sectorSet = new HashSet<int>();
			for (int p = 0; p < peers.Count; p++)
			{
				ZNetPeer peer = peers[p];
				if (peer == null || !peer.IsReady()) continue;
				Vector2s zone = ZoneSystem.GetZone(peer.GetRefPos());
				for (int dx = -1; dx <= 1; dx++)
					for (int dy = -1; dy <= 1; dy++)
					{
						ZoneSystem.SectorIndex si = ZoneSystem.SectorToIndex(zone.x + dx, zone.y + dy);
						if (si.Sector < (uint)sectors.Length) sectorSet.Add((int)si.Sector);
					}
			}

			foreach (int idx in sectorSet)
			{
				if (idx < 0 || idx >= sectors.Length) continue;
				List<ZDO> bucket = sectors[idx];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					ZDO zdo = bucket[i];
					if (zdo == null || !zdo.IsValid()) continue;
					if (!IsItemDropPrefab(zdo.GetPrefab())) continue;
					magnetCandidates.Add(zdo);
				}
			}

			long serverId = ZDOMan.GetSessionID();
			int moved = 0;
			for (int i = 0; i < magnetCandidates.Count && moved < 16; i++)
			{
				ZDO zdo = magnetCandidates[i];
				long owner = zdo.GetOwner();
				// Never steal from a connected player — that causes RequestOwn spam and physics hitching.
				if (owner != 0L && owner != serverId && ZNet.instance.GetPeer(owner) != null)
					continue;

				Vector3 itemPos = zdo.GetPosition();
				ZNetPeer closest = null;
				float bestSq = radiusSq;
				for (int p = 0; p < peers.Count; p++)
				{
					ZNetPeer peer = peers[p];
					if (peer == null || !peer.IsReady()) continue;
					float sq = (peer.GetRefPos() - itemPos).sqrMagnitude;
					if (sq < bestSq)
					{
						bestSq = sq;
						closest = peer;
					}
				}
				if (closest == null) continue;

				// Inside vanilla auto-pickup: hand ownership to the player and stop moving.
				if (bestSq <= vanillaSq)
				{
					if (owner != closest.m_uid)
					{
						zdo.SetOwner(closest.m_uid);
						ZDOMan.instance.ForceSendZDO(closest.m_uid, zdo.m_uid);
					}
					continue;
				}

				// Only nudge server-owned drops. Skip freshly airborne loot (high above ground noise).
				if (owner != 0L && owner != serverId) continue;

				Vector3 target = closest.GetRefPos() + Vector3.up * 0.15f;
				Vector3 delta = target - itemPos;
				float dist = delta.magnitude;
				if (dist < 0.1f) continue;
				Vector3 next = itemPos + delta * Mathf.Min(1f, step / dist);
				next.y = itemPos.y; // keep height — don't yank falling logs through the air
				if (owner != serverId)
					zdo.SetOwner(serverId);
				zdo.SetPosition(next);
				ZeroDropVelocity(zdo);
				moved++;
				magnetMoves++;
			}
		}

		private static void ZeroDropVelocity(ZDO zdo)
		{
			if (!ZNetScene.instance) return;
			ZNetView view = ZNetScene.instance.FindInstance(zdo);
			if (!view) return;
			Rigidbody body = view.GetComponent<Rigidbody>();
			if (body)
			{
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}
		}

		private static void TickStructureRepair()
		{
			float range = Mathf.Max(1f, Configuration.qolStationRange.Value);
			float rangeSq = range * range;
			List<CraftingStation> stations = AllStations();
			if (stations == null || stations.Count == 0) return;

			List<WearNTear> pieces = WearNTear.GetAllInstances();
			if (pieces == null || pieces.Count == 0) return;

			var stationPos = new List<Vector3>(stations.Count);
			for (int i = 0; i < stations.Count; i++)
			{
				CraftingStation s = stations[i];
				if (s && s.transform) stationPos.Add(s.transform.position);
			}
			if (stationPos.Count == 0) return;

			int budget = 24;
			for (int i = 0; i < pieces.Count && budget > 0; i++)
			{
				WearNTear wnt = pieces[i];
				if (!wnt || !wnt.m_nview || !wnt.m_nview.IsValid()) continue;
				ZDO zdo = wnt.m_nview.GetZDO();
				if (zdo == null || !zdo.IsValid()) continue;
				Vector3 pos = wnt.transform.position;
				bool near = false;
				for (int s = 0; s < stationPos.Count; s++)
				{
					if ((stationPos[s] - pos).sqrMagnitude <= rangeSq)
					{
						near = true;
						break;
					}
				}
				if (!near) continue;

				float max = wnt.m_health;
				if (max <= 0f) continue;
				float health = zdo.GetFloat(ZDOVars.s_health, max);
				if (health >= max - 0.01f) continue;

				if (zdo.GetOwner() != ZDOMan.GetSessionID())
					zdo.SetOwner(ZDOMan.GetSessionID());
				zdo.Set(ZDOVars.s_health, max);
				try
				{
					wnt.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", max);
				}
				catch (Exception) { }
				repairs++;
				budget--;
			}
		}

		internal static Vector3 InstantLootPoint(Vector3 fallback)
		{
			float range = Mathf.Max(1f, Configuration.qolInstantLootRange.Value);
			Player player = Player.GetClosestPlayer(fallback, range);
			if (player) return player.transform.position + Vector3.up * 0.5f;

			if (ZNet.instance == null) return fallback;
			ZNetPeer best = null;
			float bestSq = range * range;
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer == null || !peer.IsReady()) continue;
				float sq = (peer.GetRefPos() - fallback).sqrMagnitude;
				if (sq < bestSq)
				{
					bestSq = sq;
					best = peer;
				}
			}
			if (best != null) return best.GetRefPos() + Vector3.up * 0.5f;
			return fallback;
		}

		private static bool IsItemDropPrefab(int prefab)
		{
			if (!ZNetScene.instance) return false;
			GameObject go = ZNetScene.instance.GetPrefab(prefab);
			return go && go.GetComponent<ItemDrop>() != null;
		}

		private static List<CraftingStation> AllStations()
		{
			if (stationsField == null)
				stationsField = typeof(CraftingStation).GetField("m_allStations", BindingFlags.Static | BindingFlags.NonPublic);
			return stationsField?.GetValue(null) as List<CraftingStation>;
		}
	}
}
