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

		internal static bool TryPlantFromDrop(ZDO drop, int stack, bool cultivated, long creator, out string message)
		{
			message = null;
			EnsureRecipes(ZNetScene.instance);
			if (drop == null || !drop.IsValid() || itemToFlora == null) return false;
			if (!itemToFlora.TryGetValue(drop.GetPrefab(), out int floraHash)) return false;
			floraNames.TryGetValue(floraHash, out string floraName);
			if (string.IsNullOrEmpty(floraName)) floraName = "flora";
			int cost = Math.Max(1, Configuration.farmingItemPlantCost.Value);
			if (stack < cost) return false;
			if (creator == 0L) return false;
			if (!cultivated && !Configuration.farmingPlaceAnywhere.Value)
			{
				message = "Need cultivated ground to plant " + floraName + ".";
				return false;
			}
			if (TooClose(drop.GetPosition(), floraHash, Configuration.farmingItemPlantSpacing.Value))
			{
				message = "Too close to another " + floraName + ".";
				return false;
			}
			ZDO flora = PlaceFlora(drop.GetPosition(), floraHash, creator);
			if (flora == null)
			{
				message = "Could not plant " + floraName + ".";
				return false;
			}
			int remaining = stack - cost;
			if (remaining <= 0) ZDOMan.instance.DestroyZDO(drop);
			else drop.Set("stack", remaining);
			ProductionAreas.Observe(flora);
			message = "Planted " + floraName + ".";
			return true;
		}

		private static bool TooClose(Vector3 pos, int floraHash, float spacing)
		{
			if (ZDOMan.instance == null || spacing <= 0f) return false;
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return false;
			float limit = spacing * spacing;
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
