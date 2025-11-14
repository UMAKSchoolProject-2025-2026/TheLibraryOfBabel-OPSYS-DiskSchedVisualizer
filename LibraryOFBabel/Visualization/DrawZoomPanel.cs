using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Handles drawing the zoomed-in view of the librarian in a separate panel.
    /// Provides configurable properties for zoom factor, radius, and spacing.
    /// </summary>
    public class DrawZoomPanel
    {
        // Configurable properties
        public float ZoomFactor { get; set; } = 3.0f; // Magnification level
        public float ZoomRadius { get; set; } = 100f; // Radius in world space to draw nodes
        public float SpacingFactor { get; set; } = 2.5f; // Factor to space out nodes to prevent overlap
        public float BaseNodeRadius { get; set; } = 10f; // Base radius for nodes in zoom
        public float BaseCoreRadius { get; set; } = 10f; // Base radius for core in zoom
        public float LibrarianSizeMultiplier { get; set; } = 0.5f; // Multiplier for librarian size

        // Cached values from renderer
        private float currentCx, currentCy, currentMaxRadius, currentBaseNodeRadius, currentLayerSpacing, currentLastNodeRadius;
        private int currentLayers;
        private double currentRotationAngle;
        private float librarianAngleOffset;

        /// <summary>
        /// Updates cached values from the renderer.
        /// </summary>
        public void UpdateCache(float cx, float cy, float maxRadius, int layers, float layerSpacing, float baseNodeRadius, float lastNodeRadius, double rotationAngle, float angleOffset)
        {
            currentCx = cx;
            currentCy = cy;
            currentMaxRadius = maxRadius;
            currentLayers = layers;
            currentLayerSpacing = layerSpacing;
            currentBaseNodeRadius = baseNodeRadius;
            currentLastNodeRadius = lastNodeRadius;
            currentRotationAngle = rotationAngle;
            librarianAngleOffset = angleOffset;
        }

        /// <summary>
        /// Draws the zoomed-in view on the specified Graphics.
        /// </summary>
        public void Draw(Graphics g, int panelWidth, int panelHeight, SimulationEngine? engine, float perspectivePower, int nodeCount, float hexOrientationOffset)
        {
            var simState = engine?.CurrentState;
            if (simState == null) return;

            // Scale properties based on nodeCount for consistency (base is 200)
            float scaleFactor = nodeCount / 200f;
            float scaledZoomRadius = ZoomRadius / scaleFactor;
            float scaledSpacingFactor = SpacingFactor * scaleFactor;

            // Calculate librarian position
            int headPos = simState.HeadPosition;
            int disk = Math.Max(1, simState.DiskSize);
            double headAngle = (headPos / (double)disk) * (Math.PI * 2.0);
            double finalAngle = headAngle + librarianAngleOffset;
            float outerRadius = currentLayerSpacing * currentLayers;
            float libOutwardOffset = currentLastNodeRadius * 2.0f; // LibrarianOffsetNodeFactor
            float libRadiusFromCenter = outerRadius + libOutwardOffset;
            float hx = currentCx + libRadiusFromCenter * MathF.Cos((float)finalAngle);
            float hy = currentCy + libRadiusFromCenter * MathF.Sin((float)finalAngle);

            // Zoom center
            float zoomCx = panelWidth / 2f;
            float zoomCy = panelHeight / 2f;

            // Clear background
            g.Clear(Color.Black);

            // Draw center core
            // float zCoreRadius = BaseCoreRadius * ZoomFactor;
            // using (Brush coreBrush = new SolidBrush(Color.Yellow))
            // {
            //     g.FillEllipse(coreBrush, zoomCx - zCoreRadius, zoomCy - zCoreRadius, 2 * zCoreRadius, 2 * zCoreRadius);
            // }

            // Prepare request set
            var pendingSet = new HashSet<int>(simState.PendingRequests);
            int diskSize = simState.DiskSize;

            // Draw guide rings
            for (int layerIndex = 0; layerIndex < currentLayers; layerIndex++)
            {
                float t = layerIndex / (float)(currentLayers - 1);
                float scale = 0.1f + 0.9f * MathF.Pow(t, perspectivePower);
                float ringRadius = currentMaxRadius * scale;
                float zRingRadius = ringRadius * ZoomFactor;
                byte ringAlpha = (byte)(30 * (layerIndex + 1) / currentLayers);
                using (Pen ringPen = new Pen(Color.FromArgb(ringAlpha, Color.White)))
                {
                    g.DrawEllipse(ringPen, zoomCx - zRingRadius, zoomCy - zRingRadius, 2 * zRingRadius, 2 * zRingRadius);
                }
            }

            // Draw nodes within zoom radius
            for (int layerIndex = 0; layerIndex < currentLayers; layerIndex++)
            {
                float t = layerIndex / (float)(currentLayers - 1);
                float scale = 0.1f + 0.9f * MathF.Pow(t, perspectivePower);
                float ringRadius = currentMaxRadius * scale;
                int nodesThisLayer = nodeCount;
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
                    if (dist > scaledZoomRadius) continue;

                    // Scaled position with spacing
                    float zx = zoomCx + dx * ZoomFactor * scaledSpacingFactor;
                    float zy = zoomCy + dy * ZoomFactor * scaledSpacingFactor;

                    // Fixed scaled size
                    float zRadius = BaseNodeRadius * ZoomFactor;

                    // Cylinder mapping
                    int cylinder = diskSize > 1 ? (int)Math.Round((double)i / nodesThisLayer * (diskSize - 1)) : 0;
                    Color fillColor = pendingSet.Contains(cylinder) ? Color.Yellow : Color.Brown;

                    // Draw as hexagon
                    PointF[] points = CreateRegularPolygonPoints(6, zRadius, zx, zy, angle + hexOrientationOffset);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddPolygon(points);
                        using (Brush brush = new SolidBrush(fillColor))
                        {
                            g.FillPath(brush, path);
                        }
                        using (Pen pen = new Pen(Color.Black))
                        {
                            g.DrawPath(pen, path);
                        }
                    }
                }

                // Draw inner librarian if within zoom
                if (layerIndex < currentLayers - 1 && simState != null)
                {
                    float libRadiusFromCenterInner = ringRadius + (currentBaseNodeRadius * scale) * 2.0f;
                    float libAngleInner = (float)(finalAngle + layerRotation);
                    float libX = currentCx + libRadiusFromCenterInner * MathF.Cos(libAngleInner);
                    float libY = currentCy + libRadiusFromCenterInner * MathF.Sin(libAngleInner);

                    float dxInner = libX - hx;
                    float dyInner = libY - hy;
                    float distInner = MathF.Sqrt(dxInner * dxInner + dyInner * dyInner);
                    if (distInner <= scaledZoomRadius)
                    {
                        float zxInner = zoomCx + dxInner * ZoomFactor * scaledSpacingFactor;
                        float zyInner = zoomCy + dyInner * ZoomFactor * scaledSpacingFactor;
                        float zLibRadiusInner = BaseNodeRadius * ZoomFactor * LibrarianSizeMultiplier;
                        using (Brush innerLibBrush = new SolidBrush(Color.Blue))
                        {
                            g.FillEllipse(innerLibBrush, zxInner - zLibRadiusInner, zyInner - zLibRadiusInner, 2 * zLibRadiusInner, 2 * zLibRadiusInner);
                        }
                    }
                }
            }

            // Draw librarian
            float zLibRadius = BaseNodeRadius * ZoomFactor * LibrarianSizeMultiplier;
            using (Brush libBrush = new SolidBrush(Color.Blue))
            {
                g.FillEllipse(libBrush, zoomCx - zLibRadius, zoomCy - zLibRadius, 2 * zLibRadius, 2 * zLibRadius);
            }

            // Draw head
            float zHeadRadius = Math.Max(3f, BaseNodeRadius * 0.5f) * ZoomFactor * LibrarianSizeMultiplier;
            using (Brush headBrush = new SolidBrush(Color.Cyan))
            {
                g.FillEllipse(headBrush, zoomCx - zHeadRadius, zoomCy - zHeadRadius, 2 * zHeadRadius, 2 * zHeadRadius);
            }
        }

        /// <summary>
        /// Creates points for a regular polygon.
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
    }
}
