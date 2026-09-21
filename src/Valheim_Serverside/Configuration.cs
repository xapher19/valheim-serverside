using BepInEx.Configuration;

namespace PluginConfiguration
{
	public class Configuration
	{
		public static ConfigEntry<bool> modEnabled;
		public static ConfigEntry<bool> productionEnabled, productionLivestock, productionFlora, advanceEmptyTime, adaptiveLoading, saveAnnouncements;
        public static ConfigEntry<bool> farmingEnabled, farmingPlaceAnywhere, farmingRequireSunlight, farmingRequireGrowthSpace, farmingItemPlanting;
        public static ConfigEntry<bool> portalHubEnabled, portalHubAutoName;
        public static ConfigEntry<int> productionScanBudget, idleFps, farmingFloraRespawnMinutes, farmingItemPlantCost;
        public static ConfigEntry<float> loadingBudgetMs, sendBudgetMs, farmingCropGrowTimeMin, farmingCropGrowTimeMax, farmingItemPlantSpacing, farmingItemPlantSettleSeconds, farmingGrowSpaceScale;
        public static ConfigEntry<string> productionExclude, farmingExtraFlora, portalHubInclude, portalHubExclude, portalHubAutoNameFormat;
        public static ConfigEntry<bool> diagnosticsEnabled, diagnosticAlerts, interactionDiagnostics;
        public static ConfigEntry<int> diagnosticReportMinutes, alertDurationSeconds, alertCooldownSeconds, lowFpsThreshold;

		public static ConfigEntry<bool> maxObjectsPerFrameEnabled;
		public static ConfigEntry<int> maxObjectsPerFrame;

		public static ConfigEntry<bool> networkingEnabled;
		public static ConfigEntry<int> networkQueueSizeKB;
		public static ConfigEntry<int> networkSendRateMinKB;
		public static ConfigEntry<int> networkSendRateMaxKB;
		public static ConfigEntry<int> networkStatsMinutes;
		public static ConfigEntry<bool> networkBdpWindowEnabled;
		public static ConfigEntry<int> networkBdpTargetRateKBps;
		public static ConfigEntry<float> networkBdpFactor;
		public static ConfigEntry<bool> networkTimeoutEnabled;
		public static ConfigEntry<int> networkConnectionTimeoutSeconds;
		public static ConfigEntry<int> networkLoadingTimeoutSeconds;
		public static ConfigEntry<bool> networkGhostEvictEnabled;
		public static ConfigEntry<float> networkGhostEvictSeconds;
		public static ConfigEntry<bool> networkStationRoutingEnabled;
		public static ConfigEntry<bool> networkRelayFilterEnabled;
		public static ConfigEntry<bool> networkRelayLimitByDistance;

		public static ConfigEntry<bool> consoleCommandsEnabled;
		public static ConfigEntry<int> unityJobWorkers;

		public static ConfigEntry<int> sendIntervalMs;
		public static ConfigEntry<int> maxCatchUpMs;
		public static ConfigEntry<int> maxZonesPerTick;
		public static ConfigEntry<int> performanceStatsMinutes;
		public static ConfigEntry<int> serverTargetFps;

		public static ConfigEntry<bool> syncDirtySets;
		public static ConfigEntry<float> syncReconcileSeconds;
		public static ConfigEntry<int> syncRelayMinIntervalMs;
		public static ConfigEntry<bool> syncTopKSort;
		public static ConfigEntry<int> syncTopK;
		public static ConfigEntry<bool> skipRenderMesh;
		public static ConfigEntry<bool> deferAssetUnload;
		public static ConfigEntry<int> assetUnloadMaxDeferMinutes;
		public static ConfigEntry<bool> motionCullEnabled;
		public static ConfigEntry<float> motionCullPhysicsHz;
		public static ConfigEntry<float> motionCullNpcHz;
		public static ConfigEntry<float> motionCullVec3Meters;

		public static ConfigEntry<bool> fixSaveClientChanges;
		public static ConfigEntry<bool> allowEmptyPassword;

		public static ConfigEntry<bool> adminChatEnabled;
		public static ConfigEntry<string> adminChatPrefix;
		public static ConfigEntry<int> adminChatMaxGive;

