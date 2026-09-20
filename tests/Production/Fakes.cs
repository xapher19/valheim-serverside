using System;
using System.Collections.Generic;
using System.Linq;
namespace HarmonyLib { public class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string m) {} } }
namespace FeaturesLib { public interface IFeature { bool FeatureEnabled(); } }
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) => o != null; }
    public class GameObject : Object
    {
        public string name;
        public readonly Dictionary<Type,object> components = new();
        public T GetComponent<T>() where T:class => components.TryGetValue(typeof(T), out var v) ? v as T : null;
    }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c) {x=a;y=b;z=c;} }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
}
public struct Vector2s : IEquatable<Vector2s>
{
    public int x,y; public Vector2s(int a,int b) {x=a;y=b;}
    public bool Equals(Vector2s o)=> x==o.x && y==o.y;
    public override bool Equals(object o)=>o is Vector2s v && Equals(v);
    public override int GetHashCode()=>HashCode.Combine(x,y);
}
public struct ZDOID : IEquatable<ZDOID>
{
    public int id; public static implicit operator ZDOID(int n)=>new ZDOID{id=n};
    public bool Equals(ZDOID o)=>id==o.id;
    public override bool Equals(object o)=>o is ZDOID v && Equals(v);
    public override int GetHashCode()=>id;
}
public static class Hashes { public static int GetStableHashCode(this string s) { unchecked {int n=17;foreach(char c in s)n=n*31+c;return n;} } }
public class ZDO
{
    public ZDOID m_uid; public bool Persistent=true, valid=true; public int prefab; public long owner, creator; public bool tamed;
    public bool GetBool(int key) => tamed;
    public long GetLong(int key, long def=0) => key == ZDOVars.s_creator ? creator : def;
    public UnityEngine.Vector3 pos;
    public bool IsValid()=>valid;
    public int GetPrefab()=>prefab;
    public UnityEngine.Vector3 GetPosition()=>pos;
    public Vector2s GetSector()=>ZoneSystem.GetZone(pos);
    public long GetOwner()=>owner;
    public void SetOwner(long n)=>owner=n;
}
public class ZDOMan
{
    public static ZDOMan instance;
    public List<ZDO>[] m_objectsBySector = new List<ZDO>[4];
    public Dictionary<ZDOID,ZDO> all=new();
    public List<(long,ZDOID)> forced = new();
    public ZDO GetZDO(ZDOID id)=>all.TryGetValue(id,out var z)?z:null;
    public void FindSectorObjects(Vector2s zone,SimulationDistance d,List<ZDO> target)
    { foreach(var z in all.Values) if(z.GetSector().Equals(zone)) target.Add(z); }
    public bool IsInPeerActiveArea(UnityEngine.Vector3 p,long id)=>ZNet.instance.GetPeer(id)?.near ?? false;
    public void ForceSendZDO(long p,ZDOID id)=>forced.Add((p,id));
}
public class SimulationDistance { public SimulationDistance(int a,int b,bool c) {} }
public class ZNetPeer { public long m_uid; public ZDOID m_characterID; public bool near=true; }
public class ZNet : UnityEngine.Object
{
    public static ZNet instance; public double m_netTime; public bool server=true;
    public List<ZNetPeer> peers=new();
    public List<ZNetPeer> GetConnectedPeers()=>peers;
    public ZNetPeer GetPeer(long id)=>peers.Find(p=>p.m_uid==id);
    public static long GetUID()=>99;
    public bool IsServer()=>server;
    public int GetNrOfPlayers()=>peers.Count;
}
public class ZNetScene : UnityEngine.Object
{
    public static ZNetScene instance;
    public Dictionary<int,UnityEngine.GameObject> m_namedPrefabs=new();
}
public class ZoneSystem : UnityEngine.Object
{
    public static ZoneSystem instance; public bool LocationsGenerated=true;
    public class ZoneData {public float m_ttl;}
    public Dictionary<Vector2s,ZoneData> m_zones=new();
    public HashSet<Vector2s> ungenerated=new();
    public static Vector2s GetZone(UnityEngine.Vector3 p)=>new((int)Math.Floor((p.x+32)/64),(int)Math.Floor((p.z+32)/64));
    public bool IsZoneGenerated(Vector2s z)=>!ungenerated.Contains(z);
    public bool IsZoneLoaded(Vector2s z)=>m_zones.ContainsKey(z);
    public bool IsZoneLoaded(UnityEngine.Vector3 p)=>IsZoneLoaded(GetZone(p));
    public bool PokeLocalZone(Vector2s z) {if(m_zones.ContainsKey(z))return false;m_zones[z]=new();return true;}
}
public static class ZDOVars { public static int s_tamed=1; public static int s_creator=2; }
public class Tameable : UnityEngine.Object { public bool m_startsTamed; }
public class EggGrow : UnityEngine.Object { public bool m_tamed; }
public class Growup : UnityEngine.Object {}
public class Plant : UnityEngine.Object
{
    public UnityEngine.GameObject[] m_grownPrefabs=Array.Empty<UnityEngine.GameObject>();
    public float m_growTime, m_growTimeMax; public bool m_destroyIfCantGrow=true;
}
public class Piece : UnityEngine.Object { public bool m_groundOnly=true, m_groundPiece=true; }
public class Smelter : UnityEngine.Object {}
public class Fermenter : UnityEngine.Object {}
public class Beehive : UnityEngine.Object {}
public class CookingStation : UnityEngine.Object {}
public class SapCollector : UnityEngine.Object {}
public class Pickable : UnityEngine.Object { public int m_respawnTimeMinutes; }
public class RandomEvent {public float m_eventRange=100; public UnityEngine.Vector3 m_pos;}
public class SpawnSystem {public class SpawnData {}}
public class RandEventSystem {public RandomEvent m_activeEvent;}
public class ZNetView : UnityEngine.Object {public ZDO zdo;public bool IsValid()=>zdo!=null;public ZDO GetZDO()=>zdo;}
public class ItemDrop {public ZNetView m_nview;}
public class ZRoutedRpc
{
    public static ZRoutedRpc instance=new();public static long Everybody=0;public List<string> messages=new();
    public void InvokeRoutedRPC(long id,string m,params object[] args)=>messages.Add((string)args[1]);
}
public class MessageHud {public enum MessageType{TopLeft}}
namespace PluginConfiguration
{
    public class Entry<T> {public T Value;public Entry(T v){Value=v;}}
    public static class Configuration
    {
        public static Entry<bool> productionEnabled=new(true),productionLivestock=new(false),productionFlora=new(true), advanceEmptyTime=new(true),saveAnnouncements=new(true);
        public static Entry<bool> farmingEnabled=new(false),farmingPlaceAnywhere=new(false),farmingRequireSunlight=new(true),farmingRequireGrowthSpace=new(true);
        public static Entry<bool> portalHubEnabled=new(true), portalHubAutoName=new(false);
        public static Entry<int> productionScanBudget=new(2048), farmingFloraRespawnMinutes=new(0);
        public static Entry<float> farmingCropGrowTimeMin=new(0f), farmingCropGrowTimeMax=new(0f);
        public static Entry<string> productionExclude=new(""), farmingExtraFlora=new(""), portalHubInclude=new("*"), portalHubExclude=new(""), portalHubAutoNameFormat=new("{0} {1:D2}");
    }
}
namespace Valheim_Serverside
{
    public class Logger {public List<string> messages=new();public void LogInfo(string s)=>messages.Add(s);public void LogWarning(string s)=>messages.Add(s);}
    public static class ServersidePlugin {public static Logger logger=new();}
}
