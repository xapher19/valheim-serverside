using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
namespace FeaturesLib { public interface IFeature { bool FeatureEnabled(); } }
namespace HarmonyLib {
 public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string n) {} }
 public class Patch { public string owner; public MethodInfo PatchMethod; }
 public class Info { public List<Patch> Transpilers = new(), Prefixes = new(), Postfixes = new(), Finalizers = new(); public List<string> Owners = new(); }
 public static class AccessTools { public static MethodInfo Method(Type t, string n) => t.GetMethod(n); }
 public class Harmony {
  public static Info GetPatchInfo(MethodBase m) => new();
  public static IEnumerable<MethodBase> GetAllPatchedMethods() => Array.Empty<MethodBase>();
 }
}
namespace UnityEngine {
 public static class Time { public static double realtimeSinceStartupAsDouble; public static float realtimeSinceStartup => (float)realtimeSinceStartupAsDouble; }
 public static class Application { public static int targetFrameRate = 60; }
 public class GameObject { public string name = "Chest"; }
}
namespace PluginConfiguration {
 public class Setting<T> { public T Value; public Setting(T v) { Value = v; } }
 public static class Configuration {
  public static Setting<bool> diagnosticsEnabled = new(true), diagnosticAlerts = new(true), interactionDiagnostics = new(false);
  public static Setting<int> diagnosticReportMinutes = new(5), alertDurationSeconds = new(30), alertCooldownSeconds = new(300), lowFpsThreshold = new(25), serverTargetFps = new(60);
 }
}
namespace Valheim_Serverside {
internal static class ProductionAreas { internal static string Status => "production inactive"; }
internal static class PortalHub { internal static string Status => "portal hub inactive"; }
internal static class ServerFeedback { internal static string Status => "no save result observed"; }
 public class Logger { public List<string> Messages = new(); public void LogInfo(object m) => Messages.Add(m.ToString()); public void LogWarning(object m) => Messages.Add(m.ToString()); }
 public static class ServersidePlugin { public const string PluginGUID = "test", PluginName = "Northwatch Dedicated Simulation", PluginVersion = "1.9.4"; public static Logger logger = new(); }
}
namespace Valheim_Serverside.Features { public static class Networking { public static int QueueSize() => 48*1024; } }
public class Socket { public int Queue; public bool Connected = true; public bool IsConnected() => Connected; public int GetSendQueueSize() => Queue; }
public class ZPlayFabSocket : Socket { public int GetCurrentSendRate() => throw new Exception("Unsupported getter called"); }
public class ZNetPeer { public long m_uid; public bool m_server; public string m_playerName = "Tester"; public Socket m_socket = new ZPlayFabSocket(); public bool Ready = true; public bool IsReady() => Ready; }
public class ZNet {
 public static ZNet instance = new(); public List<ZNetPeer> Peers = new(); public float SaveDoneTime; public bool Saving, Server = true;
 public bool IsServer() => Server; public bool IsSaving() => Saving; public List<ZNetPeer> GetPeers() => Peers;
 public ZNetPeer GetPeer(long uid) => Peers.FirstOrDefault(p => p.m_uid == uid);
 public static implicit operator bool(ZNet n) => n != null;
}
public class ZDO { public long m_uid = 42; public long GetOwner() => 5; }
public class ZDOMan { public class ZDOPeer { public ZNetPeer m_peer; } public void SendZDOs() {} }
public class ZNetView { public Dictionary<int,object> m_functions = new(); public UnityEngine.GameObject gameObject = new(); public ZDO GetZDO() => new(); }
public class ZRoutedRpc { public class RoutedRPCData { public int m_methodHash; public long m_senderPeerID; } }
public static class Hash { public static int GetStableHashCode(this string s) { unchecked { int h=17; foreach (char c in s) h=h*31+c; return h; } } }
