using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	// Optional grow/respawn retunes, plus Harmony install when item planting is on.
	// Vanilla clients plant flora by dropping harvest items; no cultivator recipes.
	public class Farming : IFeature
	{
		public bool FeatureEnabled() => Configuration.farmingEnabled.Value || Configuration.farmingItemPlanting.Value
			|| Configuration.farmingGrowSpaceScale.Value < 0.999f;

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
			static bool Prefix(Plant __instance, ref bool __result, out float __state)
			{
				__state = __instance ? __instance.m_growRadius : 0f;
				if (Configuration.farmingPlaceAnywhere.Value
					|| (Configuration.farmingEnabled.Value && !Configuration.farmingRequireGrowthSpace.Value))
				{
					__result = true;
					return false;
				}
				if (__instance) __instance.m_growRadius = FarmingSupport.ScaledGrowRadius(__state);
				return true;
			}

			static void Postfix(Plant __instance, float __state)
			{
				if (__instance) __instance.m_growRadius = __state;
			}
		}
	}
}
