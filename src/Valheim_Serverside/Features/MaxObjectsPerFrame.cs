using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;


namespace Valheim_Serverside.Features
{
	public class MaxObjectsPerFrame : IFeature
	{
		public bool FeatureEnabled()
		{
			return Configuration.maxObjectsPerFrameEnabled.Value;
		}

		public static int GetMaxCreatedPerFrame()
		{
			int max = Math.Max(1, Configuration.maxObjectsPerFrame.Value);
            if (!Configuration.adaptiveLoading.Value) return max;
            return budget.Next(max, Configuration.loadingBudgetMs.Value, Time.unscaledDeltaTime, Math.Max(30, Application.targetFrameRate));
		}

        private static readonly CreationBudget budget = new CreationBudget();
        private static int currentAllowance = 10;

        // Vanilla raises the allowance for huge backlogs. Respect the configured/adaptive cap.
        public static int CapBacklog(int backlog, int allowance) => Math.Max(1, allowance);

        [HarmonyPatch(typeof(ZNetScene), "CreateObjectsSorted")]
        public static class BacklogCap
        {
            static void Prefix(ref int __1) { __1 = currentAllowance; }
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
            {
                var codes = new List<CodeInstruction>(source);
                MethodInfo max = AccessTools.Method(typeof(Mathf), "Max", new[] { typeof(int), typeof(int) });
                int found = 0;
                foreach (var code in codes)
                    if (code.Calls(max)) { code.operand = AccessTools.Method(typeof(MaxObjectsPerFrame), nameof(CapBacklog)); found++; }
                if (found != 1) throw new InvalidOperationException("Object backlog cap changed; expected one integer Max call.");
                return codes;
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateDistantObjects")]
        public static class DistantCap
        {
            static bool Prefix(ref int __1, int __2)
            {
                __1 = currentAllowance;
                if (__2 >= __1) return false;
                // Vanilla stops on > rather than >=; compensate for its extra object.
                __1 = Math.Max(0, __1 - 1);
                return true;
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) })]
        public static class CreationCost
        {
            static void Prefix(out long __state) { __state = Stopwatch.GetTimestamp(); }
            static void Postfix(long __state) { budget.Observe((Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency); }
        }

        [HarmonyPatch(typeof(ZNetScene), "CreateObjects")]
        public static class CreateObjects_Patch
        {
            static void Prefix() { currentAllowance = GetMaxCreatedPerFrame(); }
        }
    }
}
