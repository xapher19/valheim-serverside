using System;

namespace Valheim_Serverside
{
    // Clock and host independent so boundary conditions can be exercised without Unity.
    internal sealed class SustainedSignal
    {
        private double since = -1, nextWarning;
        private bool warned;
        public string Sample(bool bad, double now, double graceUntil, double duration, double cooldown)
        {
            if (!bad || now < graceUntil)
            {
                since = -1;
                if (warned) { warned = false; return "recovered"; }
                return null;
            }
            if (since < 0) since = now;
            if (now - since < duration || now < nextWarning) return null;
            warned = true;
            nextWarning = now + cooldown;
            return "sustained";
        }
    }

    internal sealed class SendMetrics
    {
        public readonly double Started;
        public long Attempts, Submitted, Blocked, Empty;
        public int PeakQueue;
        public double LastAttempt = -1, LastSubmitted = -1, MaxAttemptGap, MaxSubmittedGap;
        public readonly SustainedSignal Pressure = new SustainedSignal();
        public SendMetrics(double now) { Started = now; }
        public void Record(double now, int queue, bool blocked, bool submitted)
        {
            if (LastAttempt >= 0) MaxAttemptGap = Math.Max(MaxAttemptGap, now - LastAttempt);
            LastAttempt = now;
            Attempts++;
            PeakQueue = Math.Max(PeakQueue, queue);
            if (submitted)
            {
                Submitted++;
                if (LastSubmitted >= 0) MaxSubmittedGap = Math.Max(MaxSubmittedGap, now - LastSubmitted);
                LastSubmitted = now;
            }
            else if (blocked) Blocked++;
            else Empty++;
        }
    }
}
