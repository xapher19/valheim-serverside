## 1.11.3 — Magnet that stays on

- Magnet waits for loot to settle (`MagnetSettleSeconds`, default 1.5s) and skips rigidbodies still moving faster than `MagnetMaxSpeed`.
- Pulls with physics velocity (ground slide) instead of hard teleports; hands ownership to the player once inside the 2 m bubble and stops touching that drop.
- Still never steals ownership from a connected player. Default `MagnetPickup=true` again.

## 1.11.2 — Magnet/pickup hitch fix

- Magnet no longer steals ItemDrop ownership from connected players (was causing `wants to pickup` spam and floaty falling logs).
- Magnet keeps horizontal pull only, zeros rigidbody velocity, softer step; **defaults off** (`MagnetPickup=false`). Existing configs must set `MagnetPickup = false` once.
- Craft-from-chests defaults off; chest expand stops after one catch-up pass (no more full-scene `FindObjectsByType` every 5s).

## 1.11.1 — Backpack, craft-from-chests, taller chests

- **ChestExtraRows** (default 1): every player-built chest gets +N inventory rows via HasFields (vanilla clients).
- **Backpack**: Wave emote opens a persistent private-chest backpack (`BackpackSlots`, default 8). Disable ServersideQoL.Backpack if both are loaded.
- **CraftFromChests**: standing at a crafting station temporarily pulls nearby chest materials into your inventory; leftovers return when you walk away (materials shuttle — not Azu UI parity).

## 1.11.0 — Server-forced QoL (vanilla clients)

- New `[QoL]` section (on by default): magnet pickup, instant loot at the killer's feet, structure auto-repair near crafting stations, personalized GlobalKeys for carry weight and near-bench durability (gear does not wear while at a station).
- No client mods. Console / vanilla PC compatible.
- Craft-from-chests and ExtraSlots remain out of scope (crafting emits no RPCs; hotbar is client UI). Use ServersideQoL AutoStore/Backpack for storage QoL.

## 1.10.15 — PortalProgression compatibility

- Treat ServersideQoL **PortalProgression** as compatible with the portal hall (boss-gated ore/metal teleport). Only **AutoPortalHub** is warned as a pairing rival.
- Hall travel still uses vanilla `IsTeleportable` so PortalProgression can strip unlocked cargo before the trip.

## 1.10.14 — Idle FPS and planting log noise

- Default `[Performance] IdleTargetFps` is now `0` (keep `ServerTargetFps` while empty). Set it to `30` if you want the old empty-server CPU save. Verifier keeps re-applying the target if something resets it, and labels idle vs active in the log.
- Stop spamming the server log for leftover single harvest items that cannot plant; only warn a nearby player.
- Warn when ServersideQoL AutoPortalHub is loaded alongside the portal hall (superseded for PortalProgression in 1.10.15).

## 1.10.13 — Sync/CPU reductions (server-only)

- Dirty-set sync lists (only changed objects, full rescan every 30s), ZDO relay throttle (200 ms for non-prioritised objects), and Top-K send sorting.
- Skip undrawn heightmap render meshes; defer `UnloadUnusedAssets` until the server is empty (4h backstop).
- LeanNet-style motion/revision cull on the dedicated host (tiny Vec3/Quat writes + NPC/physics rate limits; ships and players exempt).
- Inspired by ValheimTune / LeanNet; do not also run those mods. Vanilla clients. No ownership changes.

## 1.10.12 — Long-haul networking (server-only)

- Per-peer BDP send window from Steam RTT (capped by `QueueSizeKB`); PlayFab peers keep the fixed cap. Does not change ZDO ownership.
- Optional raised ZRpc/Steam connection timeouts for slow joins.
- Ghost-owner reclaim: quiet peers lose owned objects to the **server** after a short silence while their slot stays until the full timeout.
- Station insert RPCs (smelter/fermenter/cooking/fireplace/shield/turret) re-addressed to the current owner or claimed by the server.
- Filtered Everybody routed-RPC relays so distant peers do not receive unrelated object events.
- Still incompatible with BetterNetworking / NetworkPerformanceSystem on the same server (same send path). Latency-aware player ownership is intentionally not included.

## 1.10.11 — Optional empty public-server password

