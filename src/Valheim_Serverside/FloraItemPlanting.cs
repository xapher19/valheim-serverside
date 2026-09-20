using System;
using System.Collections.Generic;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	// Dedicated-server planting for vanilla clients: drop enough harvest items on cultivated
	// ground, wait a short settle, and the server places the matching flora with a piece creator.
	internal static class FloraItemPlanting
	{
		internal static bool Installed;
		private static double nextScan;
		private static int plantsThisSession;
		private static readonly Dictionary<ZDOID, double> firstSeen = new Dictionary<ZDOID, double>();
		private static readonly HashSet<ZDOID> warned = new HashSet<ZDOID>();
		private static readonly List<ZDO> candidates = new List<ZDO>();
		private static readonly List<ZDOID> stale = new List<ZDOID>();

		internal static bool Enabled => Installed && Configuration.farmingItemPlanting.Value;
		internal static string Status => !Enabled ? "item planting inactive"
			: $"item planting {plantsThisSession} this session";

		internal static void Tick()
		{
			if (!Enabled || ZDOMan.instance == null || !ZNetScene.instance || !ZNet.instance || !ZNet.instance.IsServer())
				return;
			if (!ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated) return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextScan) return;
			nextScan = now + 1;
			FarmingSupport.EnsureRecipes(ZNetScene.instance);
			candidates.Clear();
			var live = new HashSet<ZDOID>();
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					ZDO zdo = bucket[i];
					if (zdo == null || !zdo.IsValid() || !FarmingSupport.IsPlantableItem(zdo.GetPrefab())) continue;
					live.Add(zdo.m_uid);
					if (!firstSeen.ContainsKey(zdo.m_uid)) firstSeen[zdo.m_uid] = now;
					if (now - firstSeen[zdo.m_uid] < Configuration.farmingItemPlantSettleSeconds.Value) continue;
					candidates.Add(zdo);
				}
			}
			stale.Clear();
			foreach (ZDOID id in firstSeen.Keys)
				if (!live.Contains(id)) stale.Add(id);
			foreach (ZDOID id in stale)
			{
				firstSeen.Remove(id);
				warned.Remove(id);
			}

			int plantedDrops = 0;
			int cost = Math.Max(1, Configuration.farmingItemPlantCost.Value);
			for (int i = 0; i < candidates.Count && plantedDrops < 4; i++)
			{
				ZDO zdo = candidates[i];
				if (zdo == null || !zdo.IsValid()) continue;
				int stack = StackOf(zdo);
				bool cultivated = FarmingSupport.IsCultivatedGround(zdo.GetPosition());
				long creator = FarmingSupport.CreatorAt(zdo.GetPosition(), out ZNetPeer peer);
				if (stack < cost)
				{
					WarnOnce(zdo, peer, "Need " + cost + " to plant (this drop has " + stack + ").");
					continue;
				}
				int n = FarmingSupport.TryPlantFromDrop(zdo, stack, cultivated, creator, out string message);
				if (n <= 0)
				{
					WarnOnce(zdo, peer, message);
					continue;
				}
				plantedDrops++;
				plantsThisSession += n;
				firstSeen.Remove(zdo.m_uid);
				warned.Remove(zdo.m_uid);
				if (message != null) Notify(peer, message);
			}
		}

		private static int StackOf(ZDO zdo)
		{
			ItemDrop drop = DropOf(zdo);
			if (drop != null && drop.m_itemData != null && drop.m_itemData.m_stack > 0)
				return drop.m_itemData.m_stack;
			int fromZdo = zdo.GetInt(ZDOVars.s_stack, zdo.GetInt("stack", 0));
			return fromZdo > 0 ? fromZdo : 1;
		}

		private static ItemDrop DropOf(ZDO zdo)
		{
			if (!ZNetScene.instance) return null;
			ZNetView view = ZNetScene.instance.FindInstance(zdo);
			return view ? view.GetComponent<ItemDrop>() : null;
		}

		private static void WarnOnce(ZDO zdo, ZNetPeer peer, string message)
		{
			if (zdo == null || string.IsNullOrEmpty(message) || !warned.Add(zdo.m_uid)) return;
			Notify(peer, message);
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
			ServersidePlugin.logger.LogInfo("Item planting: " + text);
		}
	}
}
