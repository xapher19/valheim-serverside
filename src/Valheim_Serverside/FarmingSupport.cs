using System;
using System.Collections.Generic;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	// Server-only farming helpers inspired by Advize PlantEverything's dedicated-server
	// behaviour (growth/respawn/restriction overrides and planted flora). Independently
	// implemented — no PlantEverything source is included. Clients still need a planting
	// mod such as PlantEverything to add cultivator recipes; this only keeps player-placed
	// flora simulated and optionally retunes server-side growth/respawn rules.
	internal static class FarmingSupport
	{
		internal static readonly string[] DefaultFloraPrefabs =
		{
			"RaspberryBush", "BlueberryBush", "CloudberryBush", "LingonberryBush",
			"Pickable_Mushroom", "Pickable_Mushroom_yellow", "Pickable_Mushroom_blue",
			"Pickable_Thistle", "Pickable_Dandelion", "Pickable_SmokePuff", "Pickable_Fiddlehead",
		};

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
				$"Farming: applied server overrides ({pickables} flora respawn, {plants} plant grow times; place-anywhere={placeAnywhere}). No cultivator recipes added.");
		}
	}
}
