using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	// Optional dedicated-server farming retunes. Does not add cultivator recipes or client UI.
	public class Farming : IFeature
	{
		public bool FeatureEnabled() => Configuration.farmingEnabled.Value;

		[HarmonyPatch(typeof(ZNetScene), "Awake")]
		public static class PrefabOverrides
		{
			static void Postfix(ZNetScene __instance) => FarmingSupport.ApplyPrefabOverrides(__instance);
		}

		[HarmonyPatch(typeof(Plant), "HaveRoof")]
		public static class PlantHaveRoof
		{
			static bool Prefix(ref bool __result)
			{
				if (!FarmingSupport.RelaxPlantRestrictions) return true;
				if (Configuration.farmingPlaceAnywhere.Value || !Configuration.farmingRequireSunlight.Value)
				{
					__result = false;
					return false;
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(Plant), "HaveGrowSpace")]
		public static class PlantHaveGrowSpace
		{
			static bool Prefix(ref bool __result)
			{
				if (!FarmingSupport.RelaxPlantRestrictions) return true;
				if (Configuration.farmingPlaceAnywhere.Value || !Configuration.farmingRequireGrowthSpace.Value)
				{
					__result = true;
					return false;
				}
				return true;
			}
		}
	}
}
