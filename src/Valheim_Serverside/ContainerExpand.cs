using System;
using System.Collections.Generic;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	/// <summary>
	/// Add ExtraRows to player-built container heights via ZNetView HasFields so vanilla
	/// clients recreate with a taller inventory. Dungeon/world chests (no creator) are skipped.
	///
	/// NEVER call ZNetScene.Destroy here — that DestroyZDO's owned objects and permanently
	/// deletes player chests (1.11.1–1.11.4 bug).
	/// </summary>
	internal static class ContainerExpand
	{
		internal const string MarkerKey = "nw_extra_rows";
		private static readonly int MarkerHash = MarkerKey.GetStableHashCode();
		private static readonly HashSet<ZDOID> checkedIds = new HashSet<ZDOID>();
		private static double nextScan;
		private static int expanded;
		private static bool scanExhausted;

		internal static bool Enabled =>
			QoLRuntime.Installed && Configuration.qolEnabled.Value && Configuration.qolChestExtraRows.Value > 0;

		internal static string Status => !Enabled ? "" : $"chest +{Configuration.qolChestExtraRows.Value}row x{expanded}";

		internal static void Tick()
		{
			if (!Enabled || scanExhausted || ZNetScene.instance == null || ZDOMan.instance == null) return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextScan) return;
			nextScan = now + 15;
			Container[] all = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
			int budget = 8;
			int pending = 0;
			for (int i = 0; i < all.Length; i++)
			{
				Container c = all[i];
				if (!c || !c.m_nview || !c.m_nview.IsValid()) continue;
				ZDOID id = c.m_nview.GetZDO().m_uid;
				if (checkedIds.Contains(id)) continue;
				pending++;
				if (budget <= 0) continue;
				checkedIds.Add(id);
				TryExpand(c);
				budget--;
			}
			if (pending == 0) scanExhausted = true;
		}

		internal static bool TryExpand(Container container)
		{
			if (!Enabled || !container) return false;
			ZNetView view = container.m_nview;
			if (!view || !view.IsValid()) return false;
			ZDO zdo = view.GetZDO();
			if (zdo == null || !zdo.IsValid()) return false;

			// Only player-built pieces (creator != 0). Skip dungeon loot chests.
			long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
			if (creator == 0L)
			{
				Piece piece = container.GetComponent<Piece>();
				if (piece != null) creator = piece.GetCreator();
			}
			if (creator == 0L) return false;

			int extra = Math.Max(0, Configuration.qolChestExtraRows.Value);
			if (extra <= 0) return false;

			GameObject prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
			Container baseContainer = prefab ? prefab.GetComponent<Container>() : null;
			if (baseContainer == null) return false;

			int width = baseContainer.m_width;
			int height = baseContainer.m_height + extra;
			int applied = zdo.GetInt(MarkerHash, 0);
			if (applied == extra && container.m_width == width && container.m_height == height)
				return false;

			// Persist so clients LoadFields() get the taller size on Awake — in place only.
			zdo.Set("HasFields", true);
			zdo.Set("HasFieldsContainer", true);
			zdo.Set("Container.m_width", width);
			zdo.Set("Container.m_height", height);
			zdo.Set(MarkerHash, extra);

			container.m_width = width;
			container.m_height = height;
			Inventory inv = container.GetInventory();
			if (inv != null && inv.GetHeight() < height)
				inv.SetHeight(height);

			expanded++;
			return true;
		}
	}
}
