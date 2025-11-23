using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LibraryOFBabel.Simulation;
using LibraryOFBabel.Simulation.Algorithms;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.IO;

namespace LibraryOFBabel.ControlPanel
{
    public partial class StasticsAndInfo : UserControl
    {
        // expose a panel reference so Form1 can inject/manage it if present in the designer
        public UpcomingRequestsPanel? UpcomingPanel { get; set; }

        // external mini-graph host (moved to Form1)
        public Panel? MiniGraphPanel { get; private set; }
        public Label? MiniServedLabel { get; private set; }

        // diagram data for pnlOverrideDiagram (now a RichTextBox)
        private List<int> diagramRequests = new List<int>();
        private int diagramHeadPosition = 0;
        private int diagramDiskSize = 200;

        // progress 0..1 used to reveal the diagram gradually (1 = fully drawn)
        private float diagramProgress = 1f;

        // animation cancellation token source (keeps one active animation at a time)
        private CancellationTokenSource? diagramAnimCts;

        // persist final diagram text until explicit reset
        private string? persistedDiagramText;

        // store the full visit order used by an animation so the final persisted formula
        // always reflects the complete visit order (prevents later updates to diagramRequests
        // from truncating the final formula)
        private List<int>? persistedFinalRequests;

        public StasticsAndInfo()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Assign the external mini-graph host controls (moved to the form) and wire necessary events.
        /// Safe to call multiple times; previous event handlers are unsubscribed before wiring.
        /// </summary>
        public void SetMiniGraphHost(Panel panel, Label? servedLabel)
        {
            // detach old handlers if we had a non-MiniGraphControl panel
            if (MiniGraphPanel != null && !(MiniGraphPanel is MiniGraphControl))
            {
                try
                {
                    MiniGraphPanel.Paint -= PnlMiniGraph_Paint;
                    MiniGraphPanel.MouseMove -= PnlMiniGraph_MouseMove;
                    MiniGraphPanel.MouseClick -= PnlMiniGraph_MouseClick;
                }
                catch { }
            }

            MiniGraphPanel = panel ?? throw new ArgumentNullException(nameof(panel));
            MiniServedLabel = servedLabel; // may be null if caller removed the label

            // If host is MiniGraphControl, configure it directly
            if (panel is MiniGraphControl mgc)
            {
                mgc.FormulaBox = this.txtShowFormula;
                mgc.ServedLabel = servedLabel;
                mgc.Invalidate();
            }
            else
            {
                // wire events to fallback Panel
                MiniGraphPanel.Paint += PnlMiniGraph_Paint;
                MiniGraphPanel.MouseMove += PnlMiniGraph_MouseMove;
                MiniGraphPanel.MouseClick += PnlMiniGraph_MouseClick;
                MiniGraphPanel.BackColor = Color.Black;
            }
        }

        // Fallback handlers kept for compatibility when a plain Panel is used as host
        private void PnlMiniGraph_MouseClick(object? sender, MouseEventArgs e)
        {
            // show context menu if we have it in the MiniGraphControl scenario; otherwise no-op
            if (e.Button == MouseButtons.Right && MiniGraphPanel != null)
            {
                // If the panel is MiniGraphControl, it will manage its own menu; nothing to do here
            }
        }

        private void PnlMiniGraph_MouseMove(object? sender, MouseEventArgs e)
        {
            // noop in fallback; MiniGraphControl handles hover
        }

        private void PnlMiniGraph_Paint(object? sender, PaintEventArgs e)
        {
            // fallback simple text if MiniGraphControl is not used
            var g = e.Graphics;
            var rect = MiniGraphPanel?.ClientRectangle ?? Rectangle.Empty;
            using (var f = new Font(FontFamily.GenericSansSerif, 9f))
            {
                g.DrawString("Mini graph host not available. Use MiniGraphControl for full visuals.", f, Brushes.Gray, new PointF(6, 6));
            }
        }

        // The previous implementation included rendering logic here. That code has been moved
        // into MiniGraphControl to keep a single-responsibility control and avoid duplication.

        // The textual diagram builder and formula logic remain here.

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
            if (txtShowFormula == null) return; // designer may not have it

            // If no requests but we have a persisted final formula, show it
            if ((diagramRequests == null || diagramRequests.Count == 0) && !string.IsNullOrEmpty(persistedDiagramText))
            {
                txtShowFormula.Text = persistedDiagramText!;
                return;
            }

