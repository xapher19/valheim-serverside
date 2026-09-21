using System;
using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
    public class Diagnostics : IFeature
    {
        public bool FeatureEnabled() => Configuration.diagnosticsEnabled.Value;

        [HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
        public static class SendObservation
        {
            public struct Sample { public bool valid; public int queue; public bool blocked; }
            static void Prefix(ZDOMan.ZDOPeer __0, bool __1, out Sample __state)
            {
                __state = default(Sample);
                if (__1) return; // Shutdown flush loops are not scheduled update opportunities.
                try
                {
                    if (__0?.m_peer?.m_socket == null) return;
                    int queued = __0.m_peer.m_socket.GetSendQueueSize();
                    int limit = DiagnosticRuntime.NetworkingPatched ? Networking.QueueSizeFor(__0) : 10240;
                    __state = new Sample { valid = true, queue = queued, blocked = limit - queued < 2048 };
                }
                catch (Exception e) { DiagnosticRuntime.Fault(e); }
            }
            static void Postfix(ZDOMan.ZDOPeer __0, bool __result, Sample __state)
            {
                if (!__state.valid) return;
                try { DiagnosticRuntime.Record(__0.m_peer, __state.queue, __state.blocked, __result); }
                catch (Exception e) { DiagnosticRuntime.Fault(e); }
            }
        }

        [HarmonyPatch(typeof(ZNetView), "HandleRoutedRPC")]
        public static class InteractionObservation
        {
            public struct Sample { public bool log; public long start; public string description; }
            static void Prefix(ZNetView __instance, ZRoutedRpc.RoutedRPCData __0, out Sample __state)
            {
                __state = default(Sample);
                if (!Configuration.interactionDiagnostics.Value) return;
                try
                {
                    bool known = __instance.m_functions.ContainsKey(__0.m_methodHash);
                    int hash = __0.m_methodHash;
                    bool interaction = hash == "RPC_RequestOpen".GetStableHashCode() || hash == "RPC_RequestOwn".GetStableHashCode() || hash == "RPC_Pick".GetStableHashCode();
                    if ((!known || interaction) && DiagnosticRuntime.AllowInteraction())
                    {
                        var peer = ZNet.instance.GetPeer(__0.m_senderPeerID);
                        var zdo = __instance.GetZDO();
                        string method = hash == "RPC_RequestOpen".GetStableHashCode() ? "RPC_RequestOpen" : hash == "RPC_RequestOwn".GetStableHashCode() ? "RPC_RequestOwn" : hash == "RPC_Pick".GetStableHashCode() ? "RPC_Pick" : hash.ToString();
                        __state = new Sample { log = true, start = System.Diagnostics.Stopwatch.GetTimestamp(),
                            description = $"RPC received: {method}, handler {(known ? "registered" : "MISSING")}, sender {__0.m_senderPeerID}, transport {peer?.m_socket?.GetType().Name ?? "local/unknown"}, object {zdo?.m_uid.ToString() ?? "none"}, prefab {DiagnosticRuntime.Clean(__instance.gameObject.name)}, owner {zdo?.GetOwner().ToString() ?? "none"}" };
                        ServersidePlugin.logger.LogInfo(__state.description);
                        __state.start = System.Diagnostics.Stopwatch.GetTimestamp();
                    }
                }
                catch (Exception e) { DiagnosticRuntime.Fault(e); }
            }
            static void Postfix(Sample __state)
            {
                if (!__state.log) return;
                try { ServersidePlugin.logger.LogInfo($"{__state.description}; dispatch returned after {1000.0 * (System.Diagnostics.Stopwatch.GetTimestamp() - __state.start) / System.Diagnostics.Stopwatch.Frequency:0.00} ms (server dispatch only; not client response time)"); }
                catch (Exception e) { DiagnosticRuntime.Fault(e); }
            }
        }
    }
}