		public static ConfigEntry<bool> qolEnabled;
		public static ConfigEntry<bool> qolMagnetPickup;
		public static ConfigEntry<float> qolMagnetRadius;
		public static ConfigEntry<float> qolMagnetStep;
		public static ConfigEntry<float> qolMagnetSettleSeconds;
		public static ConfigEntry<float> qolMagnetMaxSpeed;
		public static ConfigEntry<bool> qolInstantLoot;
		public static ConfigEntry<float> qolInstantLootRange;
		public static ConfigEntry<bool> qolStructureRepair;
		public static ConfigEntry<float> qolStationRange;
		public static ConfigEntry<float> qolCarryWeightRate;
		public static ConfigEntry<bool> qolNoDurabilityNearStation;
		public static ConfigEntry<int> qolChestExtraRows;
		public static ConfigEntry<bool> qolBackpack;
		public static ConfigEntry<string> qolBackpackEmote;
		public static ConfigEntry<int> qolBackpackSlots;
		public static ConfigEntry<bool> qolCraftFromChests;
		public static ConfigEntry<float> qolCraftChestRange;
		public static ConfigEntry<int> qolCraftMaxItems;

		public static void Load(ConfigFile config)
		{
			productionEnabled = config.Bind("Production", "Enabled", true, "Raid starts/spawns require a real nearby player, and optionally advance world time while empty. Does not keep bases loaded (that pulled in nearby dungeons). Requires restart.");
            productionLivestock = config.Bind("Production", "Livestock", false, "Unused. Production no longer keeps areas loaded around tamed animals.");
            productionFlora = config.Bind("Production", "Flora", true, "Unused. Production no longer keeps areas loaded around planted flora.");
            advanceEmptyTime = config.Bind("Production", "AdvanceTimeWhenEmpty", true, "Continue the world clock while empty when production is active. Days/weather also advance. No catch-up while the server is stopped.");
            productionScanBudget = config.Bind("Production", "ScanEntriesPerFrame", 2048, new ConfigDescription("Maximum sector/object scan steps per frame when finding existing production after restart.", new AcceptableValueRange<int>(64, 16384)));
            productionExclude = config.Bind("Production", "ExcludedPrefabs", "", "Comma-separated exact prefab names excluded from automatic production anchors. Requires restart.");
            farmingEnabled = config.Bind("Farming", "Enabled", false, "Optional dedicated-server farming retunes (grow/respawn/restriction). Does not add cultivator recipes. Requires restart.");
            farmingItemPlanting = config.Bind("Farming", "ItemPlanting", true, "Vanilla clients plant bushes and other pickable flora by dropping the matching harvest item on cultivated ground. A stack plants a grid (cost items each) and is consumed.");
            farmingItemPlantCost = config.Bind("Farming", "ItemPlantCost", 5, new ConfigDescription("Harvest items consumed per planted flora object.", new AcceptableValueRange<int>(1, 100)));
            farmingItemPlantSpacing = config.Bind("Farming", "ItemPlantSpacing", 2f, new ConfigDescription("Minimum metres between same-type planted flora.", new AcceptableValueRange<float>(0.5f, 20f)));
            farmingItemPlantSettleSeconds = config.Bind("Farming", "ItemPlantSettleSeconds", 2f, new ConfigDescription("Seconds a drop must sit before it is planted, so it can still be picked up.", new AcceptableValueRange<float>(0f, 30f)));
            farmingPlaceAnywhere = config.Bind("Farming", "PlaceAnywhere", false, "Relax plant roof, growth-space and ground checks while Farming is enabled. Also allows item planting off cultivated ground.");
            farmingRequireSunlight = config.Bind("Farming", "RequireSunlight", true, "When false (and Farming enabled), plants ignore the roof/sunlight check.");
            farmingRequireGrowthSpace = config.Bind("Farming", "RequireGrowthSpace", true, "When false (and Farming enabled), plants ignore the growth-space check.");
            farmingGrowSpaceScale = config.Bind("Farming", "GrowSpaceScale", 0.4f, new ConfigDescription("Multiply vanilla plant/tree grow radius on the server. Applies to existing saplings. 1 is vanilla; 0.4 is much tighter.", new AcceptableValueRange<float>(0.05f, 1f)));
            farmingCropGrowTimeMin = config.Bind("Farming", "CropGrowTimeMin", 0f, new ConfigDescription("Override Plant.m_growTime when Farming is enabled. 0 leaves vanilla/mod values.", new AcceptableValueRange<float>(0f, 100000f)));
            farmingCropGrowTimeMax = config.Bind("Farming", "CropGrowTimeMax", 0f, new ConfigDescription("Override Plant.m_growTimeMax when Farming is enabled. 0 leaves vanilla/mod values; below Min uses Min.", new AcceptableValueRange<float>(0f, 100000f)));
            farmingFloraRespawnMinutes = config.Bind("Farming", "FloraRespawnMinutes", 0, new ConfigDescription("Override pickable respawn minutes for configured flora prefabs when Farming is enabled. 0 leaves vanilla/mod values.", new AcceptableValueRange<int>(0, 100000)));
            farmingExtraFlora = config.Bind("Farming", "ExtraFloraPrefabs", "", "Comma-separated exact prefab names added to the default flora list for item-planting recipes' flora side and respawn overrides. Requires restart.");
            portalHubEnabled = config.Bind("PortalHub", "Enabled", true, "Leave one home portal untagged and walk through it to reach a labeled destination hall. Name outpost portals (not 'Home'). Tagged world portals return home. Empty portals are not paired with each other. Remove ServersideQoL AutoPortalHub if present. Keep PortalProgression — boss-gated portal cargo is compatible.");
            portalHubInclude = config.Bind("PortalHub", "Include", "*", "Only portals whose tag matches this wildcard filter are hub-paired (* = all).");
            portalHubExclude = config.Bind("PortalHub", "Exclude", "", "Portals whose tag matches this wildcard filter are never hub-paired.");
            portalHubAutoName = config.Bind("PortalHub", "AutoNameNewPortals", false, "Name empty-tagged new portals using AutoNameFormat before hub pairing.");
            portalHubAutoNameFormat = config.Bind("PortalHub", "AutoNameFormat", "{0} {1:D2}", "Format for AutoNameNewPortals: {0}=biome display name, {1}=unique integer.");
            adaptiveLoading = config.Bind("MaxObjectsPerFrame", "Adaptive", true, "Adjust object creation allowance using measured creation costs and frame pressure, bounded by MaxObjects.");
            loadingBudgetMs = config.Bind("MaxObjectsPerFrame", "BudgetMs", 3f, new ConfigDescription("Soft object-creation time budget. A single expensive object cannot be interrupted.", new AcceptableValueRange<float>(0.5f, 20f)));
            sendBudgetMs = config.Bind("Performance", "SendBudgetMs", 3f, new ConfigDescription("Soft budget for scheduled sends per frame; rotate fairly and retain bounded debt. A single send cannot be interrupted. 0 disables.", new AcceptableValueRange<float>(0f, 20f)));
            idleFps = config.Bind("Performance", "IdleTargetFps", 0, new ConfigDescription("Frame cap with no connected peers; restores ServerTargetFps when someone joins. 0 keeps ServerTargetFps while empty (recommended). Try 30 to save CPU on an empty host. Never raised above ServerTargetFps.", new AcceptableValueRange<int>(0, 240)));
            saveAnnouncements = config.Bind("Server", "SaveAnnouncements", true, "Show save start/result and console shutdown announcements to vanilla clients.");
            diagnosticsEnabled = config.Bind("Diagnostics", "Enabled", true, "Observe server performance and per-player sends without changing gameplay. Needs restart.");
            diagnosticAlerts = config.Bind("Diagnostics", "Alerts", true, "Warn on sustained low FPS or continuously blocked send attempts, after a 60-second startup/join grace.");
            interactionDiagnostics = config.Bind("Diagnostics", "InteractionTrace", false, "Log selected interaction and missing object RPC dispatches. At most one trace per two seconds globally. No payloads; not end-to-end latency.");
            diagnosticReportMinutes = config.Bind("Diagnostics", "ReportMinutes", 5, new ConfigDescription("Summary interval; 0 disables reports but keeps status and alerts.", new AcceptableValueRange<int>(0, 60)));
            alertDurationSeconds = config.Bind("Diagnostics", "AlertDurationSeconds", 30, new ConfigDescription("Continuous problem duration before warning.", new AcceptableValueRange<int>(5, 600)));
            alertCooldownSeconds = config.Bind("Diagnostics", "AlertCooldownSeconds", 300, new ConfigDescription("Minimum time between warnings for the same connection/signal.", new AcceptableValueRange<int>(30, 3600)));
            lowFpsThreshold = config.Bind("Diagnostics", "LowFpsThreshold", 25, new ConfigDescription("Measured FPS below which sustained alerts apply while players are connected.", new AcceptableValueRange<int>(1, 240)));
            modEnabled = config.Bind<bool>("General", "Enabled", true, "Enable or disable the mod");

			maxObjectsPerFrameEnabled = config.Bind<bool>("MaxObjectsPerFrame", "Enabled", true, "Enable or disable the feature");
			maxObjectsPerFrame = config.Bind<int>("MaxObjectsPerFrame", "MaxObjects", 100, "Maximum number of objects the server can create per frame.");

			networkingEnabled = config.Bind<bool>("Networking", "Enabled", true,
				"Raise the limits on how fast the server sends world data to each player (server-side part of BetterNetworking). Needs a restart.");
			networkQueueSizeKB = config.Bind<int>("Networking", "QueueSizeKB", 48,
				new ConfigDescription("Data queued per player before the server stops sending world updates for that tick. Valheim: 10. At 20 ticks/s, 48 KB is ~960 KB/s, just under SteamSendRateMaxKB; more needs a higher send rate too. A fuller queue delays new updates behind it. Above 80 Steam starts failing.",
					new AcceptableValueRange<int>(10, 80)));
			networkSendRateMinKB = config.Bind<int>("Networking", "SteamSendRateMinKB", 256,
				new ConfigDescription("Minimum rate Steam attempts to send to each player, KB/s. Valheim: 150. Keep it below the server's upload speed divided by the number of players.",
					new AcceptableValueRange<int>(64, 4096)));
			networkSendRateMaxKB = config.Bind<int>("Networking", "SteamSendRateMaxKB", 1024,
				new ConfigDescription("Maximum rate Steam sends to each player, KB/s. Valheim: 150.",
					new AcceptableValueRange<int>(64, 4096)));
			networkStatsMinutes = config.Bind<int>("Networking", "StatsIntervalMinutes", 5,
				"Every this many minutes, log per player how often their send queue was full. 0 disables.");
			networkBdpWindowEnabled = config.Bind("Networking", "EnableBdpWindow", true,
				"Size each player's send window from measured Steam RTT (bandwidth-delay product) instead of one fixed QueueSizeKB. Nearby players get a smaller window (less queue latency); distant players get up to QueueSizeKB. Crossplay/PlayFab peers have no RTT and keep QueueSizeKB. Does not change object ownership.");
			networkBdpTargetRateKBps = config.Bind("Networking", "BdpTargetRateKBps", 150,
				new ConfigDescription("Target throughput used to size the BDP window, KB/s. Window ≈ rate × RTT × BdpFactor, clamped between 10 KB and QueueSizeKB.",
					new AcceptableValueRange<int>(32, 1024)));
			networkBdpFactor = config.Bind("Networking", "BdpFactor", 1.25f,
				new ConfigDescription("Multiplier on the BDP window. 1.0 is exact; slightly above leaves headroom for bursts.",
					new AcceptableValueRange<float>(0.5f, 3f)));
			networkTimeoutEnabled = config.Bind("Networking", "EnableTimeoutTuning", true,
				"Raise ZRpc and Steam connection timeouts so slow long-haul joins are not dropped at vanilla's 30s. Needs Networking enabled.");
			networkConnectionTimeoutSeconds = config.Bind("Networking", "ConnectionTimeoutSeconds", 90,
				new ConfigDescription("Quiet-connection timeout in seconds (vanilla ZRpc: 30). Applies while playing.",
					new AcceptableValueRange<int>(30, 300)));
			networkLoadingTimeoutSeconds = config.Bind("Networking", "LoadingTimeoutSeconds", 120,
				new ConfigDescription("Timeout while a peer is still loading (vanilla long timeout: 90). Must be >= ConnectionTimeoutSeconds.",
					new AcceptableValueRange<int>(60, 600)));
			networkGhostEvictEnabled = config.Bind("Networking", "EvictGhostOwners", true,
				"If a peer goes quiet, reclaim ZDOs they still own to the server after GhostEvictSeconds while keeping their slot until the full connection timeout. Compatible with serverside simulation; does not hand objects to other players.");
			networkGhostEvictSeconds = config.Bind("Networking", "GhostEvictSeconds", 10f,
				new ConfigDescription("Seconds of silence before ghost-owner reclaim. Capped below the connection timeout.",
					new AcceptableValueRange<float>(3f, 120f)));
			networkStationRoutingEnabled = config.Bind("Networking", "RouteStationRequestsToOwner", true,
				"Re-address fermenter/smelter/cooking/fireplace/shield/turret item RPCs to the current owner (or claim for the server). Stops lost inserts when the client's idea of the owner is stale. Vanilla clients.");
			networkRelayFilterEnabled = config.Bind("Networking", "EnableRelayFiltering", true,
				"When relaying Everybody routed RPCs about a world object, only send to peers who know that object or are near it. Cuts footsteps/swings/damage spam on busy servers. Vanilla clients.");
			networkRelayLimitByDistance = config.Bind("Networking", "LimitRelayByDistance", false,
				"Stricter relay filter: only peers currently in the object's active area (not merely those who once knew it). Opt-in; EnableRelayFiltering must be on.");

			consoleCommandsEnabled = config.Bind<bool>("Server", "ConsoleCommands", true,
				"Read commands from standard input: status, save, stop, players, give <item> <amount> <player>. In AMP set App.HasWriteableConsole=True to type them into its console, and App.ExitMethod=String with App.ExitString=stop to shut down cleanly. On Windows also set [Logging.Console] Enabled = false in BepInEx.cfg, or BepInEx's own console takes over standard input.");
			unityJobWorkers = config.Bind<int>("Server", "UnityJobWorkers", 8,
				"Upper limit on Unity job worker threads. Unity starts one per CPU core, and on many-core hosts the idle ones still use CPU. Only ever lowers the count. 0 leaves Unity's default.");

			sendIntervalMs = config.Bind<int>("Performance", "SendIntervalMs", 100,
				new ConfigDescription("How often each player is sent world updates, in real milliseconds. Valheim sends to one player per frame, so each player waits players+1 frames: at 15 FPS with 4 players ~330 ms. Every send builds that player's list of nearby objects, so shorter intervals cost server CPU. 0 keeps Valheim's behaviour.",
					new AcceptableValueRange<int>(0, 1000)));
			maxCatchUpMs = config.Bind<int>("Performance", "MaxCatchUpMs", 100,
				new ConfigDescription("Longest frame the server counts in full (Unity's maximum allowed timestep). After a slow frame Unity runs physics and every creature's fixed update again for each 20 ms it fell behind; Valheim allows 200 ms (10 steps), 100 caps it at 5, so one slow frame does not make the next one slow too. Game time runs slightly slower during such frames. Needs a restart. 0 keeps the game's setting.",
					new AcceptableValueRange<int>(0, 1000)));
			maxZonesPerTick = config.Bind<int>("Performance", "MaxZonesPerTick", 1,
				new ConfigDescription("Most new zones the server generates per zone tick (10 ticks a second), shared by all players in turn. A new zone is generated in full in one frame, so several players exploring at once used to cost one zone each in the same frame. 0 = one per player per tick, as before.",
					new AcceptableValueRange<int>(0, 100)));
			serverTargetFps = config.Bind<int>("Performance", "ServerTargetFps", 60,
				new ConfigDescription("Frame rate the server aims for. The game sets a dedicated server to 30, so even with time to spare a frame waits 33 ms; every reaction to a player (a hit, a felled tree) waits for a server frame. 60 halves that wait whenever the server has the headroom, and costs nothing when it has not. Needs a restart. 0 keeps the game's 30.",
					new AcceptableValueRange<int>(0, 240)));
			performanceStatsMinutes = config.Bind<int>("Performance", "StatsIntervalMinutes", 5,
				"Every this many minutes, log frame times, physics steps per frame, the cost of sending world updates and of generating zones. 0 disables.");

			syncDirtySets = config.Bind("Performance", "DirtySets", true,
				"Only consider changed objects when building each player's sync list, with a full rescan every ReconcileSeconds and on zone change. Large win on big bases. Vanilla clients. Needs restart.");
			syncReconcileSeconds = config.Bind("Performance", "ReconcileSeconds", 30f,
				new ConfigDescription("Full sync-list rescan interval per player when DirtySets is on.",
					new AcceptableValueRange<float>(5f, 300f)));
			syncRelayMinIntervalMs = config.Bind("Performance", "RelayMinIntervalMs", 200,
				new ConfigDescription("Re-send a non-prioritised object to the same player at most this often (ms). 0 = vanilla. Players/creatures (Prioritized) are never delayed. Requires DirtySets.",
					new AcceptableValueRange<int>(0, 2000)));
			syncTopKSort = config.Bind("Performance", "TopKSort", true,
				"Use a bounded heap instead of a full sort when picking which objects fit in the send window (faster joins). Needs restart.");
			syncTopK = config.Bind("Performance", "TopK", 0,
				new ConfigDescription("Candidates ordered per send round. 0 = QueueSizeKB*1024/64, never below 64.",
					new AcceptableValueRange<int>(0, 4096)));
			skipRenderMesh = config.Bind("Performance", "SkipRenderMesh", true,
				"Skip heightmap render-mesh rebuilds on the dedicated server (never drawn). Collision mesh unchanged. Needs restart.");
			deferAssetUnload = config.Bind("Performance", "DeferAssetUnload", true,
				"Hold Unity's periodic UnloadUnusedAssets until no players are connected (or MaxDeferMinutes). Avoids a multi-hundred-ms hitch while people play.");
			assetUnloadMaxDeferMinutes = config.Bind("Performance", "AssetUnloadMaxDeferMinutes", 240,
				new ConfigDescription("Backstop: run deferred asset unload after this many minutes even if players are still online.",
					new AcceptableValueRange<int>(30, 1440)));
			motionCullEnabled = config.Bind("Performance", "MotionCull", true,
				"On the dedicated server, drop tiny position/rotation ZDO writes and rate-limit NPC/physics revision spam (LeanNet-style). Ships and players are exempt. Vanilla clients. Needs restart.");
			motionCullPhysicsHz = config.Bind("Performance", "MotionCullPhysicsHz", 8f,
				new ConfigDescription("Max network revision rate for physics objects (drops, projectiles). Floor 4.",
					new AcceptableValueRange<float>(4f, 20f)));
			motionCullNpcHz = config.Bind("Performance", "MotionCullNpcHz", 8f,
				new ConfigDescription("Max network revision rate for non-player characters. Floor 4.",
					new AcceptableValueRange<float>(4f, 20f)));
			motionCullVec3Meters = config.Bind("Performance", "MotionCullVec3Meters", 0.05f,
				new ConfigDescription("Ignore Vector3 ZDO writes smaller than this (metres). Rotations use a similar threshold.",
					new AcceptableValueRange<float>(0.01f, 0.2f)));

			fixSaveClientChanges = config.Bind<bool>("Fixes", "SaveClientChanges", true,
				"Mark a world chunk as changed when a player's own change to an object arrives, so the next save writes it. Valheim 1.0 only rewrites changed chunks and does not count changes received from players, so what a player just built or moved could be missing after a restart.");
			allowEmptyPassword = config.Bind("Server", "AllowEmptyPassword", false,
				"Allow a public/crossplay dedicated server to start with no password (or a short one). Vanilla requires a password for -public 1 / -crossplay. Also clear -password in the host panel. Opt-in.");

			adminChatEnabled = config.Bind<bool>("AdminChat", "Enabled", false,
				"Let admins (adminlist.txt) run commands by shouting them in chat: /give <item> [amount], /save, /help; replies go to their console (F5). Valheim 1.0 does not let a player on a dedicated server use spawn from the console, admin or not. Off by default: the server console has the same commands (give <item> <amount> <player>, players, save) for the panel the server runs in.");
			adminChatPrefix = config.Bind<string>("AdminChat", "Prefix", "/",
				"What a chat message must start with to count as a command.");
			adminChatMaxGive = config.Bind<int>("AdminChat", "MaxGiveAmount", 1000,
				"Most items one give may drop, in chat or on the console.");

			qolEnabled = config.Bind("QoL", "Enabled", true,
				"Server-forced QoL for vanilla/console clients (magnet pickup, instant loot, structure repair near stations, carry-weight and near-bench durability via personalized GlobalKeys). Needs restart.");
			qolMagnetPickup = config.Bind("QoL", "MagnetPickup", true,
				"Pull settled ground loot toward players, then hand ownership into the vanilla 2 m auto-pickup bubble. Skips airborne/falling items and never steals ownership from a connected player.");
			qolMagnetRadius = config.Bind("QoL", "MagnetRadius", 6f,
				new ConfigDescription("Metres within which settled drops slide toward a player.", new AcceptableValueRange<float>(3f, 30f)));
			qolMagnetStep = config.Bind("QoL", "MagnetStep", 0.75f,
				new ConfigDescription("Pull strength per magnet tick (~3/s). Lower = gentler slide.", new AcceptableValueRange<float>(0.25f, 2f)));
			qolMagnetSettleSeconds = config.Bind("QoL", "MagnetSettleSeconds", 1.5f,
				new ConfigDescription("Seconds after a drop appears before magnet may touch it. Lets chopped wood finish falling.", new AcceptableValueRange<float>(0.5f, 5f)));
			qolMagnetMaxSpeed = config.Bind("QoL", "MagnetMaxSpeed", 0.75f,
				new ConfigDescription("Skip drops whose rigidbody is still moving faster than this (m/s). Prevents yanking tumbling logs.", new AcceptableValueRange<float>(0.1f, 5f)));
			qolInstantLoot = config.Bind("QoL", "InstantLoot", true,
				"Spawn monster loot at the closest player's feet instead of the corpse. Vanilla auto-pickup / magnet finish the grab.");
			qolInstantLootRange = config.Bind("QoL", "InstantLootRange", 64f,
				new ConfigDescription("Max metres to the killer/player when redirecting loot.", new AcceptableValueRange<float>(8f, 128f)));
			qolStructureRepair = config.Bind("QoL", "StructureRepair", true,
				"Auto-repair WearNTear pieces near a crafting station (workbench/forge/etc.).");
			qolStationRange = config.Bind("QoL", "StationRange", 20f,
				new ConfigDescription("Metres around a crafting station for structure repair and no-durability-gear zone.", new AcceptableValueRange<float>(5f, 64f)));
			qolCarryWeightRate = config.Bind("QoL", "CarryWeightRate", 1f,
				new ConfigDescription("Per-player carry capacity multiplier via GlobalKeys.CarryWeightRate (1 = vanilla). Spoofed only to connected clients. Try 1.5–2 for backpack-like capacity without ExtraSlots.", new AcceptableValueRange<float>(0.5f, 5f)));
			qolNoDurabilityNearStation = config.Bind("QoL", "NoDurabilityNearStation", true,
				"While standing near a crafting station, set DurabilityRate to 0 for that player (gear does not wear — feels like auto-repair).");
			qolChestExtraRows = config.Bind("QoL", "ChestExtraRows", 1,
				new ConfigDescription("Extra inventory rows added to every player-built chest/container (not dungeon chests). Applied via HasFields so vanilla clients see the taller UI.", new AcceptableValueRange<int>(0, 6)));
			qolBackpack = config.Bind("QoL", "Backpack", true,
				"Emote-opens a persistent private-chest backpack (vanilla OpenResponse). Disable ServersideQoL.Backpack if both are installed.");
			qolBackpackEmote = config.Bind("QoL", "BackpackEmote", "Wave",
				"Emote name that opens the backpack (Wave, Sit, Cheer, …). Use * for any emote. Console: /bind JoystickButton3 Wave");
			qolBackpackSlots = config.Bind("QoL", "BackpackSlots", 8,
				new ConfigDescription("Backpack inventory slots (arranged as 4×N).", new AcceptableValueRange<int>(4, 32)));
			qolCraftFromChests = config.Bind("QoL", "CraftFromChests", false,
				"While near a crafting station, temporarily pull items from nearby player chests into your inventory; leftovers return when you leave. Off by default until tuned — can hitch when many chests are loaded.");
			qolCraftChestRange = config.Bind("QoL", "CraftChestRange", 12f,
				new ConfigDescription("Metres around the player to pull chest materials from while at a station.", new AcceptableValueRange<float>(5f, 40f)));
			qolCraftMaxItems = config.Bind("QoL", "CraftMaxItems", 40,
				new ConfigDescription("Max item stacks moved from chests per station visit.", new AcceptableValueRange<int>(8, 100)));
		}
	}
}
