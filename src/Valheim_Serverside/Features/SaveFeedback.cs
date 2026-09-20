using System;
using FeaturesLib;
using HarmonyLib;

namespace Valheim_Serverside.Features
{
    public class SaveFeedback : IFeature
    {
        public bool FeatureEnabled() => true;
        [HarmonyPatch(typeof(ZNet), "SaveWorld")]
        public static class SaveStarted
        {
            static void Prefix() => ServerFeedback.Begin();
        }
        [HarmonyPatch(typeof(ZNet), "SaveWorldThread")]
        public static class SaveThreadScope
        {
            static void Prefix() => ServerFeedback.ThreadBegin();
            static Exception Finalizer(Exception __exception) { ServerFeedback.ThreadEnd(__exception); return __exception; }
        }
        [HarmonyPatch(typeof(SaveSystem), "EndSave")]
        public static class SaveResult
        {
            static void Postfix(bool __1) => ServerFeedback.End(__1);
        }
    }
}