            if (diagramRequests == null || diagramRequests.Count == 0)
            {
                txtShowFormula.Text = "No pending requests";
                return;
            }

            // Choose which request list to use for rendering:
            var effectiveRequests = (diagramProgress >= 1f - 1e-6f && persistedFinalRequests != null)
                ? persistedFinalRequests
                : diagramRequests;

            // sequence from head then arrival/visit order
            var seq = new List<int> { diagramHeadPosition };
            seq.AddRange(effectiveRequests);

            var sb = new StringBuilder();

            // Always show the complete per-instance formulas
            var full = BuildCumulativeFormulas(seq);
            sb.Append(full);

            // persist final diagram until Reset clears it
            persistedDiagramText = sb.ToString();

            // assign to RichTextBox text (preserve caret at start)
            Action updateAction = () =>
            {
                try
                {
                    txtShowFormula.SuspendLayout();
                    txtShowFormula.Text = sb.ToString();
                    txtShowFormula.SelectionStart = 0;
                    txtShowFormula.SelectionLength = 0;
                    txtShowFormula.ScrollToCaret();
                }
                finally
                {
                    txtShowFormula.ResumeLayout();
                }
            };

            if (txtShowFormula.InvokeRequired)
            {
                txtShowFormula.Invoke(updateAction);
            }
            else
            {
                updateAction();
            }
        }

        public void SetDiagramProgress(float progress)
        {
            diagramProgress = Math.Clamp(progress, 0f, 1f);
            if (MiniGraphPanel is MiniGraphControl mgc) mgc.SetDiagramProgress(progress);
            else MiniGraphPanel?.Invalidate();
        }

        public async Task AnimateVisitOrderAsync(IEnumerable<int> visitOrder, int startHead, TimeSpan perSegment, CancellationToken token = default)
        {
            // if hosted control exists, forward to it
            if (MiniGraphPanel is MiniGraphControl mgc)
            {
                await mgc.AnimateVisitOrderAsync(visitOrder, startHead, perSegment, token);
                return;
            }

            // fallback: simple animation that updates textual progress
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
                // leave diagram state as-is
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
            try
            {
                diagramAnimCts?.Cancel();
            }
            catch { /* ignore */ }
            diagramAnimCts?.Dispose();
            diagramAnimCts = null;

            if (MiniGraphPanel is MiniGraphControl mgc) mgc.StopDiagramAnimation();
        }

        public void ClearPersistedDiagram()
        {
            persistedDiagramText = null;
            persistedFinalRequests = null;
            UpdateOverrideDiagramText();
            if (MiniGraphPanel is MiniGraphControl mgc) mgc.ClearPersistedDiagram();
        }

        public void UpdateHeadPosition(int pos)
        {
            txtBoxCurrentHeadPos.Text = pos.ToString();
        }

        public void UpdateTotalSeekTime(long totalSeekDistance)
        {
            txtBoxTotalSeekTime.Text = totalSeekDistance.ToString();
        }

        public void UpdateAverageSeekTime(double average)
        {
            txtBoxAverageSeekTime.Text = average.ToString("F2");
        }

