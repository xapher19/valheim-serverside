using BepInEx.Configuration;

namespace PluginConfiguration
{
	public class Configuration
	{
		public static ConfigEntry<bool> modEnabled;
		public static ConfigEntry<bool> productionEnabled, productionLivestock, productionFlora, advanceEmptyTime, adaptiveLoading, saveAnnouncements;
        public static ConfigEntry<bool> farmingEnabled, farmingPlaceAnywhere, farmingRequireSunlight, farmingRequireGrowthSpace;
        public static ConfigEntry<bool> portalHubEnabled, portalHubAutoName;
        public static ConfigEntry<int> productionScanBudget, idleFps, farmingFloraRespawnMinutes;
        public static ConfigEntry<float> loadingBudgetMs, sendBudgetMs, farmingCropGrowTimeMin, farmingCropGrowTimeMax;
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

		public static ConfigEntry<bool> consoleCommandsEnabled;
		public static ConfigEntry<int> unityJobWorkers;

		public static ConfigEntry<int> sendIntervalMs;
		public static ConfigEntry<int> maxCatchUpMs;
		public static ConfigEntry<int> maxZonesPerTick;
		public static ConfigEntry<int> performanceStatsMinutes;
		public static ConfigEntry<int> serverTargetFps;

		public static ConfigEntry<bool> fixSaveClientChanges;

		public static ConfigEntry<bool> adminChatEnabled;
		public static ConfigEntry<string> adminChatPrefix;
		public static ConfigEntry<int> adminChatMaxGive;

		public static void Load(ConfigFile config)
		{
			productionEnabled = config.Bind("Production", "Enabled", true, "Keep generated zones around player-built processing stations, planted crops and beehives active (requires a piece creator). More CPU/RAM; requires restart. Raid starts and raid spawns require a real nearby player.");
            productionLivestock = config.Bind("Production", "Livestock", false, "Keep already-tamed animals, their young and hatchable tame-animal eggs active when they have a player creator. Off by default to avoid pinning large areas. Requires restart.");
            productionFlora = config.Bind("Production", "Flora", true, "Keep player-planted berry bushes, mushrooms and flowers (creator set) active. Wild flora is ignored. Works with PlantEverything-planted objects without installing PlantEverything on the server. Requires restart.");
            advanceEmptyTime = config.Bind("Production", "AdvanceTimeWhenEmpty", true, "Continue the world clock while empty when production is active. Days/weather also advance. No catch-up while the server is stopped.");
            productionScanBudget = config.Bind("Production", "ScanEntriesPerFrame", 2048, new ConfigDescription("Maximum sector/object scan steps per frame when finding existing production after restart.", new AcceptableValueRange<int>(64, 16384)));
            productionExclude = config.Bind("Production", "ExcludedPrefabs", "", "Comma-separated exact prefab names excluded from automatic production anchors. Requires restart.");
            farmingEnabled = config.Bind("Farming", "Enabled", false, "Optional dedicated-server farming retunes (grow/respawn/restriction). Does not add cultivator recipes; clients still need PlantEverything or similar to plant flora. Requires restart.");
            farmingPlaceAnywhere = config.Bind("Farming", "PlaceAnywhere", false, "Relax plant roof, growth-space and ground checks while Farming is enabled. Server simulation only.");
            farmingRequireSunlight = config.Bind("Farming", "RequireSunlight", true, "When false (and Farming enabled), plants ignore the roof/sunlight check.");
            farmingRequireGrowthSpace = config.Bind("Farming", "RequireGrowthSpace", true, "When false (and Farming enabled), plants ignore the growth-space check.");
            farmingCropGrowTimeMin = config.Bind("Farming", "CropGrowTimeMin", 0f, new ConfigDescription("Override Plant.m_growTime when Farming is enabled. 0 leaves vanilla/mod values.", new AcceptableValueRange<float>(0f, 100000f)));
            farmingCropGrowTimeMax = config.Bind("Farming", "CropGrowTimeMax", 0f, new ConfigDescription("Override Plant.m_growTimeMax when Farming is enabled. 0 leaves vanilla/mod values; below Min uses Min.", new AcceptableValueRange<float>(0f, 100000f)));
            farmingFloraRespawnMinutes = config.Bind("Farming", "FloraRespawnMinutes", 0, new ConfigDescription("Override pickable respawn minutes for configured flora prefabs when Farming is enabled. 0 leaves vanilla/mod values.", new AcceptableValueRange<int>(0, 100000)));
            farmingExtraFlora = config.Bind("Farming", "ExtraFloraPrefabs", "", "Comma-separated exact prefab names added to the default flora list for production anchors and respawn overrides. Requires restart.");
            portalHubEnabled = config.Bind("PortalHub", "Enabled", true, "Generate a sky portal hub and pair unconnected world portals (odd count per tag) to matching hub portals. Server-side only; remove ServersideQoL AutoPortalHub if present.");
            portalHubInclude = config.Bind("PortalHub", "Include", "*", "Only portals whose tag matches this wildcard filter are hub-paired (* = all).");
            portalHubExclude = config.Bind("PortalHub", "Exclude", "", "Portals whose tag matches this wildcard filter are never hub-paired.");
            portalHubAutoName = config.Bind("PortalHub", "AutoNameNewPortals", false, "Name empty-tagged new portals using AutoNameFormat before hub pairing.");
            portalHubAutoNameFormat = config.Bind("PortalHub", "AutoNameFormat", "{0} {1:D2}", "Format for AutoNameNewPortals: {0}=biome display name, {1}=unique integer.");
            adaptiveLoading = config.Bind("MaxObjectsPerFrame", "Adaptive", true, "Adjust object creation allowance using measured creation costs and frame pressure, bounded by MaxObjects.");
            loadingBudgetMs = config.Bind("MaxObjectsPerFrame", "BudgetMs", 3f, new ConfigDescription("Soft object-creation time budget. A single expensive object cannot be interrupted.", new AcceptableValueRange<float>(0.5f, 20f)));
            sendBudgetMs = config.Bind("Performance", "SendBudgetMs", 3f, new ConfigDescription("Soft budget for scheduled sends per frame; rotate fairly and retain bounded debt. A single send cannot be interrupted. 0 disables.", new AcceptableValueRange<float>(0f, 20f)));
            idleFps = config.Bind("Performance", "IdleTargetFps", 30, new ConfigDescription("Frame cap with no connected peers; restores configured target as soon as a peer connects. 0 disables. Does not pause production or physics.", new AcceptableValueRange<int>(0, 240)));
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

			fixSaveClientChanges = config.Bind<bool>("Fixes", "SaveClientChanges", true,
				"Mark a world chunk as changed when a player's own change to an object arrives, so the next save writes it. Valheim 1.0 only rewrites changed chunks and does not count changes received from players, so what a player just built or moved could be missing after a restart.");

			adminChatEnabled = config.Bind<bool>("AdminChat", "Enabled", false,
				"Let admins (adminlist.txt) run commands by shouting them in chat: /give <item> [amount], /save, /help; replies go to their console (F5). Valheim 1.0 does not let a player on a dedicated server use spawn from the console, admin or not. Off by default: the server console has the same commands (give <item> <amount> <player>, players, save) for the panel the server runs in.");
			adminChatPrefix = config.Bind<string>("AdminChat", "Prefix", "/",
				"What a chat message must start with to count as a command.");
			adminChatMaxGive = config.Bind<int>("AdminChat", "MaxGiveAmount", 1000,
				"Most items one give may drop, in chat or on the console.");
		}
	}
}
