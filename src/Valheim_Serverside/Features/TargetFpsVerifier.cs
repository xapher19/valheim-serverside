using PluginConfiguration;
using UnityEngine;

namespace Valheim_Serverside.Features
{
	// Independent of Harmony: also covers a missing request hook or a request made before patching.
	// Started only after Core commits successfully. Never changes physics or client/network state.
	internal static class TargetFpsVerifier
	{
		private static double nextCheck;
        private static int lastModeTarget = -1;
        internal static int DesiredTarget
        {
            get
            {
                int active = Configuration.serverTargetFps.Value;
                if (active <= 0) return active;
                int idle = Configuration.idleFps.Value;
                return idle > 0 && ZNet.instance && ZNet.instance.GetPeers().Count == 0 ? Mathf.Clamp(idle, 30, Mathf.Clamp(active, 30, 240)) : active;
            }
        }
		private static int lastConfigured = int.MinValue;
		private static bool fallbackAttempted;
		private static bool mismatchReported;
		private static bool verified;

		internal static void Start(double now)
		{
			nextCheck = now + 5;
            lastModeTarget = -1;
			lastConfigured = int.MinValue;
			fallbackAttempted = mismatchReported = verified = false;
		}

		internal static void Tick(double now)
		{
			if (!ServersidePlugin.IsDedicated()) return;
            int target = DesiredTarget;
            if (lastModeTarget != target)
            {
                if (lastModeTarget >= 0 && target > 0)
                {
                    Application.targetFrameRate = Mathf.Clamp(target, 30, 240);
                    nextCheck = now;
                }
                lastModeTarget = target;
            }
            if (now < nextCheck) return;
			nextCheck = now + 10;
			if (!ServersidePlugin.IsDedicated()) return;

			int configured = DesiredTarget;
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
