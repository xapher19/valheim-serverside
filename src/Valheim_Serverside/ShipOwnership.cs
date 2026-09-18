using UnityEngine;

namespace Valheim_Serverside
{
	internal static class ShipOwnership
	{
		// A peer must still have the ship in its simulation area. A departed/disconnected
		// driver must not leave an idle ship without a simulator.
		private static bool Eligible(long uid, Vector3 position)
		{
			ZNetPeer peer = ZNet.instance.GetPeer(uid);
			return peer != null && peer.IsReady() && !peer.m_server
				&& peer.m_socket != null && peer.m_socket.IsConnected()
				&& !peer.m_characterID.IsNone()
				&& ZDOMan.instance.IsInPeerActiveArea(position, uid);
		}

		internal static void Update(Ship ship)
		{
			if (!ZNet.instance || !ZNet.instance.IsServer() || !ship.m_nview || !ship.m_nview.IsValid()) return;
			ZDO zdo = ship.m_nview.GetZDO();
			long current = zdo.GetOwner();
			long server = ZNet.GetUID();
			Vector3 position = zdo.GetPosition();
			bool keepCurrent = Eligible(current, position);

			// Do not interrupt an open cargo container. A stale disconnected client is
			// the exception: it cannot finish the interaction or simulate the ship.
			if (zdo.GetInt("InUse", 0) != 0 && (current == server || keepCurrent)) return;

			long wanted = 0L;
			if (ship.m_shipControlls != null && ship.m_shipControlls.HaveValidUser())
			{
				Player driver = Player.GetPlayer(ship.m_shipControlls.GetUser());
				if (driver != null && Eligible(driver.GetOwner(), position)) wanted = driver.GetOwner();
			}
			// Releasing the helm must not immediately move buoyancy back to the server's
			// camera-dependent weather state. Keep a healthy nearby owner first.
			if (wanted == 0L && keepCurrent) wanted = current;
			if (wanted == 0L)
			{
				foreach (Player passenger in ship.m_players)
				{
					if (passenger != null && Eligible(passenger.GetOwner(), position))
					{
						wanted = passenger.GetOwner();
						break;
					}
				}
			}
			if (wanted == 0L)
			{
				float nearest = float.MaxValue;
				foreach (ZNetPeer peer in ZNet.instance.GetPeers())
				{
					if (!Eligible(peer.m_uid, position)) continue;
					float distance = (peer.GetRefPos() - position).sqrMagnitude;
					if (distance < nearest)
					{
						nearest = distance;
						wanted = peer.m_uid;
					}
				}
			}
			if (wanted == 0L) wanted = server;
			if (wanted == current) return;

			// Protect the handoff once; do not continually suppress normal water damage.
			if (wanted == server) ship.m_lastWaterImpactTime = Time.time;
			zdo.SetOwner(wanted);
			ServersidePlugin.logger.LogDebug($"Ship ownership: {current} -> {wanted}");
		}
	}
}
