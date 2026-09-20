using System;
using System.Threading;
using PluginConfiguration;

namespace Valheim_Serverside
{
    internal static class ServerFeedback
    {
        internal static bool Installed;
        [ThreadStatic] internal static bool InWorldSave;
        [ThreadStatic] private static bool resultObserved;
        private static int pending;
        private static string result = "no save result observed";
        internal static string Status => Installed ? result : "save result observation unavailable";

        internal static void Announce(string text)
        {
            if (!Configuration.saveAnnouncements.Value || ZRoutedRpc.instance == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)MessageHud.MessageType.TopLeft, "Northwatch: " + text); }
            catch (Exception e) { ServersidePlugin.logger.LogWarning("Announcement could not be delivered: " + e.GetType().Name); }
        }
        internal static void Begin()
        {
            result = "in progress";
            Announce("Saving world…");
        }
        internal static void ThreadBegin() { InWorldSave = true; resultObserved = false; }
        internal static void End(bool success)
        {
            if (!InWorldSave) return;
            resultObserved = true;
            Interlocked.Exchange(ref pending, success ? 1 : 2);
        }
        internal static void ThreadEnd(Exception error)
        {
            if (error != null || !resultObserved) Interlocked.Exchange(ref pending, error != null ? 2 : 3);
            InWorldSave = false;
        }
        internal static void Tick()
        {
            if (!Installed) return;
            int outcome = Interlocked.Exchange(ref pending, 0);
            if (outcome == 0) return;
            result = outcome == 1 ? "game reported save commit success (not an independent disk verification)" : outcome == 2 ? "FAILED" : "finished without a commit result; success unconfirmed";
            if (outcome == 1) ServersidePlugin.logger.LogInfo("World save: " + result);
            else ServersidePlugin.logger.LogWarning("World save: " + result);
            Announce(outcome == 1 ? "World saved." : "World save failed or could not be confirmed. Check the server log.");
        }
    }
}
