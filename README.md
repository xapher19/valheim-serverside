# Northwatch Dedicated Simulation

> **Northwatch** builds on [cechacek’s upstream fork](https://github.com/cechacek/valheim-serverside) and the original project.

> **Fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside)** by ddormer, which is no longer maintained as of 2026, renamed at the original authors' request. Updated for Valheim 1.0, building on [ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118) by @mreastman.

The dedicated server simulates the world — monsters, physics, ships without a driver — instead of handing each area to whichever player got there first. **Server-side only: players keep vanilla clients.**

Current custom build: **Northwatch 1.10.15**, compiled and hook-tested against Valheim **1.0.15**. The inherited drift fingerprints retain their original review baseline.

## Patch notes

### 1.10.15 — PortalProgression compatibility
Keep ServersideQoL **PortalProgression** for boss-gated portal cargo. Northwatch only replaces **AutoPortalHub** pairing; hall teleports still honour vanilla `IsTeleportable` so progression stripping works.

### 1.10.14 — Idle FPS and planting log noise

- Empty servers keep `ServerTargetFps` by default (`IdleTargetFps` now 0). Quieter item-planting logs. Warn if ServersideQoL portal mods conflict with the hall.

### 1.10.13 — Sync/CPU reductions (server-only)

- Dirty-set sync, ZDO relay throttle, Top-K sort, skip heightmap render mesh, defer asset unload, and server-side motion cull. Vanilla clients.

### 1.10.12 — Long-haul networking (server-only)

- Per-peer BDP send windows, raised connection timeouts, ghost-owner reclaim to the server, station RPC re-addressing, and filtered Everybody relays. Vanilla clients. Does not hand simulation to players.

### 1.10.11 — Optional empty public-server password

- Opt-in `[Server] AllowEmptyPassword`: public/crossplay dedicated servers can start with no password. Clear `-password` in the host panel too.

### 1.10.10 — Stop portal reconnect spam in hall mode

- Untagged home / destination hall no longer fights vanilla's 5-second portal reconnect loop (the repeating `Connected portals` log lines).

### 1.10.9 — Portal hall cargo and durability hotfix

- Untagged home portal now blocks non-teleportable items the same way vanilla portals do.
- Destination hall no longer slowly destroys itself from unsupported sky pieces.

### 1.10.8 — Portal hall hotfix

- Untagged home portal enter radius is tighter (1.5 m). Standing near the portal no longer teleports you.
- Portal activate VFX no longer flashes every few seconds: the server only rewires/force-sends portal links when they actually change.

### 1.10.7 — Persistent planted flora and a reachable portal hall

- Drop-planted bushes now save with the world. They were disappearing after sleep or a restart because Valheim 1.0 creates those objects as non-persistent.
- The untagged home portal uses vanilla `Chat.TeleportPlayer` into a hall that is inside the map (not on the world-edge kill ring). Tagged outposts are read from the server's portal index even when their zone is unloaded.

### 1.10.6 — Find every world portal for the destination hall

- Tagged outposts are included even when their zones are not loaded. The untagged home portal links to a hall that vanilla clients can receive.
- Leave the home portal untagged, name the outposts, then walk into the home portal.

### 1.10.5 — Walk through an untagged home portal

- Walking into an untagged home portal teleports vanilla clients into the labeled destination hall. 1.10.4 could link the portal without the client ever arriving, because the hall is outside normal world sync.
- **Leave the home portal untagged.** Name at least one outpost portal (anything except `Home`). Then walk into the glowing home portal — do not only place it or press E to set a tag. In the hall, walk into the portal whose sign you want. Tagged world portals return home.
- With no named outposts yet, the hall still appears; a sign tells you to name one.

### 1.10.4 — Consume dropped harvest into a flora grid

- A dropped stack on cultivated ground plants a whole grid (50 berries → 10 bushes at the default cost of 5) and **consumes those items**. Leftovers under the cost stay in the drop.
- Vanilla clients see the pile shrink or disappear. Planting no longer leaves the original stack to pick up again.

### 1.10.3 — No persistent base loading

