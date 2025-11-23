using System;
using System.Collections.Generic;
using System.Linq;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Simulation.Algorithms
{
    public static class SCAN
    {
        public static (long TotalDistance, List<int> VisitOrder) Simulate(SimulationState state, bool circular = false)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            var pending = new List<int>(state.PendingRequests);
            pending.Sort();
            long total = 0;
            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;
            var visit = new List<int>(pending.Count);

            if (dir >= 0)
            {
                // service those >= head ascending
                var ahead = pending.Where(r => r >= head).ToList();
                foreach (var r in ahead)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }
                pending.RemoveAll(r => ahead.Contains(r));

                if (circular)
                {
                    // wrap to smallest
                    if (pending.Count > 0)
                    {
                        var first = pending.First();
                        total += Math.Abs(head - first);
                        head = first;
                        visit.Add(first);
                        pending.RemoveAt(0);
                        // then continue ascending
                        foreach (var r in pending.ToList())
                        {
                            total += Math.Abs(head - r);
                            head = r;
                            visit.Add(r);
                        }
                    }
                }
                else
                {
                    // reverse and service those below head descending
                    if (pending.Count > 0)
                    {
                        dir = -1;
                        var behind = pending.Where(r => r < head).OrderByDescending(r => r).ToList();
                        foreach (var r in behind)
                        {
                            total += Math.Abs(head - r);
                            head = r;
                            visit.Add(r);
                        }
                    }
                }
            }
            else
            {
                // dir < 0: service <= head descending
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                foreach (var r in behind)
                {
                    total += Math.Abs(head - r);
                    head = r;
                    visit.Add(r);
                }
                pending.RemoveAll(r => behind.Contains(r));

                if (circular)
                {
                    if (pending.Count > 0)
                    {
                        var last = pending.Last();
                        total += Math.Abs(head - last);
                        head = last;
                        visit.Add(last);
                        pending.RemoveAt(pending.Count - 1);
                        foreach (var r in pending.OrderByDescending(r => r))
                        {
                            total += Math.Abs(head - r);
                            head = r;
                            visit.Add(r);
                        }
                    }
                }
                else
                {
                    if (pending.Count > 0)
                    {
                        dir = +1;
                        var ahead2 = pending.Where(r => r > head).OrderBy(r => r).ToList();
                        foreach (var r in ahead2)
                        {
                            total += Math.Abs(head - r);
                            head = r;
                            visit.Add(r);
                        }
                    }
                }
            }

            return (total, visit);
        }
    }
}
