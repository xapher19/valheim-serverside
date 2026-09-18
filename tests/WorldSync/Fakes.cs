// Deliberately small game-host doubles. The real DLL and hooks are also built and
// installed against the downloaded game in GamePatchSmoke; these are policy tests.
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
    }
    public static class Time { public static float time; }
}
public struct ZDOID
{
    public int Value;
    public ZDOID(int value) { Value = value; }
    public bool IsNone() => Value == 0;
}
public class ZDO
{
    public ZDOID m_uid;
    public long Owner;
    public UnityEngine.Vector3 Position;
    public int InUse, OwnerWrites;
    public long GetOwner() => Owner;
    public UnityEngine.Vector3 GetPosition() => Position;
    public int GetInt(string key, int fallback) => InUse;
    public void SetOwner(long owner) { Owner = owner; OwnerWrites++; }
}
public class ZNetView
{
    public ZDO Zdo;
    public bool Valid = true;
    public static implicit operator bool(ZNetView view) => view != null;
    public bool IsValid() => Valid && Zdo != null;
    public ZDO GetZDO() => Zdo;
}
public class Player
{
    public long Owner;
    public static readonly Dictionary<long, Player> Players = new();
    public static Player GetPlayer(long id) => Players.TryGetValue(id, out var player) ? player : null;
    public long GetOwner() => Owner;
}
public class ShipControlls
{
    public long User;
    public bool HaveValidUser() => User != 0;
    public long GetUser() => User;
}
public class Ship
{
    public ZNetView m_nview;
    public ShipControlls m_shipControlls = new();
    public List<Player> m_players = new();
    public float m_lastWaterImpactTime = -20;
}
public class Socket
{
    public bool Connected = true;
    public bool IsConnected() => Connected;
}
public class ZRpc { }
public class ZNetPeer
{
    public long m_uid;
    public bool m_server;
    public ZDOID m_characterID;
    public Socket m_socket = new();
    public ZRpc m_rpc = new();
    public UnityEngine.Vector3 Position;
    public bool IsReady() => m_uid != 0;
    public UnityEngine.Vector3 GetRefPos() => Position;
}
public class ZNet
{
    public static ZNet instance;
    public bool Server = true;
    public List<ZNetPeer> Peers = new();
    public static implicit operator bool(ZNet net) => net != null;
    public bool IsServer() => Server;
    public static long GetUID() => 100;
    public ZNetPeer GetPeer(long uid) => Peers.FirstOrDefault(p => p.m_uid == uid);
    public List<ZNetPeer> GetPeers() => Peers;
}
public class ZDOMan
{
    public static ZDOMan instance;
    public class ZDOPeer
    {
        public ZNetPeer m_peer;
        public HashSet<ZDOID> Known = new(), Invalid = new();
    }
    public readonly List<ZDOPeer> Peers = new();
    public readonly Dictionary<ZDOID, ZDO> Objects = new();
    public int InvalidationChecks;
    public ZDOPeer FindPeer(ZRpc rpc) => Peers.FirstOrDefault(p => p.m_peer.m_rpc == rpc);
    public ZDO GetZDO(ZDOID id) => Objects.TryGetValue(id, out var zdo) ? zdo : null;
    public bool IsInPeerActiveArea(UnityEngine.Vector3 position, long uid)
    {
        var peer = ZNet.instance.GetPeer(uid);
        return peer != null && (peer.Position - position).sqrMagnitude < 100 * 100;
    }
    public void ZDOSectorInvalidated(ZDO zdo)
    {
        InvalidationChecks++;
        // Models the native observer-selection contract inspected in 1.0.15.
        foreach (var peer in Peers)
        {
            if (zdo.GetOwner() != peer.m_peer.m_uid && peer.Known.Contains(zdo.m_uid)
                && !IsInPeerActiveArea(zdo.GetPosition(), peer.m_peer.m_uid))
            {
                peer.Invalid.Add(zdo.m_uid);
                peer.Known.Remove(zdo.m_uid);
            }
        }
    }
}
namespace Valheim_Serverside
{
    public static class ServersidePlugin
    {
        public class Logger { public void LogDebug(string text) { } }
        public static Logger logger = new();
    }
}