- Add `[Server] AllowEmptyPassword` (off by default). When enabled, public/crossplay dedicated servers may start without a password. Clear `-password` in the host panel as well.

## 1.10.10 — Stop portal reconnect spam in hall mode

- Take over vanilla `ConnectPortals` while an untagged home portal owns the destination hall. Vanilla was re-pairing hall twins every 5s, then Northwatch rewired them home — endless "Connected portals" log spam and activate VFX.
- Wire hall destinations one-way to world outposts, and send unpaired tagged outposts home, without fighting the 5s reconnect loop.

## 1.10.9 — Portal hall cargo and durability hotfix

- Enforce vanilla teleportable rules on the untagged home portal so ores and other forbidden cargo cannot enter the hall.
- Stop the sky hall from collapsing under WearNTear (no support/roof wear, block damage/destroy on hub pieces, keep the hall zone loaded).

## 1.10.8 — Portal hall hotfix

- Shrink the untagged home-portal enter radius from 4 m to 1.5 m so nearby builds no longer count as walking in.
- Stop re-applying and force-sending unchanged portal connections every scan. That was making vanilla clients replay the portal activate animation every few seconds.

## 1.10.7 — Persistent planted flora and a reachable portal hall

- Mark drop-planted bushes as persistent world objects and dirty their save chunk. Valheim 1.0 `CreateNewZDO` leaves that flag off, so the plants vanished on sleep or restart.
- Read 1.0's portal index (`GetPortalList` / `m_portalObjects` dictionary), stop pairing empty tags with each other, and teleport vanilla clients with `Chat.TeleportPlayer`.
- Move the destination hall well inside the map (not onto the world-edge kill ring) and keep its pieces persistent so the home portal can actually arrive there.

## 1.10.6 — Find every world portal for the destination hall

- Scan all saved portals, not only those in loaded zones, so tagged outposts still appear when nobody is standing next to them.
- Place the destination hall inside the world (high over the outer ocean) so vanilla clients can actually receive the linked hall portal. The previous sky hub sat outside the map and the home portal stayed unconnected.
- Rebuild the hall if its lobby is missing, and treat walking within 4 m of an untagged home portal as entering it.

## 1.10.5 — Walk through an untagged home portal

- Walking into an untagged home portal now teleports vanilla clients into the destination hall. The hall sits outside normal world sync, so 1.10.4 could wire the portal without the client ever arriving.
- The server force-sends hall and destination portal objects to connected players, then teleports anyone who steps into an untagged home portal.
- An untagged portal with no named outposts still builds the hall (a sign tells you to name one). Tagged world portals return home.

## 1.10.4 — Consume dropped harvest into a flora grid

- Dropping a stack on cultivated ground plants as many bushes as the stack pays for (50 blueberries → 10 bushes at cost 5) in a spaced grid, then destroys or reduces the drop so it cannot be farmed infinitely.
- The server takes ownership of the drop, updates `ItemDrop` stack, and force-sends the change so vanilla clients see the berries vanish.
- HUD messages report planted count, too-few items, or missing cultivated ground.

## 1.10.3 — No persistent base loading

- Stop keeping bases, farms and stations loaded while nobody is nearby. The production supporting ring was still loading adjacent dungeons.
- `[Production] Enabled` retains raid start/spawn guards and the optional empty-server world clock only.
- Tighten plant/tree grow radius to 40% of vanilla (`[Farming] GrowSpaceScale`). Existing saplings pick this up when they next tick.
- Untagged home portal walks into a labeled destination hall (vanilla portals + signs). Tagged world portals return home. Empty portals are no longer paired with each other.

## 1.10.2 — Vanilla-client flora planting

- Vanilla clients plant berry bushes, mushrooms, thistle, dandelion and similar pickable flora by dropping the matching harvest item on cultivated ground (default cost 5, 2 s settle, 2 m spacing). No client mod and no cultivator recipes.
- The server consumes the drop, places the flora, and sets a piece creator on the planted object.
- `[Farming] ItemPlanting` is on by default. `[Farming] PlaceAnywhere` also allows these drops off cultivated ground.

## 1.10.1 — Server-side farming, tighter production, portal hub

