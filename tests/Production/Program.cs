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
        var smelter=Add(1,"smelter"); smelter.creator=1; var crop=Add(2,"seedcarrot"); crop.creator=1; Add(3,"rock",1000);
        ProductionAreas.Installed=true;Advance(0);
        Check(ProductionAreas.Status.Contains("2 anchors"),"Existing world anchors not discovered");
        ProductionAreas.Observe(Add(30,"smelter",1000));
        Check(ProductionAreas.Status.Contains("2 anchors"),"Creatorless station became an anchor");
        for(int i=0;i<9;i++)ProductionAreas.LoadZones(ZoneSystem.instance);
        Check(ZoneSystem.instance.m_zones.Count==9,"Supporting ring not loaded");
        Check(!ProductionAreas.Contains(new Vector3(1000,0,0)),"Unrelated world activated");
        var objects=new List<ZDO>();ProductionAreas.AddObjects(objects);
        Check(objects.Count==2 && smelter.owner==99,"Offline objects not owned by server");
        foreach(var zone in ZoneSystem.instance.m_zones.Values)zone.m_ttl=3;
        ProductionAreas.LoadZones(ZoneSystem.instance);
        foreach(var zone in ZoneSystem.instance.m_zones.Values)Check(zone.m_ttl==0,"Loaded zone TTL not refreshed");
        ZDOMan.instance.all.Remove(crop.m_uid);var grown=Add(4,"carrot"); grown.creator=1; ProductionAreas.Observe(grown);Advance(2);
        Check(ProductionAreas.Status.Contains("2 anchors"),"Mature crop replacement lost anchor or leaked old one");
        var peer=new ZNetPeer{m_uid=5,m_characterID=5};ZNet.instance.peers.Add(peer);var player=Add(5,"player",0,5);
        smelter.owner=5;ProductionAreas.AddObjects(new());Check(smelter.owner==5,"Live nearby client ownership stolen");
        peer.near=false;ProductionAreas.AddObjects(new());Check(smelter.owner==99,"Absent client ownership not reclaimed");
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
        ZDOMan.instance.all.Remove(smelter.m_uid);ZDOMan.instance.all.Remove(grown.m_uid);Advance(4);
        Check(ProductionAreas.Status.Contains("0 anchors, 0"),"Destroyed production area retained");
        ZNetScene.instance=new();Configuration.productionExclude.Value=" smelter ";Prefab("smelter",new Smelter());
        var excluded=Add(6,"smelter"); excluded.creator=1; Advance(6);Check(ProductionAreas.Status.Contains("0 anchors"),"Prefab exclusion ignored");
        ZNetScene.instance=new(); Prefab("boar",new Tameable()); Prefab("egg",new EggGrow{m_tamed=true});
        Configuration.productionLivestock.Value=true;
        var wild=Add(7,"boar",2000);var tame=Add(8,"boar",3000);tame.tamed=true;tame.creator=2;Add(9,"egg",3000).creator=2;
        Advance(8);Check(ProductionAreas.Status.Contains("2 anchors"),"Wild animals anchored or tame/egg anchors missing");
        tame.tamed=false;Advance(10);Check(ProductionAreas.Status.Contains("1 anchors"),"Untamed animal anchor retained");
        Configuration.productionLivestock.Value=false;
        ZDOMan.instance=new(); ZNetScene.instance=new(); ZoneSystem.instance=new();
        Prefab("RaspberryBush",new Pickable{m_respawnTimeMinutes=240});
        Add(20,"RaspberryBush",4000); var plantedBerry=Add(21,"RaspberryBush",4100); plantedBerry.creator=7;
        Advance(12);Check(ProductionAreas.Status.Contains("1 anchors"),"Wild flora anchored or planted flora missing");
        ZDOMan.instance.all.Remove(plantedBerry.m_uid);Advance(14);Check(ProductionAreas.Status.Contains("0 anchors"),"Removed planted flora retained");
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
        object[] space={false}; Check(!(bool)Call(typeof(Farming.PlantHaveGrowSpace),"Prefix",space) && (bool)space[0],"PlaceAnywhere growth space not relaxed");
        Configuration.farmingEnabled.Value=false; Configuration.farmingPlaceAnywhere.Value=false;
        Configuration.farmingFloraRespawnMinutes.Value=0; Configuration.farmingCropGrowTimeMin.Value=0; Configuration.farmingCropGrowTimeMax.Value=0;
        ProductionAreas.Installed=false;Configuration.advanceEmptyTime.Value=true;
        Call(typeof(Production.EmptyWorldClock),"Postfix",ZNet.instance,1f);Check(ZNet.instance.m_netTime==1,"Failed feature still advances time");
        ServerFeedback.Installed=true;ServerFeedback.Begin();ServerFeedback.ThreadBegin();ServerFeedback.End(true);ServerFeedback.ThreadEnd(null);ServerFeedback.Tick();
        Check(ServerFeedback.Status.Contains("success"),"Save success missing");
        ServerFeedback.ThreadBegin();ServerFeedback.End(false);ServerFeedback.ThreadEnd(null);ServerFeedback.Tick();Check(ServerFeedback.Status=="FAILED","Save failure misreported");
        ServerFeedback.ThreadBegin();ServerFeedback.ThreadEnd(null);ServerFeedback.Tick();Check(ServerFeedback.Status.Contains("unconfirmed"),"Early save exit falsely successful");
        ServerFeedback.End(true);ServerFeedback.Tick();Check(ServerFeedback.Status.Contains("unconfirmed"),"Unrelated save polluted world status");
        var drop=new ItemDrop{m_nview=new(){zdo=new ZDO{m_uid=42,owner=5}}};ZNet.instance.peers.Add(peer);
        Call(typeof(InteractionReliability.PickupOwnershipDelivery),"Postfix",drop,5L,99L);
        Check(ZDOMan.instance.forced.Count==1,"Pickup ownership grant not prioritised");
        Call(typeof(InteractionReliability.PickupOwnershipDelivery),"Postfix",drop,5L,5L);
        Check(ZDOMan.instance.forced.Count==1,"Duplicate request reprioritised");
        Console.WriteLine($"Passed {checks} production/performance assertions.");
    }
}
