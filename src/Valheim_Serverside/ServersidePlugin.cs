using BepInEx;
using BepInEx.Logging;
using FeaturesLib;
using HarmonyLib;
using PatchingLib;
using PluginConfiguration;
using Requirements;
using System;
using System.Collections.Generic;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Valheim_Serverside
{

	[Harmony]
	[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
	[BepInDependency(ValheimPlusPluginId, BepInDependency.DependencyFlags.SoftDependency)]

	public class ServersidePlugin : BaseUnityPlugin
	{
		// Kept from Serverside Simulations (the original this is forked from), so other mods that
		// detect it by GUID still do and the two cannot be loaded side by side.
		public const string PluginGUID = "MVP.Valheim_Serverside_Simulations";
		public const string PluginName = "Northwatch Dedicated Simulation";
		public const string PluginVersion = "1.9.4";

		private static ServersidePlugin context;

		public static Configuration configuration;

		public static Harmony harmony;

		public const string ValheimPlusPluginId = "org.bepinex.plugins.valheim_plus";

		public static ManualLogSource logger;

		private void Awake()
		{
			context = this;
			logger = Logger;

			Configuration.Load(Config);

			if (!ModIsEnabled())
			{
				Logger.LogInfo($"{PluginName} is disabled. (configuration)");
				return;
			}
			else if (!IsDedicated())
			{
				Logger.LogInfo($"{PluginName} is disabled. (not a dedicated server)");
				return;
			}
			Logger.LogInfo($"Installing {PluginName}");

			// Independent of the patches, so they stay on even if Core fails to apply and the server runs vanilla.
			LimitJobWorkers(Configuration.unityJobWorkers.Value);
			LimitPhysicsCatchUp(Configuration.maxCatchUpMs.Value);
			if (Configuration.consoleCommandsEnabled.Value)
			{
				ServerConsole.Start();
				consoleStarted = true;
			}

			harmony = new Harmony(PluginGUID);

			AvailableFeatures availableFeatures = new AvailableFeatures();
			availableFeatures.AddFeature(new Features.Core());
			availableFeatures.AddFeature(new Features.MaxObjectsPerFrame());
			availableFeatures.AddFeature(new Features.Networking());
			availableFeatures.AddFeature(new Features.Performance());
            availableFeatures.AddFeature(new Features.Diagnostics());
			availableFeatures.AddFeature(new Features.AdminChat());
			availableFeatures.AddFeature(new Features.Fixes());
			availableFeatures.AddFeature(new Features.Debugging());
			availableFeatures.AddFeature(new Features.Compat_ValheimPlus());

			PatchRequirements patchRequirements = new PatchRequirements();
			patchRequirements.AddRequirement(new PatchRequirement.DebugBuild());

			if (!PatchFeatures(availableFeatures, new HarmonyFeaturesPatcher(patchRequirements)))
			{
				return;
			}

			VanillaDrift.Check(Logger);
			installed = true;
            DiagnosticRuntime.Installed = true;
            DiagnosticRuntime.Initialize();
			Features.TargetFpsVerifier.Start(Time.realtimeSinceStartupAsDouble);
			Logger.LogInfo($"{PluginName} installed");
		}

		private static bool consoleStarted;
		private static bool installed;

		private void Update()
		{
			if (consoleStarted)
			{
				ServerConsole.ProcessPending();
			}
			if (installed)
			{
				Features.TargetFpsVerifier.Tick(Time.realtimeSinceStartupAsDouble);
				Features.PerformanceStats.Frame();
                DiagnosticRuntime.Tick();
				if (Configuration.adminChatEnabled.Value)
				{
					Features.AdminChat.Tick();
				}
			}
		}

		private void FixedUpdate()
		{
			if (installed)
			{
				Features.PerformanceStats.FixedStep();
			}
		}

		/*
			Unity starts a job worker thread per CPU core (63 on a 64-thread host) and the idle ones
			still spin. Measured on a 24-thread machine, an idle server used 108% of a core with the
			default 23 workers and 31% with 4. Only ever lowers the count.
		*/
		private void LimitJobWorkers(int limit)
		{
			int current = JobsUtility.JobWorkerCount;
			if (limit <= 0 || limit >= current)
			{
				return;
			}
			JobsUtility.JobWorkerCount = limit;
			Logger.LogInfo($"Unity job worker threads: {current} -> {JobsUtility.JobWorkerCount}");
		}

		/*
			After a slow frame Unity runs the fixed update -- physics and, in Valheim, every character,
			creature AI and synced object (MonoUpdaters.FixedUpdate) -- once per fixed step it fell
			behind, up to the maximum allowed timestep: Valheim ships 200 ms, 10 steps. On a server
			that already has little headroom, the catch-up makes the next frame slow as well. A lower
			limit ends that spiral; the price is game time running slightly slow during such frames.
		*/
		private void LimitPhysicsCatchUp(int milliseconds)
		{
			if (milliseconds <= 0)
			{
				return;
			}
			float before = Time.maximumDeltaTime;
			Time.maximumDeltaTime = Mathf.Max(milliseconds / 1000f, Time.fixedDeltaTime);
			Logger.LogInfo($"Physics catch-up: at most {Mathf.RoundToInt(Time.maximumDeltaTime / Time.fixedDeltaTime)} fixed steps per frame "
				+ $"(fixed step {1000 * Time.fixedDeltaTime:0} ms, longest frame counted {1000 * before:0} -> {1000 * Time.maximumDeltaTime:0} ms)");
		}

		/*
			Each feature is patched through its own Harmony instance so a failure can be undone
			cleanly. A patch that fails usually means the game changed under it. Half of Core is
			worse than none -- e.g. objects created around players while zones are not -- so a
			Core failure removes every patch and leaves the server vanilla. Any other feature is
			just switched off. Performance and Diagnostics use one owner per optional hook.
		*/
		private bool PatchFeatures(AvailableFeatures availableFeatures, HarmonyFeaturesPatcher patcher)
		{
			Features.Performance.ClearHookHealth();
            foreach (IFeature candidate in availableFeatures._features)
                foreach (Type hook in candidate.GetType().GetNestedTypes())
                    DiagnosticRuntime.HookState(hook, candidate.FeatureEnabled() ? "PENDING" : "DISABLED");
            List<Harmony> applied = new List<Harmony>();
			foreach (IFeature feature in availableFeatures.EnabledFeatures())
			{
				string featureName = feature.GetType().Name;
				if (feature is Features.Performance || feature is Features.Diagnostics)
				{
					foreach (Type hook in feature.GetType().GetNestedTypes())
					{
						Harmony hookHarmony = new Harmony($"{PluginGUID}.{featureName}.{hook.Name}");
						try
						{
							patcher.PatchAll(new[] { hook }, hookHarmony, verify: true);
							applied.Add(hookHarmony);
							Features.Performance.SetHookHealth(hook, true);
                            DiagnosticRuntime.HookState(hook, "ACTIVE");
							Logger.LogInfo($"Patch health: {featureName}.{hook.Name} ACTIVE (Harmony registration verified)");
						}
						catch (Exception e)
						{
							hookHarmony.UnpatchSelf();
							Features.Performance.SetHookHealth(hook, false);
                            DiagnosticRuntime.HookState(hook, "FAILED");
							Logger.LogWarning($"Patch health: {featureName}.{hook.Name} FAILED; only this hook was removed. {e}");
						}
					}
					continue;
				}
				Harmony featureHarmony = new Harmony($"{PluginGUID}.{featureName}");
				try
				{
					patcher.PatchAll(feature.GetType().GetNestedTypes(), featureHarmony, verify: feature is Features.Core);
					if (feature is Features.Core)
					{
						foreach (Type hook in feature.GetType().GetNestedTypes())
						{
							Logger.LogInfo($"Patch health: Core.{hook.Name} ACTIVE (Harmony registration verified)");
						}
					}
					foreach (Type hook in feature.GetType().GetNestedTypes()) DiagnosticRuntime.HookState(hook, "ACTIVE");
                    applied.Add(featureHarmony);
				}
				catch (Exception e)
				{
					featureHarmony.UnpatchSelf();
                    foreach (Type hook in feature.GetType().GetNestedTypes()) DiagnosticRuntime.HookState(hook, "FAILED");
					if (feature is Features.Core)
					{
						Logger.LogError($"Core patches failed to apply; {PluginName} is disabled and the server runs vanilla. {e}");
						foreach (Harmony instance in applied)
						{
							instance.UnpatchSelf();
						}
						harmony.UnpatchSelf();
                        DiagnosticRuntime.Rollback();
						Features.Performance.ClearHookHealth();
						Logger.LogError("Patch health: Core FAILED; all simulation patches rolled back. Performance and FPS verification will not start.");
						return false;
					}
					Logger.LogError($"Feature {featureName} failed to apply and is disabled. {e}");
				}
			}
			if (!new Features.Performance().FeatureEnabled())
			{
				Logger.LogInfo("Patch health: Performance DISABLED (configuration)");
			}
			return true;
		}

		public bool ModIsEnabled()
		{
			return Configuration.modEnabled.Value;
		}

		public static bool IsDedicated()
		{
			return new ZNet().IsDedicated();
		}
	}

}
