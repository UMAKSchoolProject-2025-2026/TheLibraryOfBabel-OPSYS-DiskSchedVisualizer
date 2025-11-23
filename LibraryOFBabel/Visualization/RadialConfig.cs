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
        private int nodeCount = 200;
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

        // add these new properties into RadialConfig (near other tuning props)
        public int DensityReferenceNodeCount { get; set; } = 200;
        public float DensityScaleExponent { get; set; } = 0.5f; // 0.5 => sqrt behavior
        public float DensityMinScale { get; set; } = 0.25f;
        public float DensityMaxScale { get; set; } = 1.0f;

        // --- Radius / layout scaling (separate from visual density scaling) ---
        // Use to compress the ring radius as node count increases so walkway/walls move inward.
        public int RadiusReferenceNodeCount { get; set; } = 200;
        public float RadiusScaleExponent { get; set; } = 0.5f;
        public float RadiusMinScale { get; set; } = 0.4f;
        public float RadiusMaxScale { get; set; } = 1.0f;

        // --- NodeSize-derived effective sizes (wall, walkway, pointer) ---
        // Factors to compute effective world-space sizes from NodeSize
        public float WallThicknessNodeFactor { get; set; } = 2.5f;    // wall thickness = NodeSize * factor
        public float WalkwayThicknessNodeFactor { get; set; } = 1.5f; // walkway thickness = NodeSize * factor
        public float PointerSizeNodeFactor { get; set; } = 1.0f;      // pointer size = NodeSize * factor (kept for compatibility)

        // Minimum effective sizes (world units) to avoid disappearing geometry
        public float MinEffectiveWallThickness { get; set; } = 0.5f;
        public float MinEffectiveWalkwayThickness { get; set; } = 0.5f;
        public float MinEffectivePointerSize { get; set; } = 0.5f;

        /// <summary>
        /// Effective wall thickness (world units) derived from NodeSize and factor.
        /// This is used by the renderer as the authoritative wall thickness before applying sizeScale.
        /// </summary>
        public float EffectiveWallThickness => MathF.Max(MinEffectiveWallThickness, WallThicknessNodeFactor * NodeSize);

        /// <summary>
        /// Effective walkway thickness (world units) derived from NodeSize and factor.
        /// </summary>
        public float EffectiveWalkwayThickness => MathF.Max(MinEffectiveWalkwayThickness, WalkwayThicknessNodeFactor * NodeSize);

        /// <summary>
        /// Effective pointer (librarian/head) size (world units) derived from effective walkway thickness.
        /// When PointerSizeAuto is true this returns ~50% of the effective walkway thickness (modified by PointerSizeNodeFactor).
        /// Setting explicit PointerSize disables auto.
        /// </summary>
        public float EffectivePointerSize => MathF.Max(MinEffectivePointerSize, EffectiveWalkwayThickness * 0.5f * PointerSizeNodeFactor);

        // If true (default) PointerSize is derived from walkway thickness. If false, uses explicit value set via setter.
        public bool PointerSizeAuto { get; set; } = true;

        // Anchor tuning: fraction (0..1) of walkway thickness where the pointer should be centered (0 = inner edge, 1 = outer edge)
        // Use this to fine-tune pointer placement without changing layout. Default 0.5 centers the pointer in the walkway.
        public float PointerAnchorFactor { get; set; } = 0.5f;
        // Small additional fractional offset applied to PointerAnchorFactor. Interpreted as fraction of walkway thickness.
        // Positive moves pointer outward toward outer edge. Final anchor is clamped to [0,1].
        public float PointerAnchorOffsetFraction { get; set; } = 0f;

        /// <summary>
        /// Size of pointer/head visuals in world units.
        /// When PointerSizeAuto is true this returns a value derived from effective walkway thickness (50% by default).
        /// Setting this property disables PointerSizeAuto and uses the explicit size.
        /// </summary>
        public float PointerSize
        {
            get => PointerSizeAuto ? EffectivePointerSize : pointerSize;
            set
            {
                if (Math.Abs(value - pointerSize) < float.Epsilon) return;
                pointerSize = Math.Max(0f, value);
                // explicit set => disable automatic derivation
                PointerSizeAuto = false;
                Recalculate();
            }
        }

        // Anchor radius for the pointer: computed from walkway inner radius plus a fractional inset into the walkway
        // (PointerAnchorFactor + PointerAnchorOffsetFraction). The combined factor is clamped to 0..1 so the anchor
        // always stays inside the walkway band.
        public float PointerAnchorRadius
        {
            get
            {
                float f = Math.Clamp(PointerAnchorFactor + PointerAnchorOffsetFraction, 0f, 1f);
                // Use WalkwayThickness (geometry) rather than EffectiveWalkwayThickness (visual stroke)
                // so the anchor follows the actual walkway center regardless of stroke scaling.
                return WalkwayInnerRadius + (WalkwayThickness * f);
            }
        }

        // cached derived values
        private float walkwayOuterRadius;
        private float nodeRingRadius;
        private float totalRadius;


        /// <summary>
        /// Compute a multiplicative scale (0..1+) to shrink visuals as NodeCount increases.
        /// Use in the renderer to scale node sizes, stroke widths, pointer, glow, text etc.
        /// </summary>
        public float GetDensityScale()
        {
            // protect divide-by-zero
            int nc = Math.Max(1, NodeCount);
            float refN = Math.Max(1f, DensityReferenceNodeCount);
            float raw = MathF.Pow(refN / (float)nc, DensityScaleExponent);
            // clamp to configured bounds
            return Math.Clamp(raw, DensityMinScale, DensityMaxScale);
        }

        /// <summary>
        /// Compute a multiplicative scale applied to radii/positions as NodeCount increases.
        /// Separate from GetDensityScale which controls visual sizes.
        /// </summary>
        public float GetRadiusScale()
        {
            int nc = Math.Max(1, NodeCount);
            float refN = Math.Max(1f, RadiusReferenceNodeCount);
            float raw = MathF.Pow(refN / (float)nc, RadiusScaleExponent);
            return Math.Clamp(raw, RadiusMinScale, RadiusMaxScale);
        }

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

            // compute node border stroke width (centered on hexagon edges) up-front so autoscaling uses same geometry
            float nodeBorderWidth = Math.Max(NodeBorderMinWidth, WallThickness * NodeBorderWidthFactor);
            float borderHalf = nodeBorderWidth * 0.5f;
            // apothem factor for hexagon
            const float hexApothemFactor = 0.8660254037844386f; // cos(30deg) = sqrt(3)/2

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
                    // compute apothem for current size guess
                    float ap = size * hexApothemFactor;

                    // compute ring radius based on current size guess using apothem minus half-border
                    float r = walkwayOuterRadius + NodeDistanceFromWalkway + (ap - borderHalf);
                    if (r < 0f) r = 0f;

                    // available arc length per node
                    float arc = 2f * MathF.PI * r / Math.Max(1, nodeCount);

                    // Compute maximum fitting node radius from arc length to avoid impossible density.
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
            // apothem (distance from center to flat side) for a regular hexagon based on nodeSize (vertex radius)
            float apothem = nodeSize * hexApothemFactor;

            // place node centers so that hexagon fill + inner border edge touches walkway outer.
            // inner edge of border is at (apothem - borderHalf) from node center.
            nodeRingRadius = walkwayOuterRadius + NodeDistanceFromWalkway + (apothem - borderHalf);
            if (nodeRingRadius < 0f) nodeRingRadius = 0f;

            float wallOuter = walkwayOuterRadius + WallThickness;
            float nodesOuter = nodeRingRadius + nodeSize;

            // pointer anchored to center of walkway (walkway inner + half of effective walkway thickness)
            float pointerAnchor = WalkwayInnerRadius + (EffectiveWalkwayThickness * 0.5f);
            float pointerOuter = pointerAnchor + PointerSize * 0.5f;

            totalRadius = MathF.Max(wallOuter, MathF.Max(nodesOuter, pointerOuter));
            if (totalRadius < 0f) totalRadius = 0f;
        }

    }
}