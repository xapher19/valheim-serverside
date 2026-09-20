using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	// Independent portal-hub for dedicated servers. Inspired by the public behaviour of
	// ArgusMagnus ServersideQoL AutoPortalHub (pair unpaired portal tags via a generated hub)
	// but does not include or depend on that mod's source. Remove AutoPortalHub / ServersideQoL
	// portal packages when using this; running both will fight over hub objects.
	//
	// Vanilla clients have no RPC that opens a destination dropdown. Custom RPCs are ignored
	// unless the client registered them. The selectable UI is therefore vanilla pieces the
	// server can spawn: a hall of portals with signs. Walking into an untagged home portal
	// arrives there. Typed tags remain an optional shortcut on that home portal.
	internal static class PortalHub
	{
		internal const string LobbyTag = "Home";
		internal static bool Installed;
		private static readonly int HubMarker = "nw_portal_hub".GetStableHashCode();
		private static readonly int LobbyMarker = "nw_portal_lobby".GetStableHashCode();
		private static readonly List<ZDOID> hubObjects = new List<ZDOID>();
		private static readonly HashSet<int> portalPrefabs = new HashSet<int>();
		private static string lastSignature = "";
		private static double nextScan;
		private static Regex includeRegex, excludeRegex;
		private static Vector3 hubOrigin;
		private static bool originReady;
		private static ZDOID hubLobbyId;
		internal static bool Enabled => Installed && Configuration.portalHubEnabled.Value;
		internal static string Status => !Enabled ? "portal hub inactive"
			: lastSignature.StartsWith("hall|", StringComparison.Ordinal)
				? $"portal destination hall {hubObjects.Count} pieces"
				: $"portal hub {hubObjects.Count} pieces; last plan [{lastSignature}]";

		internal static void Tick()
		{
			if (!Enabled || ZDOMan.instance == null || !ZNetScene.instance || !ZNet.instance || !ZNet.instance.IsServer())
				return;
			if (!ZoneSystem.instance || !ZoneSystem.instance.LocationsGenerated) return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextScan) return;
			nextScan = now + 2;
			EnsureOrigin();
			RebuildFilters();
			RefreshPortalPrefabIndex();
			Reconcile();
		}

		private static void EnsureOrigin()
		{
			if (originReady) return;
			float edge = 10500f;
			if (WorldGenerator.instance != null) edge = WorldGenerator.waterEdge;
			hubOrigin = new Vector3(edge + 5f * 64f, 2000f, 0f);
			originReady = true;
		}

		private static void RebuildFilters()
		{
			includeRegex = Wildcard(Configuration.portalHubInclude.Value, matchAllIfStar: true);
			excludeRegex = Wildcard(Configuration.portalHubExclude.Value, matchAllIfStar: false);
		}

		private static Regex Wildcard(string pattern, bool matchAllIfStar)
		{
			if (string.IsNullOrWhiteSpace(pattern)) return matchAllIfStar ? new Regex("^.*$") : null;
			string trimmed = pattern.Trim();
			if (matchAllIfStar && trimmed == "*") return new Regex("^.*$");
			if (!matchAllIfStar && trimmed.Length == 0) return null;
			string escaped = Regex.Escape(trimmed).Replace("\\*", ".*").Replace("\\?", ".");
			return new Regex("^" + escaped + "$", RegexOptions.CultureInvariant);
		}

		private static bool AllowedTag(string tag)
		{
			if (string.IsNullOrEmpty(tag)) return false;
			if (string.Equals(tag, LobbyTag, StringComparison.OrdinalIgnoreCase)) return false;
			if (includeRegex != null && !includeRegex.IsMatch(tag)) return false;
			if (excludeRegex != null && excludeRegex.IsMatch(tag)) return false;
			return true;
		}

		private static bool IsHubObject(ZDO zdo) => zdo != null && zdo.GetLong(HubMarker, 0L) != 0L;

		private static bool IsHubLobby(ZDO zdo) => IsHubObject(zdo) && zdo.GetLong(LobbyMarker, 0L) != 0L;

		private static void RefreshPortalPrefabIndex()
		{
			portalPrefabs.Clear();
			foreach (var pair in ZNetScene.instance.m_namedPrefabs)
				if (pair.Value && pair.Value.GetComponent<TeleportWorld>())
					portalPrefabs.Add(pair.Key);
		}

		private static List<ZDO> WorldPortals()
		{
			var list = new List<ZDO>();
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return list;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					ZDO zdo = bucket[i];
					if (zdo == null || !zdo.IsValid() || IsHubObject(zdo)) continue;
					if (portalPrefabs.Contains(zdo.GetPrefab())) list.Add(zdo);
				}
			}
			return list;
		}

		private static void MaybeAutoName(ZDO zdo)
		{
			if (!Configuration.portalHubAutoName.Value) return;
			string tag = zdo.GetString(ZDOVars.s_tag, "");
			if (!string.IsNullOrEmpty(tag)) return;
			Heightmap.Biome biome = WorldGenerator.instance != null
				? WorldGenerator.instance.GetBiome(zdo.GetPosition())
				: Heightmap.Biome.None;
			string biomeName = biome == Heightmap.Biome.None ? "Portal" : biome.ToString();
			var used = new HashSet<string>(WorldPortals().Select(p => p.GetString(ZDOVars.s_tag, "")));
			for (int i = 1; i <= 1000; i++)
			{
				string candidate = string.Format(Configuration.portalHubAutoNameFormat.Value, biomeName, i);
				if (used.Contains(candidate)) continue;
				zdo.Set(ZDOVars.s_tag, candidate);
				return;
			}
		}

		private static string TagOf(ZDO zdo) => zdo == null ? "" : (zdo.GetString(ZDOVars.s_tag, "") ?? "");

		private static bool IsGateway(ZDO zdo) => zdo != null && zdo.IsValid() && string.IsNullOrEmpty(TagOf(zdo));

		internal static List<string> UnpairedTags(IEnumerable<string> tags)
		{
			var byTag = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (string tag in tags)
			{
				if (string.IsNullOrEmpty(tag) || !AllowedTag(tag)) continue;
				byTag.TryGetValue(tag, out int n);
				byTag[tag] = n + 1;
			}
			return byTag.Where(kv => kv.Value % 2 != 0).Select(kv => kv.Key).OrderBy(t => t, StringComparer.Ordinal).ToList();
		}

		private static void Reconcile()
		{
			var portals = WorldPortals();
			foreach (ZDO zdo in portals) MaybeAutoName(zdo);
			int gateways = 0;
			foreach (ZDO zdo in portals)
				if (IsGateway(zdo)) gateways++;

			var unpaired = UnpairedTags(portals.Select(TagOf));
			if (gateways > 0)
			{
				string signature = "hall|" + gateways + "|" + string.Join("|", unpaired);
				if (signature != lastSignature)
				{
					ClearHub();
					BuildHall(unpaired);
					lastSignature = signature;
					ServersidePlugin.logger.LogInfo(
						$"Portal hall: {gateways} untagged home portal(s), {unpaired.Count} destination(s). "
						+ "Walk through an untagged home portal to pick a labeled destination. Tagged world portals return home.");
				}
				Connect();
				WireHall();
				return;
			}

			string hubSignature = string.Join("|", unpaired);
			if (hubSignature == lastSignature && (unpaired.Count == 0 ? hubObjects.Count == 0 : hubObjects.Count > 0))
				return;

			ClearHub();
			lastSignature = hubSignature;
			if (unpaired.Count == 0)
			{
				Connect();
				return;
			}

			BuildHub(unpaired);
			Connect();
			ServersidePlugin.logger.LogInfo($"Portal hub: paired {unpaired.Count} unpaired tag(s)");
		}

		internal static void WireHall()
		{
			if (!Enabled || ZDOMan.instance == null) return;
			var portals = WorldPortals();
			var gateways = new List<ZDO>();
			foreach (ZDO zdo in portals)
				if (IsGateway(zdo)) gateways.Add(zdo);
			if (gateways.Count == 0) return;
			gateways.Sort((a, b) =>
			{
				Vector3 pa = a.GetPosition(), pb = b.GetPosition();
				int c = pa.x.CompareTo(pb.x);
				return c != 0 ? c : pa.z.CompareTo(pb.z);
			});
			ZDO home = gateways[0];
			ZDO lobby = FindHubLobby();
			if (lobby == null || !lobby.IsValid())
			{
				foreach (ZDO gateway in gateways)
					SetPortalConnection(gateway, ZDOID.None);
				return;
			}

			foreach (ZDO gateway in gateways)
				SetPortalConnection(gateway, lobby.m_uid);
			SetPortalConnection(lobby, home.m_uid);

			foreach (ZDO zdo in portals)
			{
				if (IsGateway(zdo) || !AllowedTag(TagOf(zdo))) continue;
				ZDOID connected = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal);
				ZDO other = connected.IsNone() ? null : ZDOMan.instance.GetZDO(connected);
				if (other != null && other.IsValid() && IsHubObject(other))
					SetPortalConnection(zdo, home.m_uid);
			}
		}

		private static void SetPortalConnection(ZDO zdo, ZDOID target)
		{
			if (zdo == null || !zdo.IsValid()) return;
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.SetConnection(ZDOExtraData.ConnectionType.Portal, target);
		}

		internal static void HandleGatewayTag(TeleportWorld portal, string requested)
		{
			if (!Enabled || !portal || !portal.m_nview || !portal.m_nview.IsValid()) return;
			ZDO zdo = portal.m_nview.GetZDO();
			if (zdo == null || IsHubObject(zdo)) return;
			FarmingSupport.CreatorAt(zdo.GetPosition(), out ZNetPeer peer);
			if (string.IsNullOrWhiteSpace(requested))
			{
				Notify(peer, "Walk through this portal to pick a destination.");
				return;
			}
			ZDO dest = FindDestination(requested.Trim());
			if (dest == null)
			{
				Notify(peer, "No destination '" + requested.Trim() + "'. This portal is now tagged " + requested.Trim() + ".");
				return;
			}
			zdo.Set(ZDOVars.s_tag, "");
			TeleportPeer(peer, dest);
			Notify(peer, "Traveling to " + TagOf(dest) + ".");
		}

		private static ZDO FindDestination(string tag)
		{
			ZDO exact = null, ignoreCase = null;
			foreach (ZDO zdo in WorldPortals())
			{
				string name = TagOf(zdo);
				if (name.Length == 0) continue;
				if (string.Equals(name, tag, StringComparison.Ordinal)) { exact = zdo; break; }
				if (ignoreCase == null && string.Equals(name, tag, StringComparison.OrdinalIgnoreCase)) ignoreCase = zdo;
			}
			return exact ?? ignoreCase;
		}

		private static void TeleportPeer(ZNetPeer peer, ZDO dest)
		{
			if (peer == null || dest == null || !dest.IsValid()) return;
			Quaternion rot = dest.GetRotation();
			Vector3 pos = dest.GetPosition() + rot * Vector3.forward * 1.5f + Vector3.up * 0.2f;
			ZDO character = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
			if (character != null && character.IsValid()) character.SetPosition(pos);
			Player player = PlayerFromPeer(peer);
			try
			{
				if (player) player.TeleportTo(pos, rot, true);
				else if (ZRoutedRpc.instance != null)
					ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "RPC_TeleportPlayer", pos, rot, true);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger.LogWarning("Portal teleport failed: " + e.GetType().Name);
			}
		}

		private static Player PlayerFromPeer(ZNetPeer peer)
		{
			if (peer == null) return null;
			foreach (Player player in Player.GetAllPlayers())
			{
				if (!player || !player.m_nview || !player.m_nview.IsValid()) continue;
				if (player.m_nview.GetZDO().m_uid.Equals(peer.m_characterID)) return player;
			}
			return null;
		}

		private static void Notify(ZNetPeer peer, string text)
		{
			if (string.IsNullOrEmpty(text)) return;
			try
			{
				if (ZRoutedRpc.instance != null && peer != null)
					ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.TopLeft, "Northwatch: " + text);
			}
			catch (Exception) { }
			if (peer != null && peer.m_rpc != null)
			{
				try { peer.m_rpc.Invoke("RemotePrint", "[server] " + text); }
				catch (Exception) { }
			}
		}

		private static void ClearHub()
		{
			hubLobbyId = ZDOID.None;
			foreach (ZDOID id in hubObjects)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid()) ZDOMan.instance.DestroyZDO(zdo);
			}
			hubObjects.Clear();
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return;
			var orphans = new List<ZDO>();
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				foreach (ZDO zdo in bucket)
					if (zdo != null && zdo.IsValid() && IsHubObject(zdo)) orphans.Add(zdo);
			}
			foreach (ZDO zdo in orphans) ZDOMan.instance.DestroyZDO(zdo);
		}

		private static ZDO FindHubLobby()
		{
			if (!hubLobbyId.IsNone())
			{
				ZDO known = ZDOMan.instance.GetZDO(hubLobbyId);
				if (known != null && known.IsValid() && IsHubLobby(known)) return known;
			}
			foreach (ZDOID id in hubObjects)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid() && IsHubLobby(zdo)) return zdo;
			}
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return null;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				foreach (ZDO zdo in bucket)
					if (zdo != null && zdo.IsValid() && IsHubLobby(zdo)) return zdo;
			}
			return null;
		}

		private static void BuildHall(List<string> tags)
		{
			Quaternion faceDestinations = Quaternion.LookRotation(Vector3.forward);
			Quaternion faceHome = Quaternion.LookRotation(Vector3.back);
			const float spacing = 6f;
			const float destZ = 10f;
			int n = Math.Max(1, tags.Count);
			int cols = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(n)));
			int rows = Math.Max(1, (int)Math.Ceiling(tags.Count / (float)cols));
			PlacePlatform(cols, rows, spacing, destZ);

			ZDO lobby = PlacePrefab(ResolvePortalPrefab(), hubOrigin, faceDestinations);
			if (lobby != null)
			{
				lobby.Set(ZDOVars.s_tag, LobbyTag);
				lobby.Set(LobbyMarker, 1L);
				hubLobbyId = lobby.m_uid;
			}
			PlaceSign(hubOrigin + new Vector3(-2.4f, 2.2f, 1.2f), faceDestinations, "<color=yellow>Home");
			PlaceSign(hubOrigin + new Vector3(2.4f, 2.2f, 1.2f), faceDestinations,
				tags.Count == 0
					? "<color=white>Name an outpost portal"
					: "<color=white>Walk into a portal");

			for (int i = 0; i < tags.Count; i++)
			{
				int row = i / cols, col = i % cols;
				Vector3 pos = hubOrigin + new Vector3((col - (cols - 1) / 2f) * spacing, 0f, destZ + row * spacing);
				ZDO portal = PlacePrefab(ResolvePortalPrefab(), pos, faceHome);
				if (portal == null) continue;
				portal.Set(ZDOVars.s_tag, tags[i]);
				PlaceSign(pos + new Vector3(0f, 2.2f, -1.2f), faceHome, "<color=white>" + tags[i]);
			}
		}

		private static void PlacePlatform(int cols, int rows, float spacing, float destZ)
		{
			float minX = -((cols - 1) / 2f) * spacing - 4f;
			float maxX = ((cols - 1) / 2f) * spacing + 4f;
			float minZ = -4f;
			float maxZ = destZ + Math.Max(0, rows - 1) * spacing + 4f;
			for (float x = minX; x <= maxX + 0.01f; x += 4f)
				for (float z = minZ; z <= maxZ + 0.01f; z += 4f)
					PlaceFloor(hubOrigin + new Vector3(x, -0.1f, z));
		}

		private static void BuildHub(List<string> tags)
		{
			int n = tags.Count;
			int cols = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(n)));
			float spacing = 4f;
			for (int i = 0; i < n; i++)
			{
				int row = i / cols, col = i % cols;
				Vector3 pos = hubOrigin + new Vector3((col - cols / 2f) * spacing, 0f, (row - cols / 2f) * spacing);
				PlaceFloor(pos + new Vector3(0f, -0.1f, 0f));
				ZDO portal = PlacePrefab(ResolvePortalPrefab(), pos, Quaternion.identity);
				if (portal == null) continue;
				portal.Set(ZDOVars.s_tag, tags[i]);
				PlaceSign(pos + new Vector3(0f, 2f, -0.6f), Quaternion.identity, "<color=white>" + tags[i]);
			}
		}

		private static string ResolvePortalPrefab()
		{
			if (ZNetScene.instance.GetPrefab("portal_wood")) return "portal_wood";
			if (ZNetScene.instance.GetPrefab("portal")) return "portal";
			return "portal_wood";
		}

		private static void PlaceFloor(Vector3 pos)
		{
			foreach (string name in new[] { "Piece_grausten_floor_4x4", "wood_floor_4x4", "wood_floor" })
			{
				if (!ZNetScene.instance.GetPrefab(name)) continue;
				PlacePrefab(name, pos, Quaternion.identity);
				return;
			}
		}

		private static void PlaceSign(Vector3 pos, Quaternion rot, string text)
		{
			ZDO sign = PlacePrefab("sign", pos, rot);
			if (sign != null) sign.Set(ZDOVars.s_text, text);
		}

		private static ZDO PlacePrefab(string prefabName, Vector3 pos, Quaternion rot)
		{
			GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
			if (!prefab) return null;
			int hash = prefab.name.GetStableHashCode();
			ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
			zdo.SetPrefab(hash);
			zdo.SetRotation(rot);
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Set(HubMarker, 1L);
			ZNetScene.instance.CreateObject(zdo);
			hubObjects.Add(zdo.m_uid);
			return zdo;
		}

		private static void Connect()
		{
			if (Game.instance) Game.instance.ConnectPortals();
		}
	}
}
