using System;
using System.Collections.Generic;
using SkiaSharp;

namespace LibraryOFBabel.Visualization
{
    /// <summary>
    /// Renders optional debug overlays for radial scenes: node position markers, anchor lines and bounding boxes.
    /// Controlled by the <see cref="DebugEnabled"/> flag. Callers should translate the canvas to scene origin
    /// (center) before invoking these helpers so node coordinates match.
    /// </summary>
    public sealed class DebugOverlayRenderer
    {
        public bool DebugEnabled { get; set; } = false;

        /// <summary>
        /// Draw debug overlays for the provided nodes. No-op when <see cref="DebugEnabled"/> is false.
        /// The viewportScale parameter is the effective world->device pixels scale applied by the renderer.
        /// Expects node positions in 'nodes' to already be multiplied by densityScale.
        /// </summary>
        public void DrawOverlays(SKCanvas canvas, IEnumerable<NodePos> nodes, RadialConfig cfg, float viewportScale, float densityScale)
        {
            if (!DebugEnabled || canvas == null || nodes == null || cfg == null) return;

            // compute a safe viewport scale (avoid divide-by-zero)
            float vs = Math.Max(0.0001f, viewportScale);

            // Anchor lines paint (keep stroke ~1 device pixel)
            using var anchorPaint = SKPaintFactory.CreateStroke(new SKColor(255, 165, 0), 1f / vs); // orange
            // Position marker paint
            using var posPaint = SKPaintFactory.CreateFill(SKColors.Red);
            // Bounding box paint (keep stroke ~1 device pixel)
            using var boxPaint = SKPaintFactory.CreateStroke(SKColors.Lime, 1f / vs);

            // marker radius: at least 1 screen pixel (converted to world units) or a fraction of node size (scaled)
            float markerRadius = Math.Max(1f / vs, cfg.NodeSize * 0.25f * densityScale);

            // box size: prefer node-based but ensure a minimum screen size (e.g. 2 pixels)
            float boxSize = Math.Max(2f / vs, cfg.NodeSize * 2f * densityScale);

            foreach (var n in nodes)
            {
                // anchor line from center to node (nodes are already scaled)
                canvas.DrawLine(0f, 0f, n.X, n.Y, anchorPaint);

                // position marker (small filled circle)
                canvas.DrawCircle(n.X, n.Y, markerRadius, posPaint);

                // bounding box centered at node
                var left = n.X - boxSize * 0.5f;
                var top = n.Y - boxSize * 0.5f;
                var rect = new SKRect(left, top, left + boxSize, top + boxSize);
                canvas.DrawRect(rect, boxPaint);
            }
        }
    }
}
