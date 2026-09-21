using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	/// <summary>
	/// Server-only QoL that vanilla/console clients can feel: personalized GlobalKeys,
	/// magnet pickup, instant loot at the killer, structure repair near stations.
	/// </summary>
	public class QoL : IFeature
	{
		public bool FeatureEnabled() => Configuration.qolEnabled.Value;

		[HarmonyPatch(typeof(ZoneSystem), "SendGlobalKeys")]
		public static class PersonalizedGlobalKeys
		{
			static bool Prefix(ZoneSystem __instance, long peer)
			{
				if (!PeerWorldKeys.Enabled) return true;
				try
				{
					if (peer == 0L)
					{
						if (ZNet.instance != null)
						{
							foreach (ZNetPeer p in ZNet.instance.GetPeers())
							{
								if (p != null && p.IsReady())
									SendTo(p.m_uid);
							}
						}
						return false;
					}
					SendTo(peer);
					return false;
				}
				catch
				{
					return true;
				}

				void SendTo(long peerId)
				{
					var keys = new System.Collections.Generic.List<string>(__instance.GetGlobalKeys());
					PeerWorldKeys.Apply(peerId, keys);
					if (ZRoutedRpc.instance != null)
						ZRoutedRpc.instance.InvokeRoutedRPC(peerId, "GlobalKeys", keys);
				}
			}
		}

		[HarmonyPatch(typeof(CharacterDrop), "DropItems")]
		public static class InstantLootAtKiller
		{
			static void Prefix(ref Vector3 centerPos, ref float dropArea)
			{
				if (!QoLRuntime.Enabled || !Configuration.qolInstantLoot.Value) return;
				centerPos = QoLRuntime.InstantLootPoint(centerPos);
				dropArea = 0.15f;
			}
		}
	}
}
