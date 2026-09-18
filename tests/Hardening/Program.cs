using System;
using System.Linq;
using System.Reflection;
using FeaturesLib;
using HarmonyLib;
using PatchingLib;
using PluginConfiguration;
using UnityEngine;
using Valheim_Serverside;
using Valheim_Serverside.Features;

static class Program
{
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
#if FAKE_HARMONY
    static bool Install(params IFeature[] features)
    {
        var plugin = new ServersidePlugin();
        ServersidePlugin.logger = plugin.Logger;
        ServersidePlugin.harmony = new Harmony(ServersidePlugin.PluginGUID);
        return (bool)typeof(ServersidePlugin).GetMethod("PatchFeatures", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(plugin, new object[] { new AvailableFeatures(features.ToList()), new HarmonyFeaturesPatcher(new PatchRequirements()) });
    }
    static bool Active(Type t) => Harmony.Registry.Values.Any(p => p.Prefixes.Any(x => x.PatchMethod.DeclaringType == t));
    static void Reset()
    {
        Harmony.Registry.Clear();
        PatchClassProcessor.Fail = PatchClassProcessor.Skip = PatchClassProcessor.Partial = null;
    }
#endif
    static void Main()
    {
#if FAKE_HARMONY
        var broken = typeof(Performance.ZNet_SaveWorld_Timing);
        Reset();
        PatchClassProcessor.Fail = broken;
        Check(Install(new Core(), new Performance()), "Optional failure aborted installation");
        Check(!Active(broken), "Failed optional hook retained a partial patch");
        Check(Active(typeof(Performance.PresentManager_RequestTargetFrameRate_Patch)), "FPS hook lost");
        Check(Active(typeof(Performance.ZDOMan_SendZDOs_Timing)), "Send timing lost");
        Check(Active(typeof(Core.First)) && Active(typeof(Core.Second)), "Core lost");
        Check(ServersidePlugin.logger.Messages.Any(m => m.Contains("Core.First ACTIVE")), "Core health missing");
        Check(ServersidePlugin.logger.Messages.Any(m => m.Contains("ZNet_SaveWorld_Timing FAILED")), "Failure health missing");

        foreach (var hook in new[] { typeof(Performance.ZDOMan_SendZDOToPeers2_Patch), typeof(Performance.PresentManager_RequestTargetFrameRate_Patch) })
        {
            Reset();
            PatchClassProcessor.Fail = hook;
            Check(Install(new Core(), new Performance()), "Scheduler/FPS failure aborted installation");
            Check(!Active(hook) && Active(typeof(Performance.MonoUpdaters_FixedUpdate_Timing)), "Scheduler/FPS failure removed unrelated timing");
        }

        Reset();
        PatchClassProcessor.Partial = typeof(Performance.ZDOMan_SendZDOs_Timing);
        Check(Install(new Core(), new Performance()), "Partial optional registration aborted installation");
        Check(!Active(PatchClassProcessor.Partial), "Incomplete prefix/postfix pair retained");
        Check(!Performance.HookActive(PatchClassProcessor.Partial), "Missing measurement marked available");
        Configuration.performanceStatsMinutes.Value = .01f;
        Time.realtimeSinceStartupAsDouble = 0; PerformanceStats.Frame();
        Time.realtimeSinceStartupAsDouble = 1; PerformanceStats.Frame();
        Check(ServersidePlugin.logger.Messages.Any(m => m.Contains("world send timing unavailable")), "Missing timing shown as zero");

        Reset();
        PatchClassProcessor.Skip = typeof(Core.Second);
        Check(!Install(new Core(), new Performance()), "Skipped Core hook accepted");
        Check(!Active(typeof(Core.First)), "Skipped Core hook left partial Core installed");

        Reset();
        PatchClassProcessor.Fail = typeof(Core.Second);
        Check(!Install(new Performance(), new Core()), "Core failure accepted");
        Check(!Harmony.Registry.Values.Any(p => p.Prefixes.Count > 0), "Core rollback left optional patches installed");
        Check(!Performance.HookActive(typeof(Performance.ZDOMan_SendZDOs_Timing)), "Core rollback left stale hook health");
        var plugin = new ServersidePlugin();
        typeof(ServersidePlugin).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plugin, null);
        Application.targetFrameRate = 30;
        Time.realtimeSinceStartupAsDouble = 100;
        typeof(ServersidePlugin).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plugin, null);
        Check(Application.targetFrameRate == 30, "FPS fallback ran after Core failed");
#endif
        ServersidePlugin.logger = new BepInEx.Logging.ManualLogSource();
        var prefix = typeof(Performance.PresentManager_RequestTargetFrameRate_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
        Check(prefix.GetParameters()[0].Name == "__0", "FPS prefix must bind by argument index");
        Configuration.serverTargetFps.Value = 60;
        ZNet.Dedicated = true;
        object[] request = { 30 };
        prefix.Invoke(null, request);
        Check((int)request[0] == 60, "FPS prefix did not override the first argument");
        ZNet.Dedicated = false;
        request[0] = 30;
        prefix.Invoke(null, request);
        Check((int)request[0] == 30, "FPS prefix changed a client request");
        ZNet.Dedicated = true;
        Configuration.serverTargetFps.Value = 0;
        prefix.Invoke(null, request);
        Check((int)request[0] == 30, "Disabled FPS prefix changed the request");
        Configuration.serverTargetFps.Value = 60;
        ZNet.Dedicated = true;
        Application.targetFrameRate = 30;
        TargetFpsVerifier.Start(0);
        int writes = Application.Writes;
        TargetFpsVerifier.Tick(4);
        Check(Application.Writes == writes, "Fallback ran before startup grace period");
        TargetFpsVerifier.Tick(5);
        Check(Application.targetFrameRate == 60 && Application.Writes == writes + 1, "Fallback did not apply");
        TargetFpsVerifier.Tick(15);
        Check(ServersidePlugin.logger.Messages.Any(m => m.Contains("FPS verified")), "Fallback never verified");
        Application.targetFrameRate = 30;
        writes = Application.Writes;
        TargetFpsVerifier.Tick(25); TargetFpsVerifier.Tick(35);
        Check(Application.Writes == writes, "Fallback fought another override");
        Check(ServersidePlugin.logger.Messages.Count(m => m.Contains("FPS is not applied")) == 1, "Mismatch warning spammed or absent");
        Application.targetFrameRate = 60;
        TargetFpsVerifier.Tick(45);
        Check(ServersidePlugin.logger.Messages.Count(m => m.Contains("FPS verified")) == 2, "Recovery was not reported");
        Configuration.serverTargetFps.Value = 999;
        TargetFpsVerifier.Tick(55);
        Check(Application.targetFrameRate == 240, "Upper clamp failed");
        Configuration.serverTargetFps.Value = 1;
        TargetFpsVerifier.Tick(65);
        Check(Application.targetFrameRate == 30, "Lower clamp failed");
        Configuration.serverTargetFps.Value = 0;
        writes = Application.Writes;
        TargetFpsVerifier.Tick(75);
        Check(Application.Writes == writes, "Disabled FPS setting wrote a target");
        Configuration.serverTargetFps.Value = -1;
        TargetFpsVerifier.Tick(85);
        Check(Application.Writes == writes, "Negative FPS setting wrote a target");
        Configuration.serverTargetFps.Value = 60;
        ZNet.Dedicated = false;
        TargetFpsVerifier.Tick(95);
        Check(Application.Writes == writes, "Non-dedicated client target modified");
        Console.WriteLine($"Passed {checks} hardening assertions.");
    }
}