        public void PopulateFromState(SimulationState state, SimulationStats stats)
        {
            if (state == null || stats == null) return;

            // common fields
            txtBoxTotalSeekTime.Text = stats.TotalSeekDistance.ToString();
            txtBoxAverageSeekTime.Text = stats.AverageSeek.ToString("F2");
            txtBoxNumofReqServed.Text = stats.RequestsServed.ToString();
            txtBoxCurrentHeadPos.Text = state.HeadPosition.ToString();
            txtBoxCurrentDirection.Text = (state.Direction >= 0) ? "Right/Increasing" : "Left/Decreasing";
            txtBoxDistanceMoved.Text = stats.TotalSeekDistance.ToString();
            txtBoxTimeElapsed.Text = string.Empty;

            // algorithm-specific predictions
            switch (state.Algorithm)
            {
                case SchedulingAlgorithm.FCFS:
                {
                    var simf = FCFS.Simulate(state);
                    txtBoxTotalSeekTime.Text = (stats.TotalSeekDistance + simf.TotalDistance).ToString();
                    txtBoxAverageSeekTime.Text = ((stats.RequestsServed + simf.VisitOrder.Count) == 0 ? 0 : (double)(stats.TotalSeekDistance + simf.TotalDistance) / (stats.RequestsServed + simf.VisitOrder.Count)).ToString("F2");
                    SetVisibilityForAlgorithm(true);
                    SetFinalVisitOrder(simf.VisitOrder);
                    break;
                }

                case SchedulingAlgorithm.SSTF:
                {
                    var sims = SSTF.Simulate(state);
                    txtBoxTotalSeekTime.Text = (stats.TotalSeekDistance + sims.TotalDistance).ToString();
                    txtBoxAverageSeekTime.Text = ((stats.RequestsServed + sims.VisitOrder.Count) == 0 ? 0 : (double)(stats.TotalSeekDistance + sims.TotalDistance) / (stats.RequestsServed + sims.VisitOrder.Count)).ToString("F2");
                    SetVisibilityForAlgorithm(false);
                    SetFinalVisitOrder(sims.VisitOrder);
                    break;
                }

                case SchedulingAlgorithm.SCAN:
                {
                    var simscan = SCAN.Simulate(state, circular: false);
                    txtBoxTotalSeekTime.Text = (stats.TotalSeekDistance + simscan.TotalDistance).ToString();
                    txtBoxAverageSeekTime.Text = ((stats.RequestsServed + simscan.VisitOrder.Count) == 0 ? 0 : (double)(stats.TotalSeekDistance + simscan.TotalDistance) / (stats.RequestsServed + simscan.VisitOrder.Count)).ToString("F2");
                    SetVisibilityForAlgorithm(false);
                    SetFinalVisitOrder(simscan.VisitOrder);
                    break;
                }

                case SchedulingAlgorithm.CSCAN:
                {
                    var simcscan = SCAN.Simulate(state, circular: true);
                    txtBoxTotalSeekTime.Text = (stats.TotalSeekDistance + simcscan.TotalDistance).ToString();
                    txtBoxAverageSeekTime.Text = ((stats.RequestsServed + simcscan.VisitOrder.Count) == 0 ? 0 : (double)(stats.TotalSeekDistance + simcscan.TotalDistance) / (stats.RequestsServed + simcscan.VisitOrder.Count)).ToString("F2");
                    SetVisibilityForAlgorithm(false);
                    SetFinalVisitOrder(simcscan.VisitOrder);
                    break;
                }

                case SchedulingAlgorithm.LOOK:
                {
                    var simlook = LOOK.Simulate(state, circular: false);
                    txtBoxTotalSeekTime.Text = (stats.TotalSeekDistance + simlook.TotalDistance).ToString();
                    txtBoxAverageSeekTime.Text = ((stats.RequestsServed + simlook.VisitOrder.Count) == 0 ? 0 : (double)(stats.TotalSeekDistance + simlook.TotalDistance) / (stats.RequestsServed + simlook.VisitOrder.Count)).ToString("F2");
                    SetVisibilityForAlgorithm(false);
                    SetFinalVisitOrder(simlook.VisitOrder);
                    break;
                }

                case SchedulingAlgorithm.CLOOK:
                {
                    var simclook = CLOOK.Simulate(state);
                    txtBoxTotalSeekTime.Text = (stats.TotalSeekDistance + simclook.TotalDistance).ToString();
                    txtBoxAverageSeekTime.Text = ((stats.RequestsServed + simclook.VisitOrder.Count) == 0 ? 0 : (double)(stats.TotalSeekDistance + simclook.TotalDistance) / (stats.RequestsServed + simclook.VisitOrder.Count)).ToString("F2");
                    SetVisibilityForAlgorithm(false);
                    SetFinalVisitOrder(simclook.VisitOrder);
                    break;
                }
            }

            // update upcoming panel
            UpdateUpcomingFromState(state);
        }

        private void SetVisibilityForAlgorithm(bool isFcfs)
        {
            txtBoxTotalSeekTime.Visible = true;
            label2.Visible = true;
            txtBoxAverageSeekTime.Visible = true;
            label3.Visible = true;
            txtBoxNumofReqServed.Visible = true;
            label4.Visible = true;
            txtBoxCurrentHeadPos.Visible = true;
            label5.Visible = true;
            txtBoxCurrentDirection.Visible = true;
            label6.Visible = true;
            txtBoxDistanceMoved.Visible = true;
            label7.Visible = true;
            txtBoxTimeElapsed.Visible = false;
            label8.Visible = false;
        }

        private void StasticsAndInfo_Load(object? sender, EventArgs e)
        {
        }

