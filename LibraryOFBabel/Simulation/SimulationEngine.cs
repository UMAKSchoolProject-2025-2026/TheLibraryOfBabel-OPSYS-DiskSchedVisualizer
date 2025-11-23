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
        CSCAN,
        LOOK,
        CLOOK
    }

    public sealed class SimulationStats
    {
        public long TotalSeekDistance { get; internal set; }
        public int RequestsServed { get; internal set; }
        public double AverageSeek => RequestsServed == 0 ? 0.0 : (double)TotalSeekDistance / RequestsServed;
    }

    public sealed class SimulationState
    {
        public int DiskSize { get; internal set; }
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
        // Returns true if a request was served; false if no pending requests or the step moved to an edge without serving.
        public bool Step()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationEngine));
            if (!state.pendingRequests.Any()) return false;

            int disk = Math.Max(1, state.DiskSize);

            int target = ChooseNextRequest();
            // Move head directly to target (single-step behavior per user request)
            var distance = Math.Abs(state.HeadPosition - target);
            stats.TotalSeekDistance += distance;

            // capture previous head for correct direction
            int prev = state.HeadPosition;
            state.HeadPosition = target;
            if (distance > 0) state.Direction = (target > prev) ? +1 : -1;

            // Special-case: for SCAN, if we reached a physical edge, flip direction now so subsequent NextRequest reflects reversal
            if (state.Algorithm == SchedulingAlgorithm.SCAN)
            {
                if (state.HeadPosition == 0)
                {
                    state.Direction = +1;
                }
                else if (state.HeadPosition == disk - 1)
                {
                    state.Direction = -1;
                }
            }

            // remove one instance of the served request (if present)
            var removed = state.pendingRequests.Remove(target);
            if (removed)
            {
                stats.RequestsServed += 1;
                RaiseStatisticsChanged();
            }

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
                SchedulingAlgorithm.LOOK => ChooseLookNext(wrap: false),
                SchedulingAlgorithm.CLOOK => ChooseLookNext(wrap: true),
                _ => state.pendingRequests.First()
            };
        }

        private int ChooseScanNext(bool wrap)
        {
            // SCAN/C-SCAN: force travel to disk edge before reversing/wrapping
            var pending = state.pendingRequests;
            if (!pending.Any()) throw new InvalidOperationException("No pending requests");

            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;
            int disk = Math.Max(1, state.DiskSize);

            if (dir >= 0)
            {
                // requests at or ahead of head ascending
                var ahead = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                var behind = pending.Where(r => r < head).OrderByDescending(r => r).ToList();

                if (ahead.Any()) return ahead.First();

                // no requests ahead
                if (head != disk - 1)
                {
                    // need to travel to the end first
                    return disk - 1;
                }

                // already at end
                if (wrap)
                {
                    // circular: wrap to smallest pending
                    return pending.OrderBy(r => r).First();
                }

                // non-circular: reverse direction and pick the farthest behind
                if (behind.Any())
                {
                    return behind.First();
                }

                // fallback
                return pending.OrderBy(r => r).First();
            }
            else
            {
                // dir < 0: service <= head descending
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                var ahead = pending.Where(r => r > head).OrderBy(r => r).ToList();

                if (behind.Any()) return behind.First();

                // no requests behind
                if (head != 0)
                {
                    // travel to start first
                    return 0;
                }

                // already at start
                if (wrap)
                {
                    // circular: wrap to largest pending
                    return pending.OrderByDescending(r => r).First();
                }

                // non-circular: reverse direction and pick the smallest ahead
                if (ahead.Any())
                {
                    return ahead.First();
                }

                // fallback
                return pending.OrderByDescending(r => r).First();
            }
        }

        private int ChooseLookNext(bool wrap)
        {
            // LOOK/C-LOOK: do not travel to disk edge; behave like classic LOOK
            var pending = state.pendingRequests;
            if (!pending.Any()) throw new InvalidOperationException("No pending requests");

            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;

            if (dir >= 0)
            {
                var ahead = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                var behind = pending.Where(r => r < head).OrderByDescending(r => r).ToList();

                if (ahead.Any()) return ahead.First();

                if (wrap)
                {
                    // C-LOOK: jump to smallest pending (wrap-around among requests)
                    return pending.OrderBy(r => r).First();
                }

                // LOOK: reverse direction and pick farthest behind (largest < head)
                if (behind.Any())
                {
                    return behind.First();
                }

                return pending.OrderBy(r => r).First();
            }
            else
            {
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                var ahead = pending.Where(r => r > head).OrderBy(r => r).ToList();

                if (behind.Any()) return behind.First();

                if (wrap)
                {
                    // C-LOOK: jump to largest pending
                    return pending.OrderByDescending(r => r).First();
                }

                // LOOK: reverse direction and pick smallest ahead
                if (ahead.Any())
                {
                    return ahead.First();
                }

                return pending.OrderByDescending(r => r).First();
            }
        }

        /// <summary>
        /// Change the disk size used by the simulation. Pending requests outside the new range are removed
        /// and head position is clamped to the new size. Raises StateChanged.
        /// </summary>
        public void ChangeDiskSize(int newSize)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SimulationEngine));
            if (newSize <= 0) throw new ArgumentOutOfRangeException(nameof(newSize));

            // remove requests out of range
            state.pendingRequests = state.pendingRequests.Where(r => r >= 0 && r < newSize).ToList();

            // clamp head position
            state.HeadPosition = Math.Clamp(state.HeadPosition, 0, newSize - 1);

            // update disk size
            state.DiskSize = newSize;

            // notify listeners
            RaiseStateChanged();
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
