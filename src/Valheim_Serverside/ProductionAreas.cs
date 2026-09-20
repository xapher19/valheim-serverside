using System;
using System.Collections.Generic;
using System.Diagnostics;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
    // No synthetic peers: production never supplies player positions to spawn/raid systems.
    // World ZDOs are the durable index. Rebuild incrementally after every server start.
    internal static class ProductionAreas
    {
        internal static bool Installed;
        private static ZDOMan manager;
        private static ZNetScene scene;
        private static readonly HashSet<int> prefabs = new HashSet<int>();
        private static readonly HashSet<int> tamePrefabs = new HashSet<int>();
        private static readonly HashSet<int> floraPrefabs = new HashSet<int>();
        private static readonly HashSet<ZDOID> anchors = new HashSet<ZDOID>();
        private static readonly List<ZDOID> removed = new List<ZDOID>();
        private static readonly HashSet<Vector2s> zones = new HashSet<Vector2s>();
        private static readonly List<Vector2s> orderedZones = new List<Vector2s>();
        private static int sector, entry, nextZone;
        private static double nextScan, nextRefresh;
        private static bool scanning, reported;
        internal static bool Enabled => Installed && Configuration.productionEnabled.Value;
        internal static string Status => !Enabled ? "production inactive" : $"production {anchors.Count} anchors, {zones.Count} supporting zones; initial scan {(reported ? "complete" : "in progress")}";

        internal static void Tick()
        {
            if (!Enabled || ZDOMan.instance == null || !ZNetScene.instance || !ZoneSystem.instance
                || !ZNet.instance || !ZNet.instance.IsServer() || !ZoneSystem.instance.LocationsGenerated) return;
            if (manager != ZDOMan.instance || scene != ZNetScene.instance)
            {
                manager = ZDOMan.instance; scene = ZNetScene.instance;
                anchors.Clear(); zones.Clear(); orderedZones.Clear(); prefabs.Clear(); tamePrefabs.Clear(); floraPrefabs.Clear();
                sector = entry = nextZone = 0; scanning = true; reported = false; nextRefresh = nextScan = 0;
                var excluded = new HashSet<string>(Configuration.productionExclude.Value.Split(','), StringComparer.Ordinal);
                var trimmed = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in excluded) trimmed.Add(name.Trim());
                if (Configuration.productionFlora.Value)
                    foreach (int hash in FarmingSupport.ParsePrefabHashes(Configuration.farmingExtraFlora.Value, FarmingSupport.DefaultFloraPrefabs))
                        floraPrefabs.Add(hash);
                foreach (var pair in scene.m_namedPrefabs)
                {
                    GameObject p = pair.Value;
                    if (!p || trimmed.Contains(p.name)) continue;
                    Plant plant = p.GetComponent<Plant>();
                    if (plant || p.GetComponent<Smelter>() || p.GetComponent<Fermenter>() || p.GetComponent<Beehive>()
                        || p.GetComponent<CookingStation>() || p.GetComponent<SapCollector>()) prefabs.Add(pair.Key);
                    if (Configuration.productionLivestock.Value)
                    {
                        Tameable tame = p.GetComponent<Tameable>();
                        EggGrow egg = p.GetComponent<EggGrow>();
                        if (tame && tame.m_startsTamed || egg && egg.m_tamed) prefabs.Add(pair.Key);
                        else if (tame || p.GetComponent<Growup>()) tamePrefabs.Add(pair.Key);
                    }
                    // Mature crop Pickables retain an anchor after their Plant is replaced. Trees do not.
                    if (plant && plant.m_grownPrefabs != null)
                        foreach (GameObject grown in plant.m_grownPrefabs)
                            if (grown && grown.GetComponent<Pickable>() && !trimmed.Contains(grown.name)) prefabs.Add(grown.name.GetStableHashCode());
                }
                foreach (string name in trimmed) floraPrefabs.Remove(name.GetStableHashCode());
                ServersidePlugin.logger.LogInfo($"Production: indexing {prefabs.Count + tamePrefabs.Count} station/crop/livestock types and {floraPrefabs.Count} flora types; anchors require a player creator. World clock while empty: {Configuration.advanceEmptyTime.Value}. No offline catch-up.");
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!scanning && now >= nextScan) { scanning = true; sector = entry = 0; }
            if (scanning)
            {
                long started = Stopwatch.GetTimestamp();
                int budget = Configuration.productionScanBudget.Value;
                while (sector < manager.m_objectsBySector.Length && budget-- > 0)
                {
                    var list = manager.m_objectsBySector[sector];
                    if (list == null || entry >= list.Count) { sector++; entry = 0; }
                    else Observe(list[entry++]);
                    if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= 2) break;
                }
                if (sector >= manager.m_objectsBySector.Length)
                {
                    scanning = false; nextScan = now + 30;
                    if (!reported) { reported = true; ServersidePlugin.logger.LogInfo($"Production: initial scan complete, {anchors.Count} anchors found."); }
                }
            }
            if (now >= nextRefresh)
            {
                nextRefresh = now + 1;
                RefreshZones();
            }
        }

        internal static void Observe(ZDO zdo)
        {
            if (Enabled && manager == ZDOMan.instance && zdo != null && zdo.IsValid() && IsAnchor(zdo)) anchors.Add(zdo.m_uid);
        }

        private static bool IsAnchor(ZDO zdo)
        {
            // Only player-made pieces (Piece.SetCreator). Wild beehives, sap collectors,
            // bushes and other world props must not pin zones or load nearby dungeons.
            if (!FarmingSupport.HasCreator(zdo)) return false;
            int prefab = zdo.GetPrefab();
            if (prefabs.Contains(prefab)) return true;
            if (tamePrefabs.Contains(prefab) && zdo.GetBool(ZDOVars.s_tamed)) return true;
            return floraPrefabs.Contains(prefab);
        }

        private static void RefreshZones()
        {
            zones.Clear(); removed.Clear();
            foreach (ZDOID id in anchors)
            {
                ZDO zdo = manager.GetZDO(id);
                if (zdo == null || !zdo.IsValid() || !IsAnchor(zdo)) { removed.Add(id); continue; }
                Vector2s zone = zdo.GetSector();
                // One supporting ring keeps roofs, crop spacing, terrain and output physics present.
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        Vector2s candidate = new Vector2s(zone.x + x, zone.y + y);
                        if (ZoneSystem.instance.IsZoneGenerated(candidate)) zones.Add(candidate);
                    }
            }
            foreach (ZDOID id in removed) anchors.Remove(id);
            orderedZones.Clear(); orderedZones.AddRange(zones);
            orderedZones.Sort((a, b) => a.y == b.y ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        }

        internal static void LoadZones(ZoneSystem system)
        {
            if (!Enabled || manager != ZDOMan.instance) return;
            // Refresh every loaded support zone even while another zone is still loading.
            foreach (Vector2s zone in orderedZones)
                if (system.m_zones.TryGetValue(zone, out var data)) data.m_ttl = 0;
            // At most one new production zone per tick; no unbounded startup generation spike.
            int count = orderedZones.Count;
            for (int n = 0; n < count; n++)
            {
                if (nextZone >= count) nextZone = 0;
                Vector2s zone = orderedZones[nextZone++];
                if (!system.IsZoneLoaded(zone) && system.PokeLocalZone(zone)) break;
            }
        }

        internal static bool Contains(Vector3 position) => Enabled && manager == ZDOMan.instance
            && zones.Contains(ZoneSystem.GetZone(position)) && ZoneSystem.instance.IsZoneLoaded(position);

        internal static void AddObjects(List<ZDO> target)
        {
            if (!Enabled || manager != ZDOMan.instance) return;
            int first = target.Count;
            foreach (Vector2s zone in orderedZones)
                if (ZoneSystem.instance.IsZoneLoaded(zone))
                    manager.FindSectorObjects(zone, new SimulationDistance(0, 0, true), target);
            long server = ZNet.GetUID();
            for (int i = first; i < target.Count; i++)
            {
                ZDO zdo = target[i];
                if (!Contains(zdo.GetPosition())) continue;
                long owner = zdo.GetOwner();
                ZNetPeer owningPeer = ZNet.instance.GetPeer(owner);
                if (owningPeer != null && owningPeer.m_characterID.Equals(zdo.m_uid)) continue;
                // Never steal a live client's chest, item, ship, or character.
                if (owner == 0 || (owner != server && (owningPeer == null || (zdo.Persistent && !manager.IsInPeerActiveArea(zdo.GetPosition(), owner))))) zdo.SetOwner(server);
            }
        }

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