        public void UpdateUpcomingFromState(SimulationState state)
        {
            if (UpcomingPanel == null) return;
            if (state == null) return;

            var pending = state.PendingRequests.ToList();
            int? current = null;
            if (pending.Any())
            {
                current = state.Algorithm switch
                {
                    SchedulingAlgorithm.FCFS => pending.First(),
                    SchedulingAlgorithm.SSTF => pending.OrderBy(r => Math.Abs(r - state.HeadPosition)).First(),
                    SchedulingAlgorithm.SCAN => DetermineScanUpcoming(state),
                    SchedulingAlgorithm.CSCAN => DetermineCScanUpcoming(state),
                    SchedulingAlgorithm.LOOK => DetermineLookUpcoming(state),
                    SchedulingAlgorithm.CLOOK => DetermineCLookUpcoming(state),
                    _ => pending.First()
                };
            }

            var upcoming = new List<int>(pending);
            if (current.HasValue) upcoming.Remove(current.Value);

            UpcomingPanel.SetQueue(upcoming, current, lastServed: null);

            // forward axis/preview to hosted MiniGraphControl if present
            // Use EnsureAxis so the mini control decides when to rebuild axis/spacing; avoid resetting every tick.
            if (MiniGraphPanel is MiniGraphControl mgc)
            {
                mgc.EnsureAxis(pending, state.HeadPosition, state.DiskSize);
                // do NOT call mgc.SetDiagramData here on every tick; Observe predicted sequences only when user requests animation
            }
        }

        private int DetermineScanUpcoming(SimulationState state)
        {
            var pending = state.PendingRequests;
            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;
            int disk = Math.Max(1, state.DiskSize);

            if (dir >= 0)
            {
                var ahead = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                if (ahead.Any()) return ahead.First();
                // if none ahead, SCAN intends to go to end first
                return disk - 1;
            }
            else
            {
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                if (behind.Any()) return behind.First();
                return 0;
            }
        }

        private int DetermineCScanUpcoming(SimulationState state)
        {
            var pending = state.PendingRequests;
            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;

            if (dir >= 0)
            {
                var ahead = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                if (ahead.Any()) return ahead.First();
                // otherwise jump to smallest pending
                return pending.OrderBy(r => r).First();
            }
            else
            {
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                if (behind.Any()) return behind.First();
                return pending.OrderByDescending(r => r).First();
            }
        }

        private int DetermineLookUpcoming(SimulationState state)
        {
            var pending = state.PendingRequests;
            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;

            if (dir >= 0)
            {
                var ahead = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                if (ahead.Any()) return ahead.First();
                // reverse direction and pick farthest behind
                var behind = pending.Where(r => r < head).OrderByDescending(r => r).ToList();
                if (behind.Any()) return behind.First();
                return pending.OrderBy(r => r).First();
            }
            else
            {
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                if (behind.Any()) return behind.First();
                var ahead = pending.Where(r => r > head).OrderBy(r => r).ToList();
                if (ahead.Any()) return ahead.First();
                return pending.OrderByDescending(r => r).First();
            }
        }

        private int DetermineCLookUpcoming(SimulationState state)
        {
            var pending = state.PendingRequests;
            int head = state.HeadPosition;
            int dir = state.Direction >= 0 ? 1 : -1;

            if (dir >= 0)
            {
                var ahead = pending.Where(r => r >= head).OrderBy(r => r).ToList();
                if (ahead.Any()) return ahead.First();
                return pending.OrderBy(r => r).First();
            }
            else
            {
                var behind = pending.Where(r => r <= head).OrderByDescending(r => r).ToList();
                if (behind.Any()) return behind.First();
                return pending.OrderByDescending(r => r).First();
            }
        }

        private List<int> BuildAxisListFromState(SimulationState state)
        {
            var set = new HashSet<int>();
            set.Add(0);
            set.Add(Math.Max(0, state.DiskSize - 1));
            foreach (var r in state.PendingRequests) set.Add(Math.Clamp(r, 0, Math.Max(0, state.DiskSize - 1)));
            return set.OrderBy(x => x).ToList();
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

            // forward to hosted control if present
            if (MiniGraphPanel is MiniGraphControl mgc)
            {
                mgc.SetDiagramData(requests, headPosition, diskSize, preservePersistedFinalRequests);
            }

            UpdateOverrideDiagramText();
        }

        public void SetFinalVisitOrder(IEnumerable<int> visitOrder)
        {
            persistedFinalRequests = (visitOrder ?? Enumerable.Empty<int>()).ToList();
            if (MiniGraphPanel is MiniGraphControl mgc)
            {
                mgc.SetFinalVisitOrder(visitOrder);
            }
        }
    }
}
