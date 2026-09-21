using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	/// <summary>
	/// Emote-opened backpack: a persistent private chest parked under the player, force-opened
	/// with vanilla RPC_OpenResponse. Wave by default (bindable on console via /bind).
	/// Disable ServersideQoL.Backpack if both would fight over the same emote.
	/// </summary>
	internal static class BackpackRuntime
	{
		internal const string OwnerKey = "nw_backpack_owner";
		internal const string MarkerKey = "nw_backpack";
		private static readonly int OwnerHash = OwnerKey.GetStableHashCode();
		private static readonly int MarkerHash = MarkerKey.GetStableHashCode();
		private static readonly Dictionary<long, BackpackState> byPeer = new Dictionary<long, BackpackState>();
		private static MethodInfo saveMethod;

		internal static bool Enabled =>
			QoLRuntime.Installed && Configuration.qolEnabled.Value && Configuration.qolBackpack.Value;

		internal static string Status => !Enabled ? "" : $"backpack {byPeer.Count}";

		internal static void Tick()
		{
			if (!Enabled || ZNet.instance == null || ZDOMan.instance == null || !ZNetScene.instance) return;
			List<ZNetPeer> peers = ZNet.instance.GetPeers();
			var live = new HashSet<long>();
			for (int i = 0; i < peers.Count; i++)
			{
				ZNetPeer peer = peers[i];
				if (peer == null || !peer.IsReady()) continue;
				live.Add(peer.m_uid);
				TickPeer(peer);
			}
			if (byPeer.Count == live.Count) return;
			var stale = new List<long>();
			foreach (long id in byPeer.Keys)
				if (!live.Contains(id)) stale.Add(id);
			foreach (long id in stale)
			{
				Park(byPeer[id]);
				byPeer.Remove(id);
			}
		}

		private static void TickPeer(ZNetPeer peer)
		{
			ZDO character = ZDOMan.instance.GetZDO(peer.m_characterID);
			if (character == null || !character.IsValid()) return;

			if (!byPeer.TryGetValue(peer.m_uid, out BackpackState state))
			{
				state = new BackpackState { PeerId = peer.m_uid, LastEmoteId = character.GetInt(ZDOVars.s_emoteID, 0) };
				byPeer[peer.m_uid] = state;
			}

			int emoteId = character.GetInt(ZDOVars.s_emoteID, 0);
			if (emoteId != 0 && emoteId != state.LastEmoteId)
			{
				state.LastEmoteId = emoteId;
				string emote = character.GetString(ZDOVars.s_emote, "");
				if (IsOpenEmote(emote))
					Open(peer, character, state);
			}

			if (state.ZdoId.IsNone()) return;
			ZDO bag = ZDOMan.instance.GetZDO(state.ZdoId);
			if (bag == null || !bag.IsValid())
			{
				state.ZdoId = ZDOID.None;
				return;
			}

			// Keep parked under the player so AOI follows them.
			Vector3 park = character.GetPosition();
			park.y = -1000f;
			if ((bag.GetPosition() - park).sqrMagnitude > 1f)
			{
				if (bag.GetOwner() != ZDOMan.GetSessionID())
					bag.SetOwner(ZDOMan.GetSessionID());
				bag.SetPosition(park);
			}
		}

		private static bool IsOpenEmote(string emote)
		{
			string want = Configuration.qolBackpackEmote.Value;
			if (string.IsNullOrEmpty(want) || want == "*") return !string.IsNullOrEmpty(emote);
			return string.Equals(emote, want, StringComparison.OrdinalIgnoreCase);
		}

		private static void Open(ZNetPeer peer, ZDO character, BackpackState state)
		{
			ZDO bag = EnsureBag(peer, character, state);
			if (bag == null) return;

			bag.SetOwner(peer.m_uid);
			ZDOMan.instance.ForceSendZDO(peer.m_uid, bag.m_uid);

			ZNetView view = ZNetScene.instance.FindInstance(bag);
			if (view == null)
			{
				GameObject go = ZNetScene.instance.CreateObject(bag);
				view = go ? go.GetComponent<ZNetView>() : null;
			}
			if (view == null || !view.IsValid()) return;

			try
			{
				view.InvokeRPC(peer.m_uid, "RPC_OpenResponse", true);
			}
			catch (Exception e)
			{
				ServersidePlugin.logger?.LogWarning("Backpack open failed: " + e.GetType().Name);
			}
			Notify(peer, "Backpack");
		}

		private static ZDO EnsureBag(ZNetPeer peer, ZDO character, BackpackState state)
		{
			if (!state.ZdoId.IsNone())
			{
				ZDO existing = ZDOMan.instance.GetZDO(state.ZdoId);
				if (existing != null && existing.IsValid()) return existing;
			}

			// Reattach a world backpack tagged for this player.
			long playerId = character.GetLong(ZDOVars.s_playerID, 0L);
			ZDO found = FindOwnedBackpack(playerId, peer.m_uid);
			if (found != null)
			{
				state.ZdoId = found.m_uid;
				return found;
			}

			GameObject prefab = ZNetScene.instance.GetPrefab("piece_chest_private");
			if (!prefab) prefab = ZNetScene.instance.GetPrefab("piece_chest");
			if (!prefab) return null;

			int hash = prefab.name.GetStableHashCode();
			Vector3 pos = character.GetPosition();
			pos.y = -1000f;
			ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
			zdo.SetPrefab(hash);
			zdo.SetRotation(Quaternion.identity);
			zdo.SetOwner(ZDOMan.GetSessionID());
			zdo.Persistent = true;
			zdo.Set(MarkerHash, 1);
			zdo.Set(OwnerHash, playerId != 0 ? playerId : peer.m_uid);
			if (playerId != 0) zdo.Set(ZDOVars.s_creator, playerId);

			int slots = Math.Max(1, Configuration.qolBackpackSlots.Value);
			int width = 4;
			int height = Math.Max(1, (slots + width - 1) / width);
			zdo.Set("HasFields", true);
			zdo.Set("HasFieldsContainer", true);
			zdo.Set("Container.m_width", width);
			zdo.Set("Container.m_height", height);
			zdo.Set("Container.m_name", "$piece_chest_private");

			GameObject go = ZNetScene.instance.CreateObject(zdo);
			Container container = go ? go.GetComponent<Container>() : null;
			if (container != null)
			{
				container.m_width = width;
				container.m_height = height;
				try
				{
					if (saveMethod == null) saveMethod = AccessTools.Method(typeof(Container), "Save");
					saveMethod?.Invoke(container, null);
				}
				catch (Exception) { }
			}

			state.ZdoId = zdo.m_uid;
			ServersidePlugin.logger?.LogInfo($"Backpack created for peer {peer.m_uid} ({width}x{height})");
			return zdo;
		}

		private static ZDO FindOwnedBackpack(long playerId, long peerId)
		{
			List<ZDO>[] sectors = ZDOMan.instance.m_objectsBySector;
			if (sectors == null) return null;
			for (int s = 0; s < sectors.Length; s++)
			{
				List<ZDO> bucket = sectors[s];
				if (bucket == null) continue;
				for (int i = 0; i < bucket.Count; i++)
				{
					ZDO zdo = bucket[i];
					if (zdo == null || !zdo.IsValid()) continue;
					if (zdo.GetInt(MarkerHash, 0) != 1) continue;
					long owner = zdo.GetLong(OwnerHash, 0L);
					if (owner == playerId || owner == peerId) return zdo;
				}
			}
			return null;
		}

		private static void Park(BackpackState state)
		{
			if (state == null || state.ZdoId.IsNone() || ZDOMan.instance == null) return;
			ZDO bag = ZDOMan.instance.GetZDO(state.ZdoId);
			if (bag == null || !bag.IsValid()) return;
			if (bag.GetOwner() != ZDOMan.GetSessionID())
				bag.SetOwner(ZDOMan.GetSessionID());
		}

		private static void Notify(ZNetPeer peer, string text)
		{
			try
			{
				if (ZRoutedRpc.instance != null && peer != null)
					ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ShowMessage", (int)MessageHud.MessageType.TopLeft, "Northwatch: " + text);
			}
			catch (Exception) { }
		}

		private sealed class BackpackState
		{
			internal long PeerId;
			internal ZDOID ZdoId = ZDOID.None;
			internal int LastEmoteId;
		}
	}
}
