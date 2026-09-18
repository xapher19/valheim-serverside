using System;
using UnityEngine;
using Valheim_Serverside;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Reset()
    {
        ZNet.instance = new ZNet();
        ZDOMan.instance = new ZDOMan();
        Player.Players.Clear();
        Time.time = 10;
    }
    private static ZNetPeer Peer(long id, float position = 0)
    {
        var peer = new ZNetPeer { m_uid = id, m_characterID = new ZDOID((int)id), Position = new Vector3(position) };
        ZNet.instance.Peers.Add(peer);
        ZDOMan.instance.Peers.Add(new ZDOMan.ZDOPeer { m_peer = peer });
        Player.Players[id] = new Player { Owner = id };
        return peer;
    }
    private static Ship Boat(long owner) => new() { m_nview = new ZNetView { Zdo = new ZDO { Owner = owner } } };
    private static void Boats()
    {
        Reset();
        var first = Peer(1, 20);
        var second = Peer(2, 5);
        var boat = Boat(1);
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 1 && boat.m_nview.Zdo.OwnerWrites == 0, "Idle owner should remain despite a closer peer");
        Check(boat.m_lastWaterImpactTime == -20, "Idle tick must not reset damage protection");
        boat.m_shipControlls.User = 2;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 2, "Valid driver must have priority");
        boat.m_shipControlls.User = 0;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 2 && boat.m_nview.Zdo.OwnerWrites == 1, "Releasing helm must retain owner");
        boat.m_nview.Zdo.InUse = 1;
        boat.m_shipControlls.User = 1;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 2, "Cargo interaction must not be interrupted");
        second.m_socket.Connected = false;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 1, "Disconnected cargo owner must not strand ship");
        boat.m_nview.Zdo.InUse = 0;
        boat.m_shipControlls.User = 0;
        first.Position = new Vector3(1000);
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 100 && boat.m_lastWaterImpactTime == 10, "No eligible client must hand back to server with protection");
        Time.time = 12;
        int writes = boat.m_nview.Zdo.OwnerWrites;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.OwnerWrites == writes && boat.m_lastWaterImpactTime == 10, "Repeated server ownership must not reset damage timer");
        first.Position = new Vector3(5);
        second.Position = new Vector3(10);
        second.m_socket.Connected = true;
        boat.m_players.Add(null);
        boat.m_players.Add(Player.Players[2]);
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 2, "Passenger should precede nearer shore observer when choosing a new owner");
        second.Position = new Vector3(1000);
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 1, "Out-of-range owner/passenger must be replaced");
        boat.m_shipControlls.User = 99;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 1, "Unresolved driver should keep healthy owner");
        first.m_characterID = default;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 100, "Peer without character must not simulate the boat");
        first.m_characterID = new ZDOID(1);
        boat.m_nview.Zdo.InUse = 1;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 100, "Server cargo interaction should retain owner");
        boat.m_nview.Zdo.InUse = 0;
        ZNet.instance.Server = false;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 100, "Client guard must prevent ownership writes");
        ZNet.instance.Server = true;
        boat.m_nview.Valid = false;
        ShipOwnership.Update(boat);
        Check(boat.m_nview.Zdo.Owner == 100, "Invalid view must be ignored");
        boat.m_nview = null;
        ShipOwnership.Update(boat);
        Check(true, "Missing view safely ignored");
    }
    private static void PortalDeparture()
    {
        Reset();
        var traveller = Peer(1);
        var entranceObserver = Peer(2);
        var destinationObserver = Peer(3, 1000);
        var character = new ZDO { m_uid = traveller.m_characterID, Owner = 1 };
        var manager = ZDOMan.instance;
        manager.Objects[character.m_uid] = character;
        var sourceCache = manager.FindPeer(entranceObserver.m_rpc);
        var ownerCache = manager.FindPeer(traveller.m_rpc);
        var destinationCache = manager.FindPeer(destinationObserver.m_rpc);
        sourceCache.Known.Add(character.m_uid);
        ownerCache.Known.Add(character.m_uid);
        // Reproduce the native ordering: sector callback runs BEFORE new position is stored.
        manager.ZDOSectorInvalidated(character);
        Check(sourceCache.Invalid.Count == 0, "Pre-move callback does not notice departure");
        character.Position = new Vector3(1000);
        traveller.Position = character.Position;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        Check(sourceCache.Invalid.Contains(character.m_uid), "Post-receive check must queue source observer removal");
        Check(!sourceCache.Known.Contains(character.m_uid), "Source send cache must be cleared for future re-entry");
        Check(ownerCache.Invalid.Count == 0 && ownerCache.Known.Contains(character.m_uid), "Traveller's own character must not be invalidated");
        Check(destinationCache.Invalid.Count == 0, "Unseen destination character must not be invalidated");
        Check(manager.Objects[character.m_uid] == character && character.Owner == 1, "Character must not be destroyed or reassigned");
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        Check(sourceCache.Invalid.Count == 1, "Queued invalidation must be bounded and survive delayed sending");
        sourceCache.Invalid.Clear(); // Represents vanilla delivering the queued invalidation.
        destinationCache.Known.Add(character.m_uid);
        character.Position = new Vector3(1010);
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        Check(destinationCache.Known.Contains(character.m_uid) && destinationCache.Invalid.Count == 0, "Local movement must keep nearby observer");
        character.Position = new Vector3(0);
        traveller.Position = character.Position;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        Check(destinationCache.Invalid.Contains(character.m_uid), "Return teleport must invalidate the other endpoint");
        Check(!sourceCache.Known.Contains(character.m_uid), "Returning player must be eligible for a fresh full update");
        sourceCache.Known.Add(character.m_uid); // Native sync list sends it again on re-entry.
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        Check(sourceCache.Invalid.Count == 0, "Returned player must stay visible at source");

        int calls = manager.InvalidationChecks;
        PlayerDepartureSync.AfterReceive(manager, new ZRpc());
        traveller.m_characterID = default;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        traveller.m_characterID = new ZDOID(99);
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        traveller.m_characterID = character.m_uid;
        character.Owner = 2;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        character.Owner = 1;
        traveller.m_server = true;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        traveller.m_server = false;
        ZNet.instance.Server = false;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        ZNet.instance = null;
        PlayerDepartureSync.AfterReceive(manager, traveller.m_rpc);
        Check(manager.InvalidationChecks == calls, "Unknown/missing/misowned/client-side characters must be ignored");
    }
    public static void Main()
    {
        Boats();
        PortalDeparture();
        Console.WriteLine($"Passed {checks} world synchronisation assertions.");
    }
}