- Keep player-planted berry bushes, mushrooms and flowers loaded under Production (`[Production] Flora`). Wild flora is ignored. Compatible with flora planted via item drops (1.10.2) without a client planting mod.
- Require a piece creator for all production anchors so wild beehives/sap collectors and other world props no longer pin zones. Default `[Production] Livestock` to false.
- Add `[PortalHub]` (on by default): generate a sky hub that pairs unpaired portal tags. Remove ServersideQoL AutoPortalHub when using this. Independently implemented; ServersideQoL source is not bundled.
- Add optional `[Farming]` retunes (off by default): flora respawn minutes, crop grow times, and PlaceAnywhere / sunlight / growth-space relaxation for server simulation only.
- Do not add cultivator recipes, client UI or ServerSync. Extra flora is planted by dropping harvest items (1.10.2).

## 1.10.0 — Persistent production and bounded server work

- Keep generated areas around smelters (including kiln/windmill/spinning-wheel variants), fermenters, cooking stations, planted crops, beehives, sap collectors, tamed livestock and hatchable tame-animal eggs loaded on the server. Mature crop pickables remain anchors; planted trees stop being anchors when grown.
- Rebuild the production index incrementally from saved world objects after restart, discover newly placed stations, and release areas after their last anchor is removed. No extra world-save format or client mod.
- Require a connected player's character near the event for raid starts and raid spawns. Production anchors never count as players. Raid guards and production hooks install atomically; failed guards disable production, while Core retains its own atomic rollback.
- Optionally advance world time with nobody connected (enabled by default). This advances days/weather as well as production. No catch-up while the server is stopped.
- Adapt object-creation allowance to measured costs and frame pressure, enforce its cap for large backlogs, and avoid duplicate sector searches for players in the same zone.
- Budget round-robin world sends across frames, keeping bounded unserved work for the next frame instead of a catch-up burst. Existing vanilla packets and transport limits remain unchanged.
- Prioritise successful dropped-item ownership grants in the next world update, as vanilla already does for chests. No automatic repeated pickup or inventory actions.
- Lower the empty-server frame cap to 30 by default and restore the active target on connection; physics and production continue.
- Add save-start/result and console-shutdown announcements. Status distinguishes the game's save commit result from mere save-thread completion; this is not independent disk verification.
- Document Production and related Performance/Server settings in the README, with regression and real-game hook checks for the new features.

## 1.9.4 — Northwatch

- Rename the plugin, DLL, assembly metadata, packaging and status display to Northwatch Dedicated Simulation.
- Add consolidated custom-build patch notes to the README and point downloads to this fork.
- Preserve the compatibility GUID and existing configuration filename. Remove the previous simulation DLL when upgrading.
- No gameplay or networking-policy changes.

## 1.9.3 (xapher19)

- Add a server-panel `status` command with recent FPS, targets, player count, observed save state and refreshed per-hook registration health.
- Add independent send observations: connection totals for attempts, submissions, blocked attempts, other no-submission outcomes and maximum gaps.
- Label PlayFab queue values as scaled budget metrics; never call its unsupported send-rate getter.
- Add sustained low-FPS and queue-pressure alerts with grace periods, cooldowns and recovery messages.
- Add periodic memory/GC snapshots and opt-in, globally rate-limited chest/pickup/missing-object-RPC traces.
- Isolate each optional Diagnostics hook, preserving Core's atomic rollback. Add diagnostics regression and real-game installation checks.

## 1.9.2 (xapher19)

- Keep an eligible nearby client as an idle ship's owner, with driver priority, cargo protection and server fallback when players disconnect or leave range.
- Reset water-impact protection only when ownership actually changes to the server.
- Recheck player-sector invalidation after received player data is applied, so observers at a portal entrance receive the existing removal notification after the player leaves.
- Add world synchronisation regression checks and real-game ship/receive-hook installation tests. Run the full build on pull requests as well as main.

## 1.9.1 (xapher19)

- Bind the FPS request prefix by argument index to support Valheim 1.0.15's parameter names.
- Check real Harmony installation against the downloaded game method in the DLL build, with the old binding as a negative control.
- Identify the custom build as plugin/file version 1.9.1 and informational version 1.9.1-xapher19.

## [1.9.0] - 2026-09-12

