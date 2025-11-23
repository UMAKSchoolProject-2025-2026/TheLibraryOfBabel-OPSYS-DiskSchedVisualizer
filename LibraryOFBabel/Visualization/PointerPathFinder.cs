using System;
using System.Collections.Generic;
using System.Linq;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Small utility that builds node location information (via RadialLayoutEngine) and
    /// exposes seam-aware neighbor/path helpers for pointer animation.
    /// The seam (edge between node 0 and node N-1) can be treated as impassable.
    /// </summary>
    public sealed class PointerPathfinder
    {
        private RadialConfig cfg;
        private NodePos[] nodes = Array.Empty<NodePos>();

        public bool PreventSeamCrossing { get; set; } = true;

        public PointerPathfinder(RadialConfig cfg)
        {
            this.cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            Rebuild();
        }

        /// <summary>
        /// Rebuild node positions from current config. Call when config / nodecount / layout changes.
        /// </summary>
        public void Rebuild()
        {
            var engine = new RadialLayoutEngine(cfg);
            var positions = engine.GenerateNodePositions();
            nodes = positions.ToArray();
        }

        public int NodeCount => Math.Max(1, nodes.Length);

        public NodePos GetNode(int index)
        {
            if (NodeCount == 0) throw new InvalidOperationException("No nodes available");
            int i = ((index % NodeCount) + NodeCount) % NodeCount;
            return nodes[i];
        }

        /// <summary>
        /// Returns neighboring indices in non-wrapping sense when PreventSeamCrossing == true.
        /// If seam is allowed neighbors wrap around as usual.
        /// </summary>
        public (int prev, int next) GetNeighbors(int index)
        {
            int n = NodeCount;
            if (n == 0) return (0, 0);
            int i = ((index % n) + n) % n;
            int prev = i - 1;
            int next = i + 1;

            if (PreventSeamCrossing)
            {
                prev = Math.Max(0, prev);
                next = Math.Min(n - 1, next);
            }
            else
            {
                prev = (prev + n) % n;
                next = next % n;
            }

            return (prev, next);
        }

        /// <summary>
        /// True if an edge between a and b is the seam (0 <-> N-1).
        /// </summary>
        public bool IsSeamEdge(int a, int b)
        {
            int n = NodeCount;
            if (n <= 1) return false;
            int aa = ((a % n) + n) % n;
            int bb = ((b % n) + n) % n;
            return (aa == 0 && bb == n - 1) || (aa == n - 1 && bb == 0);
        }

        /// <summary>
        /// Compute an angular delta (in degrees) from fromIndex to toIndex that never crosses the seam.
        /// The returned delta is in degrees and is non-wrapping: it represents the straight arc along
        /// increasing indices if to >= from or decreasing if to < from.
        /// </summary>
        public float ComputeDeltaDegAvoidingSeam(int fromIndex, int toIndex)
        {
            int n = NodeCount;
            if (n <= 0) return 0f;
            int f = ((fromIndex % n) + n) % n;
            int t = ((toIndex % n) + n) % n;
            if (f == t) return 0f;

            // If seam prevention is disabled, choose shortest angular step (may wrap)
            if (!PreventSeamCrossing)
            {
                // shortest: positive or negative <= 180
                int rawDelta = t - f;
                // normalize into (-n/2..n/2]
                if (rawDelta > n / 2) rawDelta -= n;
                if (rawDelta <= - (n + 1) / 2) rawDelta += n;
                return rawDelta / (float)n * 360f;
            }

            // With seam prevented: do NOT wrap. If t >= f move forward, else move backward.
            if (t >= f)
            {
                return (t - f) / (float)n * 360f; // forward non-wrapping
            }
            else
            {
                return -((f - t) / (float)n * 360f); // backward non-wrapping
            }
        }

        /// <summary>
        /// Returns the list of indices visited when moving from 'fromIndex' to 'toIndex' without crossing seam.
        /// Direction: 0 -> choose non-wrap arc (forward if to>=from else backward),
        ///           >0 -> force forward (increasing indices, stops at N-1 if PreventSeamCrossing),
        ///           <0 -> force backward (decreasing indices, stops at 0 if PreventSeamCrossing).
        /// </summary>
        public List<int> ComputeNonWrappingPathIndices(int fromIndex, int toIndex, int direction = 0)
        {
            int n = NodeCount;
            var path = new List<int>();
            if (n == 0) return path;
            int f = ((fromIndex % n) + n) % n;
            int t = ((toIndex % n) + n) % n;
            if (f == t) { path.Add(f); return path; }

            if (direction == 0)
            {
                if (t >= f)
                {
                    for (int i = f; i <= t; i++) path.Add(i);
                }
                else
                {
                    for (int i = f; i >= t; i--) path.Add(i);
                }
            }
            else if (direction > 0)
            {
                if (t >= f)
                {
                    for (int i = f; i <= t; i++) path.Add(i);
                }
                else
                {
                    // forward but would need wrapping; clamp to n-1
                    for (int i = f; i <= n - 1; i++) path.Add(i);
                }
            }
            else
            {
                if (t <= f)
                {
                    for (int i = f; i >= t; i--) path.Add(i);
                }
                else
                {
                    // backward but would need wrapping; clamp to 0
                    for (int i = f; i >= 0; i--) path.Add(i);
                }
            }

            return path;
        }
    }
}
