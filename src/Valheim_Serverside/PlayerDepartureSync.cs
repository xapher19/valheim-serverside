namespace Valheim_Serverside
{
	internal static class PlayerDepartureSync
	{
		internal static void AfterReceive(ZDOMan manager, ZRpc rpc)
		{
			if (!ZNet.instance || !ZNet.instance.IsServer()) return;
			ZNetPeer peer = manager.FindPeer(rpc)?.m_peer;
			if (peer == null || !peer.IsReady() || peer.m_server || peer.m_characterID.IsNone()) return;
			ZDO character = manager.GetZDO(peer.m_characterID);
			if (character == null || character.GetOwner() != peer.m_uid) return;

			// In 1.0.15 InternalSetPosition calls SetSector (and its invalidation callback)
			// BEFORE assigning m_position. That callback tests the old location, so a
			// portal departure can leave observers holding an out-of-date player object.
			// Recheck only this sender's character after the packet has been applied.
			// Vanilla queues invalidations only for peers that know the ZDO and are now
			// out of range; it excludes the owner and removes the cached send revision.
			// SendZDOs delivers the existing invalid-sector message under its usual budget.
			// No world ZDO is destroyed, and no client RPC or packet format is changed.
			manager.ZDOSectorInvalidated(character);
		}
	}
}
