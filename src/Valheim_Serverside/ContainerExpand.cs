using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	/// <summary>
	/// Add ExtraRows to player-built container heights via ZNetView HasFields so vanilla
	/// clients recreate with a taller inventory. Dungeon/world chests (no creator) are skipped.
	/// </summary>
	internal static class ContainerExpand
	{
		internal const string MarkerKey = "nw_extra_rows";
		private static readonly int MarkerHash = MarkerKey.GetStableHashCode();
		private static MethodInfo saveMethod;
		private static double nextScan;
		private static int expanded;

		internal static bool Enabled =>
			QoLRuntime.Installed && Configuration.qolEnabled.Value && Configuration.qolChestExtraRows.Value > 0;

		internal static string Status => !Enabled ? "" : $"chest +{Configuration.qolChestExtraRows.Value}row x{expanded}";

		internal static void Tick()
		{
			if (!Enabled || ZNetScene.instance == null || ZDOMan.instance == null) return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextScan) return;
			nextScan = now + 5;
			// Catch chests that loaded before the feature was on / missed Awake postfix.
			Container[] all = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None);
			int budget = 8;
			for (int i = 0; i < all.Length && budget > 0; i++)
			{
				if (TryExpand(all[i], recreate: true)) budget--;
			}
		}

		internal static bool TryExpand(Container container, bool recreate)
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

			// Persist so clients LoadFields() get the taller size on Awake.
			zdo.Set("HasFields", true);
			zdo.Set("HasFieldsContainer", true);
			zdo.Set("Container.m_width", width);
			zdo.Set("Container.m_height", height);
			zdo.Set(MarkerHash, extra);

			if (container.m_width == width && container.m_height == height)
			{
				expanded++;
				return false;
			}

			if (!recreate) return false;

			// Refuse to shrink over a full chest — only grow.
			Inventory inv = container.GetInventory();
			if (inv != null && inv.NrOfItems() > width * height) return false;

			try
			{
				if (view.IsOwner())
				{
					if (saveMethod == null) saveMethod = AccessTools.Method(typeof(Container), "Save");
					saveMethod?.Invoke(container, null);
				}
				else
				{
					zdo.SetOwner(ZDOMan.GetSessionID());
					if (saveMethod == null) saveMethod = AccessTools.Method(typeof(Container), "Save");
					saveMethod?.Invoke(container, null);
				}
			}
			catch (Exception) { }

			container.m_width = width;
			container.m_height = height;
			try
			{
				ZNetScene.instance.Destroy(view.gameObject);
				ZNetScene.instance.CreateObject(zdo);
				expanded++;
				return true;
			}
			catch (Exception e)
			{
				ServersidePlugin.logger?.LogWarning("ContainerExpand recreate failed: " + e.GetType().Name);
				return false;
			}
		}
	}
}
