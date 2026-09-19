using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
    internal static class DiagnosticRuntime
    {
        private sealed class Peer
        {
            internal ZNetPeer Connection;
            internal SendMetrics Metrics;
            internal string Name, Transport;
        }
        private static readonly Dictionary<Type, string> hookStates = new Dictionary<Type, string>();
        internal static void HookState(Type hook, string state) { hookStates[hook] = state; }
        internal static void Rollback() { foreach (Type hook in hookStates.Keys.ToArray()) if (hookStates[hook] == "ACTIVE" || hookStates[hook] == "PENDING") hookStates[hook] = "ROLLED BACK / NOT APPLIED"; }
        private static readonly Dictionary<long, Peer> peers = new Dictionary<long, Peer>();
        private static readonly SustainedSignal lowFps = new SustainedSignal();
        private static double started = -1, windowStart, nextMaintenance, nextReport, interactionNext;
        private static long frames;
        private static double measuredFps = -1;
        private static int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        private static bool faultLogged;
        internal static bool NetworkingPatched;
        internal static bool Installed;
        internal static void Initialize()
        {
            var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(ZDOMan), "SendZDOs"));
            NetworkingPatched = info != null && info.Transpilers.Any(p => p.owner == ServersidePlugin.PluginGUID + ".Networking");
        }

        internal static void Fault(Exception e)
        {
            if (faultLogged) return;
            faultLogged = true;
            ServersidePlugin.logger.LogWarning("Diagnostics observation failed; gameplay continues: " + e.GetType().Name);
        }
        internal static string Clean(string value) => new string((value ?? "unknown").Where(c => !char.IsControl(c)).Take(64).ToArray());
        internal static bool AllowInteraction()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < interactionNext) return false;
            interactionNext = now + 2; // Global bound, including unknown RPC hashes; no unbounded hash cache.
            return true;
        }
        internal static void Record(ZNetPeer connection, int queued, bool blocked, bool submitted)
        {
            if (!Configuration.diagnosticsEnabled.Value || !ZNet.instance || !ZNet.instance.IsServer() || !connection.IsReady() || connection.m_server) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!peers.TryGetValue(connection.m_uid, out Peer peer) || !ReferenceEquals(peer.Connection, connection))
            {
                if (peers.Count >= 128 && !peers.ContainsKey(connection.m_uid)) return;
                peer = new Peer { Connection = connection, Metrics = new SendMetrics(now), Name = Clean(connection.m_playerName), Transport = connection.m_socket.GetType().Name };
                peers[connection.m_uid] = peer;
            }
            // A long observation gap is not evidence of continuously blocked sends.
            if (peer.Metrics.LastAttempt >= 0 && now - peer.Metrics.LastAttempt > 2)
                peer.Metrics.Pressure.Sample(false, now, 0, 0, 0);
            peer.Metrics.Record(now, queued, blocked, submitted);
            if (Configuration.diagnosticAlerts.Value)
            {
                string signal = peer.Metrics.Pressure.Sample(blocked && !submitted, now, peer.Metrics.Started + 60,
                    Configuration.alertDurationSeconds.Value, Configuration.alertCooldownSeconds.Value);
                Alert(signal, $"send queue pressure for {peer.Name} ({peer.Transport})");
            }
            else peer.Metrics.Pressure.Sample(false, now, 0, 0, 0);
        }
        private static void Alert(string state, string description)
        {
            if (state == "sustained") ServersidePlugin.logger.LogWarning("Diagnostics: sustained " + description);
            if (state == "recovered") ServersidePlugin.logger.LogInfo("Diagnostics: recovered from " + description);
        }
        internal static void Tick()
        {
            if (!Configuration.diagnosticsEnabled.Value) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (started < 0) { started = windowStart = now; nextReport = now + Math.Max(1, Configuration.diagnosticReportMinutes.Value) * 60; }
                frames++;
                if (now - windowStart >= 5)
                {
                    measuredFps = frames / (now - windowStart);
                    frames = 0;
                    windowStart = now;
                    bool playing = ZNet.instance && ZNet.instance.GetPeers().Any(p => p.IsReady());
                    string signal = lowFps.Sample(Configuration.diagnosticAlerts.Value && playing && measuredFps < Configuration.lowFpsThreshold.Value,
                        now, started + 60, Configuration.alertDurationSeconds.Value, Configuration.alertCooldownSeconds.Value);
                    if (Configuration.diagnosticAlerts.Value) Alert(signal, $"low FPS ({measuredFps:0.0}, threshold {Configuration.lowFpsThreshold.Value})");
                }
                if (now >= nextMaintenance)
                {
                    nextMaintenance = now + 1;
                    foreach (long uid in peers.Where(p => !ZNet.instance || !ReferenceEquals(ZNet.instance.GetPeer(p.Key), p.Value.Connection) || !p.Value.Connection.IsReady() || !p.Value.Connection.m_socket.IsConnected()).Select(p => p.Key).ToArray()) peers.Remove(uid);
                }
                int minutes = Configuration.diagnosticReportMinutes.Value;
                if (minutes <= 0) nextReport = now + 60;
                else if (now >= nextReport)
                {
                    ServersidePlugin.logger.LogInfo("Diagnostics: " + FrameStatus() + "; " + Memory());
                    foreach (Peer peer in peers.Values) ServersidePlugin.logger.LogInfo(PeerSummary(peer, now));
                    nextReport = now + minutes * 60;
                }
            }
            catch (Exception e) { Fault(e); }
        }
        private static string PeerSummary(Peer peer, double now)
        {
            SendMetrics s = peer.Metrics;
            return $"Network {peer.Name} [{peer.Transport}], connection totals over {now - s.Started:0}s: attempts {s.Attempts}, submitted packets {s.Submitted}, blocked without submission {s.Blocked}, other no-submission {s.Empty}; peak queue-budget metric {s.PeakQueue / 1024.0:0.0} KiB; max attempt gap {s.MaxAttemptGap * 1000:0} ms, max submission gap {s.MaxSubmittedGap * 1000:0} ms (idle gaps possible; not delivery latency)"
                + (peer.Transport == "ZPlayFabSocket" ? "; PlayFab budget metric = quarter of in-flight bytes; Steam rate settings do not apply" : "");
        }
        private static string FrameStatus() => !Configuration.diagnosticsEnabled.Value ? "recent FPS unavailable (diagnostics disabled)" : $"recent FPS {(measuredFps < 0 ? "warming up" : measuredFps.ToString("0.0") + " (last completed ~5s window)")}; configured target {Configuration.serverTargetFps.Value}, effective cap {Application.targetFrameRate}";
        private static string Memory()
        {
            string working;
            try { using (Process process = Process.GetCurrentProcess()) working = (process.WorkingSet64 / 1048576.0).ToString("0.0") + " MiB"; }
            catch { working = "unavailable"; }
            int a = GC.CollectionCount(0), b = GC.CollectionCount(1), c = GC.CollectionCount(2);
            string result = $"managed heap {GC.GetTotalMemory(false) / 1048576.0:0.0} MiB, working set {working}, GC since previous memory report [{a - gc0}, {b - gc1}, {c - gc2}]";
            gc0 = a; gc1 = b; gc2 = c;
            return result;
        }
        internal static string Status()
        {
            try
            {
                var patches = Harmony.GetAllPatchedMethods().Select(Harmony.GetPatchInfo).Where(p => p != null)
                    .SelectMany(p => p.Prefixes.Concat(p.Postfixes).Concat(p.Transpilers).Concat(p.Finalizers))
                    .Where(p => p.owner.StartsWith(ServersidePlugin.PluginGUID + ".", StringComparison.Ordinal)).Select(p => p.PatchMethod).ToList();
                string health = string.Join("; ", hookStates.Select(entry =>
                {
                    string state = entry.Value;
                    if (state == "ACTIVE")
                    {
                        var expected = entry.Key.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                            .Where(m => new[] { "Prefix", "Postfix", "Transpiler", "Finalizer" }.Contains(m.Name)).ToArray();
                        if (expected.Length == 0 || expected.Any(m => !patches.Contains(m))) state = "REGISTRATION MISSING";
                    }
                    return entry.Key.DeclaringType.Name + "." + entry.Key.Name + "=" + state;
                }));
                string save = ZNet.instance ? (ZNet.instance.IsSaving() ? "in progress" : ZNet.instance.SaveDoneTime > 0 ? "last completion observed " + (Time.realtimeSinceStartup - ZNet.instance.SaveDoneTime).ToString("0") + "s ago (write success not verified)" : "no completion observed") : "world unavailable";
                string result = $"{ServersidePlugin.PluginName} {ServersidePlugin.PluginVersion}; simulation {(Installed ? "installed" : "inactive/rolled back")}; diagnostics {(Configuration.diagnosticsEnabled.Value ? "enabled" : "disabled")}; players {(ZNet.instance ? ZNet.instance.GetPeers().Count : 0)}; {FrameStatus()}; save {save}; {health}";
                foreach (Peer peer in peers.Values) result += "\n" + PeerSummary(peer, Time.realtimeSinceStartupAsDouble);
                return result;
            }
            catch (Exception e) { return "Status unavailable: " + e.GetType().Name; }
        }
    }
}
