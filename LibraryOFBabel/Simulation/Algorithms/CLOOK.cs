using System;
using System.Collections.Generic;
using System.Linq;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Simulation.Algorithms
{
    public static class CLOOK
    {
        public static (long TotalDistance, List<int> VisitOrder) Simulate(SimulationState state)
        {
            // C-LOOK: serve in one direction, when none ahead wrap to smallest pending and continue
            if (state is null) throw new ArgumentNullException(nameof(state));
            var pending = new List<int>(state.PendingRequests ?? Enumerable.Empty<int>());
            pending.Sort();
            long total = 0;
            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;
            var visit = new List<int>(pending.Count);

            if (dir >= 0)
            {
                var right = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                var left = pending.Where(r => r < head).OrderBy(r => r).ToList(); // ascending

                foreach (var r in right)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }

                if (left.Count == 0) return (total, visit);

                // wrap (logical jump) to smallest pending
                int smallest = left.First();
                // do not count physical travel for wrap; head teleports
                head = smallest;

                foreach (var r in left)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }
            }
            else
            {
                var left = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                var right = pending.Where(r => r > head).OrderByDescending(r => r).ToList(); // descending

                foreach (var r in left)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }

                if (right.Count == 0) return (total, visit);

                int largest = right.First();
                head = largest;

                foreach (var r in right)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }
            }

            return (total, visit);
        }
    }
}
