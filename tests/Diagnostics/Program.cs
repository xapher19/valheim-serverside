using System;
using System.Linq;
using System.Reflection;
using Valheim_Serverside;
using Valheim_Serverside.Features;
using PluginConfiguration;
using UnityEngine;
static class Program {
 static int checks;
 static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
 static void Main() {
  DiagnosticRuntime.Installed = true;
  var a = new SustainedSignal();
  Check(a.Sample(true,0,60,30,300)==null,"grace warning");
  Check(a.Sample(true,60,60,30,300)==null,"early warning");
  Check(a.Sample(true,89,60,30,300)==null,"duration ignored");
  Check(a.Sample(true,90,60,30,300)=="sustained","missing sustained warning");
  Check(a.Sample(true,120,60,30,300)==null,"cooldown ignored");
  Check(a.Sample(false,130,60,30,300)=="recovered","missing recovery");
  Check(a.Sample(false,131,60,30,300)==null,"repeated recovery");
  a.Sample(true,140,60,30,300);
  Check(a.Sample(true,171,60,30,300)==null,"cooldown lost after recovery");
  Check(a.Sample(true,390,60,30,300)=="sustained","repeat warning missing");
  var m=new SendMetrics(0);
  m.Record(1,0,false,false); m.Record(2,50000,true,false); m.Record(3,0,false,true); m.Record(5,0,false,true);
  Check(m.Attempts==4 && m.Empty==1 && m.Blocked==1 && m.Submitted==2,"classification");
  Check(m.MaxAttemptGap==2 && m.MaxSubmittedGap==2,"gaps");
  m.Record(6,50000,true,true);
  Check(m.Submitted==3 && m.Blocked==1,"submitted override");
  var peer=new ZNetPeer { m_uid=1 }; ZNet.instance.Peers.Add(peer);
  Time.realtimeSinceStartupAsDouble=0; DiagnosticRuntime.Tick();
  DiagnosticRuntime.Record(peer,0,false,false);
  string first=DiagnosticRuntime.Status();
  Check(first.Contains("attempts 1, submitted packets 0"),"status counters");
  Check(first.Contains("quarter of in-flight bytes"),"PlayFab label");
  Check(first==DiagnosticRuntime.Status(),"status reset or changed counters");
  Time.realtimeSinceStartupAsDouble=1; peer.m_socket.Connected=false; DiagnosticRuntime.Tick();
  Check(!DiagnosticRuntime.Status().Contains("connection totals"),"disconnected state retained");
  peer=new ZNetPeer {m_uid=1}; ZNet.instance.Peers.Clear(); ZNet.instance.Peers.Add(peer);
  DiagnosticRuntime.Record(peer,0,false,true);
  Check(DiagnosticRuntime.Status().Contains("attempts 1, submitted packets 1"),"reconnect not reset");
  Configuration.diagnosticReportMinutes.Value=0; Configuration.diagnosticAlerts.Value=true;
  for(int t=2;t<=92;t++) { Time.realtimeSinceStartupAsDouble=t; DiagnosticRuntime.Record(peer,50000,true,false); }
  Check(ServersidePlugin.logger.Messages.Any(s=>s.Contains("sustained send queue pressure")),"alerts depend on summaries");
  DiagnosticRuntime.Record(peer,0,false,false);
  Check(ServersidePlugin.logger.Messages.Any(s=>s.Contains("recovered from send queue pressure")),"queue recovery");
  Check(!ServersidePlugin.logger.Messages.Any(s=>s.Contains("observation failed")),"unsupported getter or observation fault");
  var hook=typeof(Diagnostics.SendObservation); var prefix=hook.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
  object[] args={new ZDOMan.ZDOPeer {m_peer=peer},true,null}; prefix.Invoke(null,args);
  Check(!((Diagnostics.SendObservation.Sample)args[2]).valid,"flush counted");
  args[1]=false; peer.m_socket.Queue=9000; DiagnosticRuntime.NetworkingPatched=false; prefix.Invoke(null,args);
  Check(((Diagnostics.SendObservation.Sample)args[2]).blocked,"vanilla budget ignored");
  DiagnosticRuntime.NetworkingPatched=true; prefix.Invoke(null,args);
  Check(!((Diagnostics.SendObservation.Sample)args[2]).blocked,"configured budget ignored");
  ZNet.instance.Saving=true; Check(DiagnosticRuntime.Status().Contains("save in progress"),"save state");
  ZNet.instance.Saving=false; ZNet.instance.SaveDoneTime=10; Check(DiagnosticRuntime.Status().Contains("write success not verified"),"false save success");
  Check(DiagnosticRuntime.Clean("a\nb\rc")=="abc","log controls");
  Configuration.interactionDiagnostics.Value=true;
  var rpc=typeof(Diagnostics.InteractionObservation).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
  object[] input={new ZNetView(),new ZRoutedRpc.RoutedRPCData {m_methodHash=123,m_senderPeerID=1},null};
  rpc.Invoke(null,input); Check(((Diagnostics.InteractionObservation.Sample)input[2]).log,"missing RPC not observed");
  rpc.Invoke(null,input); Check(!((Diagnostics.InteractionObservation.Sample)input[2]).log,"RPC rate limit");
  Configuration.interactionDiagnostics.Value=false; Time.realtimeSinceStartupAsDouble=100; rpc.Invoke(null,input);
  Check(!((Diagnostics.InteractionObservation.Sample)input[2]).log,"disabled RPC trace");
  Time.realtimeSinceStartupAsDouble=102; Configuration.interactionDiagnostics.Value=true;
  var view = new ZNetView(); int open="RPC_RequestOpen".GetStableHashCode(); view.m_functions[open]=new object();
  input=new object[]{view,new ZRoutedRpc.RoutedRPCData {m_methodHash=open,m_senderPeerID=1},null}; rpc.Invoke(null,input);
  Check(((Diagnostics.InteractionObservation.Sample)input[2]).description.Contains("RPC_RequestOpen, handler registered"),"registered chest request not traced");
  DiagnosticRuntime.HookState(typeof(Diagnostics.SendObservation), "ACTIVE");
  Check(DiagnosticRuntime.Status().Contains("REGISTRATION MISSING"),"lost registration not reported");
  DiagnosticRuntime.HookState(typeof(Diagnostics.SendObservation), "FAILED");
  Check(DiagnosticRuntime.Status().Contains("SendObservation=FAILED"),"failed hook hidden");
  Configuration.diagnosticsEnabled.Value=false;
  Check(DiagnosticRuntime.Status().Contains("recent FPS unavailable"),"disabled metrics presented as current");
  Configuration.diagnosticsEnabled.Value=true;
  // Replace the connection without waiting for maintenance: identity must reset counters.
  var replacement = new ZNetPeer {m_uid=1}; ZNet.instance.Peers.Clear(); ZNet.instance.Peers.Add(replacement);
  DiagnosticRuntime.Record(replacement,0,false,true);
  Check(DiagnosticRuntime.Status().Contains("attempts 1, submitted packets 1"),"same UID replacement retained old counters");
  for(int id=2;id<=130;id++) { var extra=new ZNetPeer {m_uid=id}; ZNet.instance.Peers.Add(extra); DiagnosticRuntime.Record(extra,0,false,false); }
  Check(DiagnosticRuntime.Status().Split("connection totals").Length-1==128,"peer tracking unbounded");
  ZNet.instance.Peers.Clear(); Time.realtimeSinceStartupAsDouble=105; DiagnosticRuntime.Tick();
  Check(!DiagnosticRuntime.Status().Contains("connection totals"),"departed connections retained");
  var interrupted=new SustainedSignal(); interrupted.Sample(true,1,0,30,300); interrupted.Sample(false,20,0,30,300);
  Check(interrupted.Sample(true,32,0,30,300)==null,"interruption did not reset duration");
  Console.WriteLine($"Passed {checks} diagnostics assertions.");
 }
}
