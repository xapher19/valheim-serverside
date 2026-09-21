using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside
{
	/// <summary>
	/// Per-peer GlobalKeys spoofing. Vanilla clients replace their entire key set on
	/// ZoneSystem.SendGlobalKeys; we personalize that list so CarryWeightRate /
	/// DurabilityRate (and friends) apply only to the intended player.
	/// Scalar keys use Valheim's ×100 encoding (200 = 2.0×).
	/// </summary>
	internal static class PeerWorldKeys
	{
		private static readonly Dictionary<long, PeerMods> mods = new Dictionary<long, PeerMods>();
		private static FieldInfo stationsField;
		private static double nextZoneScan;

		internal static bool Enabled => QoLRuntime.Installed && Configuration.qolEnabled.Value;

		internal static void Tick()
		{
			if (!Enabled || ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
				return;
			double now = Time.realtimeSinceStartupAsDouble;
			if (now < nextZoneScan) return;
			nextZoneScan = now + 0.5;

			float carry = Mathf.Max(0.1f, Configuration.qolCarryWeightRate.Value);
			bool durabilityNearBench = Configuration.qolNoDurabilityNearStation.Value;
			float benchRange = Mathf.Max(1f, Configuration.qolStationRange.Value);
			float benchRangeSq = benchRange * benchRange;

			var live = new HashSet<long>();
			foreach (ZNetPeer peer in ZNet.instance.GetPeers())
			{
				if (peer == null || !peer.IsReady()) continue;
				live.Add(peer.m_uid);
				if (!mods.TryGetValue(peer.m_uid, out PeerMods state))
				{
					state = new PeerMods();
					mods[peer.m_uid] = state;
				}

				bool nearStation = durabilityNearBench && NearCraftingStation(peer.GetRefPos(), benchRangeSq);
				bool changed = state.SetCarry(carry) | state.SetNoDurability(nearStation);
				if (changed) Resend(peer.m_uid);
			}

			if (mods.Count == live.Count) return;
			var stale = new List<long>();
			foreach (long id in mods.Keys)
				if (!live.Contains(id)) stale.Add(id);
			foreach (long id in stale) mods.Remove(id);
		}

		internal static void Apply(long peer, List<string> keys)
		{
			if (keys == null) return;
			if (!mods.TryGetValue(peer, out PeerMods state)) return;
			if (state.CarryRate > 0f && Math.Abs(state.CarryRate - 1f) > 0.001f)
				SetScalar(keys, GlobalKeys.CarryWeightRate, state.CarryRate);
			if (state.NoDurability)
				SetScalar(keys, GlobalKeys.DurabilityRate, 0f);
		}

		internal static void Resend(long peer)
		{
			if (ZoneSystem.instance == null || peer == 0) return;
			try
			{
				AccessTools.Method(typeof(ZoneSystem), "SendGlobalKeys", new[] { typeof(long) })
					?.Invoke(ZoneSystem.instance, new object[] { peer });
			}
			catch (Exception e)
			{
				ServersidePlugin.logger?.LogWarning("PeerWorldKeys resend failed: " + e.GetType().Name);
			}
		}

		private static void SetScalar(List<string> keys, GlobalKeys gk, float rate)
		{
			string name = gk.ToString().ToLowerInvariant();
			// Valheim encodes scalars as percent: 100 => 1.0×
			string value = (rate * 100f).ToString("0.###", CultureInfo.InvariantCulture);
			string line = (name + " " + value).TrimEnd();
			for (int i = 0; i < keys.Count; i++)
			{
				string k = keys[i];
				if (k == null) continue;
				if (k.Equals(name, StringComparison.OrdinalIgnoreCase)
					|| (k.Length > name.Length && k[name.Length] == ' '
						&& k.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
				{
					keys[i] = line;
					return;
				}
			}
			keys.Add(line);
		}

		private static bool NearCraftingStation(Vector3 pos, float rangeSq)
		{
			List<CraftingStation> stations = AllStations();
			if (stations == null) return false;
			for (int i = 0; i < stations.Count; i++)
			{
				CraftingStation station = stations[i];
				if (!station || !station.transform) continue;
				if ((station.transform.position - pos).sqrMagnitude <= rangeSq) return true;
			}
			return false;
		}

		private static List<CraftingStation> AllStations()
		{
			if (stationsField == null)
				stationsField = AccessTools.Field(typeof(CraftingStation), "m_allStations");
			return stationsField?.GetValue(null) as List<CraftingStation>;
		}

		private sealed class PeerMods
		{
			internal float CarryRate = 1f;
			internal bool NoDurability;

			internal bool SetCarry(float rate)
			{
				if (Math.Abs(CarryRate - rate) < 0.001f) return false;
				CarryRate = rate;
				return true;
			}

			internal bool SetNoDurability(bool on)
			{
				if (NoDurability == on) return false;
				NoDurability = on;
				return true;
			}
		}
	}
}
