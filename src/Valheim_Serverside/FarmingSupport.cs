using System;
using System.Collections.Generic;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	// Server-only farming helpers. Independently implemented — no PlantEverything source is
	// included. Vanilla clients plant bushes and other pickable flora by dropping harvest items
	// on cultivated ground; this never adds cultivator recipes or client UI.
	internal static class FarmingSupport
	{
		internal static readonly string[] DefaultFloraPrefabs =
		{
			"RaspberryBush", "BlueberryBush", "CloudberryBush", "LingonberryBush",
			"Pickable_Mushroom", "Pickable_Mushroom_yellow", "Pickable_Mushroom_blue",
			"Pickable_Thistle", "Pickable_Dandelion", "Pickable_SmokePuff", "Pickable_Fiddlehead",
		};

		internal static readonly string[][] DefaultItemRecipes =
		{
			new[] { "Raspberry", "RaspberryBush" },
			new[] { "Blueberries", "BlueberryBush" },
			new[] { "Cloudberry", "CloudberryBush" },
			new[] { "Lingonberries", "LingonberryBush" },
			new[] { "Mushroom", "Pickable_Mushroom" },
			new[] { "MushroomYellow", "Pickable_Mushroom_yellow" },
			new[] { "MushroomBlue", "Pickable_Mushroom_blue" },
			new[] { "Thistle", "Pickable_Thistle" },
			new[] { "Dandelion", "Pickable_Dandelion" },
			new[] { "MushroomSmokePuff", "Pickable_SmokePuff" },
			new[] { "Fiddleheadfern", "Pickable_Fiddlehead" },
		};

		private static ZNetScene recipeScene;
		private static Dictionary<int, int> itemToFlora;
		private static Dictionary<int, string> floraNames;

		internal static HashSet<int> ParsePrefabHashes(string configured, IEnumerable<string> defaults)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (string name in defaults) names.Add(name);
			if (!string.IsNullOrWhiteSpace(configured))
				foreach (string part in configured.Split(','))
				{
					string trimmed = part.Trim();
					if (trimmed.Length > 0) names.Add(trimmed);
				}
			var hashes = new HashSet<int>();
			foreach (string name in names) hashes.Add(name.GetStableHashCode());
			return hashes;
		}

		internal static bool HasCreator(ZDO zdo) => zdo != null && zdo.GetLong(ZDOVars.s_creator, 0L) != 0L;

		internal static bool RelaxPlantRestrictions => Configuration.farmingEnabled.Value
			&& (Configuration.farmingPlaceAnywhere.Value
				|| !Configuration.farmingRequireSunlight.Value
				|| !Configuration.farmingRequireGrowthSpace.Value);

		internal static float ScaledGrowRadius(float original)
		{
			float scale = Configuration.farmingGrowSpaceScale.Value;
			if (scale < 0.05f) scale = 0.05f;
			if (scale > 1f) scale = 1f;
			return original * scale;
		}

		internal static void EnsureRecipes(ZNetScene scene)
		{
			if (scene == recipeScene && itemToFlora != null) return;
			recipeScene = scene;
			itemToFlora = new Dictionary<int, int>();
			floraNames = new Dictionary<int, string>();
			if (scene == null || scene.m_namedPrefabs == null) return;
			foreach (string[] recipe in DefaultItemRecipes)
			{
				int floraHash = recipe[1].GetStableHashCode();
				if (!scene.m_namedPrefabs.ContainsKey(floraHash)) continue;
				itemToFlora[recipe[0].GetStableHashCode()] = floraHash;
				floraNames[floraHash] = recipe[1];
			}
		}

		internal static bool IsPlantableItem(int prefabHash) => itemToFlora != null && itemToFlora.ContainsKey(prefabHash);

		internal static bool IsCultivatedGround(Vector3 pos)
		{
			if (Configuration.farmingPlaceAnywhere.Value) return true;
			Heightmap heightmap = Heightmap.FindHeightmap(pos);
			return heightmap && heightmap.IsCleared(pos);
		}

		internal static long CreatorAt(Vector3 pos, out ZNetPeer peer)
		{
			peer = null;
			if (ZNet.instance == null) return 0L;
			float best = float.MaxValue;
			foreach (ZNetPeer candidate in ZNet.instance.GetPeers())
			{
				if (candidate == null || !candidate.IsReady()) continue;
				Vector3 at = candidate.GetRefPos();
				float dx = at.x - pos.x, dz = at.z - pos.z;
				float d = dx * dx + dz * dz;
				if (d >= best) continue;
				best = d;
				peer = candidate;
			}
			if (peer == null) return 0L;
			ZDO character = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
			long id = character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
			return id != 0L ? id : peer.m_uid;
		}

		internal static int TryPlantFromDrop(ZDO drop, int stack, bool cultivated, long creator, out string message)
		{
			message = null;
			EnsureRecipes(ZNetScene.instance);
			if (drop == null || !drop.IsValid() || itemToFlora == null) return 0;
			if (!itemToFlora.TryGetValue(drop.GetPrefab(), out int floraHash)) return 0;
			floraNames.TryGetValue(floraHash, out string floraName);
			if (string.IsNullOrEmpty(floraName)) floraName = "flora";
			int cost = Math.Max(1, Configuration.farmingItemPlantCost.Value);
			if (stack < cost) return 0;
			if (creator == 0L) return 0;
			if (!cultivated && !Configuration.farmingPlaceAnywhere.Value)
			{
				message = "Need cultivated ground to plant " + floraName + ".";
				return 0;
			}

			int wanted = stack / cost;
			float spacing = Configuration.farmingItemPlantSpacing.Value;
			Vector3 origin = drop.GetPosition();
			var placedAt = new List<Vector3>();
			int planted = 0;
			int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(wanted)));
			int rows = Math.Max(1, (int)Math.Ceiling(wanted / (float)cols));
			for (int i = 0; i < wanted; i++)
			{
				int row = i / cols, col = i % cols;
				Vector3 want = origin + new Vector3((col - (cols - 1) / 2f) * spacing, 0f, (row - (rows - 1) / 2f) * spacing);
				if (!TryFindPlot(want, floraHash, spacing, placedAt, out Vector3 plot))
					break;
				ZDO flora = PlaceFlora(plot, floraHash, creator);
				if (flora == null) break;
				placedAt.Add(plot);
				ProductionAreas.Observe(flora);
				planted++;
			}
			if (planted == 0)
			{
				message = TooClose(origin, floraHash, spacing, null)
					? "Too close to another " + floraName + "."
					: "Could not plant " + floraName + ".";
				return 0;
			}

			ConsumeDrop(drop, stack - planted * cost);
			message = planted == 1
				? "Planted " + floraName + "."
				: "Planted " + planted + " " + floraName + ".";
			if (stack - planted * cost >= cost)
				message += " Need more cultivated ground for the rest.";
			return planted;
		}

		private static bool TryFindPlot(Vector3 want, int floraHash, float spacing, List<Vector3> used, out Vector3 plot)
		{
			if (PlotFits(want, floraHash, spacing, used))
			{
				plot = want;
				return true;
			}
			float step = Math.Max(0.5f, spacing * 0.5f);
			for (int ring = 1; ring <= 6; ring++)
			{
				for (int x = -ring; x <= ring; x++)
					for (int z = -ring; z <= ring; z++)
					{
						if (Math.Abs(x) != ring && Math.Abs(z) != ring) continue;
						Vector3 candidate = want + new Vector3(x * step, 0f, z * step);
						if (!PlotFits(candidate, floraHash, spacing, used)) continue;
						plot = candidate;
						return true;
					}
			}
			plot = want;
			return false;
		}

		private static bool PlotFits(Vector3 pos, int floraHash, float spacing, List<Vector3> used)
		{
			if (!IsCultivatedGround(pos) && !Configuration.farmingPlaceAnywhere.Value) return false;
			return !TooClose(pos, floraHash, spacing, used);
		}

		private static void ConsumeDrop(ZDO drop, int remaining)
		{
			if (drop == null || !drop.IsValid() || ZDOMan.instance == null) return;
			drop.SetOwner(ZDOMan.GetSessionID());
			int keep = Math.Max(0, remaining);
			if (ZNetScene.instance)
			{
				ZNetView view = ZNetScene.instance.FindInstance(drop);
				ItemDrop item = view ? view.GetComponent<ItemDrop>() : null;
				if (item != null) item.SetStack(keep);
			}
			drop.Set("stack", keep);
			drop.Set("stack".GetStableHashCode(), keep);
			if (ZNet.instance != null)
			{
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					if (peer == null || !peer.IsReady()) continue;
					ZDOMan.instance.ForceSendZDO(peer.m_uid, drop.m_uid);
				}
			}
			if (keep <= 0) ZDOMan.instance.DestroyZDO(drop);
		}

		private static bool TooClose(Vector3 pos, int floraHash, float spacing, List<Vector3> used)
		{
			if (spacing <= 0f) return false;
			float limit = spacing * spacing;
			if (used != null)
			{
				for (int i = 0; i < used.Count; i++)
				{
					Vector3 at = used[i];
					float ux = at.x - pos.x, uz = at.z - pos.z;
					if (ux * ux + uz * uz < limit) return true;
				}
			}
			if (ZDOMan.instance == null) return false;
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return false;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					ZDO zdo = bucket[i];
					if (zdo == null || !zdo.IsValid() || zdo.GetPrefab() != floraHash) continue;
					Vector3 at = zdo.GetPosition();
					float dx = at.x - pos.x, dz = at.z - pos.z;
					if (dx * dx + dz * dz < limit) return true;
				}
			}
			return false;
		}

		private static ZDO PlaceFlora(Vector3 pos, int floraHash, long creator)
		{
			if (ZDOMan.instance == null || !ZNetScene.instance) return null;
			ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, floraHash);
			zdo.SetPrefab(floraHash);
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set(ZDOVars.s_creator, creator);
			ZNetScene.instance.CreateObject(zdo);
			return zdo;
		}

		internal static void ApplyPrefabOverrides(ZNetScene scene)
		{
			if (!Configuration.farmingEnabled.Value || scene == null) return;
			int floraRespawn = Configuration.farmingFloraRespawnMinutes.Value;
			float growMin = Configuration.farmingCropGrowTimeMin.Value;
			float growMax = Configuration.farmingCropGrowTimeMax.Value;
			bool placeAnywhere = Configuration.farmingPlaceAnywhere.Value;
			var flora = ParsePrefabHashes(Configuration.farmingExtraFlora.Value, DefaultFloraPrefabs);
			int pickables = 0, plants = 0;
			foreach (var pair in scene.m_namedPrefabs)
			{
				GameObject prefab = pair.Value;
				if (!prefab) continue;
				Pickable pickable = prefab.GetComponent<Pickable>();
				if (pickable && floraRespawn > 0 && flora.Contains(pair.Key))
				{
					pickable.m_respawnTimeMinutes = floraRespawn;
					pickables++;
				}
				Plant plant = prefab.GetComponent<Plant>();
				if (plant == null) continue;
				if (growMin > 0f)
				{
					plant.m_growTime = growMin;
					plant.m_growTimeMax = growMax > growMin ? growMax : growMin;
					plants++;
				}
				if (placeAnywhere)
				{
					plant.m_destroyIfCantGrow = false;
					Piece piece = prefab.GetComponent<Piece>();
					if (piece != null)
					{
						piece.m_groundOnly = false;
						piece.m_groundPiece = false;
					}
				}
			}
			ServersidePlugin.logger.LogInfo(
				$"Farming: applied server overrides ({pickables} flora respawn, {plants} plant grow times; place-anywhere={placeAnywhere}). Vanilla clients plant by dropping harvest items; no cultivator recipes added.");
		}
	}
}
