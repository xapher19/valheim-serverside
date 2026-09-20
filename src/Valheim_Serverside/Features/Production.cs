using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
    // Installed as one verified, atomic feature: no production without the raid/clock guards.
    public class Production : IFeature
    {
        public bool FeatureEnabled() => Configuration.productionEnabled.Value;

        [HarmonyPatch(typeof(ZDO), "Deserialize")]
        public static class ReceivedAnchor
        {
            static void Postfix(ZDO __instance) => ProductionAreas.Observe(__instance);
        }
        [HarmonyPatch(typeof(ZDO), "SetPrefab")]
        public static class CreatedAnchor
        {
            static void Postfix(ZDO __instance) => ProductionAreas.Observe(__instance);
        }
        [HarmonyPatch(typeof(ZNet), "UpdateNetTime")]
        public static class EmptyWorldClock
        {
            static void Postfix(ZNet __instance, float __0)
            {
                if (ProductionAreas.Enabled && Configuration.advanceEmptyTime.Value && __instance.IsServer() && __instance.GetNrOfPlayers() == 0)
                    __instance.m_netTime += __0;
            }
        }
        [HarmonyPatch(typeof(RandEventSystem), "SetRandomEvent")]
        public static class RaidStartGuard
        {
            static bool Prefix(RandomEvent __0, Vector3 __1) => !ProductionAreas.Enabled || __0 == null || ProductionAreas.PlayerNear(__1, __0.m_eventRange);
        }
        [HarmonyPatch(typeof(RandEventSystem), "GetCurrentSpawners")]
        public static class RaidSpawnGuard
        {
            static bool Prefix(RandEventSystem __instance, ref System.Collections.Generic.List<SpawnSystem.SpawnData> __result)
            {
                if (!ProductionAreas.Enabled || __instance.m_activeEvent == null) return true;
                if (ProductionAreas.PlayerNear(__instance.m_activeEvent.m_pos, __instance.m_activeEvent.m_eventRange)) return true;
                __result = null;
                return false;
            }
        }
    }
}
