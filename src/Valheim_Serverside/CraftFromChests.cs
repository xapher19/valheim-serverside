using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	/// <summary>
	/// Approximate craft/build-from-chests: while near a crafting station, temporarily move
	/// items from nearby player-built containers into the player via a staging private chest
	/// + TakeAllResponse; reclaim leftovers with StackResponse on leave.
	/// Not AzuCraftyBoxes parity (crafting emits no RPCs) — materials shuttle only.
	/// </summary>
	internal static class CraftFromChests
	{
		private static readonly Dictionary<long, ShuttleState> states = new Dictionary<long, ShuttleState>();
		private static FieldInfo stationsField;
		private static MethodInfo saveMethod;
		private static double nextTick;

		internal static bool Enabled =>
			QoLRuntime.Installed && Configuration.qolEnabled.Value && Configuration.qolCraftFromChests.Value;

		internal static string Status => !Enabled ? "" : $"craft-shuttle {states.Count}";

		internal static void Tick()
		{
			if (!Enabled || ZNet.instance == null || ZDOMan.instance == null || !ZNetScene.instance) return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextTick) return;
			nextTick = now + 0.5;

			float stationRange = Mathf.Max(2f, Configuration.qolStationRange.Value);
			float chestRange = Mathf.Max(2f, Configuration.qolCraftChestRange.Value);
			float stationSq = stationRange * stationRange;
			float chestSq = chestRange * chestRange;

			var live = new HashSet<long>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer == null || !peer.IsReady()) continue;
				live.Add(peer.m_uid);
				Vector3 pos = peer.GetRefPos();
				bool atStation = NearStation(pos, stationSq);
				if (!states.TryGetValue(peer.m_uid, out ShuttleState state))
				{
					state = new ShuttleState { PeerId = peer.m_uid };
					states[peer.m_uid] = state;
				}

				if (atStation && !state.Active)
					BeginShuttle(peer, pos, chestSq, state);
				else if (!atStation && state.Active)
					EndShuttle(peer, state);
			}

			if (states.Count == live.Count) return;
			var stale = new List<long>();
			foreach (long id in states.Keys)
				if (!live.Contains(id)) stale.Add(id);
			foreach (long id in stale)
			{
				EndShuttle(ZNet.instance.GetPeer(id), states[id]);
				states.Remove(id);
			}
		}

		private static void BeginShuttle(ZNetPeer peer, Vector3 pos, float chestSq, ShuttleState state)
		{
			List<Container> sources = NearbyPlayerChests(pos, chestSq);
			if (sources.Count == 0) return;

			GameObject prefab = ZNetScene.instance.GetPrefab("piece_chest_private");
			if (!prefab) return;
			int hash = prefab.name.GetStableHashCode();
			Vector3 park = pos;
			park.y = -1000f;
			ZDO zdo = ZDOMan.instance.CreateNewZDO(park, hash);
			zdo.SetPrefab(hash);
			zdo.SetRotation(Quaternion.identity);
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Persistent = false;
			zdo.Set("HasFields", true);
			zdo.Set("HasFieldsContainer", true);
			zdo.Set("Container.m_width", 8);
			zdo.Set("Container.m_height", 8);

			GameObject go = ZNetScene.instance.CreateObject(zdo);
			Container staging = go ? go.GetComponent<Container>() : null;
			if (staging == null)
			{
				ZDOMan.instance.DestroyZDO(zdo);
				return;
			}

			Inventory stagingInv = staging.GetInventory();
			int moved = 0;
			int maxItems = Math.Max(8, Configuration.qolCraftMaxItems.Value);
			state.Sources.Clear();

			for (int s = 0; s < sources.Count && moved < maxItems; s++)
			{
				Container chest = sources[s];
				if (!chest || !chest.m_nview || !chest.m_nview.IsValid()) continue;
				if (chest.IsInUse()) continue;
				Inventory inv = chest.GetInventory();
				if (inv == null || inv.NrOfItems() == 0) continue;

				ZDO chestZdo = chest.m_nview.GetZDO();
				if (chestZdo.GetOwner() != ZDOMan.GetSessionID())
					chestZdo.SetOwner(ZDOMan.GetSessionID());

				List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(inv.GetAllItems());
				for (int i = items.Count - 1; i >= 0 && moved < maxItems; i--)
				{
					ItemDrop.ItemData item = items[i];
					if (item == null) continue;
					ItemDrop.ItemData clone = item.Clone();
					if (!stagingInv.AddItem(clone)) continue;
					inv.RemoveItem(item);
					moved++;
					state.Sources.Add(new LentItem
					{
						Name = item.m_shared != null ? item.m_shared.m_name : "",
						Prefab = item.m_dropPrefab ? item.m_dropPrefab.name : "",
						Stack = clone.m_stack,
						ChestId = chestZdo.m_uid
					});
				}
				SaveContainer(chest);
			}

			if (moved == 0)
			{
				ZNetScene.instance.Destroy(go);
				ZDOMan.instance.DestroyZDO(zdo);
				return;
			}

			SaveContainer(staging);
			zdo.SetOwner(peer.m_uid);
			ZDOMan.instance.ForceSendZDO(peer.m_uid, zdo.m_uid);
			try
			{
				staging.m_nview.InvokeRPC(peer.m_uid, "RPC_TakeAllResponse", true);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger?.LogWarning("CraftFromChests lend failed: " + e.GetType().Name);
			}

			state.Active = true;
			state.StagingId = zdo.m_uid;
			Notify(peer, "Crafting materials from nearby chests (" + moved + ")");
		}

		private static void EndShuttle(ZNetPeer peer, ShuttleState state)
		{
			if (state == null || !state.Active) return;
			state.Active = false;

			ZDO stagingZdo = state.StagingId.IsNone() ? null : ZDOMan.instance.GetZDO(state.StagingId);
			Container staging = null;
			if (stagingZdo != null && stagingZdo.IsValid())
			{
				ZNetView view = ZNetScene.instance.FindInstance(stagingZdo);
				if (view == null)
					view = ZNetScene.instance.CreateObject(stagingZdo)?.GetComponent<ZNetView>();
				staging = view ? view.GetComponent<Container>() : null;
			}

			if (staging != null && peer != null)
			{
				Inventory inv = staging.GetInventory();
				inv?.RemoveAll();
				// Seed one of each lent type so StackAll pulls matching leftovers from the player.
				var seeded = new HashSet<string>();
				for (int i = 0; i < state.Sources.Count; i++)
				{
					LentItem lent = state.Sources[i];
					if (string.IsNullOrEmpty(lent.Prefab) || !seeded.Add(lent.Prefab)) continue;
					GameObject dropPrefab = ZNetScene.instance.GetPrefab(lent.Prefab);
					ItemDrop drop = dropPrefab ? dropPrefab.GetComponent<ItemDrop>() : null;
					if (drop == null || drop.m_itemData == null) continue;
					ItemDrop.ItemData seed = drop.m_itemData.Clone();
					seed.m_stack = 1;
					seed.m_dropPrefab = dropPrefab;
					inv?.AddItem(seed);
				}
				SaveContainer(staging);
				stagingZdo.SetOwner(peer.m_uid);
				ZDOMan.instance.ForceSendZDO(peer.m_uid, stagingZdo.m_uid);
				try
				{
					staging.m_nview.InvokeRPC(peer.m_uid, "RPC_StackResponse", true);
				}
				catch (Exception) { }

				// After a short delay the reclaim chest holds leftovers — push back to sources.
				// Best-effort immediate redistribute from staging inventory after stack.
				Redistribute(staging, state);
				try
				{
					ZNetScene.instance.Destroy(staging.gameObject);
					ZDOMan.instance.DestroyZDO(stagingZdo);
				}
				catch (Exception) { }
				Notify(peer, "Returned unused materials to chests");
			}

			state.Sources.Clear();
			state.StagingId = ZDOID.None;
		}

		private static void Redistribute(Container staging, ShuttleState state)
		{
			Inventory inv = staging.GetInventory();
			if (inv == null || inv.NrOfItems() == 0) return;
			List<ItemDrop.ItemData> left = new List<ItemDrop.ItemData>(inv.GetAllItems());
			for (int i = 0; i < left.Count; i++)
			{
				ItemDrop.ItemData item = left[i];
				Container dest = null;
				for (int s = 0; s < state.Sources.Count; s++)
				{
					if (state.Sources[s].Name != (item.m_shared != null ? item.m_shared.m_name : "")) continue;
					ZDO zdo = ZDOMan.instance.GetZDO(state.Sources[s].ChestId);
					if (zdo == null || !zdo.IsValid()) continue;
					ZNetView view = ZNetScene.instance.FindInstance(zdo);
					Container c = view ? view.GetComponent<Container>() : null;
					if (c != null) { dest = c; break; }
				}
				if (dest == null)
				{
					List<Container> near = NearbyPlayerChests(
						ZNet.instance.GetPeer(state.PeerId)?.GetRefPos() ?? Vector3.zero,
						Configuration.qolCraftChestRange.Value * Configuration.qolCraftChestRange.Value);
					if (near.Count > 0) dest = near[0];
				}
				if (dest == null) continue;
				if (dest.m_nview.GetZDO().GetOwner() != ZDOMan.GetSessionID())
					dest.m_nview.GetZDO().SetOwner(ZDOMan.GetSessionID());
				if (dest.GetInventory().AddItem(item.Clone()))
					inv.RemoveItem(item);
				SaveContainer(dest);
			}
			SaveContainer(staging);
		}

		private static List<Container> NearbyPlayerChests(Vector3 pos, float rangeSq)
		{
			var result = new List<Container>();
			Container[] all = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
			for (int i = 0; i < all.Length; i++)
			{
				Container c = all[i];
				if (!c || !c.m_nview || !c.m_nview.IsValid()) continue;
				ZDO zdo = c.m_nview.GetZDO();
				if (zdo.GetLong(ZDOVars.s_creator, 0L) == 0L)
				{
					Piece piece = c.GetComponent<Piece>();
					if (piece == null || piece.GetCreator() == 0L) continue;
				}
				// Skip backpacks / staging (under world).
				if (zdo.GetPosition().y < -500f) continue;
				if ((c.transform.position - pos).sqrMagnitude > rangeSq) continue;
				if (c.GetInventory() == null) continue;
				result.Add(c);
			}
			return result;
		}

		private static bool NearStation(Vector3 pos, float rangeSq)
		{
			List<CraftingStation> stations = AllStations();
			if (stations == null) return false;
			for (int i = 0; i < stations.Count; i++)
			{
				CraftingStation s = stations[i];
				if (s && s.transform && (s.transform.position - pos).sqrMagnitude <= rangeSq)
					return true;
			}
			return false;
		}

		private static List<CraftingStation> AllStations()
		{
			if (stationsField == null)
				stationsField = AccessTools.Field(typeof(CraftingStation), "m_allStations");
			return stationsField?.GetValue(null) as List<CraftingStation>;
		}

		private static void SaveContainer(Container c)
		{
			if (!c || !c.m_nview || !c.m_nview.IsValid()) return;
			try
			{
				if (saveMethod == null) saveMethod = AccessTools.Method(typeof(Container), "Save");
				saveMethod?.Invoke(c, null);
			}
			catch (Exception) { }
		}

		private static void Notify(ZNetPeer peer, string text)
		{
			if (peer == null || string.IsNullOrEmpty(text)) return;
			try
			{
				if (ZRoutedRpc.instance != null)
					ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.TopLeft, "Northwatch: " + text);
			}
			catch (Exception) { }
		}

		private sealed class ShuttleState
		{
			internal long PeerId;
			internal bool Active;
			internal ZDOID StagingId = ZDOID.None;
			internal readonly List<LentItem> Sources = new List<LentItem>();
		}

		private struct LentItem
		{
			internal string Name;
			internal string Prefab;
			internal int Stack;
			internal ZDOID ChestId;
		}
	}
}