### Fixed

- `[Fixes] SaveClientChanges`: Valheim 1.0 saves only the world chunks it marked as changed, and a change that arrives from a player for an object the player owns marks nothing, so the new state lived only in memory until something else in that chunk changed. The chunk is now marked when such a change arrives. With this mod the server owns nearly everything near players, so the window was small: what a player just built, the ship they steer, their drops. Reported for 1.0 by ValheimCommunityPatch.


## [1.8.0] - 2026-09-11

### Added

- Console replies are also written to standard output, so a panel such as AMP shows them even with BepInEx's console off.
- Console commands `give <item> <amount> <player>` (drops the items in front of that player, stacked as the item allows; the name may be a unique beginning) and `players`, next to `save` and `stop`, for the panel the server runs in. Valheim 1.0 only lets the host use cheat commands, so `spawn` from the game console says "not valid in the current context" on a dedicated server even for admins.
- `[AdminChat]` (off by default): admins listed in adminlist.txt can shout `/give <item> [amount]`, `/save` and `/help`; replies go to their console.
- The performance log now says what the slowest frame of each period was doing: players' messages, world updates, object creation, zone generation, creature logic, other game updates, saving, and how much was Unity's own work (physics, garbage collection), with the garbage collections that ran in that frame. On the live server a ~300 ms frame showed up almost every 5 minutes without a known cause.


## [1.7.0] - 2026-09-11

### Added

- `[Performance] ServerTargetFps` (60): the game sets a dedicated server to 30 FPS, so a frame finished in 12 ms still lasts 33 ms and every reaction to a player waits for it. Measured on the live server with one player: 30 FPS at a median frame of 35 ms while the game logic took a fraction of that. 60 halves the wait whenever the server has the headroom and changes nothing under load. 0 keeps the game's 30.


## [1.6.0] - 2026-09-11

### Added

- `[Performance]` settings that smooth the server's frame time, and a periodic performance log (`StatsIntervalMinutes`): FPS, frame time (average, median, 95%, 99%, worst), fixed steps per frame and the share of time their game logic takes, the cost of sending world updates and of generating zones. With this mod the server simulates everything, so its frame time is what players feel: a felled tree turns into wood only after the server has caught up.
- `SendIntervalMs` (100): every player gets world updates at a fixed interval, the sends spread over the frames in between. Valheim serves one player per frame, so with N players each waited N+1 frames: at 15 FPS with 4 players about 330 ms, and longer with every player who joins. Measured under load with 4 players: about 3x as many sends for 0.4 percentage points more of the main thread.
- `MaxCatchUpMs` (100): after a slow frame Unity reruns the fixed step (physics, every character, creature AI) once per 20 ms missed; Valheim allowed 200 ms of catch-up, i.e. 10 steps after one bad frame, which made the next frame bad too. Now at most 5. Game time runs slightly slow during such frames.
- `MaxZonesPerTick` (1): new zones are generated one per zone tick, players taking turns, instead of one per exploring player in the same frame. Measured under load with 4 players exploring: the worst zone tick fell from 70-177 ms to 22-47 ms, with the same number of zones generated per minute. It caps how many zones a tick generates, not what one costs: a zone holding a large location can still take a few hundred ms.

None of these raise the frame rate; they cut the spikes and the waiting between server and player. Measured on a local copy of a live world with four simulated players walking outward and an artificial 50 ms of load per frame plus a 300 ms stall every 10 s.


## [1.5.0] - 2026-09-10

### Added

- Console commands on standard input: `save` saves the world, `stop` saves and shuts down cleanly. Lets server panels such as AMP stop the server without losing progress since the last autosave (AMP: App.ExitMethod=String, BepInEx console disabled).
- Cap Unity job worker threads at 8 (`[Server] UnityJobWorkers`). Unity starts one per CPU core and idle ones still use CPU; an idle server on a 24-thread machine went from 108% to 38% of a core.
- The server creates the objects nearest to a player first. It sorted them by its own reference position, which on a dedicated server lies outside the world, so after a portal or entering a new area the surroundings of a player could be the last to become active. Idea from upstream PR #100 by jsza.


### Changed

