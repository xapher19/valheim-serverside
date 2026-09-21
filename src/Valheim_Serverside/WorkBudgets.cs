using System;

namespace Valheim_Serverside
{
    /// <summary>Shared flag: tumbling TreeLogs / debris need a faster peer send cadence.</summary>
    internal static class HotPhysicsGate
    {
        private static double hotUntil;
        internal static bool Active => UnityEngine.Time.realtimeSinceStartupAsDouble < hotUntil;
        internal static void Note(double holdSeconds = 0.5) =>
            hotUntil = Math.Max(hotUntil, UnityEngine.Time.realtimeSinceStartupAsDouble + holdSeconds);
    }

    internal sealed class CreationBudget
    {
        private double costMs = 0.1;
        private int previous = 10;
        internal void Observe(double milliseconds)
        {
            if (milliseconds >= 0 && !double.IsNaN(milliseconds) && !double.IsInfinity(milliseconds))
                costMs = costMs * 0.8 + Math.Max(0.01, milliseconds) * 0.2;
        }
        internal int Next(int ceiling, double budgetMs, double frameSeconds, int targetFps)
        {
            ceiling = Math.Max(1, ceiling);
            int desired = Math.Max(1, Math.Min(ceiling, (int)(budgetMs / Math.Max(0.01, costMs))));
            if (frameSeconds > 1.5 / Math.Max(30, targetFps)) desired = Math.Min(desired, Math.Max(1, previous / 2));
            previous = Math.Min(desired, previous + Math.Max(1, previous / 10));
            return Math.Max(1, Math.Min(ceiling, previous));
        }
    }

    internal sealed class SendBudget
    {
        private double owed, last = -1;
        private int next;
        internal int Due(double now, int count, double interval)
        {
            double elapsed = last < 0 ? 0 : Math.Max(0, now - last);
            last = now;
            if (count <= 0 || interval <= 0) { owed = 0; next = 0; return 0; }
            owed = Math.Min(count, owed + count * elapsed / interval);
            return (int)owed;
        }
        internal int Take(int count)
        {
            if (next >= count) next = 0;
            int result = next++;
            owed = Math.Max(0, owed - 1);
            return result;
        }
    }
}
