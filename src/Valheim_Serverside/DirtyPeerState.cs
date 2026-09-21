using System;
using System.Collections.Generic;

namespace Valheim_Serverside
{
	// Per-peer dirty-set bookkeeping for sync list building. No Unity types besides what
	// the caller passes in, so the drain/full-scan rules stay easy to reason about.
	internal sealed class DirtyPeerState
	{
		private (int x, int y) lastZone = (int.MinValue, int.MinValue);
		private float lastFullScan = float.NegativeInfinity;
		private bool wasActive;
		private readonly List<ZDOID> prune = new List<ZDOID>();

		internal HashSet<ZDOID> Pending { get; } = new HashSet<ZDOID>();

		internal bool NeedsFullScan((int x, int y) zone, float now, float reconcileSeconds, bool active)
		{
			bool full = !wasActive || zone != lastZone || now - lastFullScan > reconcileSeconds;
			wasActive = active;
			if (full)
			{
				lastZone = zone;
				lastFullScan = now;
			}
			return full;
		}

		internal void Drain(List<ZDOID> into, Func<ZDOID, bool> exists, Func<ZDOID, bool> inArea, Func<ZDOID, bool> shouldSend, Func<ZDOID, bool> deferSend)
		{
			prune.Clear();
			foreach (ZDOID id in Pending)
			{
				if (!exists(id) || !inArea(id) || !shouldSend(id))
				{
					prune.Add(id);
					continue;
				}
				if (deferSend != null && deferSend(id)) continue;
				into.Add(id);
			}
			foreach (ZDOID id in prune) Pending.Remove(id);
		}
	}
}
