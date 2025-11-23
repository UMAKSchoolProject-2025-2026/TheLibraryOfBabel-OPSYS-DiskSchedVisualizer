using System;
using System.Linq;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using LibraryOFBabel.Simulation;
using System.Collections.Generic;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Lightweight renderer that exposes a SKGL control and simple scene rendering using RadialConfig + RadialLayoutEngine.
    /// Provides a small compatible surface for Form1 to host without the previous large renderer dependency.
    /// </summary>
    public sealed class RadialRenderer : IDisposable
    {
        private readonly SKGLControl skglControl;
        private readonly System.Windows.Forms.Timer animationTimer;
        private bool disposed = false;

        public RadialConfig Config { get; } = new RadialConfig();

        private SimulationEngine? engine;
        public SimulationEngine? Engine
        {
            get => engine;
            set
            {
                if (engine == value) return;
                if (engine != null)
                {
                    engine.StateChanged -= Engine_StateChanged;
                }
                engine = value;
                if (engine != null)
                {
                    engine.StateChanged += Engine_StateChanged;
                    // initialize pointer angles from current engine state
                    InitializePointerFromEngine(engine.CurrentState);
                }
            }
        }

        public bool EnableDiagnostics { get; set; } = false;
        public bool IsDimmed { get; set; } = false;

        public float PerspectivePower { get; set; } = 1.2f; // for compatibility
        public float HexOrientationOffset { get; set; } = MathF.PI / 6f;

        public NodeRenderMode NodeMode { get; set; } = NodeRenderMode.Hexagon;

        public Control? PanelZoom { get; set; }

        private readonly DebugOverlayRenderer debugRenderer = new DebugOverlayRenderer();

        // Pointer pathfinder to handle seam-aware path computations
        private readonly PointerPathfinder pointerFinder;

        // Cached wall path for performance
        private SKPath? cachedWallPath = null;
        private int cachedWallNodeCount = -1;
        private float cachedWallNodeSize = -1f;
        private float cachedWallNodeRingRadius = -1f;
        private float cachedWallSizeScale = -1f;
        private float cachedWallRadiusScale = -1f;

        // LOD threshold: when node count exceeds this, render walls as a smooth circle instead of a large polyline
        private const int WallPathLodThreshold = 2000;

        // Pointer animation state (degrees)
        private float pointerAnglePrevDeg = 0f;
        private float pointerAngleTargetDeg = 0f;
        private float pointerAngleCurrentDeg = 0f;
        private float pointerAnimElapsed = 0f;
        private float pointerAnimDuration = 0.25f; // seconds (base duration)
        // effective duration for current transition (computed per transition)
        private float pointerEffectiveDuration = 0.25f;

        // Exposed control for enabling/disabling pointer animation and adjusting duration
        public bool PointerAnimationEnabled { get; set; } = true;

        // multiplier applied to base duration to slow/speed animations; >1 => slower
        public float PointerAnimationSpeedFactor { get; set; } = 2.0f; // default slower for better visibility

        public float PointerAnimationDuration
        {
            get => pointerAnimDuration;
            set => pointerAnimDuration = Math.Max(0f, value);
        }

        private float EffectivePointerAnimDurationBase => MathF.Max(0f, pointerAnimDuration * MathF.Max(0.0001f, PointerAnimationSpeedFactor));

        private int lastHeadPosition = 0; // Track previous head for direction calculation

        // queued per-node animation (fallback)
        private readonly Queue<int> pointerPathQueue = new Queue<int>();
        private int currentAnimatingToIndex = -1;
        private float perStepDuration = 0.06f; // default per-node duration (seconds)

        // fluid animation (single continuous unwrapped angle)
        private bool useDirectAngleInterpolation = false;

        public RadialRenderer()
        {
            skglControl = new SKGLControl { Dock = DockStyle.Fill };
            skglControl.PaintSurface += OnPaintSurface;
            skglControl.Resize += (s, e) => skglControl.Invalidate();

            // initialize pointer pathfinder using the shared config
            pointerFinder = new PointerPathfinder(Config);

            animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
            // advance animation and invalidate on each tick
            animationTimer.Tick += (s, e) =>
            {
                UpdateAnimation(animationTimer.Interval / 1000f);
                skglControl.Invalidate();
            };
        }

        private void InitializePointerFromEngine(SimulationState st)
        {
            if (st == null) return;
            int disk = Math.Max(1, st.DiskSize);
            float deg = st.HeadPosition / (float)disk * 360f;
            pointerAnglePrevDeg = deg;
            pointerAngleTargetDeg = deg;
            pointerAngleCurrentDeg = deg;
            // set elapsed to effective duration so UpdateAnimation treats it as completed
            pointerAnimElapsed = EffectivePointerAnimDurationBase;
            pointerEffectiveDuration = EffectivePointerAnimDurationBase;
            lastHeadPosition = st.HeadPosition; // Initialize last position
        }

        private void Engine_StateChanged(object? sender, SimulationState st)
        {
            if (st == null) return;
            int disk = Math.Max(1, st.DiskSize);
            float newDeg = st.HeadPosition / (float)disk * 360f;

            // If an external animation is in progress we do not want to snap the pointer to the engine head.
            // Only update/snap when there's no active animation.
            if (pointerAnimElapsed < pointerEffectiveDuration - 1e-6f)
            {
                // leave current animation alone
                return;
            }

            // Determine direction: +1 for increasing index (clockwise), -1 for decreasing (counterclockwise)
            int direction = (st.HeadPosition > lastHeadPosition) ? 1 : -1;

            // Animate along the path in the correct direction (long arc if needed) but never cross seam
            AnimatePointerAlong(lastHeadPosition, st.HeadPosition, 0.25f, direction);

            // Update last position
            lastHeadPosition = st.HeadPosition;
        }

        /// <summary>
        /// Animate the pointer by a signed number of steps (positive = forward/increasing indices, negative = backward)
        /// over the provided total duration in seconds. This allows the host to request a multi-track animation
        /// that follows a specified direction (useful when engine teleports to a distant target).
        /// </summary>
        public void AnimatePointerBySteps(int fromIndex, int signedStepCount, float durationSeconds)
        {
            int disk = 0;
            if (engine != null) disk = Math.Max(1, engine.CurrentState.DiskSize);
            else disk = Math.Max(1, Config.NodeCount);

            int toIndex = fromIndex + signedStepCount;
            toIndex = Math.Max(0, Math.Min(disk - 1, toIndex));

            // Try fluid path first; falls back to queued steps if fluid not available
            if (!TryBuildFluidPath(fromIndex, toIndex, Math.Sign(signedStepCount), durationSeconds, out float sAngle, out float eAngle))
            {
                EnqueueSeamSafePath(fromIndex, toIndex, Math.Sign(signedStepCount), durationSeconds);
                return;
            }

            StartFluidAnimation(sAngle, eAngle, durationSeconds);
        }

        private void UpdateAnimation(float deltaSeconds)
        {
            if (!PointerAnimationEnabled) return;

            float effectiveDur = MathF.Max(1e-6f, pointerEffectiveDuration);
            if (pointerAnimElapsed >= effectiveDur) return; // nothing to do

            pointerAnimElapsed += deltaSeconds;
            float t = pointerAnimElapsed / effectiveDur;
            if (t >= 1f)
            {
                // finish current step/fluid animation
                pointerAngleCurrentDeg = pointerAngleTargetDeg;
                pointerAnglePrevDeg = pointerAngleCurrentDeg;
                pointerAnimElapsed = effectiveDur;

                // if we were running a fluid animation, clear the flag
                if (useDirectAngleInterpolation)
                {
                    useDirectAngleInterpolation = false;
                }

                // if there are queued per-node steps, start the next one
                if (pointerPathQueue.Count > 0)
                {
                    StartNextAnimationStep();
                    return;
                }

                // nothing left to animate
                return;
            }

            // cubic ease-in-out for smoother motion
            float ease;
            if (t < 0.5f) ease = 4f * t * t * t;
            else
            {
                float nt = (t - 1f);
                ease = 1f + 4f * nt * nt * nt;
            }

            // interpolation: either direct (no wrap) or shortest-angle
            float delta = useDirectAngleInterpolation
                ? (pointerAngleTargetDeg - pointerAnglePrevDeg) // direct unwrapped delta
                : ShortestAngleDiff(pointerAnglePrevDeg, pointerAngleTargetDeg); // shortest (may wrap)

            pointerAngleCurrentDeg = pointerAnglePrevDeg + delta * ease;
            // normalize to 0..360 only for display; underlying prev/target may be unwrapped
            pointerAngleCurrentDeg = (pointerAngleCurrentDeg % 360f + 360f) % 360f;
        }

        private static float ShortestAngleDiff(float fromDeg, float toDeg)
        {
            float diff = (toDeg - fromDeg + 540f) % 360f - 180f;
            return diff;
        }

        public Control GetVisualizationControl() => skglControl;

        public void Start()
        {
            animationTimer.Start();
        }

        public void Stop()
        {
            animationTimer.Stop();
        }

        private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(SKColors.Black);

            // ensure config derived values are fresh
            Config.Recalculate();

            // rebuild pathfinder if layout changed
            pointerFinder.Rebuild();

            // determine effective viewport scale (auto-fit or user-specified)
            float finalScale = Config.ViewportScale;

            // compute separate scales: sizeScale for visual sizes, radiusScale for positions/radii
            float sizeScale = Config.GetDensityScale();
            float radiusScale = Config.GetRadiusScale();

            if (Config.AutoFitToViewport)
            {
                // available device pixels after padding
                float padding = Config.FitPaddingPixels;
                float availW = MathF.Max(1f, info.Width - padding * 2f);
                float availH = MathF.Max(1f, info.Height - padding * 2f);

                // world diameter we need to fit (TotalRadius is a world-space radius)
                // Use radius-scaled diameter so AutoFit fits the layout after radius compression
                float worldDiameter = MathF.Max(1f, (float)(Config.TotalRadius * 2f * radiusScale));

                // fit scale converts world units -> device pixels
                float fitScale = MathF.Min(availW / worldDiameter, availH / worldDiameter);

                // apply a slight margin to avoid touching edges
                finalScale = MathF.Max(0.0001f, fitScale * 0.95f);
            }

            // center origin and apply scale. Important: scale first, then translate using world units
            canvas.Save();
            canvas.Scale(finalScale);
            canvas.Translate(info.Width / 2f / finalScale, info.Height / 2f / finalScale);

            // compute pointer angle (degrees) from animated state if available, otherwise derive from engine
            // Use animated angle by default; if animations are disabled fall back to engine state instantly.
            float pointerDeg = pointerAngleCurrentDeg;
            if (!PointerAnimationEnabled)
            {
                if (Engine?.CurrentState != null)
                {
                    var st = Engine.CurrentState;
                    int disk = Math.Max(1, st.DiskSize);
                    pointerDeg = st.HeadPosition / (float)disk * 360f;
                }
            }

            // DRAW ORDER (bottom -> top):
            // 1) walls (bottom)
            // 2) nodes
            // 3) walkway
            // 4) pending request markers (above nodes)
            // 5) debug overlays (optional)
            // 6) pointer (top)

            // generate node positions once (world-space) and create a radius-scaled copy for geometry
            var layout = new RadialLayoutEngine(Config);
            var nodes = layout.GenerateNodePositions();
            var scaledNodes = nodes.Select(n => new NodePos(n.X * radiusScale, n.Y * radiusScale, n.AngleDegrees, n.AngleRadians)).ToList();

            // 1) walls (pass effective scale and both scales and scaled node list)
            DrawWalls(canvas, Config, finalScale, sizeScale, radiusScale, scaledNodes);

            // 2) nodes (use indexed loop so we can label each node). scaledNodes used so positions are already radius-scaled.
            for (int i = 0; i < scaledNodes.Count; i++)
            {
                var n = scaledNodes[i];
                int label = i; // 0-based labels (previously 1-based)
                if (NodeMode == NodeRenderMode.Circle) DrawCircleNode(canvas, n, Config, label, sizeScale);
                else DrawHexagonNode(canvas, n, Config, label, sizeScale);
            }

            // 3) walkway (drawn above nodes) - use radiusScale for radii and sizeScale for stroke thickness
            DrawWalkway(canvas, Config, sizeScale, radiusScale);

            // 4) draw pending request markers (group duplicates, highlight next target)
            if (Engine?.CurrentState != null)
            {
                var st = Engine.CurrentState;
                var pending = st.PendingRequests;
                if (pending != null && pending.Count > 0)
                {
                    // group duplicates so we can show counts
                    var groups = pending.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

                    // paints
                    using var pendingPaint = SKPaintFactory.CreateFill(SKColors.Red.WithAlpha(200));
                    using var highlightPaint = SKPaintFactory.CreateFill(SKColors.Cyan.WithAlpha(220));
                    using var labelPaint = SKPaintFactory.CreateFill(SKColors.Black);
                    labelPaint.IsAntialias = true;

                    float markerRadius = MathF.Max(1f, Config.NodeSize * 0.35f * sizeScale);

                    // highlight the next request chosen by algorithm if available
                    int? next = Engine?.NextRequest;

                    foreach (var kv in groups)
                    {
                        int track = kv.Key;
                        int count = kv.Value;

                        // map track -> node index (guard if track out of range)
                        int idx = Math.Clamp(track, 0, scaledNodes.Count - 1);
                        var np = scaledNodes[idx];
                        float px = np.X;
                        float py = np.Y;

                        // if this is the next request, draw highlighted marker
                        if (next.HasValue && next.Value == track)
                        {
                            canvas.DrawCircle(px, py, markerRadius * 1.45f, highlightPaint);
                        }

                        // draw pending marker
                        canvas.DrawCircle(px, py, markerRadius, pendingPaint);

                        // draw count label if >1
                        if (count > 1)
                        {
                            // create font from paint
                            using var lblFont = new SKFont(labelPaint.Typeface, labelPaint.TextSize);
                            labelPaint.TextSize = MathF.Max(6f, markerRadius * 1.1f);
                            var text = count.ToString();

                            // use font metrics to compute baseline
                            var fm = lblFont.Metrics;
                            float baseline = -(fm.Ascent + fm.Descent) * 0.5f;
                            canvas.DrawText(text, px + markerRadius + 2f, py + baseline, lblFont, labelPaint);
                        }
                    }
                }
            }

            // 5) debug overlays (draw above walkway but below pointer) (pass radius-scaled nodes)
            if (debugRenderer.DebugEnabled)
                debugRenderer.DrawOverlays(canvas, scaledNodes, Config, finalScale, sizeScale);

            // draw subtle arc trail between previous and current pointer while animating
            // sample along shortest angular path to avoid issues with arc direction APIs
            try
            {
                float effectiveDur = MathF.Max(1e-6f, pointerEffectiveDuration);
                if (PointerAnimationEnabled && pointerAnimElapsed < effectiveDur)
                {
                    // anchor radius in canvas units
                    float anchorWorld = Config.PointerAnchorRadius;
                    float anchor = anchorWorld * radiusScale;
                    float delta = useDirectAngleInterpolation
                        ? (pointerAngleTargetDeg - pointerAnglePrevDeg)
                        : ShortestAngleDiff(pointerAnglePrevDeg, pointerAngleTargetDeg);
                    int segments = 28; // smooth enough
                    using var trailPaint = SKPaintFactory.CreateStroke(SKColors.Cyan.WithAlpha(80), MathF.Max(0.6f, 0.6f * sizeScale));
                    trailPaint.IsAntialias = true;
                    using var path = new SKPath();
                    for (int i = 0; i <= segments; i++)
                    {
                        float tt = i / (float)segments;
                        float ang = pointerAnglePrevDeg + delta * tt;
                        float rad = ang * (MathF.PI / 180f);
                        float x = anchor * MathF.Cos(rad);
                        float y = anchor * MathF.Sin(rad);
                        if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                    }
                    canvas.DrawPath(path, trailPaint);
                }
            }
            catch { }

            // 6) pointer on very top
            DrawPointer(canvas, pointerDeg, Config, sizeScale, radiusScale);

            canvas.Restore();

            // dim overlay if requested
            if (IsDimmed)
            {
                using var dim = SKPaintFactory.CreateFillWithAlpha(new SKColor(0, 0, 0), 110);
                canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), dim);
            }
        }

        /// <summary>
        /// Draws a donut-shaped walkway centered at the current canvas origin using the provided configuration.
        /// </summary>
        public void DrawWalkway(SKCanvas canvas, RadialConfig cfg, float sizeScale, float radiusScale)
        {
            if (canvas == null || cfg == null) return;
            float inner = cfg.WalkwayInnerRadius * radiusScale;
            float outer = cfg.WalkwayOuterRadius * radiusScale;
            if (outer <= inner) return;
            // thickness derived from NodeSize (world units), then scaled by visual sizeScale for render
            float strokeWidth = MathF.Max(0.0f, cfg.EffectiveWalkwayThickness * sizeScale);
            float midRadius = inner + (outer - inner) * 0.5f;
            var walkwayColor = cfg.WalkwayAndNodeFillColor;
            using var p = SKPaintFactory.CreateStroke(walkwayColor, strokeWidth);
            p.IsAntialias = true;
            canvas.DrawCircle(0f, 0f, midRadius, p);
        }

        /// <summary>
        /// Draws outer walls as a stroked ring immediately outside the walkway.
        /// Supports glow and caches path; uses LOD for very large node counts.
        /// Expects node positions in 'nodes' to already be multiplied by radiusScale.
        /// </summary>
        public void DrawWalls(SKCanvas canvas, RadialConfig cfg, float effectiveScale, float sizeScale, float radiusScale, List<NodePos> nodes)
        {
            if (canvas == null || cfg == null) return;

            // use effective wall thickness derived from NodeSize
            if (cfg.EffectiveWallThickness <= 0f) return;

            int nodeCount = Math.Max(1, cfg.NodeCount);

            // LOD: if too many nodes, draw as a smooth circle instead of building huge path
            bool useCircle = nodeCount > WallPathLodThreshold;

            // build or reuse cached path when not using circle
            if (!useCircle)
            {
                if (cachedWallPath == null || cachedWallNodeCount != cfg.NodeCount || Math.Abs(cachedWallNodeSize - cfg.NodeSize) > 0.001f || Math.Abs(cachedWallSizeScale - sizeScale) > 0.001f || Math.Abs(cachedWallRadiusScale - radiusScale) > 0.001f)
                {
                    cachedWallPath?.Dispose();
                    var path = new SKPath();
                    if (nodes != null && nodes.Count > 0)
                    {
                        // nodes are already radius-scaled; use coords directly
                        path.MoveTo(nodes[0].X, nodes[0].Y);
                        for (int i = 1; i < nodes.Count; i++)
                            path.LineTo(nodes[i].X, nodes[i].Y);
                        path.Close();
                    }
                    cachedWallPath = path;
                    cachedWallNodeCount = cfg.NodeCount;
                    cachedWallNodeSize = cfg.NodeSize;
                    cachedWallNodeRingRadius = cfg.NodeRingRadius * radiusScale;
                    cachedWallSizeScale = sizeScale;
                    cachedWallRadiusScale = radiusScale;
                }
            }

            // draw glow if enabled
            if (cfg.WallGlowEnabled && cfg.WallGlowWidth > 0f)
            {
                float glowWidthWorld = cfg.WallGlowWidth * sizeScale; // visual glow tied to sizeScale
                float glowSigma = MathF.Max(0.5f, glowWidthWorld * effectiveScale); // device pixels
                float glowStroke = MathF.Max(1f, cfg.EffectiveWallThickness * sizeScale + glowWidthWorld * 2f);

                using var glowPaint = new SKPaint
                {
                    Color = cfg.WallGlowColor,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = glowStroke,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeCap = SKStrokeCap.Round,
                    ImageFilter = SKImageFilter.CreateBlur(glowSigma, glowSigma)
                };

                if (useCircle)
                {
                    // approximate circle at node ring radius (scaled by radiusScale)
                    float r = cfg.NodeRingRadius * radiusScale;
                    canvas.DrawCircle(0f, 0f, r, glowPaint);
                }
                else if (cachedWallPath != null)
                {
                    canvas.DrawPath(cachedWallPath, glowPaint);
                }
            }

            // draw main wall stroke
            var wallColor = cfg.WallAndBorderColor;
            float wallStroke = MathF.Max(0.5f, cfg.EffectiveWallThickness * sizeScale);
            using var paint = SKPaintFactory.CreateStroke(wallColor, wallStroke);
            paint.StrokeJoin = SKStrokeJoin.Round;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.IsAntialias = true;

            if (useCircle)
            {
                float r = cfg.NodeRingRadius * radiusScale;
                canvas.DrawCircle(0f, 0f, r, paint);
            }
            else if (cachedWallPath != null)
            {
                canvas.DrawPath(cachedWallPath, paint);
            }
        }

        public void DrawCircleNode(SKCanvas canvas, NodePos pos, RadialConfig cfg, int label, float sizeScale)
        {
            if (canvas == null || cfg == null) return;
            canvas.Save();
            // pos is already radius-scaled
            canvas.Translate(pos.X, pos.Y);
            float nodeSize = MathF.Max(1f, cfg.NodeSize * sizeScale);
            var nodeColor = cfg.WalkwayAndNodeFillColor;
            using var p = SKPaintFactory.CreateFill(nodeColor);
            canvas.DrawCircle(0f, 0f, nodeSize, p);

            // draw centered label anchored to this node and computed from node center
            if (cfg.ShowNodeLabels)
                DrawCenteredNodeLabel(canvas, label.ToString(), cfg, pos.X, pos.Y, sizeScale);

            canvas.Restore();
        }

        public void DrawHexagonNode(SKCanvas canvas, NodePos pos, RadialConfig cfg, int label, float sizeScale)
        {
            if (canvas == null || cfg == null) return;
            canvas.Save();
            // pos is already radius-scaled
            canvas.Translate(pos.X, pos.Y);

            // draw rotated hex (rotation used only for hex geometry)
            canvas.Save();
            canvas.RotateDegrees(pos.AngleDegrees + (HexOrientationOffset * (180f / MathF.PI)));

            float nodeSize = MathF.Max(1f, cfg.NodeSize * sizeScale);

            // fill color from config (light gold, opaque)
            var nodeColor = cfg.WalkwayAndNodeFillColor;
            using var fillPaint = SKPaintFactory.CreateFill(nodeColor);

            // border color: use same dark gold as wall from config
            var borderColor = cfg.WallAndBorderColor;
            float borderWidth = MathF.Max(cfg.NodeBorderMinWidth, cfg.EffectiveWallThickness * cfg.NodeBorderWidthFactor) * sizeScale;
            using var borderPaint = SKPaintFactory.CreateStroke(borderColor, borderWidth);
            borderPaint.StrokeJoin = SKStrokeJoin.Round;
            borderPaint.StrokeCap = SKStrokeCap.Round;
            borderPaint.IsAntialias = true;

            var path = new SKPath();
            const int sides = 6;
            float step = 2f * MathF.PI / sides;
            for (int i = 0; i < sides; i++)
            {
                float a = i * step;
                float x = nodeSize * MathF.Cos(a);
                float y = nodeSize * MathF.Sin(a);
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            path.Close();

            // draw fill then border (both opaque)
            canvas.DrawPath(path, fillPaint);
            canvas.DrawPath(path, borderPaint);

            canvas.Restore(); // back to translate-only (no hex rotation)

            // draw centered label anchored to this node and computed from node center
            if (cfg.ShowNodeLabels)
                DrawCenteredNodeLabel(canvas, label.ToString(), cfg, pos.X, pos.Y, sizeScale);

            path.Dispose();
            canvas.Restore();
        }

        // Draw centered numeric label anchored at the node's center.
        // nodeX/nodeY are the node's world coordinates (already scaled by radiusScale).
        private void DrawCenteredNodeLabel(SKCanvas canvas, string text, RadialConfig cfg, float nodeX, float nodeY, float sizeScale)
        {
            if (canvas == null || cfg == null || string.IsNullOrEmpty(text)) return;
            if (!cfg.ShowNodeLabels) return;

            // Interpret padding factor (support fraction or percent), clamp to sensible range.
            float raw = cfg.NodeLabelPaddingFactor;
            float pad;
            if (raw <= 1f) pad = MathF.Max(0f, MathF.Min(0.45f, raw));
            else if (raw <= 100f) pad = MathF.Max(0f, MathF.Min(0.45f, raw / 100f));
            else pad = 0.45f;

            // For a regular hexagon: apothem = radius * cos(pi/6) (~0.8660254)
            float apothem = cfg.NodeSize * 0.866025403784f * sizeScale;

            // Available width/height inside the hex (conservative): use apothem*2 and apply padding.
            float available = apothem * 2f * (1f - pad);
            if (available <= 0f) available = cfg.NodeSize * 1.0f * sizeScale;

            // create paint for text using wall/border color (dark gold)
            using var textPaint = SKPaintFactory.CreateFill(cfg.WallAndBorderColor);
            textPaint.IsAntialias = true;
            textPaint.TextAlign = SKTextAlign.Center;
            textPaint.Typeface = SKTypeface.Default;

            // Start with available as a baseline TextSize (world units) then apply global scale and density
            textPaint.TextSize = MathF.Max(1f, available * cfg.NodeLabelScale);

            // create SKFont from paint to measure and use DrawText overloads
            using var font = new SKFont(textPaint.Typeface, textPaint.TextSize);

            // Measure text and scale to fit available width
            float measured = font.MeasureText(text);
            if (measured <= 0f) measured = 1f;
            float scale = available / measured;

            // apply a small safety margin and global scale
            float finalSize = textPaint.TextSize * scale * 0.9f;

            // shrink single digits slightly for better visual balance using configurable factor
            if (text.Length == 1) finalSize *= cfg.SingleDigitScaleFactor;

            textPaint.TextSize = MathF.Max(1f, finalSize);

            // compute vertical baseline to center text using font metrics
            using var finalFont = new SKFont(textPaint.Typeface, textPaint.TextSize);
            var fm = finalFont.Metrics;
            float baseline = -(fm.Ascent + fm.Descent) * 0.5f;

            // compute inward direction (from node toward origin)
            // nodeX/nodeY are already scaled; no extra sizeScale multiply required
            float dx = -nodeX;
            float dy = -nodeY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            float ux = 0f, uy = -1f; // default if at center
            if (len > 1e-6f) { ux = dx / len; uy = dy / len; }

            // compute inset so the label sits visually inside the hex (use apothem and configurable inset factor)
            float inset = apothem * cfg.NodeLabelInsetFactor * (1f - pad);

            canvas.Save();
            // canvas is already translated to node center by caller ? move inward along radial vector
            canvas.Translate(ux * inset, uy * inset);

            // keep text upright to viewport (no per-node rotation)
            canvas.DrawText(text, 0f, baseline, finalFont, textPaint);
            canvas.Restore();
        }

        public void DrawPointer(SKCanvas canvas, float pointerAngle, RadialConfig cfg, float sizeScale, float radiusScale)
        {
            if (canvas == null || cfg == null) return;
            canvas.Save();
            canvas.RotateDegrees(pointerAngle);

            // anchor computed in world units using walkway geometry (cfg.PointerAnchorRadius uses WalkwayThickness)
            float anchorWorld = cfg.PointerAnchorRadius;

            // pointer radius in world units (PointerSize is treated as diameter)
            float pointerRadiusWorld = cfg.PointerSize * 0.5f;

            // visible walkway stroke half (world units) used for clamping so pointer stays inside the visible band
            float strokeHalfWorld = cfg.EffectiveWalkwayThickness * 0.5f;

            // clamp anchor in world units to keep pointer fully inside the visible walkway stroke
            float minAnchorWorld = cfg.WalkwayInnerRadius + strokeHalfWorld + pointerRadiusWorld;
            float maxAnchorWorld = (cfg.WalkwayInnerRadius + cfg.WalkwayThickness) - strokeHalfWorld - pointerRadiusWorld;

            // if walkway is too thin, fall back to walkway center
            if (maxAnchorWorld < minAnchorWorld)
            {
                float center = cfg.WalkwayInnerRadius + (cfg.WalkwayThickness * 0.5f);
                minAnchorWorld = center;
                maxAnchorWorld = center;
            }

            float clampedAnchorWorld = Math.Max(minAnchorWorld, Math.Min(maxAnchorWorld, anchorWorld));

            // convert to canvas units (apply radiusScale to positions, sizeScale to visual radius)
            float anchor = clampedAnchorWorld * radiusScale;
            float pointerRadius = pointerRadiusWorld * sizeScale;

            // Diagnostics: draw intended anchor, clamped anchor, and pointer outline when enabled
            if (EnableDiagnostics)
            {
                // intended anchor marker (magenta)
                using (var paintIntended = SKPaintFactory.CreateStroke(SKColors.Magenta, 0.8f))
                {
                    paintIntended.IsAntialias = true;
                    canvas.DrawCircle(anchor, 0f, 0.6f, paintIntended);
                }

                // clamped anchor marker (lime)
                using (var paintClamped = SKPaintFactory.CreateStroke(SKColors.Lime, 0.8f))
                {
                    paintClamped.IsAntialias = true;
                    canvas.DrawCircle(anchor, 0f, 0.6f, paintClamped);
                }

                // outline of pointer radius at clamped anchor (yellow)
                using (var outline = SKPaintFactory.CreateStroke(SKColors.Yellow, MathF.Max(0.5f, pointerRadius)))
                {
                    outline.IsAntialias = true;
                    canvas.DrawCircle(anchor, 0f, pointerRadius, outline);
                }

                // also draw walkway center for reference (cyan small dot at center of walkway)
                float walkwayCenter = (cfg.WalkwayInnerRadius + (cfg.WalkwayThickness * 0.5f)) * radiusScale;
                using (var paintCenter = SKPaintFactory.CreateStroke(SKColors.Cyan, 0.6f))
                {
                    paintCenter.IsAntialias = true;
                    canvas.DrawCircle(walkwayCenter, 0f, 0.5f, paintCenter);
                }
            }

            var headColor = SKColors.Cyan.WithAlpha((byte)200);
            using var p = SKPaintFactory.CreateFill(headColor);
            canvas.DrawCircle(anchor, 0f, pointerRadius, p);
            canvas.Restore();
        }

        /// <summary>
        /// Simple zoom panel draw used by Form1.PanelZoom Paint event. Centers on the librarian/head position
        /// and draws a small view of nearby nodes.
        /// </summary>
        public void DrawZoomPanel(System.Drawing.Graphics g, int panelWidth, int panelHeight)
        {
            if (g == null) return;
            // simple GDI drawing fallback using RadialLayoutEngine positions
            var nodes = new RadialLayoutEngine(Config).GenerateNodePositions();
            // compute librarian world pos
            float libX = 0f, libY = 0f;
            if (Engine?.CurrentState != null)
            {
                var st = Engine.CurrentState;
                int disk = Math.Max(1, st.DiskSize);
                float headAngle = (st.HeadPosition / (float)disk) * (MathF.PI * 2f);
                libX = Config.NodeRingRadius * MathF.Cos(headAngle);
                libY = Config.NodeRingRadius * MathF.Sin(headAngle);
            }

            float zoomCx = panelWidth / 2f;
            float zoomCy = panelHeight / 2f;
            float scale = 1.0f; // not implementing zoomfactor here; keep simple

            // clear
            g.Clear(System.Drawing.Color.Black);

            using var brushNode = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, System.Drawing.Color.FromArgb(Config.WalkwayAndNodeFillColor.Red, Config.WalkwayAndNodeFillColor.Green, Config.WalkwayAndNodeFillColor.Blue)));
            using var brushHead = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, System.Drawing.Color.Cyan));

            foreach (var n in nodes)
            {
                float dx = n.X - libX;
                float dy = n.Y - libY;
                float zx = zoomCx + dx * scale;
                float zy = zoomCy + dy * scale;
                float r = Config.NodeSize;
                g.FillEllipse(brushNode, zx - r, zy - r, 2 * r, 2 * r);
            }

            // draw head at center
            g.FillEllipse(brushHead, zoomCx - 4, zoomCy - 4, 8, 8);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                animationTimer?.Stop();
                animationTimer?.Dispose();
                skglControl?.Dispose();
                cachedWallPath?.Dispose();
                disposed = true;
            }
        }

        /// <summary>
        /// Animate the pointer to the node at the given head index over the specified duration (seconds).
        /// Safe to call from UI thread.
        /// </summary>
        public void AnimatePointerToHead(int headIndex, float durationSeconds)
        {
            // compute target angle based on current engine/disk size if available, otherwise use Config.NodeCount
            int disk = 0;
            if (engine != null) disk = Math.Max(1, engine.CurrentState.DiskSize);
            else disk = Math.Max(1, Config.NodeCount);

            int toIndex = ((headIndex % disk) + disk) % disk;

            // approximate current index from current angle to compute non-wrapping delta
            int fromIndex = (int)MathF.Round((pointerAngleCurrentDeg / 360f) * disk) % disk;
            if (fromIndex < 0) fromIndex += disk;

            // Try fluid path first; fallback to per-node queue
            if (!TryBuildFluidPath(fromIndex, toIndex, 0, durationSeconds, out float sAngle, out float eAngle))
            {
                EnqueueSeamSafePath(fromIndex, toIndex, 0, durationSeconds);
                return;
            }

            StartFluidAnimation(sAngle, eAngle, durationSeconds);
        }

        /// <summary>
        /// Animate the pointer along the path from one node index to another over the specified duration, in the given direction.
        /// If direction > 0, move clockwise; if < 0, counterclockwise; if 0, shortest path.
        /// The seam between node (disk-1) and node 0 is treated as an impassable edge and will not be crossed.
        /// </summary>
        public void AnimatePointerAlong(int fromIndex, int toIndex, float durationSeconds, int direction = 0)
        {
            int disk = 0;
            if (engine != null) disk = Math.Max(1, engine.CurrentState.DiskSize);
            else disk = Math.Max(1, Config.NodeCount);

            int f = ((fromIndex % disk) + disk) % disk;
            int t = ((toIndex % disk) + disk) % disk;

            // Try fluid path first; fallback to per-node queue
            if (!TryBuildFluidPath(f, t, direction, durationSeconds, out float sAngle, out float eAngle))
            {
                EnqueueSeamSafePath(f, t, direction, durationSeconds);
                return;
            }

            StartFluidAnimation(sAngle, eAngle, durationSeconds);
        }

        /// <summary>
        /// Update pointer progress as fraction [0..1] from previous to target angle. Useful when animating by simulation timer.
        /// </summary>
        public void UpdatePointerProgress(float fraction)
        {
            if (!PointerAnimationEnabled) return;
            fraction = Math.Clamp(fraction, 0f, 1f);
            // ease-out curve
            float t = 1f - (1f - fraction) * (1f - fraction);
            float delta = useDirectAngleInterpolation ? (pointerAngleTargetDeg - pointerAnglePrevDeg) : ShortestAngleDiff(pointerAnglePrevDeg, pointerAngleTargetDeg);
            pointerAngleCurrentDeg = pointerAnglePrevDeg + delta * t;
            pointerAngleCurrentDeg = (pointerAngleCurrentDeg % 360f + 360f) % 360f;
            // invalidate control for redraw
            try { skglControl.Invalidate(); } catch { }
        }

        // Enqueue a seam-aware path from `fromIndex` to `toIndex` and begin animating step-by-step.
        // totalDuration is distributed across steps; if <= 0 a default per-step duration is used.
        private void EnqueueSeamSafePath(int fromIndex, int toIndex, int direction, float totalDuration)
        {
            // use pointerFinder to compute a non-wrapping index path
            var path = pointerFinder.ComputeNonWrappingPathIndices(fromIndex, toIndex, direction);
            if (path == null || path.Count == 0) return;

            // If path only contains the start, snap to target
            if (path.Count == 1)
            {
                SnapPointerToIndex(path[0]);
                return;
            }

            // clear any existing queued path and enqueue steps (skip first because it's the 'from')
            pointerPathQueue.Clear();
            for (int i = 1; i < path.Count; i++)
                pointerPathQueue.Enqueue(path[i]);

            // compute per-step duration
            int steps = Math.Max(1, path.Count - 1);
            if (totalDuration > 1e-6f)
                perStepDuration = Math.Max(0.001f, totalDuration / steps);
            else
                perStepDuration = Math.Max(0.001f, perStepDuration);

            // mark currentAnimatingToIndex so StartNextAnimationStep will compute correct fromIndex
            currentAnimatingToIndex = -1;

            // start first step immediately
            StartNextAnimationStep();
        }

        private void SnapPointerToIndex(int index)
        {
            // get absolute node angle from pathfinder (node angles are world-space in degrees)
            var node = pointerFinder.GetNode(index);
            float angle = node.AngleDegrees;

            pointerAnglePrevDeg = angle;
            pointerAngleTargetDeg = angle;
            pointerAngleCurrentDeg = angle;
            pointerAnimElapsed = EffectivePointerAnimDurationBase;
            pointerEffectiveDuration = EffectivePointerAnimDurationBase;
            PointerAnimationEnabled = pointerEffectiveDuration > 0f;
            currentAnimatingToIndex = -1;
            pointerPathQueue.Clear();
        }

        /// <summary>
        /// Start the next step animation if any queued. Uses exact node angles from PointerPathfinder so no seam-wrap occurs.
        /// </summary>
        private void StartNextAnimationStep()
        {
            if (pointerPathQueue.Count == 0)
            {
                currentAnimatingToIndex = -1;
                return;
            }

            // Determine 'from' index for this step.
            int n = pointerFinder.NodeCount;
            int fromIndex;
            if (currentAnimatingToIndex >= 0)
            {
                fromIndex = currentAnimatingToIndex;
            }
            else
            {
                // approximate current index from current angle (fallback)
                fromIndex = (int)MathF.Round((pointerAngleCurrentDeg / 360f) * n) % n;
                if (fromIndex < 0) fromIndex += n;
            }

            int nextIndex = pointerPathQueue.Dequeue();

            // angles from pointerFinder (guaranteed non-wrapping since indices are adjacent on non-wrapping path)
            var fromNode = pointerFinder.GetNode(fromIndex);
            var toNode = pointerFinder.GetNode(nextIndex);
            float fromAngle = fromNode.AngleDegrees;
            float toAngle = toNode.AngleDegrees;

            // set up step animation
            pointerAnglePrevDeg = fromAngle;
            pointerAngleTargetDeg = toAngle;
            pointerAngleCurrentDeg = fromAngle;
            pointerAnimElapsed = 0f;
            pointerAnimDuration = perStepDuration;
            pointerEffectiveDuration = Math.Max(1e-6f, pointerAnimDuration);
            PointerAnimationEnabled = pointerEffectiveDuration > 0f;

            useDirectAngleInterpolation = true; // per-node steps should not wrap either
            currentAnimatingToIndex = nextIndex;
        }

        /// <summary>
        /// Validate and build a fluid unwrapped-angle start/end for a path. Returns false if validation fails.
        /// </summary>
        private bool TryBuildFluidPath(int fromIndex, int toIndex, int direction, float totalDuration, out float startAngle, out float endAngle)
        {
            startAngle = 0f;
            endAngle = 0f;

            var path = pointerFinder.ComputeNonWrappingPathIndices(fromIndex, toIndex, direction);
            if (path == null || path.Count == 0) return false;

            // If path contains seam edge, reject (pointerFinder should avoid seam when configured, but double-check)
            for (int i = 1; i < path.Count; i++)
            {
                if (pointerFinder.IsSeamEdge(path[i - 1], path[i])) return false;
            }

            // build angle list and unwrap to monotonic sequence
            var angles = new List<float>(path.Count);
            foreach (var idx in path) angles.Add(pointerFinder.GetNode(idx).AngleDegrees);

            // unwrapping: make sequence monotonic in the chosen direction
            float prev = angles[0];
            angles[0] = prev;
            for (int i = 1; i < angles.Count; i++)
            {
                float a = angles[i];
                // normalize a to be within +/-180 of prev
                while (a - prev > 180f) a -= 360f;
                while (a - prev < -180f) a += 360f;

                // enforce monotonicity when direction specified
                if (direction > 0)
                {
                    while (a < prev) a += 360f;
                }
                else if (direction < 0)
                {
                    while (a > prev) a -= 360f;
                }

                angles[i] = a;
                prev = a;
            }

            // final start/end
            startAngle = angles[0];
            endAngle = angles[angles.Count - 1];

            // Quick sanity: ensure total angular travel isn't using seam (difference magnitude < 360)
            float totalDelta = MathF.Abs(endAngle - startAngle);
            if (totalDelta >= 360f - 1e-3f) return false;

            return true;
        }

        /// <summary>
        /// Start a fluid (single continuous unwrapped angle) animation.
        /// </summary>
        private void StartFluidAnimation(float startAngle, float endAngle, float totalDuration)
        {
            // clear queued steps; fluid animation replaces per-node queue
            pointerPathQueue.Clear();
            currentAnimatingToIndex = -1;

            pointerAnglePrevDeg = startAngle;
            pointerAngleTargetDeg = endAngle;
            pointerAngleCurrentDeg = startAngle;
            pointerAnimElapsed = 0f;
            pointerAnimDuration = MathF.Max(0f, totalDuration);
            pointerEffectiveDuration = Math.Max(1e-6f, pointerAnimDuration);
            useDirectAngleInterpolation = true; // interpolate raw delta without shortest-angle wrapping
            PointerAnimationEnabled = pointerEffectiveDuration > 0f;
        }
    }
}