- Stop keeping bases, farms and stations loaded while nobody is nearby. The old production ring was still pulling in adjacent dungeons and a large RAM set.
- `[Production] Enabled` now only keeps raid start/spawn guards and the optional empty-server world clock. Zones load around connected players, as in vanilla dedicated simulation.
- Tighten plant/tree grow radius to **40% of vanilla** (`[Farming] GrowSpaceScale`). Already-planted saplings use the smaller check as soon as they are simulated.
- One **untagged home portal** walks into a labeled destination hall of vanilla portals and signs. Walk into the portal you want. Tagged world portals return home. Untagged portals no longer pair with each other. Naming an outpost `Home` is reserved for the hall's return portal; pressing E and typing a tag is an optional shortcut.

### 1.10.2 — Vanilla-client flora planting

- Vanilla clients plant berry bushes, mushrooms, thistle, dandelion and similar pickable flora by **dropping the matching harvest item** on cultivated ground (default 5 items, 2 s settle, 2 m spacing). No client mod and no cultivator recipes.
- The server consumes the stack, places the flora, and sets a piece creator on the planted object.
- `[Farming] ItemPlanting` is on by default. `[Farming] PlaceAnywhere` also allows these drops off cultivated ground.

### 1.10.1 — Server-side farming, tighter production, portal hub

- Keep **player-planted** berry bushes, mushrooms and flowers loaded under Production (`[Production] Flora`, on by default). Wild flora is ignored so meadows are not pinned.
- **Production anchors now require a piece creator** (player-built). Wild beehives, sap collectors and other world props no longer pin zones or load nearby dungeons. `[Production] Livestock` defaults to off.
- Built-in **portal hub** (`[PortalHub]`, on by default): unpaired portal tags get a matching hub portal in a sky platform. Remove ServersideQoL **AutoPortalHub** so it does not fight pairing. Keep **PortalProgression** (boss-gated ores through portals) — it is compatible. Independently implemented from public behaviour docs; that mod’s source is not bundled.
- Optional `[Farming]` retunes (off by default): flora respawn minutes, crop grow times, and PlaceAnywhere / sunlight / growth-space relaxation for server-side plant simulation.
- Does **not** add cultivator recipes, meshes, hover UI or ServerSync. Planting extra flora uses item drops (1.10.2), not a client planting mod.
### 1.10.0 — Persistent production and bounded server work

- Automatically keep generated areas around smelters (including kiln/windmill/spinning-wheel variants), fermenters, cooking stations, planted crops, beehives, sap collectors, tamed livestock and hatchable tame-animal eggs loaded on the server. Mature crop pickables remain anchors; planted trees stop being anchors when grown.
- Rebuild the production index incrementally from saved world objects after restart, discover newly placed stations, and release areas after their last anchor is removed. No extra world-save format or client mod.
- Require a connected player's character near the event for raid starts and raid spawns. Production anchors never count as players. Raid guards and production hooks install atomically; failed guards disable production, while Core retains its own atomic rollback.
- Optionally advance world time with nobody connected (enabled by default). This advances days/weather as well as production. No catch-up while the server is stopped.
- Adapt object-creation allowance to measured costs and frame pressure, enforce its cap for large backlogs, and avoid duplicate sector searches for players in the same zone.
- Budget round-robin world sends across frames, keeping bounded unserved work for the next frame instead of a catch-up burst. Existing vanilla packets and transport limits remain unchanged.
- Prioritise successful dropped-item ownership grants in the next world update, as vanilla already does for chests. No automatic repeated pickup or inventory actions.
- Lower the empty-server frame cap to 30 by default and restore the active target on connection; physics and production continue.
- Add save-start/result and console-shutdown announcements. Status distinguishes the game's save commit result from mere save-thread completion; this is not independent disk verification.

### 1.9.4 — Northwatch

- Renamed the plugin, assembly metadata, DLL, packaging and status output to **Northwatch Dedicated Simulation**.
- New DLL: `Northwatch_Dedicated_Simulation.dll`. Remove the previous simulation DLL before installing; do not load both.
- Updated installation/release links to this fork and added these patch notes.
- Preserved the plugin GUID and existing `MVP.Valheim_Serverside_Simulations.cfg`; settings carry over. No gameplay changes in this release.

### 1.9.3 — Diagnostics

- Added the server-panel `status` command, per-player send attempt/submission counts, queue-pressure and low-FPS alerts, and memory/GC reports.
- Added optional, rate-limited chest/pickup/missing-RPC diagnostics.
- Corrected PlayFab queue labels and isolated optional diagnostics hooks.

