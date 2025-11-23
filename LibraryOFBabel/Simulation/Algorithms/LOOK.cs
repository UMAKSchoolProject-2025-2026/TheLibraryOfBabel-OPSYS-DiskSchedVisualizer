using System;
using System.Collections.Generic;
using System.Linq;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Simulation.Algorithms
{
    public static class LOOK
    {
        public static (long TotalDistance, List<int> VisitOrder) Simulate(SimulationState state, bool circular = false)
        {
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
                var left = pending.Where(r => r < head).OrderByDescending(r => r).ToList(); // descending for return

                foreach (var r in right)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }

                if (left.Count == 0) return (total, visit);

                if (circular)
                {
                    // C-LOOK: wrap to smallest pending without travelling to disk edge
                    var smallest = pending.Min();
                    // wrap distance is treated as a logical jump (no physical travel)
                    head = smallest;

                    foreach (var r in pending.Where(r => r >= smallest && r < head).OrderBy(r => r))
                    {
                        total += Math.Abs(head - r);
                        head = r;
                        visit.Add(r);
                    }

                    // simpler: service remaining left items in ascending order from smallest
                    foreach (var r in left.OrderBy(r => r))
                    {
                        total += Math.Abs(head - r);
                        head = r;
                        visit.Add(r);
                    }
                }
                else
                {
                    foreach (var r in left)
                    {
                        total += Math.Abs(head - r);
                        head = r;
                        visit.Add(r);
                    }
                }
            }
            else
            {
                var left = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                var right = pending.Where(r => r > head).OrderBy(r => r).ToList();

                foreach (var r in left)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }

                if (right.Count == 0) return (total, visit);

                if (circular)
                {
                    var largest = pending.Max();
                    head = largest;
                    foreach (var r in right.OrderByDescending(r => r))
                    {
                        total += Math.Abs(head - r);
                        head = r;
                        visit.Add(r);
                    }
                }
                else
                {
                    foreach (var r in right)
                    {
                        total += Math.Abs(head - r);
                        head = r;
                        visit.Add(r);
                    }
                }
            }

            return (total, visit);
        }
    }
}
