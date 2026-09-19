# Sarkastic.eu Dedicated Simulation

> **Fork of [Serverside Simulations](https://github.com/ddormer/valheim-serverside)** by ddormer, which is no longer maintained as of 2026, renamed at the original authors' request. Updated for Valheim 1.0, building on [ddormer/valheim-serverside#118](https://github.com/ddormer/valheim-serverside/pull/118) by @mreastman.

The dedicated server simulates the world — monsters, physics, ships without a driver — instead of handing each area to whichever player got there first. **Server-side only: players keep vanilla clients.**

Updated for Valheim **1.0.7**.

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
- **Server-side networking limits** from BetterNetworking, with a per-player log of how often they are reached.
- **Cap on Unity job worker threads**, which otherwise idle at CPU cost on many-core hosts.
- **Fix: player changes that the save skipped.** Valheim 1.0 rewrites only the world chunks it marked as changed, and a change received from a player marks nothing, so what a player just built or moved could be missing after a restart.
- **Admin commands on the server console:** `give <item> <amount> <player>`, `players`, `save`, `stop`, typed into the panel the server runs in (AMP). Valheim 1.0 does not let a player on a dedicated server use `spawn` from the game console, admin or not. Optionally the same as chat commands for admins.
- **Smoother server frames:** world updates reach every player at a steady interval however many are online, one slow frame no longer makes the next one slow through physics catch-up, and new zones are generated one per tick instead of one per exploring player. A periodic log shows frame times and what they are spent on.
- **`save` and `stop` console commands** for server panels that write to standard input.
- **Safety:** a startup check warns when a vanilla method the mod replaces has changed in a game update; if the core patches cannot be applied, the mod removes itself and the server runs vanilla.

## Installation

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2350 or newer on the dedicated server.
2. Copy `SarkasticEU_Dedicated_Simulation.dll` from the [latest release](https://github.com/cechacek/valheim-serverside/releases/latest) into `BepInEx/plugins/`.
3. Back up the world and restart the server. `BepInEx/LogOutput.log` should show `Sarkastic.eu Dedicated Simulation installed` and `Vanilla drift check passed`.

Clients need nothing.

**Upgrading from Serverside Simulations:** delete `Serverside_Simulations.dll`. Both use the same plugin GUID, so only one can load; the config file `MVP.Valheim_Serverside_Simulations.cfg` carries over.

**Do not also run BetterNetworking on the server:** its server-side limits are built in.

## Configuration

`BepInEx/config/MVP.Valheim_Serverside_Simulations.cfg`, read at startup:

| Setting | Default | |
|---|---|---|
| `[General] Enabled` | true | Turn the mod off without removing it. |
| `[MaxObjectsPerFrame] MaxObjects` | 100 | Objects the server creates per frame. Higher loads areas faster at more CPU. |
| `[Networking] QueueSizeKB` | 48 | Data queued per player before the server holds world updates for that tick (Valheim: 10). 48 KB at 20 ticks/s is about 960 KB/s, just under the send rate cap; above 80 Steam starts failing. |
| `[Networking] SteamSendRateMinKB` / `MaxKB` | 256 / 1024 | Steam send rate per player, KB/s (Valheim: 150). Keep min × players below the server's upload. |
| `[Networking] StatsIntervalMinutes` | 5 | How often to log, per player, how often the send queue was full. Near 0% means the limits are not what holds you back. 0 disables. |
| `[Server] UnityJobWorkers` | 8 | Upper limit on Unity job worker threads (Unity: one per CPU core). Only ever lowers the count; 0 leaves Unity's default. |
| `[Server] ConsoleCommands` | true | Read commands from standard input: `save`, `stop` (saves first), `players`, `give <item> <amount> <player>` (drops the items in front of that player; the name may be a unique beginning). In AMP this is its console, see the AMP chapter. |
| `[Performance] SendIntervalMs` | 100 | How often each player gets world updates. Valheim serves one player per frame, so with N players each waits N+1 frames (330 ms at 15 FPS with 4 players). Each send costs server CPU; see the performance log. 0 keeps Valheim's behaviour. |
| `[Performance] MaxCatchUpMs` | 100 | Longest frame counted in full. After a slow frame Unity reruns physics and every creature's fixed update for each 20 ms missed (Valheim allows 200 ms, 10 times); 100 caps it at 5. Game time runs slightly slow during such frames. 0 keeps the game's setting. |
| `[Performance] MaxZonesPerTick` | 1 | New zones generated per zone tick (10 per second), players taking turns. 0 = one per player per tick, as before. |
| `[Performance] ServerTargetFps` | 60 | Frame rate the server aims for (the game sets 30). With time to spare a frame no longer waits 33 ms, so reactions to players halve; under load it changes nothing. 0 keeps 30. |
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
