// Minimal stand-ins for the game/Unity host. These tests do not simulate gameplay or detours.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if FAKE_HARMONY
namespace HarmonyLib
{
    public class Harmony : Attribute
    {
        public string Id;
        public Harmony() { }
        public Harmony(string id) { Id = id; }
        internal static readonly Dictionary<MethodBase, Patches> Registry = new();
        public IEnumerable<MethodBase> GetPatchedMethods() => Registry.Where(x => x.Value.Prefixes.Any(p => p.owner == Id)).Select(x => x.Key).ToArray();
        public static Patches GetPatchInfo(MethodBase method) => Registry.TryGetValue(method, out var p) ? p : null;
        public void UnpatchSelf() { foreach (var p in Registry.Values) p.Prefixes.RemoveAll(x => x.owner == Id); }
    }
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
    public class Patch { public string owner; public MethodInfo PatchMethod; }
    public class Patches
    {
        public List<Patch> Prefixes = new(), Postfixes = new(), Transpilers = new(), Finalizers = new();
    }
    public class PatchClassProcessor
    {
        public static Type Fail, Skip, Partial;
        readonly Harmony harmony;
        readonly Type type;
        public PatchClassProcessor(Harmony h, Type t) { harmony = h; type = t; }
        public List<MethodInfo> Patch()
        {
            if (type == Skip) return null;
            var methods = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => new[] { "Prefix", "Postfix", "Transpiler", "Finalizer" }.Contains(m.Name)).ToArray();
            foreach (var m in methods)
            {
                if (type == Partial && m.Name == "Postfix") continue;
                if (!Harmony.Registry.TryGetValue(m, out var p)) Harmony.Registry[m] = p = new Patches();
                p.Prefixes.Add(new Patch { owner = harmony.Id, PatchMethod = m });
            }
            // Fail after installing something to exercise rollback of partial application.
            if (type == Fail) throw new InvalidOperationException("Injected patch failure");
            return new List<MethodInfo>(); // Real Harmony returns replacements, not registry keys.
        }
    }
}
#endif
namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public readonly List<string> Messages = new();
        public void LogInfo(object s) => Messages.Add(s.ToString());
        public void LogWarning(object s) => Messages.Add(s.ToString());
        public void LogError(object s) => Messages.Add(s.ToString());
    }
}
namespace BepInEx
{
    public class BaseUnityPlugin { public BepInEx.Logging.ManualLogSource Logger = new(); public object Config; }
    public class BepInPlugin : Attribute { public BepInPlugin(string a, string b, string c) { } }
    public class BepInDependency : Attribute
    {
        public enum DependencyFlags { SoftDependency }
        public BepInDependency(string s, DependencyFlags f) { }
    }
}
namespace Unity.Jobs.LowLevel.Unsafe { public static class JobsUtility { public static int JobWorkerCount; } }
namespace UnityEngine
{
    public static class Time { public static double realtimeSinceStartupAsDouble; public static float maximumDeltaTime, fixedDeltaTime = .02f; }
    public static class Mathf
    {
        public static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
    public static class Application
    {
        public static int Writes;
        static int fps;
        public static int targetFrameRate { get => fps; set { fps = value; Writes++; } }
    }
}
namespace PluginConfiguration
{
    public class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    public class Configuration
    {
        public static Setting<int> sendIntervalMs = new(100), serverTargetFps = new(60), unityJobWorkers = new(0), maxCatchUpMs = new(0);
        public static Setting<float> performanceStatsMinutes = new(5);
        public static Setting<bool> modEnabled = new(true), consoleCommandsEnabled = new(false), adminChatEnabled = new(false);
        public static void Load(object c) { }
    }
}
namespace Requirements
{
    public static class PatchRequirement
    {
        public class DebugBuild : PatchingLib.IPatchRequirement { public string Name => "DebugBuild"; public Func<bool> Checker => () => true; }
    }
}
namespace Valheim_Serverside
{
    public static class ServerConsole { public static void Start() { } public static void ProcessPending() { } }
    public static class VanillaDrift { public static void Check(object logger) { } }
}
namespace Valheim_Serverside.Features
{
    public class Core : FeaturesLib.IFeature
    {
        public bool FeatureEnabled() => true;
        public class First { static void Prefix() { } }
        public class Second { static void Prefix() { } }
    }
    public class MaxObjectsPerFrame : FeaturesLib.IFeature { public bool FeatureEnabled() => false; }
    public class Networking : MaxObjectsPerFrame { }
    public class AdminChat : MaxObjectsPerFrame { public static void Tick() { } }
    public class Fixes : MaxObjectsPerFrame { }
    public class Debugging : MaxObjectsPerFrame { }
    public class Compat_ValheimPlus : MaxObjectsPerFrame { }
}
public static class ZLog { public static void Log(string s) { } }
public class ZNet
{
    public static bool Dedicated = true;
    public static ZNet instance = new();
    public bool IsDedicated() => Dedicated;
    public static implicit operator bool(ZNet n) => n != null;
    public List<int> GetPeers() => new() { 1 };
}
public class ZDOMan
{
    public class ZDOPeer { }
    public List<ZDOPeer> m_peers = new();
    public void SendZDOs(ZDOPeer p, bool flush) { }
}
public class ZNetScene { }
public class MonoUpdaters { }
public class PresentManager { }
