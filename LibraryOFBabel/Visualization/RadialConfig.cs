using System;
using SkiaSharp;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Configuration container for radial visualization parameters.
    /// Changing any public parameter will trigger a recalculation of derived helper values.
    /// </summary>
    public sealed class RadialConfig
    {
        // backing fields
        private int nodeCount = 50;
        private float viewportScale = 1.0f;
        private float walkwayInnerRadius = 100f;
        private float walkwayThickness = 10f;
        private float nodeDistanceFromWalkway = -2.5f;
        private float nodeSize = 6f; // this will be overwritten by autoscaling when enabled
        private float pointerSize = 3f;
        private float wallThickness = 15f;

        // autoscaling controls
        public bool AutoScaleNodeSize { get; set; } = true;
        public float MinNodeSize { get; set; } = 2f;
        public float MaxNodeSize { get; set; } = 40f;

        // viewport fitting controls
        // When enabled the renderer will compute a scale so the scene fills the control.
        public bool AutoFitToViewport { get; set; } = true;
        // Padding in device pixels applied when auto-fitting (keeps a margin).
        public float FitPaddingPixels { get; set; } = 20f;

        // --- Node border configuration ---
        // Factor applied to WallThickness to derive node border stroke width (world units).
        // Border width = Max(NodeBorderMinWidth, WallThickness * NodeBorderWidthFactor)
        public float NodeBorderWidthFactor { get; set; } = 0.15f;
        // Minimum world-space border width
        public float NodeBorderMinWidth { get; set; } = 0.5f;

        // --- Label configuration ---
        // Fraction of node diameter reserved as padding for label (0..0.5). Smaller = larger text.
        // Accepts fractional (0..1) or percentage (1..100) values (backwards-compatible).
        public float NodeLabelPaddingFactor { get; set; } = 0.30f;

        // Toggle labels on/off
        public bool ShowNodeLabels { get; set; } = true;

        // Global multiplier applied to computed text size (world units). Use to scale labels up/down.
        public float NodeLabelScale { get; set; } = 1.0f;

        // Inset factor applied to hex apothem to compute radial label offset (0..1)
        public float NodeLabelInsetFactor { get; set; } = 0f;

        // Scale factor applied to single-digit labels for visual balance (0..1)
        public float SingleDigitScaleFactor { get; set; } = 0.5f;

        // Extra rotation applied to node labels (degrees). Positive = further rotate clockwise after inward rotation.
        public float NodeLabelRotationDegrees { get; set; } = 0f;

        // --- Color configuration (SKColor includes alpha) ---
        // Dark gold used for walls and node borders (opaque by default)
        public SKColor WallAndBorderColor { get; set; } = new SKColor(184, 134, 11); // DarkGoldenrod (#B8860B)
        // Light gold used for hexagon fill and walkway (opaque by default)
        public SKColor WalkwayAndNodeFillColor { get; set; } = new SKColor(255, 215, 0); // Gold (#FFD700)

        // --- Wall glow configuration ---
        public bool WallGlowEnabled { get; set; } = true;
        // Glow width in world units (how "thick" the glow is). This will be multiplied by the effective viewport scale for the blur sigma.
        public float WallGlowWidth { get; set; } = 3f;
        // Glow color (alpha controls intensity)
        public SKColor WallGlowColor { get; set; } = new SKColor(255, 215, 0, 120); // Gold with alpha

        // cached derived values
        private float walkwayOuterRadius;
        private float nodeRingRadius;
        private float totalRadius;

        public RadialConfig()
        {
            Recalculate();
        }

        /// <summary>
        /// Number of nodes around the ring.
        /// </summary>
        public int NodeCount
        {
            get => nodeCount;
            set
            {
                if (value == nodeCount) return;
                nodeCount = Math.Max(1, value);
                Recalculate();
            }
        }

        /// <summary>
        /// Global viewport scale applied to all world distances when rendering.
        /// When AutoFitToViewport is enabled the renderer may override this per-frame to best-fit the canvas.
        /// </summary>
        public float ViewportScale
        {
            get => viewportScale;
            set
            {
                if (Math.Abs(value - viewportScale) < float.Epsilon) return;
                viewportScale = Math.Max(0f, value);
                Recalculate();
            }
        }

        /// <summary>
        /// Inner radius of the walkway (distance from center to inner edge of walkway) in world units.
        /// </summary>
        public float WalkwayInnerRadius
        {
            get => walkwayInnerRadius;
            set
            {
                if (Math.Abs(value - walkwayInnerRadius) < float.Epsilon) return;
                walkwayInnerRadius = Math.Max(0f, value);
                Recalculate();
            }
        }

        /// <summary>
        /// Thickness of the walkway ring in world units.
        /// </summary>
        public float WalkwayThickness
        {
            get => walkwayThickness;
            set
            {
                if (Math.Abs(value - walkwayThickness) < float.Epsilon) return;
                walkwayThickness = Math.Max(0f, value);
                Recalculate();
            }
        }

        /// <summary>
        /// Radial gap from the walkway outer edge to the center of nodes.
        /// </summary>
        public float NodeDistanceFromWalkway
        {
            get => nodeDistanceFromWalkway;
            set
            {
                if (Math.Abs(value - nodeDistanceFromWalkway) < float.Epsilon) return;
                nodeDistanceFromWalkway = Math.Max(0f, value);
                Recalculate();
            }
        }

        /// <summary>
        /// Visual node radius (from center to vertex) in world units.
        /// When AutoScaleNodeSize is enabled this value is computed automatically.
        /// </summary>
        public float NodeSize
        {
            get => nodeSize;
            set
            {
                if (Math.Abs(value - nodeSize) < float.Epsilon) return;
                nodeSize = Math.Max(0f, value);
                // if user manually sets NodeSize we disable autoscaling to respect explicit intent
                AutoScaleNodeSize = false;
                Recalculate();
            }
        }

        /// <summary>
        /// Size of pointer/head visuals in world units.
        /// </summary>
        public float PointerSize
        {
            get => pointerSize;
            set
            {
                if (Math.Abs(value - pointerSize) < float.Epsilon) return;
                pointerSize = Math.Max(0f, value);
                Recalculate();
            }
        }

        /// <summary>
        /// Thickness of the wall drawn between nodes in world units.
        /// </summary>
        public float WallThickness
        {
            get => wallThickness;
            set
            {
                if (Math.Abs(value - wallThickness) < float.Epsilon) return;
                wallThickness = Math.Max(0f, value);
                Recalculate();
            }
        }

        // --- Derived helper properties ---

        /// <summary>
        /// Outer radius of the walkway (inner + thickness).
        /// Updated automatically when inputs change.
        /// </summary>
        public float WalkwayOuterRadius => walkwayOuterRadius;

        /// <summary>
        /// Radius at which node centers are placed (walkway outer + offset + node size).
        /// </summary>
        public float NodeRingRadius => nodeRingRadius;

        /// <summary>
        /// Total radius required for the layout including walls and pointer.
        /// </summary>
        public float TotalRadius => totalRadius;

        /// <summary>
        /// Force recalculation of derived values (public API for bulk updates if needed).
        /// NodeSize will be auto-scaled based on NodeCount and ring radius when AutoScaleNodeSize is true.
        /// </summary>
        public void Recalculate()
        {
            // compute derived values ensuring non-negative and stable results
            walkwayOuterRadius = WalkwayInnerRadius + WalkwayThickness;
            if (walkwayOuterRadius < 0f) walkwayOuterRadius = 0f;

            // When autoscaling NodeSize we must avoid a circular dependency between NodeSize and NodeRingRadius.
            // We iteratively converge: start from a reasonable guess and recompute until stable.
            if (AutoScaleNodeSize)
            {
                // Use an iterative approach: start from the current backing value and converge.
                float size = MathF.Min(MaxNodeSize, MathF.Max(MinNodeSize, nodeSize));
                const int maxIter = 20;
                const float eps = 0.01f;

                for (int i = 0; i < maxIter; i++)
                {
                    // compute ring radius based on current size guess
                    float r = walkwayOuterRadius + NodeDistanceFromWalkway + size;
                    if (r < 0f) r = 0f;

                    // available arc length per node
                    float arc = 2f * MathF.PI * r / Math.Max(1, nodeCount);

                    // Compute maximum fitting node radius from arc length to avoid impossible density.
                    // User-provided formula: maxFittingNodeSize = (2?R / NodeCount) / 2
                    // Apply a small padding factor (0.9) to avoid touching.
                    float maxFittingRadius = (arc * 0.5f) * 0.9f;

                    // desired size: try to use as much space as possible but respect global min/max
                    float desiredSize = MathF.Min(MaxNodeSize, MathF.Max(MinNodeSize, maxFittingRadius));

                    if (Math.Abs(desiredSize - size) < eps)
                    {
                        size = desiredSize;
                        break;
                    }

                    size = desiredSize;
                }

                // update backing field directly (avoid NodeSize setter which would disable autoscale)
                nodeSize = size;
            }

            // compute node ring radius and total radius
            nodeRingRadius = walkwayOuterRadius + NodeDistanceFromWalkway + nodeSize;
            if (nodeRingRadius < 0f) nodeRingRadius = 0f;

            float wallOuter = walkwayOuterRadius + WallThickness;
            float nodesOuter = nodeRingRadius + nodeSize;
            float pointerOuter = walkwayOuterRadius + PointerSize * 0.5f;

            totalRadius = MathF.Max(wallOuter, MathF.Max(nodesOuter, pointerOuter));
            if (totalRadius < 0f) totalRadius = 0f;
        }

    }
}