### 1.9.2 — Boats and portals

- Keep a suitable nearby client as an idle boat’s owner; retain driver priority, cargo protection and server fallback.
- Reset water-impact protection only on an actual handoff to the server.
- Recheck player departures after incoming position updates to address stale portal replicas.
- Added automatic PR builds and world-synchronisation regression checks.

### 1.9.1 and initial hardening

- Fixed FPS-hook argument binding and added actual-game installation checks.
- Isolated optional Performance hooks, added startup patch-health reporting and verified/fell back to the configured FPS target.
- Preserved Core’s atomic rollback and vanilla-client/PS5 compatibility.

See [CHANGELOG.md](CHANGELOG.md) for the full release history, including 1.10.0 and earlier upstream notes. Automated checks verify logic and installation; live unattended-production, raid and portal cleanup still need observer tests.

## Why, compared to vanilla

In vanilla, the first player to enter an area owns it: their game runs the monster AI and physics there, and everyone else nearby sees that area through them. If that player has a poor connection or a slow PC, everyone around suffers — monsters jump around, hits land late — and updates travel from each player to the server, on to the owner and back.

With this mod the server owns and simulates those areas:

- Each player depends only on their own connection to the server, not on someone else's.
- Clients no longer run AI and physics for the areas they would have owned, which helps slower PCs.
- Ships are handed to their driver, so steering has no round trip. Releasing the helm retains a suitable nearby client as owner; unattended ships fall back to the server when no eligible client remains.

What it costs:

- The server needs more CPU, RAM and upload than a vanilla server.
- A player alone in an area now has their round trip to the server where vanilla would have had none. With a nearby server this is rarely noticeable.

### Observed on one server

Valheim 1.0.7, Windows dedicated server, up to four players, September 2026. One group's session, not a benchmark.

- No exceptions or mod warnings during play.
- Items picked up from the ground: 98% of 437 on the first ownership request (1.2.0), 214 of 214 (1.5.0); the rest within 2 s.
- The per-player send queue was full in at most 0.2% of send ticks, only in bursts such as portals.
- About 0.8 of a CPU core on average with one player, and 15–25% of a core while empty (with the job worker cap). RAM about 1.6 GB empty, 2–2.6 GB with players, levelling off.

Not covered yet: Frost Foundry, sailing, raids, Deep North events, a non-default `-simulationdistance`, and the console commands under a Windows server panel.

## What this fork adds

Compared to Serverside Simulations 1.1.9 (details in the [changelog](CHANGELOG.md)):

- **Valheim 1.0 support**, and a review of every patched method against the 1.0 code.
- **Fixes:** location prefabs were never released (a memory leak); zones could be generated before their locations; no objects were created with a non-classic `-simulationdistance`; 1.0 errors on the server with ship sails, the Frost Foundry (which duplicated items) and leviathans; the far ring of unexplored land was not pre-generated as in vanilla, so distant trees, cliffs and the Mistlands mist appeared late.
- **Objects nearest to a player are created first**, e.g. after a portal.
- **Server-side networking** (BetterNetworking-style limits plus BDP windows, timeouts, ghost reclaim, station routing, relay filtering), with a per-player log of queue pressure.
- **Cap on Unity job worker threads**, which otherwise idle at CPU cost on many-core hosts.
- **Fix: player changes that the save skipped.** Valheim 1.0 rewrites only the world chunks it marked as changed, and a change received from a player marks nothing, so what a player just built or moved could be missing after a restart.
- **Admin commands on the server console:** `give <item> <amount> <player>`, `players`, `save`, `stop`, typed into the panel the server runs in (AMP). Valheim 1.0 does not let a player on a dedicated server use `spawn` from the game console, admin or not. Optionally the same as chat commands for admins.
- **Smoother server frames:** world updates reach every player at a steady interval however many are online, one slow frame no longer makes the next one slow through physics catch-up, and new zones are generated one per tick instead of one per exploring player. A periodic log shows frame times and what they are spent on.
- **Raid guards and empty-world clock (1.10.3):** raid starts/spawns still need a real player nearby. Bases are **not** kept loaded when players leave (that was loading nearby dungeons). Optional empty-server world-time advance, adaptive object creation, budgeted sends, prioritised pickup grants, idle FPS cap, save announcements, and optional `[Farming]` retunes. Vanilla clients plant extra flora by dropping harvest items on cultivated ground (`[Farming] ItemPlanting`).
- **`save` and `stop` console commands** for server panels that write to standard input.
- **Safety:** a startup check warns when a vanilla method the mod replaces has changed in a game update; if the core patches cannot be applied, the mod removes itself and the server runs vanilla.

