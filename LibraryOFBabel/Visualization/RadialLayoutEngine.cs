using System;
using System.Collections.Generic;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Deterministic layout engine that computes node positions around a radial ring
    /// based on a provided <see cref="RadialConfig"/>.
    /// </summary>
    public sealed class RadialLayoutEngine
    {
        private readonly RadialConfig config;

        public RadialLayoutEngine(RadialConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            // ensure derived values are up-to-date for deterministic results
            this.config.Recalculate();
        }

        /// <summary>
        /// Generates the world-space positions for each node placed on the node ring.
        /// The returned list is stable: same config => same ordering and values.
        /// </summary>
        public List<NodePos> GenerateNodePositions()
        {
            // ensure config is fresh in case callers modified it after construction
            config.Recalculate();

            int count = Math.Max(1, config.NodeCount);
            var result = new List<NodePos>(count);

            // angle step in degrees
            float angleStep = 360f / count;

            float radius = config.NodeRingRadius;

            for (int i = 0; i < count; i++)
            {
                float angleDeg = i * angleStep;
                double angleRad = angleDeg * (Math.PI / 180.0);

                // deterministic trig -> use System.Math (double) and cast down to float
                float x = (float)(Math.Cos(angleRad) * radius);
                float y = (float)(Math.Sin(angleRad) * radius);

                result.Add(new NodePos(x, y, angleDeg, (float)angleRad));
            }

            return result;
        }
    }

    /// <summary>
    /// Position information for a single node on the ring.
    /// </summary>
    public readonly struct NodePos
    {
        public NodePos(float x, float y, float angleDegrees, float angleRadians)
        {
            X = x;
            Y = y;
            AngleDegrees = angleDegrees;
            AngleRadians = angleRadians;
        }

        public float X { get; }
        public float Y { get; }
        public float AngleDegrees { get; }
        public float AngleRadians { get; }
    }
}
