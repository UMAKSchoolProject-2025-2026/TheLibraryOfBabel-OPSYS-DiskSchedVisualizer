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
        public SimulationEngine? Engine { get; set; }

        public bool EnableDiagnostics { get; set; } = false;
        public bool IsDimmed { get; set; } = false;

        public float PerspectivePower { get; set; } = 1.2f; // for compatibility
        public float HexOrientationOffset { get; set; } = MathF.PI / 6f;

        public NodeRenderMode NodeMode { get; set; } = NodeRenderMode.Hexagon;

        public Control? PanelZoom { get; set; }

        private readonly DebugOverlayRenderer debugRenderer = new DebugOverlayRenderer();

        public RadialRenderer()
        {
            skglControl = new SKGLControl { Dock = DockStyle.Fill };
            skglControl.PaintSurface += OnPaintSurface;
            skglControl.Resize += (s, e) => skglControl.Invalidate();

            animationTimer = new System.Windows.Forms.Timer { Interval = 16 };
            animationTimer.Tick += (s, e) => skglControl.Invalidate();
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

            // determine effective viewport scale (auto-fit or user-specified)
            float finalScale = Config.ViewportScale;
            if (Config.AutoFitToViewport)
            {
                // available device pixels after padding
                float padding = Config.FitPaddingPixels;
                float availW = Math.Max(1f, info.Width - padding * 2f);
                float availH = Math.Max(1f, info.Height - padding * 2f);

                // world diameter we need to fit (TotalRadius is a world-space radius)
                float worldDiameter = Math.Max(1f, Config.TotalRadius * 2f);

                // fit scale converts world units -> device pixels
                float fitScale = Math.Min(availW / worldDiameter, availH / worldDiameter);

                // apply a slight margin to avoid touching edges
                finalScale = Math.Max(0.0001f, fitScale * 0.95f);
            }

            // center origin and apply scale. Important: scale first, then translate using world units
            canvas.Save();
            canvas.Scale(finalScale);
            canvas.Translate(info.Width / 2f / finalScale, info.Height / 2f / finalScale);

            // compute pointer angle from engine if available
            float pointerDeg = 0f;
            if (Engine?.CurrentState != null)
            {
                var st = Engine.CurrentState;
                int disk = Math.Max(1, st.DiskSize);
                pointerDeg = (float)(st.HeadPosition / (double)disk * 360.0);
            }

            // DRAW ORDER (bottom -> top):
            // 1) walls (bottom)
            // 2) nodes
            // 3) walkway
            // 4) debug overlays (optional)
            // 5) pointer (top)

            // 1) walls (pass effective scale so glow blur is device-consistent)
            DrawWalls(canvas, Config, finalScale);

            // 2) nodes (use indexed loop so we can label each node)
            var layout = new RadialLayoutEngine(Config);
            var nodes = layout.GenerateNodePositions();
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                int label = i + 1; // 1-based labels
                if (NodeMode == NodeRenderMode.Circle) DrawCircleNode(canvas, n, Config, label);
                else DrawHexagonNode(canvas, n, Config, label);
            }

            // 3) walkway (drawn above nodes)
            DrawWalkway(canvas, Config);

            // 4) debug overlays (draw above walkway but below pointer)
            if (debugRenderer.DebugEnabled)
                debugRenderer.DrawOverlays(canvas, nodes, Config, finalScale);

            // 5) pointer on very top
            DrawPointer(canvas, pointerDeg, Config);

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
        public void DrawWalkway(SKCanvas canvas, RadialConfig cfg)
        {
            if (canvas == null || cfg == null) return;
            float inner = cfg.WalkwayInnerRadius;
            float outer = cfg.WalkwayOuterRadius;
            if (outer <= inner) return;
            float strokeWidth = outer - inner;
            float midRadius = inner + strokeWidth * 0.5f;
            // use walkway color from config (light gold, opaque by default)
            var walkwayColor = cfg.WalkwayAndNodeFillColor;
            using var p = SKPaintFactory.CreateStroke(walkwayColor, strokeWidth);
            canvas.DrawCircle(0f, 0f, midRadius, p);
        }

        /// <summary>
        /// Draws outer walls as a stroked ring immediately outside the walkway.
        /// Reimplemented to generate a thick polyline between node centers so the wall follows node geometry.
        /// </summary>
        public void DrawWalls(SKCanvas canvas, RadialConfig cfg, float effectiveScale)
        {
            if (canvas == null || cfg == null) return;

            // do nothing if there is no thickness or not enough nodes
            if (cfg.WallThickness <= 0f) return;

            // generate node positions for the current config
            var nodes = new RadialLayoutEngine(cfg).GenerateNodePositions();
            if (nodes == null || nodes.Count < 2) return;

            // build closed path from node centers
            using var path = new SKPath();
            path.MoveTo(nodes[0].X, nodes[0].Y);
            for (int i = 1; i < nodes.Count; i++)
                path.LineTo(nodes[i].X, nodes[i].Y);
            path.Close();

            // optional outward glow: draw blurred stroked path underneath the wall
            if (cfg.WallGlowEnabled && cfg.WallGlowWidth > 0f)
            {
                // glow stroke width: make it noticeably larger than the wall so blur spreads outward
                float glowStrokeWidth = Math.Max(1f, cfg.WallThickness + cfg.WallGlowWidth * 2f);

                using var glowPaint = new SKPaint
                {
                    Color = cfg.WallGlowColor,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = glowStrokeWidth,
                    StrokeJoin = SKStrokeJoin.Round,
                    StrokeCap = SKStrokeCap.Round,
                    // blur sigma in device pixels; scale world-units glow width to device pixels
                    ImageFilter = SKImageFilter.CreateBlur(cfg.WallGlowWidth * effectiveScale, cfg.WallGlowWidth * effectiveScale)
                };

                canvas.DrawPath(path, glowPaint);
            }

            // draw main wall stroke on top of glow
            var wallColor = cfg.WallAndBorderColor;
            using var paint = SKPaintFactory.CreateStroke(wallColor, cfg.WallThickness);
            paint.StrokeJoin = SKStrokeJoin.Round;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.IsAntialias = true;
            canvas.DrawPath(path, paint);
        }

        public void DrawCircleNode(SKCanvas canvas, NodePos pos, RadialConfig cfg, int label)
        {
            if (canvas == null || cfg == null) return;
            canvas.Save();
            canvas.Translate(pos.X, pos.Y);
            // use configured node fill color (light gold)
            var nodeColor = cfg.WalkwayAndNodeFillColor;
            using var p = SKPaintFactory.CreateFill(nodeColor);
            canvas.DrawCircle(0f, 0f, cfg.NodeSize, p);

            // draw centered label anchored to this node and computed from node center
            DrawCenteredNodeLabel(canvas, label.ToString(), cfg, pos.X, pos.Y);

            canvas.Restore();
        }

        public void DrawHexagonNode(SKCanvas canvas, NodePos pos, RadialConfig cfg, int label)
        {
            if (canvas == null || cfg == null) return;
            canvas.Save();
            canvas.Translate(pos.X, pos.Y);

            // draw rotated hex (rotation used only for hex geometry)
            canvas.Save();
            canvas.RotateDegrees(pos.AngleDegrees + (HexOrientationOffset * (180f / MathF.PI)));

            // fill color from config (light gold, opaque)
            var nodeColor = cfg.WalkwayAndNodeFillColor;
            using var fillPaint = SKPaintFactory.CreateFill(nodeColor);

            // border color: use same dark gold as wall from config
            var borderColor = cfg.WallAndBorderColor;
            float borderWidth = MathF.Max(cfg.NodeBorderMinWidth, cfg.WallThickness * cfg.NodeBorderWidthFactor);
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
                float x = cfg.NodeSize * MathF.Cos(a);
                float y = cfg.NodeSize * MathF.Sin(a);
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            path.Close();

            // draw fill then border (both opaque)
            canvas.DrawPath(path, fillPaint);
            canvas.DrawPath(path, borderPaint);

            canvas.Restore(); // back to translate-only (no hex rotation)

            // draw centered label anchored to this node and computed from node center
            DrawCenteredNodeLabel(canvas, label.ToString(), cfg, pos.X, pos.Y);

            path.Dispose();
            canvas.Restore();
        }

        // Draw centered numeric label anchored at the node's center.
        // nodeX/nodeY are the node's world coordinates (used to compute inward angle).
        private void DrawCenteredNodeLabel(SKCanvas canvas, string text, RadialConfig cfg, float nodeX, float nodeY)
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
            float apothem = cfg.NodeSize * 0.866025403784f;

            // Available width/height inside the hex (conservative): use apothem*2 and apply padding.
            float available = apothem * 2f * (1f - pad);
            if (available <= 0f) available = cfg.NodeSize * 1.0f;

            // create paint for text using wall/border color (dark gold)
            using var textPaint = SKPaintFactory.CreateFill(cfg.WallAndBorderColor);
            textPaint.IsAntialias = true;
            textPaint.TextAlign = SKTextAlign.Center;
            textPaint.Typeface = SKTypeface.Default;

            // Start with available as a baseline TextSize (world units) then apply global scale
            textPaint.TextSize = Math.Max(1f, available * cfg.NodeLabelScale);

            // Measure text and scale to fit available width
            float measured = textPaint.MeasureText(text);
            if (measured <= 0f) measured = 1f;
            float scale = available / measured;

            // apply a small safety margin and global scale
            float finalSize = textPaint.TextSize * scale * 0.9f;

            // shrink single digits slightly for better visual balance using configurable factor
            if (text.Length == 1) finalSize *= cfg.SingleDigitScaleFactor;

            textPaint.TextSize = Math.Max(1f, finalSize);

            // compute vertical baseline to center text
            var fm = textPaint.FontMetrics;
            float baseline = -(fm.Ascent + fm.Descent) * 0.5f;

            // compute inward direction (from node toward origin)
            float dx = -nodeX;
            float dy = -nodeY;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            float ux = 0f, uy = -1f; // default if at center
            if (len > 1e-6f) { ux = dx / len; uy = dy / len; }

            // compute inset so the label sits visually inside the hex (use apothem and configurable inset factor)
            float inset = apothem * cfg.NodeLabelInsetFactor * (1f - pad);

            canvas.Save();
            // canvas is already translated to node center by caller — move inward along radial vector
            canvas.Translate(ux * inset, uy * inset);

            // keep text upright to viewport (no per-node rotation)
            canvas.DrawText(text, 0f, baseline, textPaint);
            canvas.Restore();
        }

        public void DrawPointer(SKCanvas canvas, float pointerAngle, RadialConfig cfg)
        {
            if (canvas == null || cfg == null) return;
            canvas.Save();
            canvas.RotateDegrees(pointerAngle);

            // Center pointer on the walkway (midpoint between inner edge and outer edge).
            float midWalkway = cfg.WalkwayInnerRadius + cfg.WalkwayThickness * 0.5f;

            // Clamp so the pointer circle stays fully inside the walkway band.
            float halfPointer = cfg.PointerSize * 0.5f;
            float minR = cfg.WalkwayInnerRadius + halfPointer;
            float maxR = MathF.Max(minR, cfg.WalkwayOuterRadius - halfPointer);
            float radius = Math.Clamp(midWalkway, minR, maxR);

            // semi-transparent cyan librarian/head (unchanged)
            var headColor = SKColors.Cyan.WithAlpha((byte)200);
            using var p = SKPaintFactory.CreateFill(headColor);
            canvas.DrawCircle(radius, 0f, cfg.PointerSize, p);
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
                double headAngle = (st.HeadPosition / (double)disk) * (Math.PI * 2.0);
                libX = Config.NodeRingRadius * MathF.Cos((float)headAngle);
                libY = Config.NodeRingRadius * MathF.Sin((float)headAngle);
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
                disposed = true;
            }
        }
    }
}