## Installation

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or newer on the dedicated server.
2. Stop the server and remove the previous simulation plugin DLL from `BepInEx/plugins/` (including subfolders). Keep its backup outside the plugins folder. Copy `Northwatch_Dedicated_Simulation.dll` from the [latest release](https://github.com/xapher19/valheim-serverside/releases/latest) into `BepInEx/plugins/`.
3. Back up the world and restart the server. `BepInEx/LogOutput.log` should show `Northwatch Dedicated Simulation installed` and `Vanilla drift check passed`.

Clients need nothing.

**Upgrading from Serverside Simulations:** delete `Serverside_Simulations.dll`. Both use the same plugin GUID, so only one can load; the config file `MVP.Valheim_Serverside_Simulations.cfg` carries over.

**Do not also run BetterNetworking, NetworkPerformanceSystem, ValheimTune, SkadiNet, or LeanNet on the server:** they overlap the same send/sync paths, and NPS/SkadiNet ownership fights Northwatch's serverside simulation.

## Configuration

`BepInEx/config/MVP.Valheim_Serverside_Simulations.cfg`, read at startup:

| Setting | Default | |
|---|---|---|
| `[General] Enabled` | true | Turn the mod off without removing it. |
| `[MaxObjectsPerFrame] MaxObjects` | 100 | Objects the server creates per frame. Higher loads areas faster at more CPU. |
| `[MaxObjectsPerFrame] Adaptive` | true | Adjust creation allowance to measured cost and frame pressure, still bounded by MaxObjects. |
| `[MaxObjectsPerFrame] BudgetMs` | 3 | Soft object-creation time budget; a single expensive object cannot be interrupted. |
| `[Networking] QueueSizeKB` | 48 | Max data queued per player before the server holds world updates for that tick (Valheim: 10). Also the BDP window cap. Above 80 Steam starts failing. |
| `[Networking] EnableBdpWindow` | true | Size each player's send window from Steam RTT (rate × RTT × factor), clamped 10 KB–QueueSizeKB. PlayFab keeps QueueSizeKB. |
| `[Networking] BdpTargetRateKBps` / `BdpFactor` | 150 / 1.25 | Target throughput and headroom for BDP window sizing. |
| `[Networking] EnableTimeoutTuning` | true | Raise ZRpc and Steam quiet-connection timeouts (see Connection/LoadingTimeoutSeconds). |
| `[Networking] ConnectionTimeoutSeconds` / `LoadingTimeoutSeconds` | 90 / 120 | Quiet-connection timeouts while playing / loading (vanilla 30 / 90). |
| `[Networking] EvictGhostOwners` / `GhostEvictSeconds` | true / 10 | Reclaim quiet peers' owned ZDOs to the server after this silence; slot kept until full timeout. |
| `[Networking] RouteStationRequestsToOwner` | true | Re-address station item RPCs to the current owner (or claim for the server). |
| `[Networking] EnableRelayFiltering` / `LimitRelayByDistance` | true / false | Drop Everybody object relays for peers who neither know nor are near the object. Distance-only is opt-in. |
| `[Networking] SteamSendRateMinKB` / `MaxKB` | 256 / 1024 | Steam send rate per player, KB/s (Valheim: 150). Keep min × players below the server's upload. |
| `[Networking] StatsIntervalMinutes` | 5 | How often to log, per player, how often the send queue was full (includes RTT/window when known). 0 disables. |
| `[Server] UnityJobWorkers` | 8 | Upper limit on Unity job worker threads (Unity: one per CPU core). Only ever lowers the count; 0 leaves Unity's default. |
| `[Server] ConsoleCommands` | true | Read commands from standard input: `save`, `stop` (saves first), `players`, `give <item> <amount> <player>` (drops the items in front of that player; the name may be a unique beginning). In AMP this is its console, see the AMP chapter. |
| `[Server] SaveAnnouncements` | true | Show save start/result and console-shutdown announcements to vanilla clients. |
| `[Server] AllowEmptyPassword` | false | Allow public/crossplay dedicated servers to start with no password. Also clear `-password` in the host panel. |
| `[Production] Enabled` | true | Raid start/spawn guards and optional empty-world clock. Does **not** keep bases loaded. Restart required. |
| `[Production] AdvanceTimeWhenEmpty` | true | Advance world time (days/weather) with no players. No offline catch-up. |
| `[PortalHub] Enabled` | true | Leave one home portal untagged and walk through it to pick a labeled destination. Name outposts; tagged world portals return home. |
| `[PortalHub] Include` / `Exclude` | `*` / empty | Wildcard tag filters for hub pairing. |
| `[PortalHub] AutoNameNewPortals` | false | Auto-name empty portal tags before pairing. |
| `[PortalHub] AutoNameFormat` | `{0} {1:D2}` | Biome name + unique integer. |
| `[Farming] Enabled` | false | Optional server-side grow/respawn/restriction retunes. No cultivator recipes. Restart required. |
| `[Farming] ItemPlanting` | true | Drop matching harvest items on cultivated ground to plant a grid of bushes/mushrooms/flowers. Consumes the stack. Vanilla clients. |
| `[Farming] ItemPlantCost` / `ItemPlantSpacing` / `ItemPlantSettleSeconds` | 5 / 2 / 2 | Items consumed per plant; grid spacing in metres; seconds a drop must sit before planting. |
| `[Farming] PlaceAnywhere` | false | Relax plant roof, growth-space and ground checks while Farming is enabled. Also allows item planting off cultivated ground. |
| `[Farming] RequireSunlight` / `RequireGrowthSpace` | true / true | When false (and Farming enabled), skip the matching plant check. |
| `[Farming] GrowSpaceScale` | 0.4 | Server grow-radius multiplier for plants/trees. Applies to existing saplings. 1 is vanilla. |
| `[Farming] CropGrowTimeMin` / `Max` | 0 / 0 | Override plant grow times when Farming is enabled; 0 leaves vanilla/mod values. |
| `[Farming] FloraRespawnMinutes` | 0 | Override pickable respawn for configured flora when Farming is enabled; 0 leaves vanilla/mod values. |
| `[Farming] ExtraFloraPrefabs` | empty | Extra exact prefab names for flora respawn overrides. Restart required. |
| `[Performance] SendIntervalMs` | 100 | How often each player gets world updates. Valheim serves one player per frame, so with N players each waits N+1 frames (330 ms at 15 FPS with 4 players). Each send costs server CPU; see the performance log. 0 keeps Valheim's behaviour. |
| `[Performance] SendBudgetMs` | 3 | Soft scheduled-send budget per frame; rotate fairly and retain bounded debt. 0 disables. |
| `[Performance] DirtySets` | true | Only sync objects that changed since the last round (full rescan every ReconcileSeconds). Large win on big bases. |
| `[Performance] ReconcileSeconds` | 30 | Full sync-list rescan interval when DirtySets is on. |
| `[Performance] RelayMinIntervalMs` | 200 | Min ms between re-sending the same non-prioritised object to a peer. 0 = off. Requires DirtySets. |
| `[Performance] TopKSort` / `TopK` | true / 0 | Bounded-heap send sort; 0 = derive K from QueueSizeKB. |
| `[Performance] SkipRenderMesh` | true | Skip undrawn heightmap render meshes on dedicated. |
| `[Performance] DeferAssetUnload` / `AssetUnloadMaxDeferMinutes` | true / 240 | Hold UnloadUnusedAssets until empty (or backstop). |
| `[Performance] MotionCull` (+ Hz / Vec3) | true | Cull tiny motion ZDO writes and rate-limit NPC/physics revisions on the server. |
| `[Performance] MaxCatchUpMs` | 100 | Longest frame counted in full. After a slow frame Unity reruns physics and every creature's fixed update for each 20 ms missed (Valheim allows 200 ms, 10 times); 100 caps it at 5. Game time runs slightly slow during such frames. 0 keeps the game's setting. |
| `[Performance] MaxZonesPerTick` | 1 | New zones generated per zone tick (10 per second), players taking turns. 0 = one per player per tick, as before. |
| `[Performance] ServerTargetFps` | 60 | Frame rate the server aims for (the game sets 30). With time to spare a frame no longer waits 33 ms, so reactions to players halve; under load it changes nothing. 0 keeps 30. |
| `[Performance] IdleTargetFps` | 0 | Empty-server frame cap (never above ServerTargetFps); 0 keeps ServerTargetFps while empty. Try 30 to save CPU when nobody is online. |
| `[Fixes] SaveClientChanges` | true | Count a change that arrives from a player as a change to its world chunk, so the next save writes it. Valheim 1.0 rewrites only changed chunks and skips those. |
| `[AdminChat] Enabled` | false | Admins (adminlist.txt) can shout `/give <item> [amount]`, `/save` and `/help`; replies appear in their console (F5). The shout is visible to players nearby; the server console does the same without it. |
| `[AdminChat] Prefix` / `MaxGiveAmount` | `/` / 1000 | Command prefix; most items one `/give` drops. |
| `[Performance] StatsIntervalMinutes` | 5 | How often to log FPS, frame times, physics steps per frame, the cost of world updates and zone generation, and what the slowest frame was doing, while players are online. 0 disables. |

## Hosting notes

- **After a game update**, look for `Vanilla ... changed` warnings in the log, and keep world backups.
- Running under AMP (CubeCoders)? Settings and pitfalls (Sleep mode, stop without save, console commands) are in [cechacek/amp-valheim-bepinex](https://github.com/cechacek/amp-valheim-bepinex).

## Caveats

- Only runs on dedicated servers.
- Uses considerably more server resources than vanilla; a weak CPU or little RAM may make play worse, not better.
- Disable the mod when using the `optterrain` command.
- It does not prevent cheating or any kind of client manipulation.
- Game updates can break it in unexpected ways; back up characters and worlds before updating.

## How it works

Ordinarily, to keep server resource usage low, the Valheim server hands off simulation of an area to the first client that enters it. This mod makes terrain, monsters and other objects that are normally created and owned by clients be created on — and thus owned and simulated by — the server, around every connected player.

#### For mod developers - compatibility

This mod keeps the plugin GUID of Serverside Simulations, `MVP.Valheim_Serverside_Simulations`, so existing checks for it keep working.

If your mod changes the simulation or behaviour of the world, it has to be able to run on the dedicated server:
- `Player.m_localPlayer` is always `null` on a dedicated server; check for it.
- On a dedicated server, `ZNet.instance.GetReferencePosition()` returns a position outside of the world, unrelated to any player.
- Graphics or HUD code should be behind a `ZNet.instance.IsDedicated()` check if it can run on the server.

## Patch health and FPS verification

Startup logs report each Core and Performance hook as `ACTIVE` after checking its
Harmony registration. Core remains atomic: any failed or missing Core hook rolls
back all simulation patches and prevents the Performance/FPS update loop starting.
Each optional Performance hook has a separate Harmony owner, so a failed profiler
or scheduler only removes that hook. Missing send/game-logic timing is reported as
`unavailable` rather than zero activity. Other missing profiling sections contribute
to the report's `unaccounted` time; consult startup health before interpreting it.
Registration confirms installation, not that a game method has executed.

On dedicated servers, five seconds after installation the plugin checks
`Application.targetFrameRate` against `ServerTargetFps` (clamped to 30–240). If they
differ, it warns and applies one direct fallback, then checks again ten seconds
later. Further overrides warn without repeated writes; changing the configured
value permits a new fallback attempt. A value at or below zero leaves the game's
target alone. Verification checks the requested frame cap, not achieved FPS under
load; periodic performance reports still show actual throughput.

These changes are server-only and add no client requirements or network protocol
changes. See [hardening regression checks](tests/Hardening/README.md) for automated
checks and the dedicated-server smoke test, including vanilla/PS5 clients.

## Ship ownership and portal departures (1.9.2)

Idle ships prefer an eligible existing client owner, then a passenger, then the
nearest eligible peer. Drivers take priority unless cargo is in use. Eligibility
requires a connected, ready player with the ship in their active simulation area.
Disconnected or out-of-range owners are replaced; the server remains the fallback.
The server handoff resets water-impact protection once, rather than every idle
tick. Normal water-impact damage is therefore no longer continually suppressed.

This addresses a plausible cause of idle boats disagreeing with client-visible
waves: the dedicated server's camera-dependent environment need not match the
players' weather. It does not change buoyancy coefficients, global weather, send
rates or client interpolation, and it is not a promise of exact wave alignment.

After a player's incoming world-data packet, the server rechecks the accepted
character position against observers' areas. In the inspected 1.0.15 code, the
original sector callback runs before the new position is stored and can miss a
portal departure. The recheck uses vanilla sector invalidation and send budgets;
it does not destroy the player's world object, change ownership, or add a client
RPC. Returning players can receive normal fresh updates after the cache is cleared.

The build checks both new hooks against the downloaded game. See the
[world synchronisation tests](tests/WorldSync/README.md) for regression coverage and
the live PC/PS5 test procedure. Both hooks are in Core's atomic patch group.

## Building

Create `src/Environment.props` pointing at a Valheim dedicated server install that has BepInEx:

```
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <!-- Needs to be your path to the base Valheim dedicated server folder -->
    <VALHEIM_DEDI_INSTALL>E:\SteamLibrary\steamapps\common\Valheim dedicated server</VALHEIM_DEDI_INSTALL>
  </PropertyGroup>
</Project>
```

Then, from the repository root (Windows or Linux, tested with .NET SDK 10):

```
dotnet build src/Valheim_Serverside/Serverside_Simulations.csproj -c Release -p:SolutionDir=<repository root>/
```

The DLL ends up in `bin/Release/`. `SolutionDir` is needed when building the project on its own; building `Valheim_Serverside.sln` sets it.

## Diagnostics (1.9.3)

Type `status` in the hosting panel's server console. This uses the existing
standard-input command reader; `[Server] ConsoleCommands` must be enabled and the
host must forward typed input. It is not a new in-game/PS5 console command.
The response includes version, simulation state, recent measured FPS, configured
and effective frame caps, connected peers, save activity and hook registration.
Reading status does not reset counters. Registration is rechecked on demand; it
is not proof that the hook has executed or that gameplay is correct. Save completion
means the game reported finishing; this command does not verify disk-write success.

The `[Diagnostics]` settings are:

| Setting | Default | Meaning |
|---|---:|---|
| Enabled | true | Enable observations; restart after changing |
| ReportMinutes | 5 | Memory and per-connection summaries; 0 disables summaries only |
| Alerts | true | Warn about sustained low FPS and continuous queue blockage |
| LowFpsThreshold | 25 | Threshold for completed approximately five-second FPS windows |
| AlertDurationSeconds | 30 | Sustained problem duration before warning |
| AlertCooldownSeconds | 300 | Minimum time between warnings for a signal |
| InteractionTrace | false | Sample chest, pickup and missing object RPC dispatches |

FPS alerts wait 60 seconds after diagnostics start and require connected, ready
players. Queue alerts wait 60 seconds after first observing a connection. A recovery
message follows a warned episode. Gaps longer than two seconds between send
observations break continuity; silence is not treated as proof of congestion.

Send totals are cumulative for the observed connection, not per reporting window.
Shutdown flushes are excluded. A submitted packet can contain only object removals.
An attempt that sends nothing can simply have no changes to send. Submission gaps
may therefore be normal idle time; neither these gaps nor server dispatch time
measure client delivery or visible interaction latency. Reconnects start fresh,
disconnected peers are pruned, and at most 128 connections are tracked.

PlayFab's queue-budget metric in the inspected 1.0.15 game is one quarter of its
in-flight bytes. Steam send-rate settings do not apply to PlayFab, including PC
players connecting through crossplay. The unsupported PlayFab send-rate getter is
never called. Existing Networking summaries retain their configured interval and
now label the socket and budget metric; Diagnostics supplies the richer totals.

Memory snapshots use non-collecting managed heap reads, process working set when
available, and collection-count deltas since the previous memory report. They do
not force collection or enumerate all Unity objects.

Interaction tracing observes `RPC_RequestOpen`, `RPC_RequestOwn`, `RPC_Pick`, and
unknown object-RPC hashes, at most one sampled dispatch per two seconds globally.
It records sender ID, transport, object/prefab, owner, handler presence and server
dispatch duration, without reading or logging payloads. It does not change RPC
handling; unsampled errors still follow the game's normal logging. Tracing may
miss an individual interaction by design. Enable only while investigating.

These additions do not change send budgets, simulation distance, update priority,
boat physics or garbage collection. No client installation is required.

## Persistent production (removed in 1.10.3)

Northwatch 1.10.0–1.10.2 kept zones loaded around player-built stations and farms. That supporting ring loaded nearby dungeons and used a large amount of RAM, so **bases are no longer kept loaded** when players leave. Simulation follows connected players.

`[Production] Enabled` still installs raid start/spawn guards (a real nearby player is required) and the optional empty-server world clock. `[Farming] ItemPlanting` still lets vanilla clients plant flora by dropping harvest items.

Optional `[Farming]` (off by default) can retune server-side plant grow times, flora respawn minutes, and plant roof/growth-space checks. Northwatch does not add cultivator recipes or client UI.

A raid can start/spawn only with a connected character in its configured event range (typically the vanilla event radius), in the same outdoor height band. Leaving stops further raid spawning; the existing event's lifecycle and already-spawned creatures otherwise follow vanilla behaviour. Manually requested random raids are subject to the same start guard. Forced-event spawns also require real nearby players.

| Setting | Default | Effect |
|---|---|---|
| `[Production] Enabled` | true | Raid start/spawn guards; does not keep bases loaded; restart required. |
| `[Production] AdvanceTimeWhenEmpty` | true | World time, including day/weather, advances with no players. No offline catch-up. |
| `[PortalHub] Enabled` | true | Leave one home portal untagged and walk through it to pick a labeled destination. Name outposts; tagged world portals return home. Remove AutoPortalHub; keep PortalProgression. |
| `[PortalHub] Include` / `Exclude` | `*` / empty | Wildcard filters on portal tags. |
| `[PortalHub] AutoNameNewPortals` | false | Name empty tags using AutoNameFormat before pairing. |
| `[PortalHub] AutoNameFormat` | `{0} {1:D2}` | `{0}`=biome name, `{1}`=unique integer. |
| `[Farming] Enabled` | false | Optional server grow/respawn/restriction retunes; no cultivator recipes; restart required. |
| `[Farming] ItemPlanting` | true | Drop harvest items on cultivated ground to plant a matching flora grid and consume the stack; vanilla clients. |
| `[Farming] ItemPlantCost` / `ItemPlantSpacing` / `ItemPlantSettleSeconds` | 5 / 2 / 2 | Items per plant; grid spacing in metres; settle delay in seconds. |
| `[Farming] PlaceAnywhere` | false | Relax plant roof, growth-space and ground checks while Farming is enabled. Also allows item planting off cultivated ground. |
| `[Farming] RequireSunlight` / `RequireGrowthSpace` | true / true | When false (and Farming enabled), skip the matching plant check. |
| `[Farming] GrowSpaceScale` | 0.4 | Server grow-radius multiplier for plants/trees. Applies to existing saplings. 1 is vanilla. |
| `[Farming] CropGrowTimeMin` / `Max` | 0 / 0 | Override plant grow times when Farming is enabled; 0 leaves vanilla/mod values. |
| `[Farming] FloraRespawnMinutes` | 0 | Override pickable respawn for configured flora when Farming is enabled; 0 leaves vanilla/mod values. |
| `[Farming] ExtraFloraPrefabs` | empty | Extra exact prefab names for flora respawn overrides; restart required. |
| `[MaxObjectsPerFrame] Adaptive` | true | Adjust creation allowance to measured cost and frame pressure. |
| `[MaxObjectsPerFrame] BudgetMs` | 3 | Soft creation budget; individual operations cannot be interrupted. `MaxObjects` remains the ceiling. |
| `[Performance] SendBudgetMs` | 3 | Soft scheduled-send budget per frame; 0 disables the budget. |
| `[Performance] IdleTargetFps` | 0 | Empty-server cap, never above active target; 0 keeps ServerTargetFps. Requires a positive `ServerTargetFps`. |
| `[Server] SaveAnnouncements` | true | Vanilla in-game save/result and console-shutdown messages. |
| `[Server] AllowEmptyPassword` | false | Allow public/crossplay dedicated servers to start with no password. Clear `-password` in the host panel too. |

Validation includes regression tests and hook installation against real game assemblies. Live gameplay validation is still required for raid, portal-hub and planting behaviour. Installation: stop the server, back up the world, replace the existing Northwatch DLL, then inspect startup patch health and `status`.
