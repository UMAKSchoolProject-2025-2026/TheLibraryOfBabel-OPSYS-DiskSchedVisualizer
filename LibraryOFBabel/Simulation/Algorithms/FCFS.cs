using System;
using System.Collections.Generic;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Simulation.Algorithms
{
    /// <summary>
    /// Simple First-Come, First-Served scheduler helper for the simulation.
    /// Keeps logic isolated so SimulationEngine can call into algorithm implementations later.
    /// </summary>
    public static class FCFS
    {
        /// <summary>
        /// Choose the next request value using FCFS semantics (first item in the pending list).
        /// Throws <see cref="InvalidOperationException"/> when no pending requests are available.
        /// </summary>
        public static int ChooseNext(IReadOnlyList<int> pendingRequests)
        {
            if (pendingRequests is null) throw new ArgumentNullException(nameof(pendingRequests));
            if (pendingRequests.Count == 0) throw new InvalidOperationException("No pending requests available.");

            return pendingRequests[0];
        }

        /// <summary>
        /// Convenience overload that accepts the simulation state and returns the next request.
        /// Uses the state's public PendingRequests collection.
        /// </summary>
        public static int ChooseNext(SimulationState state)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            return ChooseNext(state.PendingRequests);
        }

        /// <summary>
        /// Simulate servicing all pending requests using FCFS order starting from the provided state's head position.
        /// Returns the total seek distance required to service the remaining pending requests in arrival order
        /// and the ordered visit sequence. This does not mutate the provided state.
        /// </summary>
        public static (long TotalDistance, List<int> VisitOrder) Simulate(SimulationState state)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            var seq = new List<int>(state.PendingRequests);
            long total = 0;
            int head = state.HeadPosition;
            var visit = new List<int>(seq.Count);
            foreach (var req in seq)
            {
                total += Math.Abs(head - req);
                head = req;
                visit.Add(req);
            }
            return (total, visit);
        }
    }
}