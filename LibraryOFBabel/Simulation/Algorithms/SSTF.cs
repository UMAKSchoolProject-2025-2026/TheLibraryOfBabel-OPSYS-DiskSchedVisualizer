using System;
using System.Collections.Generic;
using System.Linq;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Simulation.Algorithms
{
    public static class SSTF
    {
        public static (long TotalDistance, List<int> VisitOrder) Simulate(SimulationState state)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            var pending = new List<int>(state.PendingRequests);
            long total = 0;
            int head = state.HeadPosition;
            var visit = new List<int>(pending.Count);
            while (pending.Count > 0)
            {
                // find nearest
                int best = pending.OrderBy(r => Math.Abs(r - head)).First();
                total += Math.Abs(head - best);
                head = best;
                visit.Add(best);
                pending.Remove(best);
            }
            return (total, visit);
        }
    }
}
