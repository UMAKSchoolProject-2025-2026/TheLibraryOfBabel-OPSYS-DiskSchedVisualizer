using System;
using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using LibraryOFBabel.Simulation;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Renderer for the disk visualization. Exposes several public configuration properties
    /// (see below) so you can tweak visuals without changing drawing code.
    /// Connect to a SimulationEngine via the `Engine` property to display requests, head and layer rotations.
    /// </summary>
    public class DiskVisualizationRenderer : IDisposable
    {
        private SKGLControl skglControl;
        private System.Windows.Forms.Timer animationTimer;
        private bool disposed = false;

        // --- Configurable properties (tweak these at runtime before calling Start) ---
        public int LayerCount { get; set; } = 3;                            // preferred number of concentric layers
        public int NodeCount { get; set; } = 30;                            // base number of nodes in innermost ring
        public float CoreRadiusFactor { get; set; } = 0.08f;               // core radius as fraction of min dimension
        public float MaxRadiusMarginFactor { get; set; } = 0.05f;          // margin from edges as fraction of min dimension
        public float NodeRadiusFactor { get; set; } = 0.35f;               // node radius as fraction of layer spacing (max)
        public float HexOrientationOffset { get; set; } = MathF.PI / 6f;   // rotate hex shape slightly for nicer look

        /// <summary>
        /// Desired spacing between layers expressed as fraction of the min(control width,height).
        /// Used to compute the maximum number of layers that fit the available area.
        /// </summary>
        public float DesiredLayerSpacingFactor { get; set; } = 0.12f;

        /// <summary>
        /// When true the renderer will automatically choose the layer count to fit the panel.
        /// When false the renderer will clamp the configured LayerCount to the maximum that fits.
        /// </summary>
        public bool AutoAdjustLayerCount { get; set; } = true;

        /// <summary>
        /// rotation speed in radians per second for the base layer (time-based update).
        /// Increase to rotate faster; decrease to slow down.
        /// </summary>
        public float RotationSpeedRadiansPerSecond { get; set; } = 0.9f;

        public float LibrarianSpeedFactor { get; set; } = 1.3f;            // librarian speed relative to base rotation
        public float LibrarianOffsetNodeFactor { get; set; } = 1.5f;     // librarian offset as multiple of outermost node radius
        public float LibrarianRadiusFactor { get; set; } = 0.02f;          // librarian visual radius fraction of min dim
        public float LibrarianSizeMultiplier { get; set; } = 0.4f;       // multiplier for librarian size
        public float PerspectivePower { get; set; } = 1.2f;               // power for perspective scaling

        /// <summary>
        /// Minimum radial gap between layers in pixels to prevent overlap and maintain visual separation.
        /// </summary>
        public float MinLayerGap { get; set; } = 12f;

        /// <summary>
        /// Adjustment for walkway width: negative extends outward, positive extends inward.
        /// </summary>
        public float WalkwayWidthAdjustment { get; set; } = 30f;

        /// <summary>
        /// Adjustment for walkway offset from ring center: negative offsets outward, positive offsets inward.
        /// </summary>
        public float WalkwayOffsetAdjustment { get; set; } = 15f;

        public byte GuideRingAlpha { get; set; } = 30;                     // alpha (0..255) for the guide ring
        public int AnimationIntervalMs
        {
            get => animationTimer?.Interval ?? 16;
            set
            {
                if (animationTimer != null) animationTimer.Interval = Math.Max(1, value);
            }
        }

        // Link to simulation (optional). Set by Form1 after creating engine.
        public SimulationEngine? Engine { get; set; }

        // When true the renderer draws a dim overlay (used when simulation is paused).
        public bool IsDimmed { get; set; } = false;

        // --- Path highlighting settings ---
        public bool DrawHeadToNextArrow { get; set; } = true;              // draw arrow from head to next request
        public float ArrowStrokeWidth { get; set; } = 3f;                  // arrow line width
        public SKColor ArrowColor { get; set; } = SKColors.Red;            // arrow color
        public float ArrowHeadSize { get; set; } = 10f;                    // size of arrowhead

        // --- Diagnostics / tuning (opt-in) ---
        public bool EnableDiagnostics { get; set; } = false;
        public float DiagnosticsDeltaThresholdSeconds { get; set; } = 0.05f;
        public float MaxDeltaClampSeconds { get; set; } = 0.5f; // existing clamp used (you can lower later)

        // --- internal state ---
        private double rotationAngle = 0.0; // fallback rotation if engine is not set
        // high-resolution timer to compute deltaTime for smooth motion
        private readonly Stopwatch stopwatch = new();
        private double lastStopwatchSeconds = 0.0;
        private Point? previousMousePosition = null;
        private float librarianAngleOffset = 0f;

        public event EventHandler<SKPaintGLSurfaceEventArgs>? PaintRequested;

        public DiskVisualizationRenderer()
        {
            InitializeSkiaSharp();
            InitializeAnimationTimer();
        }

        public Control GetVisualizationControl() => skglControl;

        public void Start()
        {
            // initialize timing state to avoid large first-delta jumps
            stopwatch.Start();
            lastStopwatchSeconds = stopwatch.Elapsed.TotalSeconds;
            animationTimer.Start();

            if (EnableDiagnostics)
            {
                Debug.WriteLine($"[DiskVis] Start() - time={lastStopwatchSeconds:F6}");
            }
        }

        public void Stop()
        {
            animationTimer.Stop();
            stopwatch.Stop();
            stopwatch.Reset();
            lastStopwatchSeconds = 0.0;

            if (EnableDiagnostics)
            {
                Debug.WriteLine("[DiskVis] Stop()");
            }
        }

        public bool IsRunning => animationTimer.Enabled;

        private void InitializeSkiaSharp()
        {
            skglControl = new SKGLControl()
            {
                Dock = DockStyle.Fill
            };

            // ensure we re-render on resize immediately
            skglControl.PaintSurface += OnPaintSurface;
            skglControl.Resize += (s, e) => skglControl.Invalidate();
            skglControl.MouseMove += OnMouseMove;
        }

        private void InitializeAnimationTimer()
        {
            animationTimer = new System.Windows.Forms.Timer()
            {
                Interval = 16 // ~60 FPS nominal
            };

            animationTimer.Tick += (sender, e) =>
            {
                // compute precise delta time using Stopwatch for smoothness even if Tick is irregular
                var now = stopwatch.Elapsed.TotalSeconds;
                var deltaSeconds = (float)(now - lastStopwatchSeconds);

                // diagnostics: log negative deltas (shouldn't happen) and large deltas
                if (EnableDiagnostics)
                {
                    if (deltaSeconds < 0f)
                    {
                        Debug.WriteLine($"[DiskVis][WARN] negative deltaSeconds={deltaSeconds:F6} (now={now:F6}, last={lastStopwatchSeconds:F6})");
                    }
                    else if (deltaSeconds > DiagnosticsDeltaThresholdSeconds)
                    {
                        Debug.WriteLine($"[DiskVis][DELTA] deltaSeconds={deltaSeconds:F6} (now={now:F6}, last={lastStopwatchSeconds:F6})");
                    }
                }

                // clamp delta to avoid large jumps after pauses/dragging (safety)
                if (deltaSeconds < 0f) deltaSeconds = 0f;
                if (deltaSeconds > MaxDeltaClampSeconds)
                {
                    if (EnableDiagnostics)
                    {
                        Debug.WriteLine($"[DiskVis][CLAMP] deltaSeconds capped from {deltaSeconds:F6} to {MaxDeltaClampSeconds:F6}");
                    }
                    deltaSeconds = MaxDeltaClampSeconds;
                }

                lastStopwatchSeconds = now;

                // always update rotation for continuous rotation
                rotationAngle += RotationSpeedRadiansPerSecond * deltaSeconds;
                const double normalizeThreshold = 1e6;
                if (rotationAngle >= normalizeThreshold || rotationAngle <= -normalizeThreshold)
                {
                    rotationAngle = rotationAngle % (Math.PI * 2.0);
                    if (rotationAngle < 0.0) rotationAngle += Math.PI * 2.0;
                }

                skglControl?.Invalidate();
                PanelZoom?.Invalidate();
            };
        }

        private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
        {
            // snapshot rotation and simulation state to avoid mid-paint changes
            double fallbackAngle = rotationAngle;
            var simState = Engine?.CurrentState;

            // Allow external subscribers to completely override rendering
            if (PaintRequested != null)
            {
                PaintRequested.Invoke(this, e);
                return;
            }

            RenderScalableLayers(e, simState, fallbackAngle);

            // dim overlay when requested (keeps SKGLControl running but visually dim)
            if (IsDimmed)
            {
                var canvas = e.Surface.Canvas;
                using var dimPaint = new SKPaint { Color = new SKColor(0, 0, 0, 110), IsAntialias = false, Style = SKPaintStyle.Fill };
                canvas.DrawRect(new SKRect(0, 0, e.Info.Width, e.Info.Height), dimPaint);
            }
        }

        private void RenderScalableLayers(SKPaintGLSurfaceEventArgs e, SimulationState? simState, double fallbackAngle)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;

            canvas.Clear(SKColors.Black);

            // Dynamic center
            float cx = info.Width / 2f;
            float cy = info.Height / 2f;
            float minDim = Math.Min(info.Width, info.Height);

            // Cache values for zoom panel
            currentCx = cx;
            currentCy = cy;
            currentRotationAngle = fallbackAngle;

            // Maximum usable radius (leave margin)
            float margin = minDim * MaxRadiusMarginFactor + (minDim * CoreRadiusFactor);
            float maxRadius = (minDim / 2f) - margin;
            if (maxRadius < 0f) maxRadius = 0f;

            // Determine desired spacing (pixels) and compute the maximum number of layers that fit
            float desiredSpacing = Math.Max(8f, minDim * DesiredLayerSpacingFactor); // minimum spacing safeguard
            int maxLayersThatFit = (int)Math.Floor(maxRadius / Math.Max(1f, desiredSpacing));
            if (maxLayersThatFit < 1) maxLayersThatFit = 1;

            // Disk size for mapping nodes -> cylinders
            int diskSize = simState?.DiskSize ?? 1;

            float lastNodeRadius = 0f;

            // Compute the maximum number of layers that fit based on perspective scaling and spacing constraints
            // First, estimate baseNodeRadius for calculation
            float tempBaseNodeRadius = Math.Max(1f, maxRadius * 0.35f); // rough estimate
            int maxLayers = CalculateMaxLayers(maxRadius, tempBaseNodeRadius, MinLayerGap, PerspectivePower);

            // Choose effective layer count: either auto-adjust to fit, or clamp configured LayerCount
            int layers = AutoAdjustLayerCount ? maxLayers : Math.Min(LayerCount, maxLayers);

            // Compute layer spacing for compatibility (outermost radius)
            float layerSpacing = (layers > 0) ? (maxRadius / layers) : maxRadius;

            // Compute base node radius for outer layer
            float baseNodeRadius = 0f;
            {
                float candidateBySpacing = (maxRadius / layers) * NodeRadiusFactor;
                float circumference = 2f * MathF.PI * MathF.Max(0.0001f, maxRadius);
                float candidateByCirc = (circumference / Math.Max(1, NodeCount)) * 0.45f;
                float candidateByRadial = Math.Max(1f, (maxRadius / layers) * 0.9f / 2f);
                baseNodeRadius = MathF.Min(candidateBySpacing, MathF.Min(candidateByCirc, candidateByRadial));
                lastNodeRadius = baseNodeRadius;
            }

            // Compute the maximum number of layers that fit based on perspective scaling and spacing constraints
            maxLayers = CalculateMaxLayers(maxRadius, baseNodeRadius, MinLayerGap, PerspectivePower);

            // Choose effective layer count: either auto-adjust to fit, or clamp configured LayerCount
            layers = AutoAdjustLayerCount ? maxLayers : Math.Min(LayerCount, maxLayers);

            // Compute layer spacing for compatibility (outermost radius)
            layerSpacing = (layers > 0) ? (maxRadius / layers) : maxRadius;

            // Cache values
            currentMaxRadius = maxRadius;
            currentLayers = layers;
            currentLayerSpacing = layerSpacing;
            currentBaseNodeRadius = baseNodeRadius;
            currentLastNodeRadius = lastNodeRadius;

            zoomDrawer?.UpdateCache(cx, cy, maxRadius, layers, layerSpacing, baseNodeRadius, lastNodeRadius, fallbackAngle, librarianAngleOffset);
            // Draw center curiosity core scaled to layerSpacing
            using (var corePaint = new SKPaint { Color = SKColors.Yellow, IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                float coreRadius = Math.Max(4f, layerSpacing * CoreRadiusFactor * 2f);
                canvas.DrawCircle(cx, cy, coreRadius, corePaint);
            }

            // Prepare request set for quick lookup (map integers)
            var pendingSet = simState != null ? new HashSet<int>(simState.PendingRequests) : new HashSet<int>();

            // Disk size for mapping nodes -> cylinders
            diskSize = simState?.DiskSize ?? 1;

            lastNodeRadius = 0f;

            // Compute base node radius for outer layer
            baseNodeRadius = 0f;
            {
                float candidateBySpacing = (maxRadius / layers) * NodeRadiusFactor;
                float circumference = 2f * MathF.PI * MathF.Max(0.0001f, maxRadius);
                float candidateByCirc = (circumference / Math.Max(1, NodeCount)) * 0.45f;
                float candidateByRadial = Math.Max(1f, (maxRadius / layers) * 0.9f / 2f);
                baseNodeRadius = MathF.Min(candidateBySpacing, MathF.Min(candidateByCirc, candidateByRadial));
                lastNodeRadius = baseNodeRadius;
            }

            // Compute librarian angle
            double librarianAngle = 0.0;
            if (simState != null)
            {
                int headPos = simState.HeadPosition;
                int disk = Math.Max(1, simState.DiskSize);
                double headAngle = (headPos / (double)disk) * (Math.PI * 2.0);
                librarianAngle = headAngle + librarianAngleOffset;
            }

            // Draw each layer (floor first, then main)
            for (int layerIndex = 0; layerIndex < layers; layerIndex++)
            {
                // perspective scaling
                float t = layerIndex / (float)(layers - 1);
                float scale = 0.1f + 0.9f * MathF.Pow(t, PerspectivePower);
                float ringRadius = maxRadius * scale;

                // nodes per layer stays constant
                int nodesThisLayer = NodeCount;

                // angle offset for this layer (rotation) — outermost does not rotate, inner rotate at different rates and alternating directions
                float rotationMultiplier = (layers - layerIndex - 1) * 0.1f * (layerIndex % 2 == 0 ? 1f : -1f);
                float layerRotation = (float)(fallbackAngle * rotationMultiplier);

                float drawNodeRadius = baseNodeRadius * scale;

                if (layerIndex == layers - 1) lastNodeRadius = baseNodeRadius; // keep full for outer librarian

                // Ring paint with gradual dimming for inner rings
                byte ringAlpha = (byte)(GuideRingAlpha * (layerIndex + 1) / layers);
                using var ringPaint = new SKPaint { Color = SKColors.White.WithAlpha(ringAlpha), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.5f, 1f * scale) };

                // Node paints with gradual darkening for inner layers
                byte nodeAlpha = (byte)(255 * (layerIndex + 1) / layers);
                using var baseFill = new SKPaint { Color = SKColor.Parse("#B8860B").WithAlpha(nodeAlpha), IsAntialias = true, Style = SKPaintStyle.Fill };
                using var requestedFill = new SKPaint { Color = SKColors.Yellow, IsAntialias = true, Style = SKPaintStyle.Fill }; // keep requested full brightness
                using var strokePaint = new SKPaint { Color = SKColors.Black.WithAlpha((byte)(200 * (layerIndex + 1) / layers)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(0.5f, 1f * scale) };
                using var borderPaint = new SKPaint { Color = SKColor.Parse("#FFD700"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f * (layerIndex + 1f) / layers };

                // ----- MAIN LAYER -----
                // draw ring faint
                canvas.DrawCircle(cx, cy, ringRadius + drawNodeRadius * 0.6f, ringPaint);

                // angle step and draw nodes
                float angleStep = (2f * MathF.PI) / nodesThisLayer;

                for (int i = 0; i < nodesThisLayer; i++)
                {
                    float angle = (i * angleStep) + layerRotation;
                    float x = cx + ringRadius * MathF.Cos(angle);
                    float y = cy + ringRadius * MathF.Sin(angle);

                    // Map this node to a cylinder index so we can highlight requested nodes.
                    int cylinder = diskSize > 1 ? (int)Math.Round((double)i / nodesThisLayer * (diskSize - 1)) : 0;

                    // choose paint (requested -> yellow)
                    var fill = pendingSet.Contains(cylinder) ? requestedFill : baseFill;

                    var nodePath = CreateRegularPolygonPath(6, drawNodeRadius, x, y, angle + HexOrientationOffset);
                    canvas.DrawPath(nodePath, fill);
                    canvas.DrawPath(nodePath, borderPaint);
                    canvas.DrawPath(nodePath, strokePaint);
                    nodePath.Dispose();
                }

                // Draw walkway ring above hexagons, below librarian
                {
                    float S = scale;
                    float thickness = WalkwayWidthAdjustment * S;
                    float walkwayRadius = ringRadius - WalkwayOffsetAdjustment * S - thickness / 2f;
                    float outerRadiusWalk = walkwayRadius + thickness / 2f;
                    float innerRadiusWalk = Math.Max(0f, walkwayRadius - thickness / 2f);
                    byte walkwayAlpha = (byte)(nodeAlpha * scale);
                    using var walkwayPaint = new SKPaint { Color = SKColor.Parse("#B8860B").WithAlpha(walkwayAlpha), IsAntialias = true, Style = SKPaintStyle.Fill };
                    var walkwayPath = new SKPath();
                    walkwayPath.AddCircle(cx, cy, outerRadiusWalk);
                    walkwayPath.AddCircle(cx, cy, innerRadiusWalk, SKPathDirection.CounterClockwise);
                    canvas.DrawPath(walkwayPath, walkwayPaint);
                    walkwayPath.Dispose();
                }

                // Draw inner librarian for this layer, mirroring the outer
                if (layerIndex < layers - 1 && simState != null)
                {
                    float libRadiusFromCenter = ringRadius - drawNodeRadius * LibrarianOffsetNodeFactor;
                    float libAngle = (float)(librarianAngle + layerRotation);
                    float libX = cx + libRadiusFromCenter * MathF.Cos(libAngle);
                    float libY = cy + libRadiusFromCenter * MathF.Sin(libAngle);

                    using var innerLibPaint = new SKPaint { Color = SKColors.Blue.WithAlpha(nodeAlpha), IsAntialias = false, Style = SKPaintStyle.Fill };
                    canvas.DrawCircle(libX, libY, drawNodeRadius * LibrarianSizeMultiplier, innerLibPaint);
                }
            }

            // Draw head / librarian on outermost layer.
            if (simState != null)
            {
                int headPos = simState.HeadPosition; // integral cylinder (renderer uses engine's stable API)
                int disk = Math.Max(1, simState.DiskSize);
                // map headPos to angle around circle (outermost)
                double headAngle = (headPos / (double)disk) * (Math.PI * 2.0);

                // fallback rotation addition
                double finalAngle = headAngle + librarianAngleOffset;

                float outerRadius = layerSpacing * layers;
                float libOutwardOffset = lastNodeRadius * LibrarianOffsetNodeFactor;
                float libRadiusFromCenter = outerRadius - libOutwardOffset;

                float hx = cx + libRadiusFromCenter * MathF.Cos((float)finalAngle);
                float hy = cy + libRadiusFromCenter * MathF.Sin((float)finalAngle);

                // draw head and librarian: head = cyan, librarian = blue fill scaled to node size
                using var libPaint = new SKPaint { Color = SKColors.Blue, IsAntialias = false, Style = SKPaintStyle.Fill };

                canvas.DrawCircle(hx, hy, lastNodeRadius * LibrarianSizeMultiplier, libPaint);
            }

            // Draw arrow from head to next request if enabled
            if (DrawHeadToNextArrow && simState != null && Engine?.NextRequest.HasValue == true)
            {
                int nextCylinder = Engine.NextRequest.Value;
                int headPos = simState.HeadPosition;
                int disk = Math.Max(1, simState.DiskSize);

                // map head and next to angles
                double headAngle = (headPos / (double)disk) * (Math.PI * 2.0);
                double nextAngle = (nextCylinder / (double)disk) * (Math.PI * 2.0);

                // fallback rotation addition
                double finalHeadAngle = headAngle + fallbackAngle;
                double finalNextAngle = nextAngle + fallbackAngle;

                float outerRadius = layerSpacing * layers;
                float libOutwardOffset = lastNodeRadius * LibrarianOffsetNodeFactor;
                float radius = outerRadius - libOutwardOffset;

                float hx = cx + radius * MathF.Cos((float)finalHeadAngle);
                float hy = cy + radius * MathF.Sin((float)finalHeadAngle);
                float nx = cx + radius * MathF.Cos((float)finalNextAngle);
                float ny = cy + radius * MathF.Sin((float)finalNextAngle);

                // draw arrow line
                using var arrowPaint = new SKPaint { Color = ArrowColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = ArrowStrokeWidth, StrokeCap = SKStrokeCap.Round };
                canvas.DrawLine(hx, hy, nx, ny, arrowPaint);

                // draw arrowhead (simple triangle)
                float dx = nx - hx;
                float dy = ny - hy;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 0)
                {
                    dx /= len;
                    dy /= len;
                    float arrowSize = ArrowHeadSize;
                    float ax1 = nx - dx * arrowSize - dy * arrowSize * 0.5f;
                    float ay1 = ny - dy * arrowSize + dx * arrowSize * 0.5f;
                    float ax2 = nx - dx * arrowSize + dy * arrowSize * 0.5f;
                    float ay2 = ny - dy * arrowSize - dx * arrowSize * 0.5f;

                    using var arrowHeadPaint = new SKPaint { Color = ArrowColor, IsAntialias = true, Style = SKPaintStyle.Fill };
                    using var arrowPath = new SKPath();
                    arrowPath.MoveTo(nx, ny);
                    arrowPath.LineTo(ax1, ay1);
                    arrowPath.LineTo(ax2, ay2);
                    arrowPath.Close();
                    canvas.DrawPath(arrowPath, arrowHeadPaint);
                }
            }
        }

        /// <summary>
        /// Draws the zoomed-in view of the librarian node on the specified Graphics object.
        /// Centers on the current librarian position and shows only nodes within ZoomRadius.
        /// </summary>
        public void DrawZoomPanel(Graphics g, int panelWidth, int panelHeight)
        {
            var simState = Engine?.CurrentState;
            if (simState == null) return;

            // Calculate librarian position
            int headPos = simState.HeadPosition;
            int disk = Math.Max(1, simState.DiskSize);
            double headAngle = (headPos / (double)disk) * (Math.PI * 2.0);
            double finalAngle = headAngle + librarianAngleOffset;
            float outerRadius = currentLayerSpacing * currentLayers;
            float libOutwardOffset = currentLastNodeRadius * LibrarianOffsetNodeFactor;
            float libRadiusFromCenter = outerRadius - libOutwardOffset;
            float hx = currentCx + libRadiusFromCenter * MathF.Cos((float)finalAngle);
            float hy = currentCy + libRadiusFromCenter * MathF.Sin((float)finalAngle);

            // Zoom center
            float zoomCx = panelWidth / 2f;
            float zoomCy = panelHeight / 2f;

            // Clear background
            g.Clear(Color.Black);

            // Fixed base sizes for zoom panel
            float fixedNodeRadius = 5f; // base node radius for zoom
            float fixedCoreRadius = 10f; // base core radius for zoom

            // Draw center curiosity core
            float zCoreRadius = fixedCoreRadius * ZoomFactor;
            using (Brush coreBrush = new SolidBrush(Color.Yellow))
            {
                g.FillEllipse(coreBrush, zoomCx - zCoreRadius, zoomCy - zCoreRadius, 2 * zCoreRadius, 2 * zCoreRadius);
            }

            // Prepare request set
            var pendingSet = new HashSet<int>(simState.PendingRequests);
            int diskSize = simState.DiskSize;

            // Draw guide rings
            for (int layerIndex = 0; layerIndex < currentLayers; layerIndex++)
            {
                float t = layerIndex / (float)(currentLayers - 1);
                float scale = 0.1f + 0.9f * MathF.Pow(t, PerspectivePower);
                float ringRadius = currentMaxRadius * scale;
                float zRingRadius = ringRadius * ZoomFactor;
                byte ringAlpha = (byte)(30 * (layerIndex + 1) / currentLayers); // similar alpha
                using (Pen ringPen = new Pen(Color.FromArgb(ringAlpha, Color.White)))
                {
                    g.DrawEllipse(ringPen, zoomCx - zRingRadius, zoomCy - zRingRadius, 2 * zRingRadius, 2 * zRingRadius);
                }
            }

            // Draw nodes within zoom radius
            for (int layerIndex = 0; layerIndex < currentLayers; layerIndex++)
            {
                float t = layerIndex / (float)(currentLayers - 1);
                float scale = 0.1f + 0.9f * MathF.Pow(t, PerspectivePower);
                float ringRadius = currentMaxRadius * scale;
                int nodesThisLayer = NodeCount;
                float rotationMultiplier = (currentLayers - layerIndex - 1) * 0.1f * (layerIndex % 2 == 0 ? 1f : -1f);
                float layerRotation = (float)(currentRotationAngle * rotationMultiplier);

                float angleStep = (2f * MathF.PI) / nodesThisLayer;
                for (int i = 0; i < nodesThisLayer; i++)
                {
                    float angle = (i * angleStep) + layerRotation;
                    float x = currentCx + ringRadius * MathF.Cos(angle);
                    float y = currentCy + ringRadius * MathF.Sin(angle);

                    // Distance from librarian
                    float dx = x - hx;
                    float dy = y - hy;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > ZoomRadius) continue;

                    // Scaled position
                    float zx = zoomCx + dx * ZoomFactor * SpacingFactor;
                    float zy = zoomCy + dy * ZoomFactor * SpacingFactor;

                    // Fixed scaled size
                    float zRadius = fixedNodeRadius * ZoomFactor;

                    // Cylinder mapping
                    int cylinder = diskSize > 1 ? (int)Math.Round((double)i / nodesThisLayer * (diskSize - 1)) : 0;
                    Color fillColor = pendingSet.Contains(cylinder) ? Color.Yellow : Color.DarkGoldenrod;

                    // Draw node as hexagon
                    PointF[] points = CreateRegularPolygonPoints(6, zRadius, zx, zy, angle + HexOrientationOffset);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddPolygon(points);
                        using (Brush brush = new SolidBrush(fillColor))
                        {
                            g.FillPath(brush, path);
                        }
                        using (Pen pen = new Pen(Color.LightGoldenrodYellow))
                        {
                            pen.Width = 2f * (layerIndex + 1f) / currentLayers;
                            g.DrawPath(pen, path);
                        }
                    }
                }

                // Draw inner librarian for this layer if within zoom
                if (layerIndex < currentLayers - 1 && simState != null)
                {
                    float libRadiusFromCenterInner = ringRadius - (currentBaseNodeRadius * scale) * LibrarianOffsetNodeFactor;
                    float libAngleInner = (float)(finalAngle + layerRotation);
                    float libX = currentCx + libRadiusFromCenterInner * MathF.Cos(libAngleInner);
                    float libY = currentCy + libRadiusFromCenterInner * MathF.Sin(libAngleInner);

                    float dxInner = libX - hx;
                    float dyInner = libY - hy;
                    float distInner = MathF.Sqrt(dxInner * dxInner + dyInner * dyInner);
                    if (distInner <= ZoomRadius)
                    {
                        float zxInner = zoomCx + dxInner * ZoomFactor * SpacingFactor;
                        float zyInner = zoomCy + dyInner * ZoomFactor * SpacingFactor;
                        float zLibRadiusInner = fixedNodeRadius * ZoomFactor * LibrarianSizeMultiplier; // same size
                        using (Brush innerLibBrush = new SolidBrush(Color.Blue))
                        {
                            g.FillEllipse(innerLibBrush, zxInner - zLibRadiusInner, zyInner - zLibRadiusInner, 2 * zLibRadiusInner, 2 * zLibRadiusInner);
                        }
                    }
                }
            }

            // Draw librarian (outer circle)
            float zLibRadius = fixedNodeRadius * ZoomFactor * LibrarianSizeMultiplier;
            using (Brush libBrush = new SolidBrush(Color.Blue))
            {
                g.FillEllipse(libBrush, zoomCx - zLibRadius, zoomCy - zLibRadius, 2 * zLibRadius, 2 * zLibRadius);
            }

            // Draw head (inner circle)
            float zHeadRadius = Math.Max(3f, fixedNodeRadius * 0.5f) * ZoomFactor * LibrarianSizeMultiplier;
            using (Brush headBrush = new SolidBrush(Color.Cyan))
            {
                g.FillEllipse(headBrush, zoomCx - zHeadRadius, zoomCy - zHeadRadius, 2 * zHeadRadius, 2 * zHeadRadius);
            }
        }

        // Zoom panel
        private DrawZoomPanel? zoomDrawer;
        public DrawZoomPanel ZoomPanel => zoomDrawer ??= new DrawZoomPanel();
        public Control? PanelZoom { get; set; }

        // Zoom panel properties
        public float ZoomFactor { get; set; } = 2.0f;
        public float ZoomRadius { get; set; } = 100f; // pixels in world space
        public float SpacingFactor { get; set; } = 1.0f; // optional spacing to prevent overlap in dense areas

        // Cached values for zoom panel
        private float currentCx, currentCy, currentMaxRadius, currentBaseNodeRadius, currentLayerSpacing, currentLastNodeRadius;
        private int currentLayers;
        private double currentRotationAngle;

        // Helper: create a regular polygon SKPath (e.g., hexagon) with rotation offset
        private SKPath CreateRegularPolygonPath(int sides, float radius, float centerX, float centerY, float rotationRadians = 0f)
        {
            var path = new SKPath();
            if (sides < 3) return path;

            float angleStep = 2 * MathF.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                float a = rotationRadians + i * angleStep;
                float x = centerX + radius * MathF.Cos(a);
                float y = centerY + radius * MathF.Sin(a);
                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }
            path.Close();
            return path;
        }

        /// <summary>
        /// Creates an array of points for a regular polygon (e.g., hexagon) with rotation.
        /// </summary>
        private PointF[] CreateRegularPolygonPoints(int sides, float radius, float centerX, float centerY, float rotationRadians = 0f)
        {
            PointF[] points = new PointF[sides];
            float angleStep = 2 * MathF.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                float a = rotationRadians + i * angleStep;
                float x = centerX + radius * MathF.Cos(a);
                float y = centerY + radius * MathF.Sin(a);
                points[i] = new PointF(x, y);
            }
            return points;
        }

        /// <summary>
        /// Calculates the maximum number of layers that can fit within the given constraints.
        /// Uses an iterative approach starting from the outermost layer and adding inward.
        /// </summary>
        private int CalculateMaxLayers(float maxRadius, float maxNodeSize, float minGap, float perspectivePower)
        {
            if (maxRadius <= 0 || maxNodeSize <= 0 || minGap < 0) return 1;

            int n = 1;
            while (true)
            {
                bool canFit = true;
                for (int k = 0; k < n - 1; k++)
                {
                    float t_k = (float)k / (n - 1);
                    float t_k1 = (float)(k + 1) / (n - 1);
                    float scale_k = 0.1f + 0.9f * MathF.Pow(t_k, perspectivePower);
                    float scale_k1 = 0.1f + 0.9f * MathF.Pow(t_k1, perspectivePower);
                    float ringRadius_k = maxRadius * scale_k;
                    float ringRadius_k1 = maxRadius * scale_k1;
                    float spacing = ringRadius_k1 - ringRadius_k;
                    float gap = minGap * (1 + 2 * t_k1); // larger gap at outer layers to prevent clipping, smaller near center
                    float required = maxNodeSize * scale_k1 + gap;
                    if (spacing < required)
                    {
                        canFit = false;
                        break;
                    }
                }
                if (!canFit) break;
                n++;
            }
            return n - 1;
        }

        public void Dispose()
        {
            if (!disposed)
            {
                animationTimer?.Stop();
                animationTimer?.Dispose();
                skglControl?.Dispose();
                stopwatch?.Stop();
                stopwatch?.Reset();
                disposed = true;
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (previousMousePosition.HasValue)
            {
                float dx = e.X - previousMousePosition.Value.X;
                // accumulate horizontal movement, scale to angle
                librarianAngleOffset += dx * 0.01f; // adjust factor as needed
            }
            previousMousePosition = new Point(e.X, e.Y);
        }
    }
}
