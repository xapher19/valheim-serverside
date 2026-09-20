using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	// Independent portal-hub for dedicated servers. Inspired by the public behaviour of
	// ArgusMagnus ServersideQoL AutoPortalHub (pair unpaired portal tags via a generated hub)
	// but does not include or depend on that mod's source. Remove AutoPortalHub / ServersideQoL
	// portal packages when using this; running both will fight over hub objects.
	internal static class PortalHub
	{
		internal static bool Installed;
		private static readonly int HubMarker = "nw_portal_hub".GetStableHashCode();
		private static readonly List<ZDOID> hubObjects = new List<ZDOID>();
		private static readonly HashSet<int> portalPrefabs = new HashSet<int>();
		private static string lastSignature = "";
		private static double nextScan;
		private static Regex includeRegex, excludeRegex;
		private static Vector3 hubOrigin;
		private static bool originReady;
		internal static bool Enabled => Installed && Configuration.portalHubEnabled.Value;
		internal static string Status => !Enabled ? "portal hub inactive"
			: $"portal hub {hubObjects.Count} pieces; last plan [{lastSignature}]";

		internal static void Tick()
		{
			if (!Enabled || ZDOMan.instance == null || !ZNetScene.instance || !ZNet.instance || !ZNet.instance.IsServer())
				return;
			if (!ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated) return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextScan) return;
			nextScan = now + 2;
			EnsureOrigin();
			RebuildFilters();
			RefreshPortalPrefabIndex();
			Reconcile();
		}

		private static void EnsureOrigin()
		{
			if (originReady) return;
			float edge = 10500f;
			if (WorldGenerator.instance != null) edge = WorldGenerator.waterEdge;
			hubOrigin = new Vector3(edge + 5f * 64f, 2000f, 0f);
			originReady = true;
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
			if (includeRegex != null && !includeRegex.IsMatch(tag ?? "")) return false;
			if (excludeRegex != null && excludeRegex.IsMatch(tag ?? "")) return false;
			return true;
		}

		private static bool IsHubObject(ZDO zdo) => zdo != null && zdo.GetLong(HubMarker, 0L) != 0L;

		private static void RefreshPortalPrefabIndex()
		{
			portalPrefabs.Clear();
			foreach (var pair in ZNetScene.instance.m_namedPrefabs)
				if (pair.Value && pair.Value.GetComponent<TeleportWorld>())
					portalPrefabs.Add(pair.Key);
		}

		private static List<ZDO> WorldPortals()
		{
			var list = new List<ZDO>();
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return list;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					ZDO zdo = bucket[i];
					if (zdo == null || !zdo.IsValid() || IsHubObject(zdo)) continue;
					if (portalPrefabs.Contains(zdo.GetPrefab())) list.Add(zdo);
				}
			}
			return list;
		}

		private static void MaybeAutoName(ZDO zdo)
		{
			if (!Configuration.portalHubAutoName.Value) return;
			string tag = zdo.GetString(ZDOVars.s_tag, "");
			if (!string.IsNullOrEmpty(tag)) return;
			Heightmap.Biome biome = WorldGenerator.instance != null
				? WorldGenerator.instance.GetBiome(zdo.GetPosition())
				: Heightmap.Biome.None;
			string biomeName = biome == Heightmap.Biome.None ? "Portal" : biome.ToString();
			var used = new HashSet<string>(WorldPortals().Select(p => p.GetString(ZDOVars.s_tag, "")));
			for (int i = 1; i <= 1000; i++)
			{
				string candidate = string.Format(Configuration.portalHubAutoNameFormat.Value, biomeName, i);
				if (used.Contains(candidate)) continue;
				zdo.Set(ZDOVars.s_tag, candidate);
				return;
			}
		}

		private static void Reconcile()
		{
			var portals = WorldPortals();
			foreach (ZDO zdo in portals) MaybeAutoName(zdo);

			var byTag = new Dictionary<string, List<ZDO>>(StringComparer.Ordinal);
			foreach (ZDO zdo in portals)
			{
				string tag = zdo.GetString(ZDOVars.s_tag, "") ?? "";
				if (!AllowedTag(tag)) continue;
				if (!byTag.TryGetValue(tag, out var group)) byTag[tag] = group = new List<ZDO>();
				group.Add(zdo);
			}

			var unpaired = byTag.Where(kv => kv.Value.Count % 2 != 0).Select(kv => kv.Key).OrderBy(t => t, StringComparer.Ordinal).ToList();
			string signature = string.Join("|", unpaired);
			if (signature == lastSignature && (unpaired.Count == 0 ? hubObjects.Count == 0 : hubObjects.Count > 0))
				return;

			ClearHub();
			lastSignature = signature;
			if (unpaired.Count == 0)
			{
				Connect();
				return;
			}

			BuildHub(unpaired);
			Connect();
			ServersidePlugin.logger.LogInfo($"Portal hub: paired {unpaired.Count} unpaired tag(s)");
		}

		private static void ClearHub()
		{
			foreach (ZDOID id in hubObjects)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid()) ZDOMan.instance.DestroyZDO(zdo);
			}
			hubObjects.Clear();
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return;
			var orphans = new List<ZDO>();
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				foreach (ZDO zdo in bucket)
					if (zdo != null && zdo.IsValid() && IsHubObject(zdo)) orphans.Add(zdo);
			}
			foreach (ZDO zdo in orphans) ZDOMan.instance.DestroyZDO(zdo);
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
				portal.Set(ZDOVars.s_tag, tags[i]);
				ZDO sign = PlacePrefab("sign", pos + new Vector3(0f, 2f, -0.6f), Quaternion.identity);
				if (sign != null) sign.Set(ZDOVars.s_text, "<color=white>" + tags[i]);
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
			ZNetScene.instance.CreateObject(zdo);
			hubObjects.Add(zdo.m_uid);
			return zdo;
		}

		private static void Connect()
		{
			if (Game.instance) Game.instance.ConnectPortals();
		}
	}
}
