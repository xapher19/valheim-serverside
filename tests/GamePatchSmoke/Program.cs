using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

// Runs on the Windows build host against the real downloaded game and built plugin.
// Installs detours but never invokes the game method (which needs the Unity runtime).
internal static class Program
{
    private static void BadPrefix(ref int value) { }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected server install and plugin DLL paths");
            var managed = Path.Combine(Path.GetFullPath(args[0]), "valheim_server_Data", "Managed");
            var core = Path.Combine(Path.GetFullPath(args[0]), "BepInEx", "core");
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            {
                string file = new AssemblyName(request.Name).Name + ".dll";
                foreach (string directory in new[] { managed, core })
                {
                    string path = Path.Combine(directory, file);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            var game = Assembly.LoadFrom(Path.Combine(managed, "assembly_valheim.dll"));
            var plugin = Assembly.LoadFrom(Path.GetFullPath(args[1]));
            var target = AccessTools.Method(game.GetType("PresentManager", true), "RequestTargetFrameRate");
            if (target == null) throw new Exception("FPS request method missing");
            Console.WriteLine("Actual game target: " + target + "; parameters: " + string.Join(", ", target.GetParameters().Select(p => p.Name)));
            var negative = new Harmony("xapher19.tests.fps-negative");
            bool rejected = false;
            try
            {
                negative.Patch(target, prefix: new HarmonyMethod(typeof(Program).GetMethod("BadPrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                if (!ex.ToString().Contains("value")) throw;
                rejected = true;
                Console.WriteLine("PASS: old named argument binding is rejected.");
            }
            finally { negative.UnpatchSelf(); }
            if (!rejected) throw new Exception("Negative control unexpectedly accepted the old value parameter");

            var owner = new Harmony("xapher19.tests.fps-real-game");
            var hook = plugin.GetType("Valheim_Serverside.Features.Performance+PresentManager_RequestTargetFrameRate_Patch", true);
            try
            {
                new PatchClassProcessor(owner, hook).Patch();
                var info = Harmony.GetPatchInfo(target);
                if (info == null || !info.Prefixes.Any(p => p.owner == owner.Id && p.PatchMethod.DeclaringType == hook))
                    throw new Exception("Built plugin FPS prefix not registered on real game target");
                Console.WriteLine("PASS: built plugin FPS hook installs on the real Valheim method.");
            }
            finally { owner.UnpatchSelf(); }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
