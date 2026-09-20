using System;
using System.Collections.Generic;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
    // Production keep-alive (loading zones around stations/farms) was removed: the supporting
    // ring pulled in nearby dungeons and pinned large amounts of RAM. Raid start/spawn still
    // require a real nearby player. No synthetic peers.
    internal static class ProductionAreas
    {
        internal static bool Installed;
        internal static bool Enabled => Installed && Configuration.productionEnabled.Value;
        internal static string Status => !Enabled ? "production inactive" : "production keep-alive off (player areas only)";

        internal static void Tick() { }

        internal static void Observe(ZDO zdo) { }

        internal static void LoadZones(ZoneSystem system) { }

        internal static bool Contains(Vector3 position) => false;

        internal static void AddObjects(List<ZDO> target) { }

        internal static bool PlayerNear(Vector3 position, float radius)
        {
            if (!ZNet.instance || ZDOMan.instance == null) return false;
            foreach (ZNetPeer peer in ZNet.instance.GetConnectedPeers())
            {
                ZDO player = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (player == null || player.GetOwner() != peer.m_uid) continue;
                Vector3 p = player.GetPosition();
                if (p.y > 3000 || Math.Abs(p.y - position.y) > 100) continue;
                float dx = p.x - position.x, dz = p.z - position.z;
                if (dx * dx + dz * dz < radius * radius) return true;
            }
            return false;
        }
    }
}
