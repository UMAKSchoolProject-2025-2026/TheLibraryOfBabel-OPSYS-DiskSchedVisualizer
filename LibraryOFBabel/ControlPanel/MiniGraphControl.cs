using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LibraryOFBabel.ControlPanel
{
    // Self-contained panel that renders the mini path graph and provides export/animation APIs.
    public class MiniGraphControl : Panel
    {
        // linkable external UI pieces
        public RichTextBox? FormulaBox { get; set; }
        public Label? ServedLabel { get; set; }

        // diagram data
        private List<int> diagramRequests = new List<int>();
        private int diagramHeadPosition = 0;
        private int diagramDiskSize = 200;
        private float diagramProgress = 1f;
        private CancellationTokenSource? diagramAnimCts;
        private string? persistedDiagramText;
        private List<int>? persistedFinalRequests;

        // visualization backing: these represent the sequence of actually served cylinders
        private List<int> axisTicks = new List<int>();
        private List<int> servedSequence = new List<int>();

        // --- cache to avoid reinitializing axis every tick ---
        private string? lastPendingSnapshot;
        private int lastHeadPosition = -1;
        private int lastDiskSize = -1;

        // context menu
        private ContextMenuStrip miniCtxMenu = new ContextMenuStrip();

        public MiniGraphControl()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;

            var exportPng = new ToolStripMenuItem("Export PNG");
            exportPng.Click += (s, e) => ExportMiniGraphPng();
            var exportCsv = new ToolStripMenuItem("Export CSV");
            exportCsv.Click += (s, e) => ExportMiniGraphCsv();
            var clear = new ToolStripMenuItem("Clear");
            clear.Click += (s, e) => { ClearMiniGraphData(); };
            miniCtxMenu.Items.AddRange(new ToolStripItem[] { exportPng, exportCsv, clear });

            MouseMove += PnlMiniGraph_MouseMove;
            MouseClick += PnlMiniGraph_MouseClick;
        }

        private void ClearMiniGraphData()
        {
            servedSequence.Clear();
            axisTicks.Clear();
            UpdateServedLabel();
            Invalidate();
        }

        private void ExportMiniGraphCsv()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Index,ServedCylinder");
                for (int i = 0; i < servedSequence.Count; i++) sb.AppendLine($"{i},{servedSequence[i]}");

                var dlg = new SaveFileDialog { Filter = "CSV files|*.csv", DefaultExt = "csv" };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(dlg.FileName, sb.ToString());
                }
            }
            catch { }
        }

        private void ExportMiniGraphPng()
        {
            try
            {
                using var bmp = new Bitmap(Width, Height);
                DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                var dlg = new SaveFileDialog { Filter = "PNG Image|*.png", DefaultExt = "png" };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        private void PnlMiniGraph_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                miniCtxMenu.Show(this, e.Location);
            }
        }

        private void PnlMiniGraph_MouseMove(object? sender, MouseEventArgs e)
        {
            // only redraw hover if there is anything to hit-test
            var fullSeq = BuildFullSequenceWithHead();
            if (fullSeq == null || fullSeq.Count == 0) return;
            var idx = HitTestPoint(e.Location);
            if (idx >= 0 && idx < fullSeq.Count)
            {
                Invalidate();
            }
        }

        // Ensure servedSequence reflects progressive reveal based on diagramProgress and persistedFinalRequests.
        private void UpdateServedSequenceFromProgress()
        {
            if (persistedFinalRequests == null)
            {
                // No stable visit order; keep servedSequence as explicit append-only list (unchanged)
                return;
            }

            int total = persistedFinalRequests.Count;
            if (total == 0)
            {
                servedSequence = new List<int>();
                UpdateServedLabel();
                return;
            }

            float p = Math.Clamp(diagramProgress, 0f, 1f);
            float segmentsToDrawF = p * total;
            int fullSegments = (int)Math.Floor(segmentsToDrawF);

            if (p >= 1f - 1e-6f)
            {
                // fully revealed
                servedSequence = persistedFinalRequests.ToList();
            }
            else
            {
                // show only fully revealed served cylinders
                servedSequence = persistedFinalRequests.Take(fullSegments).ToList();
            }

            UpdateServedLabel();
        }

        private void ComputeVisualParams(Rectangle rect, out float markerRadius, out float penWidth, out int maxTickLabels)
        {
            float baseMarker = 4f;
            float basePen = 2f;

            // account for head + served points when computing vertical density
            int count = Math.Max(1, ((servedSequence?.Count ?? 0) + 1));
            int nAxis = Math.Max(1, axisTicks?.Count ?? 1);

            float verticalDensity = rect.Height / (float)Math.Max(1, count);
            float horizontalDensity = rect.Width / (float)Math.Max(1, nAxis);

            float scale = Math.Min(1f, Math.Min(verticalDensity / 14f, horizontalDensity / 10f));
            scale = Math.Max(0.35f, scale);

            markerRadius = Math.Max(1f, baseMarker * scale);
            penWidth = Math.Max(0.6f, basePen * scale);

            maxTickLabels = Math.Max(2, (int)Math.Ceiling((axisTicks?.Count ?? 1) / 8.0));
        }

        private int HitTestPoint(Point p)
        {
            var fullSeq = BuildFullSequenceWithHead();
            if (fullSeq == null || fullSeq.Count == 0) return -1;
            var rect = ClientRectangle;
            int left = 8, right = rect.Width - 8;
            int w = Math.Max(1, right - left);
            int n = axisTicks.Count;
            if (n <= 1) return -1;

            ComputeVisualParams(rect, out float markerRadius, out _, out _);

            float px = p.X;
            float bestDist = float.MaxValue;
            int best = -1;
            for (int i = 0; i < fullSeq.Count; i++)
            {
                int toAxisIdx = Math.Clamp(axisTicks.IndexOf(fullSeq[i]), 0, n - 1);
                float x = left + (toAxisIdx / (float)(n - 1)) * w;
                float d = Math.Abs(px - x);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            float threshold = Math.Max(8f, markerRadius * 2.5f);
            if (bestDist > threshold) return -1;
            return best;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var rect = ClientRectangle;
            using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.White, Color.FromArgb(245, 248, 250), 90f)) g.FillRectangle(lg, rect);

            if (axisTicks == null || axisTicks.Count == 0) BuildAxisTicks();

            int left = 8, right = rect.Width - 8, top = 26, bottom = rect.Height - 28;
            int w = Math.Max(1, right - left);
            int h = Math.Max(1, bottom - top);

            ComputeVisualParams(rect, out float markerRadius, out float penWidth, out int tickStep);

            using (var axisPen = new Pen(Color.FromArgb(200, 100, 100, 100), 1f))
            {
                g.DrawLine(axisPen, left, top - 12, right, top - 12);
            }

            if (axisTicks.Count > 0)
            {
                var sorted = axisTicks.Distinct().OrderBy(x => x).ToList();
                int n = sorted.Count;
                using var tickPen = new Pen(Color.FromArgb(200, 150, 150, 150));
                using var font = new Font(FontFamily.GenericSansSerif, Math.Max(7f, 8f * (markerRadius / 4f)));
                // draw every unique tick label (user requested)
                for (int i = 0; i < n; i++)
                {
                    float x = left + (i / (float)Math.Max(1, n - 1)) * w;
                    g.DrawLine(tickPen, x, top - 16, x, top - 8);
                    var s = sorted[i].ToString();
                    var sz = g.MeasureString(s, font);
                    g.DrawString(s, font, Brushes.DimGray, x - sz.Width / 2f, top - 26);
                }
            }

            // If we have a final planned visit order we still use the progressive draw helper.
            if (persistedFinalRequests != null && persistedFinalRequests.Count > 0)
            {
                DrawPathProgressive(g, rect, left, top, w, h, markerRadius, penWidth);
            }
            else
            {
                // Build full sequence starting at head then appended served points.
                var seq = BuildFullSequenceWithHead();
                if (seq != null && seq.Count > 0 && axisTicks.Count > 0)
                {
                    var sorted = axisTicks.Distinct().OrderBy(x => x).ToList();
                    int nAxis = sorted.Count;
                    var points = new List<PointF>(seq.Count);
                    for (int i = 0; i < seq.Count; i++)
                    {
                        int axisIdx = Math.Clamp(sorted.IndexOf(seq[i]), 0, nAxis - 1);
                        float x = left + (axisIdx / (float)Math.Max(1, nAxis - 1)) * w;
                        float y = top + (i / (float)Math.Max(1, seq.Count - 1)) * h;
                        points.Add(new PointF(x, y));
                    }

                    // draw subtle shadow for connections
                    if (points.Count > 1)
                    {
                        using (var shadowPen = new Pen(Color.FromArgb(40, 0, 0, 0), penWidth + 2f))
                        {
                            for (int i = 0; i < points.Count - 1; i++)
                                g.DrawLine(shadowPen, points[i].X + 2, points[i].Y + 2, points[i + 1].X + 2, points[i + 1].Y + 2);
                        }
                    }

                    // draw actual path for existing segments (head -> first served, first->second, ...).
                    using var pen = new Pen(Color.FromArgb(200, 34, 139, 34), penWidth);
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Only draw segments that actually exist (i.e., between known points: head + served items)
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        var a = points[i];
                        var b = points[i + 1];
                        g.DrawLine(pen, a, b);
                        float ax = a.X + (b.X - a.X) * 0.5f;
                        float ay = a.Y + (b.Y - a.Y) * 0.5f;
                        DrawArrowhead(g, ax, ay, (float)Math.Atan2(b.Y - a.Y, b.X - a.X), markerRadius);
                    }

                    // draw markers: head highlighted, served points normal
                    using var fillServed = new SolidBrush(Color.FromArgb(220, 34, 139, 34));
                    using var fillHead = new SolidBrush(Color.FromArgb(220, 30, 144, 255));
                    using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));

                    for (int i = 0; i < points.Count; i++)
                    {
                        var pnt = points[i];
                        // shadow
                        g.FillEllipse(shadowBrush, pnt.X - markerRadius + 2, pnt.Y - markerRadius + 2, markerRadius * 2, markerRadius * 2);

                        // head at index 0
                        if (i == 0)
                        {
                            g.FillEllipse(fillHead, pnt.X - markerRadius, pnt.Y - markerRadius, markerRadius * 2, markerRadius * 2);
                        }
                        else
                        {
                            g.FillEllipse(fillServed, pnt.X - markerRadius, pnt.Y - markerRadius, markerRadius * 2, markerRadius * 2);
                        }
                    }
                }
                else
                {
                    // even if no served points, draw the head marker alone (helps orientation)
                    if (axisTicks.Count > 0)
                    {
                        var sorted = axisTicks.Distinct().OrderBy(x => x).ToList();
                        int nAxis = sorted.Count;
                        var axisIdx = Math.Clamp(sorted.IndexOf(diagramHeadPosition), 0, Math.Max(0, nAxis - 1));
                        float x = left + (axisIdx / (float)Math.Max(1, nAxis - 1)) * w;
                        float y = top;
                        using var fillHead = new SolidBrush(Color.FromArgb(220, 30, 144, 255));
                        using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));
                        g.FillEllipse(shadowBrush, x - markerRadius + 2, y - markerRadius + 2, markerRadius * 2, markerRadius * 2);
                        g.FillEllipse(fillHead, x - markerRadius, y - markerRadius, markerRadius * 2, markerRadius * 2);
                    }
                }
            }

            UpdateServedLabel();
            using var f2 = new Font(FontFamily.GenericSansSerif, Math.Max(7f, 9f * (markerRadius / 4f)), FontStyle.Bold);
            var labelText = ServedLabel?.Text ?? $"Served: {servedSequence.Count}";
            g.DrawString(labelText, f2, Brushes.DimGray, new PointF(6, rect.Height - 18));
        }

        private void DrawPathProgressive(Graphics g, Rectangle rect, int left, int top, int w, int h, float markerRadius, float penWidth)
        {
            if (persistedFinalRequests == null || persistedFinalRequests.Count == 0) return;

            var seq = new List<int> { diagramHeadPosition };
            seq.AddRange(persistedFinalRequests);

            int totalSegments = Math.Max(1, seq.Count - 1);
            float totalProgress = Math.Clamp(diagramProgress, 0f, 1f);
            float segmentsToDrawF = totalProgress * totalSegments;
            int fullSegments = (int)Math.Floor(segmentsToDrawF);
            float partial = segmentsToDrawF - fullSegments;

            var sorted = axisTicks.Distinct().OrderBy(x => x).ToList();
            int nAxis = sorted.Count;

            var pts = new List<PointF>(seq.Count);
            for (int i = 0; i < seq.Count; i++)
            {
                int axisIdx = Math.Clamp(sorted.IndexOf(seq[i]), 0, Math.Max(1, nAxis) - 1);
                float x = left + (axisIdx / (float)Math.Max(1, nAxis - 1)) * w;
                float y = top + (i / (float)Math.Max(1, seq.Count - 1)) * h;
                pts.Add(new PointF(x, y));
            }

            using var pen = new Pen(Color.FromArgb(220, 34, 139, 34), penWidth);
            pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // draw full segments
            for (int i = 0; i < fullSegments && i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                g.DrawLine(pen, a, b);
                float ax = a.X + (b.X - a.X) * 0.5f;
                float ay = a.Y + (b.Y - a.Y) * 0.5f;
                DrawArrowhead(g, ax, ay, (float)Math.Atan2(b.Y - a.Y, b.X - a.X), markerRadius);
            }

            // draw partial segment if requested
            PointF? partialPoint = null;
            if (partial > 1e-6f && fullSegments < pts.Count - 1)
            {
                var a = pts[fullSegments];
                var b = pts[fullSegments + 1];
                var mid = new PointF(a.X + (b.X - a.X) * partial, a.Y + (b.Y - a.Y) * partial);
                g.DrawLine(pen, a, mid);
                float ax = a.X + (mid.X - a.X) * 0.5f;
                float ay = a.Y + (mid.Y - a.Y) * 0.5f;
                DrawArrowhead(g, ax, ay, (float)Math.Atan2(mid.Y - a.Y, mid.X - a.X), markerRadius * 0.8f);
                partialPoint = mid;
            }

            // draw markers for revealed points (include head and fullSegments reached points)
            using var fill = new SolidBrush(Color.FromArgb(220, 34, 139, 34));
            using var shadowBrush = new SolidBrush(Color.FromArgb(40, 0, 0, 0));

            int markersToShow = Math.Min(pts.Count, fullSegments + 1); // head + fullSegments
            for (int i = 0; i < markersToShow; i++)
            {
                var p = pts[i];
                // shadow
                g.FillEllipse(shadowBrush, p.X - markerRadius + 2, p.Y - markerRadius + 2, markerRadius * 2, markerRadius * 2);
                // marker
                g.FillEllipse(fill, p.X - markerRadius, p.Y - markerRadius, markerRadius * 2, markerRadius * 2);
            }

            // partial marker if present
            if (partialPoint.HasValue)
            {
                var p = partialPoint.Value;
                g.FillEllipse(shadowBrush, p.X - markerRadius + 2, p.Y - markerRadius + 2, markerRadius * 2 * 0.9f, markerRadius * 2 * 0.9f);
                g.FillEllipse(fill, p.X - markerRadius * 0.9f, p.Y - markerRadius * 0.9f, markerRadius * 2 * 0.9f, markerRadius * 2 * 0.9f);
            }
        }

        private void DrawArrowhead(Graphics g, float cx, float cy, float angle, float markerRadius)
        {
            float len = Math.Max(6f, markerRadius * 3f);
            float w = Math.Max(3f, markerRadius * 1.5f);
            var p1 = new PointF(cx, cy);
            var p2 = new PointF(cx - len * (float)Math.Cos(angle) + w * (float)Math.Sin(angle), cy - len * (float)Math.Sin(angle) - w * (float)Math.Cos(angle));
            var p3 = new PointF(cx - len * (float)Math.Cos(angle) - w * (float)Math.Sin(angle), cy - len * (float)Math.Sin(angle) + w * (float)Math.Cos(angle));
            using var brush = new SolidBrush(Color.Green);
            g.FillPolygon(brush, new PointF[] { p1, p2, p3 });
        }

        private void UpdateServedLabel()
        {
            if (ServedLabel != null)
                ServedLabel.Text = $"Served: {servedSequence.Count}";
        }

        private void BuildAxisTicks()
        {
            var set = new HashSet<int>();
            // always include head boundaries
            set.Add(0);
            set.Add(Math.Max(0, diagramDiskSize - 1));

            // include pending requests
            if (diagramRequests != null)
            {
                foreach (var r in diagramRequests) set.Add(Math.Clamp(r, 0, Math.Max(0, diagramDiskSize - 1)));
            }

            // include any persisted final visit order
            if (persistedFinalRequests != null)
            {
                foreach (var r in persistedFinalRequests) set.Add(Math.Clamp(r, 0, Math.Max(0, diagramDiskSize - 1)));
            }

            // include served sequence so markers align with axis even as dots are appended
            if (servedSequence != null)
            {
                foreach (var r in servedSequence) set.Add(Math.Clamp(r, 0, Math.Max(0, diagramDiskSize - 1)));
            }

            // include current head position explicitly
            set.Add(Math.Clamp(diagramHeadPosition, 0, Math.Max(0, diagramDiskSize - 1)));

            axisTicks = set.OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Ensure the X-axis (axis ticks) is initialized for the provided pending set, head and diskSize.
        /// This method is idempotent and will only rebuild axis/diagram data when something actually changed.
        /// It does not clear or modify the progressive servedSequence (NotifyServed controls that).
        /// </summary>
        public void EnsureAxis(IEnumerable<int>? pending, int headPosition, int diskSize = 200)
        {
            var incoming = (pending ?? Enumerable.Empty<int>()).ToList();
            var snap = string.Join(",", incoming.OrderBy(x => x));
            if (string.Equals(lastPendingSnapshot, snap, StringComparison.Ordinal)
                && lastHeadPosition == headPosition
                && lastDiskSize == diskSize)
            {
                return; // nothing changed
            }

            diagramRequests = incoming;
            diagramHeadPosition = Math.Clamp(headPosition, 0, Math.Max(1, diskSize) - 1);
            diagramDiskSize = Math.Max(1, diskSize);

            BuildAxisTicks();

            lastPendingSnapshot = snap;
            lastHeadPosition = diagramHeadPosition;
            lastDiskSize = diagramDiskSize;

            UpdateOverrideDiagramText();
            Invalidate();
        }

        public void SetDiagramData(IEnumerable<int>? requests, int headPosition, int diskSize = 200, bool preservePersistedFinalRequests = false)
        {
            var incoming = (requests ?? Enumerable.Empty<int>()).ToList();

            if (incoming.Count > 0)
            {
                persistedDiagramText = null;
                if (!preservePersistedFinalRequests) persistedFinalRequests = null;
            }

            diagramRequests = incoming;
            diagramHeadPosition = Math.Clamp(headPosition, 0, Math.Max(1, diskSize) - 1);
            diagramDiskSize = Math.Max(1, diskSize);
            diagramProgress = Math.Clamp(diagramProgress, 0f, 1f);
            try { diagramAnimCts?.Cancel(); } catch { }

            BuildAxisTicks();

            // If a persisted final visit order exists use it for servedSequence only based
            // on diagramProgress. Otherwise we keep servedSequence as the explicit
            // append-only served list (so the path only grows when callers append).
            if (persistedFinalRequests != null)
            {
                if (diagramProgress >= 1f)
                    servedSequence = persistedFinalRequests.ToList();
                else
                    servedSequence = new List<int>();
            }
            else
            {
                // do not auto-populate servedSequence from pending requests; rendered path
                // should grow only when callers append served items.
                servedSequence = servedSequence ?? new List<int>();
            }

            UpdateServedLabel();
            Invalidate();

            UpdateOverrideDiagramText();
        }

        public void SetFinalVisitOrder(IEnumerable<int> visitOrder)
        {
            persistedFinalRequests = (visitOrder ?? Enumerable.Empty<int>()).ToList();
            BuildAxisTicks();
            if (diagramProgress >= 1f)
                servedSequence = persistedFinalRequests.ToList();
            UpdateServedLabel();
            Invalidate();

            UpdateOverrideDiagramText();
        }

        /// <summary>
        /// Append a served cylinder to the progressive path. Call this when a request is actually served.
        /// The control will draw a new dot and connect it to the previous point.
        /// </summary>
        public void NotifyServed(int cylinder)
        {
            servedSequence.Add(cylinder);
            // rebuild axis to ensure the new served value aligns on X axis labels
            BuildAxisTicks();
            UpdateServedLabel();
            Invalidate();
        }

        /// <summary>
        /// Clear the progressive served sequence (does not clear persisted final visit order).
        /// </summary>
        public void ClearServedSequence()
        {
            servedSequence.Clear();
            // rebuild axis because servedSequence contributed ticks
            BuildAxisTicks();
            UpdateServedLabel();
            Invalidate();
        }

        private static string BuildCumulativeFormulas(IList<int> seq)
        {
            if (seq == null || seq.Count < 2) return string.Empty;

            var diffs = new List<int>(seq.Count - 1);
            long total = 0;
            for (int i = 1; i < seq.Count; i++)
            {
                int d = Math.Abs(seq[i - 1] - seq[i]);
                diffs.Add(d);
                total += d;
            }

            var sb = new StringBuilder();
            for (int inst = 0; inst < diffs.Count; inst++)
            {
                sb.Append($"{inst + 1}. ");
                long instanceSum = 0;
                for (int j = 0; j <= inst; j++)
                {
                    int a = seq[j];
                    int b = seq[j + 1];
                    int diff = diffs[j];
                    instanceSum += diff;

                    sb.Append($"|{a}-{b}|");
                    if (j < inst) sb.Append(" + ");
                }

                sb.Append($" = {instanceSum}");
                if (inst < diffs.Count - 1) sb.AppendLine();
            }

            if (diffs.Count > 0)
            {
                sb.AppendLine();
                sb.Append($"Total = {total}");
            }

            return sb.ToString();
        }

        private void UpdateOverrideDiagramText()
        {
            if (FormulaBox == null) return;

            if ((diagramRequests == null || diagramRequests.Count == 0) && !string.IsNullOrEmpty(persistedDiagramText))
            {
                FormulaBox.Text = persistedDiagramText!;
                return;
            }

            if (diagramRequests == null || diagramRequests.Count == 0)
            {
                FormulaBox.Text = "No pending requests";
                return;
            }

            var effectiveRequests = (diagramProgress >= 1f - 1e-6f && persistedFinalRequests != null)
                ? persistedFinalRequests
                : diagramRequests;

            var seq = new List<int> { diagramHeadPosition };
            seq.AddRange(effectiveRequests);

            var sb = new StringBuilder();
            var full = BuildCumulativeFormulas(seq);
            sb.Append(full);

            persistedDiagramText = sb.ToString();

            Action updateAction = () =>
            {
                try
                {
                    FormulaBox.SuspendLayout();
                    FormulaBox.Text = sb.ToString();
                    FormulaBox.SelectionStart = 0;
                    FormulaBox.SelectionLength = 0;
                    FormulaBox.ScrollToCaret();
                }
                finally
                {
                    FormulaBox.ResumeLayout();
                }
            };

            if (FormulaBox.InvokeRequired)
                FormulaBox.Invoke(updateAction);
            else
                updateAction();
        }

        public void SetDiagramProgress(float progress)
        {
            diagramProgress = Math.Clamp(progress, 0f, 1f);
            // update servedSequence so hover/export reflect currently revealed points
            UpdateServedSequenceFromProgress();
            Invalidate();
        }

        public async Task AnimateVisitOrderAsync(IEnumerable<int> visitOrder, int startHead, TimeSpan perSegment, CancellationToken token = default)
        {
            diagramAnimCts?.Cancel();
            diagramAnimCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = diagramAnimCts.Token;

            var reqs = (visitOrder ?? Enumerable.Empty<int>()).ToList();
            persistedFinalRequests = reqs.ToList();
            SetDiagramData(reqs, startHead, diagramDiskSize, preservePersistedFinalRequests: true);

            int segments = Math.Max(0, reqs.Count);
            if (segments == 0)
            {
                SetDiagramProgress(1f);
                return;
            }

            try
            {
                for (int s = 0; s < segments; s++)
                {
                    ct.ThrowIfCancellationRequested();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.Elapsed < perSegment)
                    {
                        ct.ThrowIfCancellationRequested();
                        float frac = (float)(sw.Elapsed.TotalMilliseconds / Math.Max(1.0, perSegment.TotalMilliseconds));
                        frac = Math.Clamp(frac, 0f, 1f);
                        float progress = ((float)s + frac) / (float)segments;
                        SetDiagramProgress(progress);
                        await Task.Delay(16, ct);
                    }

                    SetDiagramProgress(((float)(s + 1)) / (float)segments);
                    await Task.Delay(40, ct);
                }

                SetDiagramProgress(1f);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (diagramAnimCts != null && diagramAnimCts.IsCancellationRequested)
                {
                    diagramAnimCts.Dispose();
                    diagramAnimCts = null;
                }
            }
        }

        public void StopDiagramAnimation()
        {
            try { diagramAnimCts?.Cancel(); } catch { }
            diagramAnimCts?.Dispose();
            diagramAnimCts = null;
        }

        public void ClearPersistedDiagram()
        {
            persistedDiagramText = null;
            persistedFinalRequests = null;
            UpdateOverrideDiagramText();
            servedSequence.Clear();
            axisTicks.Clear();
            Invalidate();
        }

        // helper used by external callers when wanting a quick axis list
        public List<int> BuildAxisListFromState(int diskSize, IEnumerable<int> pending)
        {
            var set = new HashSet<int>();
            set.Add(0);
            set.Add(Math.Max(0, diskSize - 1));
            foreach (var r in pending) set.Add(Math.Clamp(r, 0, Math.Max(0, diskSize - 1)));
            return set.OrderBy(x => x).ToList();
        }

        // Build a sequence that always starts with head and then the explicit servedSequence.
        private List<int> BuildFullSequenceWithHead()
        {
            var seq = new List<int> { diagramHeadPosition };
            if (servedSequence != null && servedSequence.Count > 0) seq.AddRange(servedSequence);
            return seq;
        }
    }
}