- Default per-player send queue raised from 32 KB to 48 KB. On a live server with four players it was full in about 0.1% of send ticks, only in bursts (portals, new areas), peaking at the 32 KB limit. 48 KB at 20 ticks/s is about 960 KB/s, just under the Steam send rate cap. Existing config files keep their value.


### Fixed

- Generate ghost zones around players again, as vanilla does. The ZoneSystem.Update replacement only created local zones, which reach the near simulation distance, so unexplored land in the far ring was not generated ahead of players and distant objects there (large trees, cliffs, the Mistlands mist) appeared only much closer.


## [1.3.0] - 2026-09-10

### Added

- Server-side networking limits from BetterNetworking (by CW-Jesse, MIT): per-player send queue 32 KB instead of 10 KB and Steam send rate 256-1024 KB/s instead of 150 KB/s, configurable under [Networking], with a periodic log of how often each player's send queue was full. Clients stay vanilla.


## [1.2.0] - 2026-09-10

### Changed

- Upstream renamed its fork of Serverside Simulations by ddormer. The plugin GUID is unchanged; delete `Serverside_Simulations.dll` when upgrading.
- Game members are accessed directly instead of through Traverse, so game updates that rename them fail the build instead of silently doing nothing. If a Core patch fails to apply the mod now removes all its patches and the server runs vanilla. A startup check logs a warning when a vanilla method replaced by the mod has changed since it was last reviewed.


### Fixed

- Update for Valheim 1.0 (Vector2s zones and SimulationDistance). Based on ddormer/valheim-serverside#118 by @mreastman, which also fixed item pickup and the Pickable.RPC_Pick null reference.
- Fix the ZoneSystem.Update replacement dropping vanilla behaviour: location prefabs loaded by the server were never released (a memory leak), zones were created while locations were still being generated, and ZoneSystem.TimeSinceStart stayed at zero.
- Fix the server never creating objects when started with a non-classic -simulationdistance (any value other than 0 or 2).
- Fix Valheim 1.0 null references on the server: ship sails changing (repeated every physics frame while the sail moved), taking items from a Frost Foundry (the item was duplicated and stayed in the station) and leviathans diving.
- Update ValheimPlus compatibility (GetNearbyChests)


## [1.1.9] - 2025-05-14

### Fixed

- Fix missing effects (Revert EffectList.Create patch)


## [1.1.8] - 2025-04-20

### Added

- Remove `AudioMan.Update`, reducing future error spam


### Fixed

- Fix null references to `WearNTear.m_bounds` and `Humanoid.m_currentAttack.m_character`
- Fix shield generators and audio log spam


## [1.1.7] - 2024-11-12

### Added

- Environment.props and bepinex publicizer


### Fixed

- Update method references to static, fixing Bog Witch launch issue. Thanks to @bpage-dev


## [1.1.6] - 2023-11-22

### Fixed

- Fix exception when updating ship owner in 0.217.28
- Fix MaxObjectsPerFrame transpiler for Valheim 0.217.28


## [1.1.5] - 2023-10-25

### Fixed

- Fix private method access errors in Release build


## [1.1.4] - 2023-10-24

### Changed

- Mistlands update


### Misc

- Fix AssemblyPublicizer output path.
- Update BepInEx, Harmony and MonoMod libs


## [1.1.4] - 2023-10-24

### Changed

- Mistlands update


## [1.1.3] - 2021-09-22

### Fixed

- Fix Valheim Plus autofuel compatibility.


## [1.1.2] - 2021-09-16

### Changed

- Remove old fishing fixes (appears to have been fixed in latest Valheim patch)


## [1.1.1] - 2021-05-14

### Changed

- Update to BepInEx 5.4.10


### Fixed

- Fix fishing


## [1.1.0] - 2021-05-07

### Added

- Added "max objects per frame" configuration. Allowing for faster or slower area loading on the server.


## [1.0.3] - 2021-04-21

### Fixed

- Fix Ship container access and improve Ship ownership transfer
- Fix Ship taking 10 damage when owner changes to server.


## [1.0.2] - 2021-04-19

### Fixed

- Fix objects not being created on the server in 0.150.3.


## [1.0.1] - 2021-04-15

### Fixed

- Prevent event monsters from spawning outside of the random event area, during a random event.
