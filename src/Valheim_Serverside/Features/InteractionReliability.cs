using FeaturesLib;
using HarmonyLib;

namespace Valheim_Serverside.Features
{
    public class InteractionReliability : IFeature
    {
        public bool FeatureEnabled() => true;

        [HarmonyPatch(typeof(ItemDrop), "RPC_RequestOwn")]
        public static class PickupOwnershipDelivery
        {
            static void Prefix(ItemDrop __instance, out long __state)
            {
                var view = __instance.m_nview;
                __state = view && view.IsValid() ? view.GetZDO().GetOwner() : 0;
            }
            static void Postfix(ItemDrop __instance, long __0, long __state)
            {
                var view = __instance.m_nview;
                if (!view || !view.IsValid() || !ZNet.instance || ZDOMan.instance == null) return;
                // Vanilla chests already force their ownership update into the next send. Drops do not.
                // Queue only an actual server-to-connected-client grant. Never repeat the pickup RPC.
                if (__state == ZNet.GetUID() && __0 != __state && view.GetZDO().GetOwner() == __0 && ZNet.instance.GetPeer(__0) != null)
                    ZDOMan.instance.ForceSendZDO(__0, view.GetZDO().m_uid);
            }
        }
    }
}
