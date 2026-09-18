using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	// Independent of Harmony: also covers a missing request hook or a request made before patching.
	// Started only after Core commits successfully. Never changes physics or client/network state.
	internal static class TargetFpsVerifier
	{
		private static double nextCheck;
		private static int lastConfigured = int.MinValue;
		private static bool fallbackAttempted;
		private static bool mismatchReported;
		private static bool verified;

		internal static void Start(double now)
		{
			nextCheck = now + 5;
			lastConfigured = int.MinValue;
			fallbackAttempted = mismatchReported = verified = false;
		}

		internal static void Tick(double now)
		{
			if (now < nextCheck) return;
			nextCheck = now + 10;
			if (!ServersidePlugin.IsDedicated()) return;

			int configured = Configuration.serverTargetFps.Value;
			if (configured != lastConfigured)
			{
				lastConfigured = configured;
				fallbackAttempted = mismatchReported = verified = false;
				if (configured <= 0)
					ServersidePlugin.logger.LogInfo("Server target FPS verification disabled (configuration); leaving the game's target unchanged.");
			}
			if (configured <= 0) return;

			int wanted = Mathf.Clamp(configured, 30, 240);
			int actual = Application.targetFrameRate;
			if (actual == wanted)
			{
				if (!verified || mismatchReported)
					ServersidePlugin.logger.LogInfo($"Server target FPS verified: configured {configured}, effective target {actual}. This is a frame cap, not measured throughput.");
				verified = true;
				mismatchReported = false;
				return;
			}

			verified = false;
			if (!fallbackAttempted)
			{
				fallbackAttempted = true;
				ServersidePlugin.logger.LogWarning($"Server target FPS mismatch: configured {configured} (clamped to {wanted}), actual target {actual}. Applying one fallback via Application.targetFrameRate; will verify on the next check.");
				Application.targetFrameRate = wanted;
				return;
			}
			if (!mismatchReported)
			{
				mismatchReported = true;
				ServersidePlugin.logger.LogWarning($"Server target FPS is not applied: expected {wanted}, actual target {actual} after the fallback attempt. Check game/mod frame-rate overrides; no further writes for this configuration.");
			}
		}
	}
}
