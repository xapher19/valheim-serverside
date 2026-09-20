using System;
using System.Collections.Generic;
using System.Reflection;
using PluginConfiguration;
using UnityEngine;
using Valheim_Serverside;
using Valheim_Serverside.Features;

class Program
{
    static int checks;
    static void Check(bool ok,string reason) {checks++;if(!ok)throw new Exception(reason);}
    static object Call(Type t,string method,params object[] args)=>t.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
    static ZDO Add(int id,string prefab,float x=0,long owner=0)
    {
        var z=new ZDO{m_uid=id,prefab=prefab.GetStableHashCode(),pos=new Vector3(x,0,0),owner=owner};
        ZDOMan.instance.all[z.m_uid]=z;
        (ZDOMan.instance.m_objectsBySector[0]??=new()).Add(z);return z;
    }
    static GameObject Prefab<T>(string name,T c)
    {
        var p=new GameObject{name=name};p.components[typeof(T)]=c;ZNetScene.instance.m_namedPrefabs[name.GetStableHashCode()]=p;return p;
    }
    static void Advance(double time) {Time.realtimeSinceStartupAsDouble=time;ProductionAreas.Tick();}
    static void Main()
    {
        var b=new CreationBudget();
        Check(b.Next(100,3,.016,60)<=100,"Creation cap exceeded");
        for(int i=0;i<10;i++)b.Observe(10);
        Check(b.Next(100,3,.2,60)==1,"Expensive objects did not reduce allowance");
        Check(b.Next(0,3,.016,60)==1,"Invalid ceiling starves creation");
        var sends=new SendBudget();
        Check(sends.Due(0,3,.1)==0,"Unexpected startup debt");
        Check(sends.Due(10,3,.1)==3,"Long pause must cap debt to one round");
        Check(sends.Take(3)==0,"First peer");
        Check(sends.Due(10,3,.1)==2,"Unserved debt lost at time budget");
        Check(sends.Take(3)==1 && sends.Take(3)==2,"Round robin skipped peers");
        Check(sends.Due(11,0,.1)==0,"Disconnect must clear debt");
        Check(sends.Due(11,1,.1)==0,"Reconnect inherits stale debt");
        Check(sends.Due(11.1,1,.1)<=1,"Send debt unbounded");
        ZNet.instance=new();ZDOMan.instance=new();ZNetScene.instance=new();ZoneSystem.instance=new();
        Prefab("smelter",new Smelter());Prefab("fermenter",new Fermenter());
        var mature=Prefab("carrot",new Pickable());Prefab("seedcarrot",new Plant{m_grownPrefabs=new[]{mature}});
        Prefab("rock",new object());
        var smelter=Add(1,"smelter"); smelter.creator=1; Add(3,"rock",1000);
        ProductionAreas.Installed=true;Advance(0);
        Check(ProductionAreas.Status.Contains("keep-alive off"),"Production keep-alive still presented as active");
        ProductionAreas.Observe(Add(30,"smelter",1000));
        for(int i=0;i<9;i++)ProductionAreas.LoadZones(ZoneSystem.instance);
        Check(ZoneSystem.instance.m_zones.Count==0,"Production still loaded supporting zones");
        Check(!ProductionAreas.Contains(new Vector3(0,0,0)),"Production still treats bases as loaded");
        var objects=new List<ZDO>();ProductionAreas.AddObjects(objects);
        Check(objects.Count==0 && smelter.owner==0,"Production still claimed offline objects");
        var peer=new ZNetPeer{m_uid=5,m_characterID=5};ZNet.instance.peers.Add(peer);var player=Add(5,"player",0,5);
        var ev=new RandomEvent();
        Check((bool)Call(typeof(Production.RaidStartGuard),"Prefix",ev,new Vector3()),"Nearby real player blocked raid");
        player.pos=new Vector3(1000,0,0);
        Check(!(bool)Call(typeof(Production.RaidStartGuard),"Prefix",ev,new Vector3()),"Distant player allowed raid");
        Check((bool)Call(typeof(Production.RaidStartGuard),"Prefix",null,new Vector3()),"Clearing raid blocked");
        object[] spawn={new RandEventSystem{m_activeEvent=ev},new List<SpawnSystem.SpawnData>()};
        Check(!(bool)Call(typeof(Production.RaidSpawnGuard),"Prefix",spawn) && spawn[1]==null,"Unattended raid spawns allowed");
        ZNet.instance.peers.Clear();
        Call(typeof(Production.EmptyWorldClock),"Postfix",ZNet.instance,1f);
        Check(ZNet.instance.m_netTime==1,"Empty world clock stopped");
        ZNet.instance.peers.Add(peer);Call(typeof(Production.EmptyWorldClock),"Postfix",ZNet.instance,1f);
        Check(ZNet.instance.m_netTime==1,"Online world time double advanced");
        ZNet.instance.peers.Clear();Configuration.advanceEmptyTime.Value=false;Call(typeof(Production.EmptyWorldClock),"Postfix",ZNet.instance,1f);
        Check(ZNet.instance.m_netTime==1,"Clock opt-out ignored");
        ZNetScene.instance=new();
        Prefab("RaspberryBush",new Pickable{m_respawnTimeMinutes=240});
        Configuration.farmingEnabled.Value=true; Configuration.farmingFloraRespawnMinutes.Value=30;
        Configuration.farmingCropGrowTimeMin.Value=10; Configuration.farmingCropGrowTimeMax.Value=20;
        Configuration.farmingPlaceAnywhere.Value=true;
        Prefab("RaspberryBush",new Pickable{m_respawnTimeMinutes=240});
        var plantPrefab=Prefab("seedcarrot",new Plant{m_growTime=4000,m_growTimeMax=5000,m_destroyIfCantGrow=true});
        plantPrefab.components[typeof(Piece)]=new Piece();
        FarmingSupport.ApplyPrefabOverrides(ZNetScene.instance);
        Check(ZNetScene.instance.m_namedPrefabs["RaspberryBush".GetStableHashCode()].GetComponent<Pickable>().m_respawnTimeMinutes==30,"Flora respawn override ignored");
        var plant=ZNetScene.instance.m_namedPrefabs["seedcarrot".GetStableHashCode()].GetComponent<Plant>();
        Check(plant.m_growTime==10 && plant.m_growTimeMax==20 && !plant.m_destroyIfCantGrow,"Crop grow/place-anywhere override ignored");
        object[] roof={false}; Check(!(bool)Call(typeof(Farming.PlantHaveRoof),"Prefix",roof) && !(bool)roof[0],"PlaceAnywhere roof not relaxed");
        var spacePlant=new Plant{m_growRadius=2f};
        object[] space={spacePlant,false,0f}; Check(!(bool)Call(typeof(Farming.PlantHaveGrowSpace),"Prefix",space) && (bool)space[1],"PlaceAnywhere growth space not relaxed");
        Configuration.farmingPlaceAnywhere.Value=false;
        Check(FarmingSupport.ScaledGrowRadius(2f)==0.8f,"Grow-space scale ignored");
        object[] tighter={spacePlant,false,0f}; Check((bool)Call(typeof(Farming.PlantHaveGrowSpace),"Prefix",tighter) && spacePlant.m_growRadius==0.8f,"Existing plant grow radius not tightened");
        Call(typeof(Farming.PlantHaveGrowSpace),"Postfix",spacePlant,2f); Check(spacePlant.m_growRadius==2f,"Grow radius not restored after check");
        Configuration.farmingItemPlanting.Value=true; Configuration.farmingItemPlantCost.Value=5; Configuration.farmingItemPlantSpacing.Value=2f;
        Heightmap.Current.cleared=true;
        Prefab("RaspberryBush",new Pickable{m_respawnTimeMinutes=240});
        ZNet.instance.peers.Clear();
        var planter=new ZNetPeer{m_uid=11,m_characterID=11,pos=new Vector3()};
        ZNet.instance.peers.Add(planter);
        var planterChar=Add(11,"player"); planterChar.playerId=42;
        var berryDrop=Add(50,"Raspberry"); berryDrop.stack=5;
        Check(FarmingSupport.TryPlantFromDrop(berryDrop,5,true,42,out var plantedMsg)==1 && plantedMsg!=null && plantedMsg.Contains("Planted"),"Item planting did not place flora");
        Check(!berryDrop.valid,"Planting did not consume the drop");
        ZDO plantedBush=null;
        foreach (var z in ZDOMan.instance.all.Values)
            if (z.GetPrefab()=="RaspberryBush".GetStableHashCode() && z.creator==42) plantedBush=z;
        Check(plantedBush!=null && plantedBush.creator==42,"Planted flora missing creator");
        var shortDrop=Add(51,"Raspberry",80); shortDrop.stack=4;
        Check(FarmingSupport.TryPlantFromDrop(shortDrop,4,true,42,out _)==0,"Undersized stack planted");
        Check(shortDrop.valid,"Undersized stack consumed");
        var wildDrop=Add(52,"Raspberry",90); wildDrop.stack=5;
        Check(FarmingSupport.TryPlantFromDrop(wildDrop,5,false,42,out var groundMsg)==0 && groundMsg!=null && groundMsg.Contains("cultivated"),"Wild ground planting allowed");
        var closeDrop=Add(53,"Raspberry"); closeDrop.stack=5;
        Check(FarmingSupport.TryPlantFromDrop(closeDrop,5,true,42,out _)==1,"Stack on occupied plot did not plant beside it");
        Check(!closeDrop.valid,"Adjacent plant did not consume the drop");
        var fieldDrop=Add(54,"Raspberry",200); fieldDrop.stack=50;
        Check(FarmingSupport.TryPlantFromDrop(fieldDrop,50,true,42,out var manyMsg)==10 && manyMsg!=null && manyMsg.Contains("10"),"50 berries should plant 10 bushes");
        Check(!fieldDrop.valid,"50-stack drop was not consumed");
        int fieldBushes=0;
        foreach (var z in ZDOMan.instance.all.Values)
            if (z.GetPrefab()=="RaspberryBush".GetStableHashCode() && Math.Abs(z.pos.x-200)<20) fieldBushes++;
        Check(fieldBushes==10,"50-berry field was not a 10-bush grid");
        Configuration.farmingEnabled.Value=false; Configuration.farmingPlaceAnywhere.Value=false;
        Configuration.farmingFloraRespawnMinutes.Value=0; Configuration.farmingCropGrowTimeMin.Value=0; Configuration.farmingCropGrowTimeMax.Value=0;
        ProductionAreas.Installed=false;Configuration.advanceEmptyTime.Value=true;
        Call(typeof(Production.EmptyWorldClock),"Postfix",ZNet.instance,1f);Check(ZNet.instance.m_netTime==1,"Failed feature still advances time");
        ServerFeedback.Installed=true;ServerFeedback.Begin();ServerFeedback.ThreadBegin();ServerFeedback.End(true);ServerFeedback.ThreadEnd(null);ServerFeedback.Tick();
        Check(ServerFeedback.Status.Contains("success"),"Save success missing");
        ServerFeedback.ThreadBegin();ServerFeedback.End(false);ServerFeedback.ThreadEnd(null);ServerFeedback.Tick();Check(ServerFeedback.Status=="FAILED","Save failure misreported");
        ServerFeedback.ThreadBegin();ServerFeedback.ThreadEnd(null);ServerFeedback.Tick();Check(ServerFeedback.Status.Contains("unconfirmed"),"Early save exit falsely successful");
        ServerFeedback.End(true);ServerFeedback.Tick();Check(ServerFeedback.Status.Contains("unconfirmed"),"Unrelated save polluted world status");
        ZDOMan.instance.forced.Clear();
        var drop=new ItemDrop{m_nview=new(){zdo=new ZDO{m_uid=42,owner=5}}};ZNet.instance.peers.Add(peer);
        Call(typeof(InteractionReliability.PickupOwnershipDelivery),"Postfix",drop,5L,99L);
        Check(ZDOMan.instance.forced.Count==1,"Pickup ownership grant not prioritised");
        Call(typeof(InteractionReliability.PickupOwnershipDelivery),"Postfix",drop,5L,5L);
        Check(ZDOMan.instance.forced.Count==1,"Duplicate request reprioritised");
        var odd=PortalHub.UnpairedTags(new[]{"Swamp","Swamp","Plains","Home",""});
        Check(odd.Count==1 && odd[0]=="Plains","Odd-count destinations should appear in the hall; Home is reserved");
        Prefab("portal_wood",new TeleportWorld());
        Prefab("sign",new object());
        Prefab("wood_floor_4x4",new object());
        var homePortal=Add(200,"portal_wood");
        var swamp=Add(201,"portal_wood",80); swamp.Set(ZDOVars.s_tag,"Swamp");
        PortalHub.Installed=true;
        Time.realtimeSinceStartupAsDouble=200;
        PortalHub.Tick();
        Check(PortalHub.Status.Contains("destination hall"),"Untagged home did not build a destination hall");
        ZDO lobby=null;
        foreach (var z in ZDOMan.instance.all.Values)
            if (z.GetLong("nw_portal_lobby".GetStableHashCode(),0)!=0) lobby=z;
        Check(lobby!=null && homePortal.connection.Equals(lobby.m_uid),"Home portal does not walk into the hall");
        Check(lobby.connection.Equals(homePortal.m_uid),"Hall Home portal does not return to the untagged portal");
        PortalHub.Installed=false;
        Console.WriteLine($"Passed {checks} production/performance assertions.");
    }
}
