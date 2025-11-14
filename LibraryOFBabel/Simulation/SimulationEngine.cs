using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOFBabel.Simulation
{
    public enum SchedulingAlgorithm
    {
        FCFS,
        SSTF,
        SCAN,
        CSCAN
    }

    public sealed class SimulationStats
    {
        public long TotalSeekDistance { get; internal set; }
        public int RequestsServed { get; internal set; }
        public double AverageSeek => RequestsServed == 0 ? 0.0 : (double)TotalSeekDistance / RequestsServed;
    }

    public sealed class SimulationState
    {
        public int DiskSize { get; init; }
        public int HeadPosition { get; internal set; }
        public int Direction { get; internal set; } // +1 = increasing, -1 = decreasing
        public IReadOnlyList<int> PendingRequests => pendingRequests.AsReadOnly();
        public SchedulingAlgorithm Algorithm { get; internal set; }

        internal List<int> pendingRequests = new();
        internal List<double> layerRotationOffsets = new(); // Added for per-layer rotations
    }

    public sealed class SimulationEngine : IDisposable
    {
        private readonly SimulationState state;
        private readonly SimulationStats stats = new();
        private bool disposed;

        public event EventHandler<SimulationState>? StateChanged;
        public event EventHandler<SimulationStats>? StatisticsChanged;

        public SimulationEngine(int diskSize = 200, int initialHead = 0, SchedulingAlgorithm initialAlgorithm = SchedulingAlgorithm.FCFS)
        {
            if (diskSize <= 0) throw new ArgumentOutOfRangeException(nameof(diskSize));
            state = new SimulationState
            {
                DiskSize = diskSize,
                HeadPosition = Math.Clamp(initialHead, 0, diskSize - 1),
                Direction = +1,
                Algorithm = initialAlgorithm
            };
        }

        public SimulationState CurrentState => state;
        public SimulationStats CurrentStats => stats;
        public int? NextRequest => state.pendingRequests.Any() ? ChooseNextRequest() : null;

        // Enqueue a single request (will be ignored if out-of-range)
        public void EnqueueRequest(int position)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationEngine));
            if (position < 0 || position >= state.DiskSize) return;
            state.pendingRequests.Add(position);
            RaiseStateChanged();
        }

        // Enqueue multiple requests
        public void EnqueueRequests(IEnumerable<int> positions)
        {
            foreach (var p in positions) EnqueueRequest(p);
        }

        // Remove all pending requests
        public void ClearRequests()
        {
            state.pendingRequests.Clear();
            RaiseStateChanged();
        }

        // Step: moves head to the next request according to current algorithm.
        // Returns true if a request was served; false if no pending requests.
        public bool Step()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationEngine));
            if (!state.pendingRequests.Any()) return false;

            int target = ChooseNextRequest();
            // Move head directly to target (single-step behavior per user request)
            var distance = Math.Abs(state.HeadPosition - target);
            stats.TotalSeekDistance += distance;
            stats.RequestsServed += 1;

            state.HeadPosition = target;
            // update direction based on move
            if (distance > 0) state.Direction = (target > state.HeadPosition) ? +1 : -1;
            // remove one instance of the served request (FCFS serves first, others chosen by value)
            var removed = state.pendingRequests.Remove(target);
            // For FCFS we should remove the earliest inserted matching the target value:
            // (the list.Remove removes first occurrence which preserves FCFS semantics when target is the first entry)

            RaiseStatisticsChanged();
            RaiseStateChanged();
            return removed;
        }

        private int ChooseNextRequest()
        {
            return state.Algorithm switch
            {
                SchedulingAlgorithm.FCFS => state.pendingRequests.First(),
                SchedulingAlgorithm.SSTF => state.pendingRequests.OrderBy(r => Math.Abs(r - state.HeadPosition)).First(),
                SchedulingAlgorithm.SCAN => ChooseScanNext(wrap: false),
                SchedulingAlgorithm.CSCAN => ChooseScanNext(wrap: true),
                _ => state.pendingRequests.First()
            };
        }

        private int ChooseScanNext(bool wrap)
        {
            // prefer requests in current direction
            if (state.Direction >= 0)
            {
                var ahead = state.pendingRequests.Where(r => r >= state.HeadPosition).OrderBy(r => r).FirstOrDefault();
                if (ahead != 0 || state.pendingRequests.Contains(0) && !state.pendingRequests.Contains(ahead)) // handle default 0 returned by FirstOrDefault
                {
                    if (state.pendingRequests.Contains(ahead)) return ahead;
                }

                // if none ahead and wrap is true, choose smallest (circular)
                if (wrap)
                {
                    return state.pendingRequests.OrderBy(r => r).First();
                }

                // reverse direction and pick farthest in new direction
                state.Direction = -1;
                var behind = state.pendingRequests.Where(r => r <= state.HeadPosition).OrderByDescending(r => r).First();
                return behind;
            }
            else
            {
                var behind = state.pendingRequests.Where(r => r <= state.HeadPosition).OrderByDescending(r => r).FirstOrDefault();
                if (behind != 0 || state.pendingRequests.Contains(0) && !state.pendingRequests.Contains(behind))
                {
                    if (state.pendingRequests.Contains(behind)) return behind;
                }

                if (wrap)
                {
                    return state.pendingRequests.OrderByDescending(r => r).First();
                }

                state.Direction = +1;
                var ahead = state.pendingRequests.Where(r => r >= state.HeadPosition).OrderBy(r => r).First();
                return ahead;
            }
        }

        public void Reset(int? headPosition = null)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationEngine));
            state.pendingRequests.Clear();
            stats.TotalSeekDistance = 0;
            stats.RequestsServed = 0;
            state.HeadPosition = headPosition.HasValue ? Math.Clamp(headPosition.Value, 0, state.DiskSize - 1) : 0;
            state.Direction = +1;
            RaiseStatisticsChanged();
            RaiseStateChanged();
        }

        public void ChangeAlgorithm(SchedulingAlgorithm algorithm)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationEngine));
            state.Algorithm = algorithm;
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, state);
        }

        private void RaiseStatisticsChanged()
        {
            StatisticsChanged?.Invoke(this, stats);
        }

        public void Dispose()
        {
            disposed = true;
            StateChanged = null;
            StatisticsChanged = null;
        }
    }
}
