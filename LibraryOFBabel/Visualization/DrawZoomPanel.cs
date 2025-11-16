using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Visualization
{
    public class LayerSnapshot
    {
        public float LayerScale { get; set; }
        public float RingRadius { get; set; }           // world units
        public float NodeRadiusScaled { get; set; }     // world units (node radius in world space)
        public float LayerRotation { get; set; }
        public float WallRadius { get; set; }           // world units (center radius for wall)
        public float WalkwayRadius { get; set; }        // world units (center radius for walkway)
        public float BaseNodeRadius { get; set; }       // world units
        public PointF[] NodeWorldPositions { get; set; } = Array.Empty<PointF>(); // world coords
    }

    /// <summary>
    /// Camera-style zoom viewport. All geometry is computed/kept in world coordinates first and only
    /// transformed to zoom coordinates at draw time using:
    ///   zx = zoomCx + (worldX - libWorldX) * ZoomFactor
    ///   zy = zoomCy + (worldY - libWorldY) * ZoomFactor
    /// IMPORTANT: radii/thickness values are world units and are multiplied exactly once by ZoomFactor.
    /// </summary>
    public class DrawZoomPanel
    {
        public float ZoomFactor { get; set; } = 2.0f;
        public float ZoomRadius { get; set; } = 100f;
        public float SpacingFactor { get; set; } = 1.0f; // preserved but NOT applied to camera transform
        public float LibrarianSizeMultiplier { get; set; } = 0.4f;

        // Cached renderer globals (world-space)
        private float currentCx, currentCy, currentMaxRadius, currentBaseNodeRadius, currentLayerSpacing, currentLastNodeRadius;
        private int currentLayers;
        private double currentRotationAngle;
        private float librarianAngleOffset;
        private float currentMinScale;
        private float currentLibrarianOffsetNodeFactor;
        private float currentWallWidthAdjustment, currentWallOffsetAdjustment;
        private float currentWalkwayWidthAdjustment, currentWalkwayOffsetAdjustment;

        // Optional exact snapshots provided by the SKIA renderer
        private IReadOnlyList<LayerSnapshot>? layerSnapshots;

        public void UpdateCache(
            float cx,
            float cy,
            float maxRadius,
            int layers,
            float layerSpacing,
            float baseNodeRadius,
            float lastNodeRadius,
            double rotationAngle,
            float angleOffset,
            float minScale,
            float librarianOffsetNodeFactor,
            float wallWidthAdjustment,
            float wallOffsetAdjustment,
            float walkwayWidthAdjustment,
            float walkwayOffsetAdjustment,
            IReadOnlyList<LayerSnapshot>? snapshots = null)
        {
            currentCx = cx;
            currentCy = cy;
            currentMaxRadius = maxRadius;
            currentLayers = Math.Max(1, layers);
            currentLayerSpacing = layerSpacing;
            currentBaseNodeRadius = baseNodeRadius;
            currentLastNodeRadius = lastNodeRadius;
            currentRotationAngle = rotationAngle;
            librarianAngleOffset = angleOffset;
            currentMinScale = minScale;
            currentLibrarianOffsetNodeFactor = librarianOffsetNodeFactor;
            currentWallWidthAdjustment = wallWidthAdjustment;
            currentWallOffsetAdjustment = wallOffsetAdjustment;
            currentWalkwayWidthAdjustment = walkwayWidthAdjustment;
            currentWalkwayOffsetAdjustment = walkwayOffsetAdjustment;
            layerSnapshots = snapshots;
        }

        public void Draw(Graphics g, int panelWidth, int panelHeight, SimulationEngine? engine, float perspectivePower, int nodeCount, float hexOrientationOffset)
        {
            var simState = engine?.CurrentState;
            if (simState == null) return;

            // Compute librarian and head world positions using exact renderer formulas
            int headPos = simState.HeadPosition;
            int disk = Math.Max(1, simState.DiskSize);
            double headAngle = (headPos / (double)disk) * (Math.PI * 2.0);
            double libAngle = headAngle + librarianAngleOffset;

            float outerRadius = currentLayerSpacing * currentLayers;
            float libOutwardOffset = currentLastNodeRadius * currentLibrarianOffsetNodeFactor;
            float libRadiusFromCenter = outerRadius - libOutwardOffset;

            float libWorldX = currentCx + libRadiusFromCenter * MathF.Cos((float)libAngle);
            float libWorldY = currentCy + libRadiusFromCenter * MathF.Sin((float)libAngle);

            float headRadiusFromCenter = outerRadius - currentLastNodeRadius * currentLibrarianOffsetNodeFactor;
            float headWorldX = currentCx + headRadiusFromCenter * MathF.Cos((float)headAngle);
            float headWorldY = currentCy + headRadiusFromCenter * MathF.Sin((float)headAngle);

            float zoomCx = panelWidth / 2f;
            float zoomCy = panelHeight / 2f;

            var oldSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);

            var pendingSet = new HashSet<int>(simState.PendingRequests);
            int nodesPerLayer = Math.Max(1, nodeCount);

            // Helper: world -> zoom transform
            static (float zx, float zy) WorldToZoom(float worldX, float worldY, float libWorldX, float libWorldY, float zoomCx, float zoomCy, float zoomFactor)
            {
                return (zoomCx + (worldX - libWorldX) * zoomFactor, zoomCy + (worldY - libWorldY) * zoomFactor);
            }

            // If snapshots available, draw from them (preferred - pixel perfect)
            if (layerSnapshots != null)
            {
                for (int li = 0; li < layerSnapshots.Count; li++)
                {
                    var snap = layerSnapshots[li];

                    // Guide ring: center is disk center transformed to zoom coords
                    var (centerZx, centerZy) = WorldToZoom(currentCx, currentCy, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                    float zRingRadius = snap.RingRadius * ZoomFactor;
                    byte ringAlpha = (byte)Math.Clamp(30 * (li + 1) / layerSnapshots.Count, 0, 255);
                    using (var ringPen = new Pen(Color.FromArgb(ringAlpha, Color.White), Math.Max(0.5f, snap.LayerScale) * ZoomFactor))
                        g.DrawEllipse(ringPen, centerZx - zRingRadius, centerZy - zRingRadius, 2 * zRingRadius, 2 * zRingRadius);

                    // Walls: node world positions are stored. wallThickness is world units -> multiply once by ZoomFactor.
                    float wallThicknessWorld = Math.Max(0.5f, currentWallWidthAdjustment * snap.NodeRadiusScaled);
                    using (var wallPen = new Pen(Color.Goldenrod, wallThicknessWorld * ZoomFactor) { EndCap = LineCap.Round })
                    {
                        var nodes = snap.NodeWorldPositions;
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            var p1 = nodes[i];
                            var p2 = nodes[(i + 1) % nodes.Length];
                            var (zx1, zy1) = WorldToZoom(p1.X, p1.Y, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                            var (zx2, zy2) = WorldToZoom(p2.X, p2.Y, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                            g.DrawLine(wallPen, zx1, zy1, zx2, zy2);
                        }
                    }

                    // Nodes: use stored world radius and positions; multiply radius exactly once by ZoomFactor.
                    for (int i = 0; i < snap.NodeWorldPositions.Length; i++)
                    {
                        var wp = snap.NodeWorldPositions[i];
                        float dx = wp.X - libWorldX;
                        float dy = wp.Y - libWorldY;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);
                        if (dist > ZoomRadius) continue;

                        var (zx, zy) = WorldToZoom(wp.X, wp.Y, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                        float zRadius = snap.NodeRadiusScaled * ZoomFactor; // only *ZoomFactor, no extra scaling

                        int cylinder = disk > 1 ? (int)Math.Round((double)i / snap.NodeWorldPositions.Length * (disk - 1)) : 0;
                        Color fill = pendingSet.Contains(cylinder) ? Color.Yellow : Color.DarkGoldenrod;

                        PointF[] poly = CreateRegularPolygonPoints(6, zRadius, zx, zy, hexOrientationOffset);
                        using (var path = new GraphicsPath())
                        {
                            path.AddPolygon(poly);
                            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                            using (var p = new Pen(Color.LightGoldenrodYellow, Math.Max(1f, 2f * (li + 1f) / Math.Max(1, layerSnapshots.Count)) * ZoomFactor))
                                g.DrawPath(p, path);
                        }
                    }

                    // Walkway: computed in world units in snapshot; compute outer/inner radii in world units and multiply once by ZoomFactor.
                    if (snap.WalkwayRadius > 0)
                    {
                        float walkwayThicknessWorld = currentWalkwayWidthAdjustment * snap.NodeRadiusScaled;
                        float outerRWorld = snap.WalkwayRadius + walkwayThicknessWorld / 2f;
                        float innerRWorld = Math.Max(0f, snap.WalkwayRadius - walkwayThicknessWorld / 2f);
                        float outerR = outerRWorld * ZoomFactor;
                        float innerR = innerRWorld * ZoomFactor;

                        using (var p = new GraphicsPath())
                        {
                            p.AddEllipse(centerZx - outerR, centerZy - outerR, 2 * outerR, 2 * outerR);
                            p.AddEllipse(centerZx - innerR, centerZy - innerR, 2 * innerR, 2 * innerR);
                            using (var b = new SolidBrush(Color.FromArgb(200, Color.DarkGoldenrod))) g.FillPath(b, p);
                        }
                    }
                }

                // Draw librarian at zoom center (camera centers librarian)
                float libZradius = currentLastNodeRadius * ZoomFactor * LibrarianSizeMultiplier; // world radius * ZoomFactor
                using (var libBrush = new SolidBrush(Color.Blue)) g.FillEllipse(libBrush, zoomCx - libZradius, zoomCy - libZradius, 2 * libZradius, 2 * libZradius);

                // Draw head using world->zoom transform
                var (zxHead, zyHead) = WorldToZoom(headWorldX, headWorldY, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                float headZradius = Math.Max(3f, currentLastNodeRadius * 0.5f) * ZoomFactor * LibrarianSizeMultiplier;
                using (var hBrush = new SolidBrush(Color.Cyan)) g.FillEllipse(hBrush, zxHead - headZradius, zyHead - headZradius, 2 * headZradius, 2 * headZradius);

                g.SmoothingMode = oldSmoothing;
                return;
            }

            // Fallback: compute world geometry using exact renderer formulas (including perspective) then transform at draw time
            var computed = new List<LayerSnapshot>(Math.Max(1, currentLayers));
            for (int layerIndex = 0; layerIndex < Math.Max(1, currentLayers); layerIndex++)
            {
                float t = (currentLayers > 1) ? layerIndex / (float)(currentLayers - 1) : 1f;
                float layerScale = currentMinScale + (1 - currentMinScale) * MathF.Pow(t, perspectivePower);
                float ringRadius = currentMaxRadius * layerScale;
                float nodeRadiusScaled = MathF.Min(currentBaseNodeRadius * layerScale, (2f * MathF.PI * ringRadius / nodesPerLayer) * 0.45f);

                float rotationMultiplier = (currentLayers - layerIndex - 1) * 0.1f * (layerIndex % 2 == 0 ? 1f : -1f);
                float layerRotation = (float)(currentRotationAngle * rotationMultiplier);

                // node world positions
                var nodeWorldPositions = new PointF[nodesPerLayer];
                float angleStep = (2f * MathF.PI) / nodesPerLayer;
                for (int ni = 0; ni < nodesPerLayer; ni++)
                {
                    float a = (ni * angleStep) + layerRotation;
                    nodeWorldPositions[ni] = new PointF(currentCx + ringRadius * MathF.Cos(a), currentCy + ringRadius * MathF.Sin(a));
                }

                float wallRadius = (ringRadius + nodeRadiusScaled) - currentWallOffsetAdjustment * nodeRadiusScaled - (currentWallWidthAdjustment * nodeRadiusScaled) / 2f;
                float walkwayRadius = ringRadius - currentWalkwayOffsetAdjustment * nodeRadiusScaled - (currentWalkwayWidthAdjustment * nodeRadiusScaled) / 2f;

                computed.Add(new LayerSnapshot
                {
                    LayerScale = layerScale,
                    RingRadius = ringRadius,
                    NodeRadiusScaled = nodeRadiusScaled,
                    LayerRotation = layerRotation,
                    WallRadius = wallRadius,
                    WalkwayRadius = walkwayRadius,
                    BaseNodeRadius = currentBaseNodeRadius,
                    NodeWorldPositions = nodeWorldPositions
                });
            }

            // Draw computed world primitives transformed by camera (no extra scaling of radii except *ZoomFactor)
            for (int li = 0; li < computed.Count; li++)
            {
                var snap = computed[li];

                var (centerZx, centerZy) = WorldToZoom(currentCx, currentCy, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                float zRingRadius = snap.RingRadius * ZoomFactor;
                byte ringAlpha = (byte)Math.Clamp(30 * (li + 1) / computed.Count, 0, 255);
                using (var ringPen = new Pen(Color.FromArgb(ringAlpha, Color.White), Math.Max(0.5f, snap.LayerScale) * ZoomFactor))
                    g.DrawEllipse(ringPen, centerZx - zRingRadius, centerZy - zRingRadius, 2 * zRingRadius, 2 * zRingRadius);

                // walls
                float wallThicknessWorld = Math.Max(0.5f, currentWallWidthAdjustment * snap.NodeRadiusScaled);
                using (var wallPen = new Pen(Color.Goldenrod, wallThicknessWorld * ZoomFactor) { EndCap = LineCap.Round })
                {
                    var nodes = snap.NodeWorldPositions;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        var p1 = nodes[i];
                        var p2 = nodes[(i + 1) % nodes.Length];
                        var (zx1, zy1) = WorldToZoom(p1.X, p1.Y, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                        var (zx2, zy2) = WorldToZoom(p2.X, p2.Y, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                        g.DrawLine(wallPen, zx1, zy1, zx2, zy2);
                    }
                }

                // nodes
                var nodePositions = snap.NodeWorldPositions;
                for (int i = 0; i < nodePositions.Length; i++)
                {
                    var wp = nodePositions[i];
                    float dx = wp.X - libWorldX;
                    float dy = wp.Y - libWorldY;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > ZoomRadius) continue;

                    var (zx, zy) = WorldToZoom(wp.X, wp.Y, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
                    float zRadius = snap.NodeRadiusScaled * ZoomFactor; // only once

                    int cylinder = disk > 1 ? (int)Math.Round((double)i / nodePositions.Length * (disk - 1)) : 0;
                    Color fill = pendingSet.Contains(cylinder) ? Color.Yellow : Color.DarkGoldenrod;

                    PointF[] poly = CreateRegularPolygonPoints(6, zRadius, zx, zy, hexOrientationOffset);
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(poly);
                        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                        using (var p = new Pen(Color.LightGoldenrodYellow, Math.Max(1f, 2f * (li + 1f) / Math.Max(1, computed.Count)) * ZoomFactor)) g.DrawPath(p, path);
                    }
                }

                // walkway annulus
                if (snap.WalkwayRadius > 0)
                {
                    float walkwayThicknessWorld = currentWalkwayWidthAdjustment * snap.NodeRadiusScaled;
                    float outerR = (snap.WalkwayRadius + walkwayThicknessWorld / 2f) * ZoomFactor;
                    float innerR = Math.Max(0f, (snap.WalkwayRadius - walkwayThicknessWorld / 2f)) * ZoomFactor;
                    using (var p = new GraphicsPath())
                    {
                        p.AddEllipse(centerZx - outerR, centerZy - outerR, 2 * outerR, 2 * outerR);
                        p.AddEllipse(centerZx - innerR, centerZy - innerR, 2 * innerR, 2 * innerR);
                        using (var b = new SolidBrush(Color.FromArgb(200, Color.DarkGoldenrod))) g.FillPath(b, p);
                    }
                }
            }

            // Draw librarian and head (camera transform)
            float libZr = currentLastNodeRadius * ZoomFactor * LibrarianSizeMultiplier;
            using (var libBrush = new SolidBrush(Color.Blue)) g.FillEllipse(libBrush, zoomCx - libZr, zoomCy - libZr, 2 * libZr, 2 * libZr);

            var (zxh, zyh) = WorldToZoom(headWorldX, headWorldY, libWorldX, libWorldY, zoomCx, zoomCy, ZoomFactor);
            float headZr = Math.Max(3f, currentLastNodeRadius * 0.5f) * ZoomFactor * LibrarianSizeMultiplier;
            using (var hb = new SolidBrush(Color.Cyan)) g.FillEllipse(hb, zxh - headZr, zyh - headZr, 2 * headZr, 2 * headZr);

            g.SmoothingMode = oldSmoothing;
        }

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
    }
}
