using FeaturesLib;
using HarmonyLib;
using PluginConfiguration;

namespace Valheim_Serverside.Features
{
	public class PortalHubHooks : IFeature
	{
		public bool FeatureEnabled() => Configuration.portalHubEnabled.Value;

		[HarmonyPatch(typeof(Game), "ConnectPortals")]
		public static class ConnectPortalsWire
		{
			static void Postfix() => PortalHub.WireHall();
		}

		[HarmonyPatch(typeof(Game), "FindRandomUnconnectedPortal")]
		public static class SkipEmptyPairing
		{
			static void Postfix(string tag, ref ZDO __result)
			{
				if (string.IsNullOrEmpty(tag)) __result = null;
			}
		}

		[HarmonyPatch(typeof(TeleportWorld), "Teleport")]
		public static class GatewayTeleport
		{
			static bool Prefix(TeleportWorld __instance, Player player)
			{
				return !PortalHub.TryInterceptTeleport(__instance, player);
			}
		}

		[HarmonyPatch(typeof(TeleportWorld), "SetText")]
		public static class GatewayTag
		{
			static void Prefix(TeleportWorld __instance, out string __state)
			{
				__state = "";
				if (!__instance || !__instance.m_nview || !__instance.m_nview.IsValid()) return;
				ZDO zdo = __instance.m_nview.GetZDO();
				__state = zdo != null ? (zdo.GetString(ZDOVars.s_tag, "") ?? "") : "";
			}

			static void Postfix(TeleportWorld __instance, string text, string __state)
			{
				if (!string.IsNullOrEmpty(__state)) return;
				PortalHub.HandleGatewayTag(__instance, text);
			}
		}

		// Hall sits in the sky with no terrain support; block WearNTear so floors/portals
		// do not slowly collapse while players pick a destination.
		[HarmonyPatch(typeof(WearNTear), "ApplyDamage")]
		public static class HubWearDamage
		{
			static bool Prefix(WearNTear __instance) => !PortalHub.IsHubWear(__instance);
		}

		[HarmonyPatch(typeof(WearNTear), "Destroy")]
		public static class HubWearDestroy
		{
			static bool Prefix(WearNTear __instance) => !PortalHub.IsHubWear(__instance);
		}

		[HarmonyPatch(typeof(WearNTear), "Remove")]
		public static class HubWearRemove
		{
			static bool Prefix(WearNTear __instance) => !PortalHub.IsHubWear(__instance);
		}
	}
}
