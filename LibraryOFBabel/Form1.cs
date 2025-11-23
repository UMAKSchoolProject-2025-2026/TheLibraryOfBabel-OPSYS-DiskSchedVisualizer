using System;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using LibraryOFBabel.UI;
using LibraryOFBabel.Visualization;
using LibraryOFBabel.ControlPanel;
using LibraryOFBabel.Simulation;
using System.Collections.Generic;

namespace LibraryOFBabel
{
    public partial class Form1 : Form
    {
        private RadialRenderer? renderer;
        private ControlManager? controlManager;
        private ToolTip? sharedToolTip;
        private SimulationEngine? engine;
        private System.Windows.Forms.Timer? simulationUpdateTimer;
        private Stopwatch? simulationStopwatch;
        private double lastSimulationTime = 0.0;
        private double simulationSpeedMultiplier = 1.0; // mapped from trackbar
        private double simulationAccumulator = 0.0;
        // base step rate (steps/sec) when multiplier == 1
        // Set so default behavior is ~1 node per 1.5s -> 1/1.5 = 0.6666667 steps/sec
        private const double StepsPerSecondBase = 0.6666666666667;

        // keep references to engine event handlers so we can unsubscribe on shutdown
        private EventHandler<SimulationState>? engineStateChangedHandler;
        private EventHandler<SimulationStats>? engineStatsChangedHandler;

        private int? plannedStepTarget = null; // planned per-step animation target

        public Form1()
        {
            InitializeComponent();
            InitializeVisualization();
            InitializeControlHost();
            InitializeSimulationEngine();
        }

        private void InitializeVisualization()
        {
            renderer = new RadialRenderer();

            // Enable diagnostics optionally
            renderer.EnableDiagnostics = false;

            // add renderer control to visualizer panel and send it to back so overlay panels stay visible
            var vis = renderer.GetVisualizationControl();
            vis.Dock = DockStyle.Fill;
            panelVisualizer.Controls.Add(vis);
            try { vis.SendToBack(); } catch { }

            // ensure the compact mini-graph and its label remain visible above the renderer
            try
            {
                pnlMiniGraph?.BringToFront();
            }
            catch { }

            renderer.Start();

            // single form-closing handler to dispose owned resources
            this.FormClosing += OnFormClosing;
        }

        private void InitializeControlHost()
        {
            // create a single shared tooltip to reuse across controls
            sharedToolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 100,
                ShowAlways = true
            };

            controlManager = new ControlManager(panelControlsHost, sharedToolTip);

            // instantiate your existing controls and register them with the manager
            var mode = new ModeControl();
            var stats = new StasticsAndInfo();

            controlManager.RegisterControl("mode", mode, takeOwnership: true);
            controlManager.RegisterControl("stats", stats, takeOwnership: true);

            // instantiate MiniGraphControl inside the designer panel and wire it to the stats control
            try
            {
                var mini = new MiniGraphControl()
                {
                    Dock = DockStyle.Fill
                };
                pnlMiniGraph.Controls.Clear();
                pnlMiniGraph.Controls.Add(mini);
                stats.SetMiniGraphHost(mini, null);
            }
            catch { }


            // ensure the stats diagram initially reflects the nudHeadStartPos value
            // (before the engine has emitted any state). This uses the ModeControl API
            // to read the numeric up-down value and shows a diagram column for it.
            stats.SetDiagramData(Enumerable.Empty<int>(), mode.HeadStartPosition, mode.DiskSize);
            // ensure static mini-graph shows full prediction (none yet) => fully revealed
            try
            {
                var statsInit = controlManager.GetInstance("stats") as StasticsAndInfo;
                statsInit?.SetDiagramProgress(1f);
            }
            catch { }

            // set initial pointer animation duration based on the mode speed control (if available)
            try
            {
                var tb = mode.Controls.OfType<TrackBar>().FirstOrDefault();
                var speedInit = tb?.Value ?? 5;
                if (renderer != null && tb != null)
                {
                    // compute mapped multiplier and duration using same logic as runtime
                    int min = tb.Minimum;
                    int max = tb.Maximum;
                    if (speedInit == min)
                    {
                        renderer.PointerAnimationEnabled = false;
                        simulationSpeedMultiplier = 0.0;
                    }
                    else
                    {
                        // map slider -> multiplier: right = fastest
                        const double MaxMultiplier = 20.0; // tunable
                        double f = (speedInit - min) / (double)Math.Max(1, (max - min));
                        simulationSpeedMultiplier = 1.0 + f * (MaxMultiplier - 1.0);
                        renderer.PointerAnimationEnabled = true;
                    }

                    // pointer animation duration derived from step timing (smaller duration = faster motion)
                    var dur = simulationSpeedMultiplier > 0.0
                        ? 1.0f / (float)(Math.Max(0.001, StepsPerSecondBase * simulationSpeedMultiplier))
                        : 1.0f;
                    renderer.PointerAnimationDuration = dur;
                }
            }
            catch { }

            // wire menu items to swap controls (this does not affect the SKGLControl)
            modeControlToolStripMenuItem.Click += (s, e) => controlManager.Show("mode");
            statsAndInfoToolStripMenuItem.Click += (s, e) => controlManager.Show("stats");

            // show default
            controlManager.Show("mode");

            // wire data events from mode control
            mode.AddRequests += (s, list) =>
            {
                if (engine == null) return;
                var arr = list.ToList();
                engine.EnqueueRequests(arr);
                // update UI
                UpdateStatsInUI();
                // update the diagram with the new pending requests
                var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
                statsCtrl?.SetDiagramData(engine.CurrentState.PendingRequests, engine.CurrentState.HeadPosition, engine.CurrentState.DiskSize);
                statsCtrl?.SetDiagramProgress(1f);
                // inform user
                MessageBox.Show(this, $"Added {arr.Count} request(s)", "Requests Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            mode.GenerateRandomRequests += (s, count) =>
            {
                if (engine == null) return;
                var rand = new Random();
                var disk = Math.Max(1, engine.CurrentState.DiskSize);
                var gen = Enumerable.Range(0, count).Select(_ => rand.Next(0, disk)).ToList();
                // show generated list in the request textbox and prompt user to click Add Request
                mode.SetRequestText(gen);
                MessageBox.Show(this, $"Generated {gen.Count} random request(s). Review and click 'Add Request' to enqueue.", "Random Requests Generated", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            mode.ClearRequests += (s, e) =>
            {
                engine?.ClearRequests();
                UpdateStatsInUI();

                // also clear mini graph served dots if available
                try
                {
                    var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
                    if (statsCtrl?.MiniGraphPanel is MiniGraphControl mini)
                    {
                        mini.ClearServedSequence();
                    }
                }
                catch { }
            };

            mode.DiskSizeChanged += (s, newSize) =>
            {
                if (engine == null) return;
                // change engine disk size and reflect on renderer config
                engine.ChangeDiskSize(newSize);
                if (renderer != null)
                {
                    renderer.Config.NodeCount = newSize; // NodeCount == DiskSize mapping
                    renderer.Config.Recalculate();
                }
                UpdateStatsInUI();
            };

            mode.HeadStartPositionChanged += (s, pos) =>
            {
                if (engine == null) return;
                // set head position via Reset to ensure stats consistent
                engine.Reset(pos);
                UpdateStatsInUI();

                // clear mini graph served sequence on head reset
                try
                {
                    var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
                    if (statsCtrl?.MiniGraphPanel is MiniGraphControl mini)
                    {
                        mini.ClearServedSequence();
                        // also set diagram head position so head marker updates
                        statsCtrl.SetDiagramData(engine.CurrentState.PendingRequests, engine.CurrentState.HeadPosition, engine.CurrentState.DiskSize);
                        statsCtrl.SetDiagramProgress(1f);
                    }
                }
                catch { }
            };

            // Note: ModeControl.SpeedChanged is handled in InitializeSimulationEngine so we avoid duplicate handlers here.
        }

        private void InitializeSimulationEngine()
        {
            // Use the existing SimulationEngine constructor (no layerCount parameter)
            engine = new SimulationEngine(diskSize: 200, initialHead: 0, initialAlgorithm: SchedulingAlgorithm.FCFS);

            // attach engine to renderer so renderer can read state
            if (renderer != null) renderer.Engine = engine;

            if (controlManager is null) return;

            // Ensure mode control is visible to wire events
            controlManager.Show("mode");
            var modeCtrl = controlManager.CurrentControl as ModeControl;

            if (modeCtrl != null)
            {
                // initial speed multiplier from UI
                var tb = modeCtrl.Controls.OfType<TrackBar>().FirstOrDefault();
                if (tb != null)
                {
                    int min = tb.Minimum;
                    int max = tb.Maximum;
                    int initVal = tb.Value;
                    if (initVal == min)
                    {
                        simulationSpeedMultiplier = 0.0;
                        if (renderer != null) renderer.PointerAnimationEnabled = false;
                    }
                    else
                    {
                        const double MaxMultiplier = 20.0;
                        double f = (initVal - min) / (double)Math.Max(1, (max - min));
                        simulationSpeedMultiplier = 1.0 + f * (MaxMultiplier - 1.0);
                        if (renderer != null) renderer.PointerAnimationEnabled = true;
                    }
                }

                modeCtrl.PlayToggled += (s, playing) =>
                {
                    if (playing) StartSimulationUpdates();
                    else PauseSimulationUpdates();
                };

                modeCtrl.SpeedChanged += (s, speed) =>
                {
                    var tb2 = modeCtrl.Controls.OfType<TrackBar>().FirstOrDefault();
                    if (tb2 == null) return;
                    int min = tb2.Minimum;
                    int max = tb2.Maximum;

                    // when speed slider at minimum, disable stepping/animation
                    if (speed <= min)
                    {
                        simulationSpeedMultiplier = 0.0;
                        if (renderer != null) renderer.PointerAnimationEnabled = false;
                    }
                    else
                    {
                        // map slider -> multiplier consistently (right = fastest)
                        const double MaxMultiplier = 20.0;
                        double f = (speed - min) / (double)Math.Max(1, (max - min));
                        simulationSpeedMultiplier = 1.0 + f * (MaxMultiplier - 1.0);

                        // pointer animation enabled when slider not at minimum
                        if (renderer != null) renderer.PointerAnimationEnabled = true;
                    }

                    // update pointer animation duration to match step timing
                    if (renderer != null)
                    {
                        // avoid division by zero; if multiplier==0 use a default long duration but animation disabled
                        var dur = simulationSpeedMultiplier > 0.0
                            ? 1.0f / (float)(Math.Max(0.001, StepsPerSecondBase * simulationSpeedMultiplier))
                            : 1.0f;
                        renderer.PointerAnimationDuration = dur;
                    }
                };

                modeCtrl.AlgorithmChanged += (s, alg) =>
                {
                    engine?.ChangeAlgorithm(alg);
                };

                modeCtrl.StepRequested += (s, e) =>
                {
                    // step once immediately (schedules/serves according to engine semantics)
                    engine?.Step();
                    UpdateStatsInUI();
                };

                modeCtrl.ResetRequested += (s, e) =>
                {
                    engine?.Reset();
                    UpdateStatsInUI();

                    // clear persisted diagram in the stats control so the formula disappears on Reset
                    var statsCtrlLocal = controlManager.GetInstance("stats") as StasticsAndInfo;
                    statsCtrlLocal?.ClearPersistedDiagram();

                    // clear mini graph served sequence as well
                    try
                    {
                        if (statsCtrlLocal?.MiniGraphPanel is MiniGraphControl mini)
                            mini.ClearServedSequence();
                    }
                    catch { }
                };
            }

            // prepare simulation update timer and stopwatch
            simulationUpdateTimer = new System.Windows.Forms.Timer { Interval = 16 }; // nominal 60Hz
            simulationStopwatch = new Stopwatch();

            simulationUpdateTimer.Tick += (s, e) =>
            {
                if (engine == null || simulationStopwatch == null) return;

                var now = simulationStopwatch.Elapsed.TotalSeconds;
                var delta = now - lastSimulationTime;
                if (delta <= 0)
                {
                    lastSimulationTime = now;
                    return;
                }

                // accumulate real elapsed seconds (do NOT apply multiplier here)
                simulationAccumulator += delta;

                // clamp multiplier (0 -> paused stepping)
                double m = Math.Max(0.0, simulationSpeedMultiplier);

                // compute step interval (seconds per Step)
                double stepInterval = double.PositiveInfinity;
                if (m > 0.0)
                {
                    stepInterval = 1.0 / (StepsPerSecondBase * m);
                }

                // compute progress fraction toward the next step (0..1)
                float progress = 0f;
                if (!double.IsInfinity(stepInterval) && stepInterval > 0.0)
                {
                    progress = (float)Math.Clamp(simulationAccumulator / stepInterval, 0.0, 1.0);
                }

                // plan per-track animation for the upcoming step (only once per interval)
                try
                {
                    if (renderer != null && renderer.PointerAnimationEnabled && engine != null && !double.IsInfinity(stepInterval) && stepInterval > 0.0)
                    {
                        var st = engine.CurrentState;
                        int head = st.HeadPosition;
                        int? next = engine.NextRequest;
                        // schedule only when we are at the start of the interval (progress small) to avoid the case
                        // where we schedule and immediately execute the step in the same tick.
                        if (next.HasValue && next.Value != head && plannedStepTarget == null && progress < 0.999f)
                        {
                            // schedule animation along the full path to the chosen next request (visual only)
                            int fullTarget = Math.Clamp(next.Value, 0, st.DiskSize - 1);
                            renderer.AnimatePointerAlong(head, fullTarget, (float)stepInterval);
                            plannedStepTarget = fullTarget;
                        }
                    }
                }
                catch { }

                // if renderer present, update pointer visual progress between last and target
                if (renderer != null && renderer.PointerAnimationEnabled)
                {
                    renderer.UpdatePointerProgress(progress);
                }

                // guard: don't spin forever if accumulator huge
                int guard = 0;
                while (!double.IsInfinity(stepInterval) && simulationAccumulator >= stepInterval && guard < 100)
                {
                    // capture pending list before stepping
                    var pendingBefore = engine.CurrentState.PendingRequests.ToList();

                    // perform one simulation Step and detect whether a request was served
                    bool served = engine.Step();

                    // after a step executes, clear planned step target so next interval can be scheduled
                    plannedStepTarget = null;

                    // capture pending after step
                    var pendingAfter = engine.CurrentState.PendingRequests.ToList();

                    // if a request was served, try to determine which value was removed and notify upcoming panel
                    if (served)
                    {
                        int? servedValue = null;
                        if (pendingBefore.Count > pendingAfter.Count)
                        {
                            // compute multiset difference (handles duplicates)
                            var counts = new Dictionary<int, int>();
                            foreach (var v in pendingBefore)
                            {
                                if (!counts.TryGetValue(v, out var c)) c = 0;
                                counts[v] = c + 1;
                            }
                            foreach (var v in pendingAfter)
                            {
                                if (counts.TryGetValue(v, out var c2))
                                {
                                    if (c2 <= 1) counts.Remove(v);
                                    else counts[v] = c2 - 1;
                                }
                            }
                            // the remaining key(s) are the served values; pick one
                            foreach (var kv in counts)
                            {
                                if (kv.Value > 0) { servedValue = kv.Key; break; }
                            }
                        }

                        var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
                        var upcomingPanel = statsCtrl?.UpcomingPanel;
                        if (upcomingPanel != null && servedValue.HasValue)
                        {
                            upcomingPanel.NotifyServed(servedValue.Value, engine.CurrentState.PendingRequests);
                        }
                        else if (upcomingPanel != null && !servedValue.HasValue)
                        {
                            // fallback: signal with first-before (best-effort)
                            upcomingPanel.NotifyServed(pendingBefore.FirstOrDefault(), engine.CurrentState.PendingRequests);
                        }
                    }

                    simulationAccumulator -= stepInterval;
                    guard++;
                }

                // update stats UI if visible (no forced control switches)
                UpdateStatsInUI();

                lastSimulationTime = now;
            };

            // wire engine events to update UI
            engineStateChangedHandler = new EventHandler<SimulationState>((s, st) =>
            {
                // update diagram live when simulation state changes
                // var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
                // statsCtrl?.SetDiagramData(st.PendingRequests, st.HeadPosition, st.DiskSize);

                // keep ModeControl UI in sync so head-start numeric max follows disk size
                var modeInstance = controlManager.GetInstance("mode") as ModeControl;
                if (modeInstance != null)
                {
                    // update disk size shown in UI (will not re-raise DiskSizeChanged because SetDiskSize suppresses events)
                    modeInstance.SetDiskSize(st.DiskSize);

                    // keep head-start value in sync with engine head (optional: useful after Reset)
                    modeInstance.SetHeadStartPosition(st.HeadPosition);
                }
            });
            engine.StatisticsChanged += engineStatsChangedHandler = new EventHandler<SimulationStats>((s, st) => UpdateStatsInUI());
            engine.StateChanged += engineStateChangedHandler;

            // Ensure stats control has initial data and the mini-graph is rendered immediately
            try
            {
                UpdateStatsInUI();
                var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
                if (statsCtrl != null && engine != null)
                {
                    // ensure the designer panel hosts a MiniGraphControl and wire it
                    try
                    {
                        if (pnlMiniGraph.Controls.OfType<MiniGraphControl>().FirstOrDefault() is MiniGraphControl mini)
                        {
                            statsCtrl.SetMiniGraphHost(mini, null);
                        }
                        else
                        {
                            var mini2 = new MiniGraphControl() { Dock = DockStyle.Fill };
                            pnlMiniGraph.Controls.Clear();
                            pnlMiniGraph.Controls.Add(mini2);
                            statsCtrl.SetMiniGraphHost(mini2, null);
                        }
                    }
                    catch { }
                    statsCtrl.SetDiagramData(engine.CurrentState.PendingRequests, engine.CurrentState.HeadPosition, engine.CurrentState.DiskSize);
                    statsCtrl.SetDiagramProgress(1f);
                }
            }
            catch { }
        }

        private void StartSimulationUpdates()
        {
            if (simulationUpdateTimer == null || simulationStopwatch == null) return;
            simulationAccumulator = 0.0;
            simulationStopwatch.Restart();
            // prefer recording current stopwatch time as baseline to avoid a large first-delta
            lastSimulationTime = simulationStopwatch.Elapsed.TotalSeconds;
            simulationUpdateTimer.Start();

            if (renderer != null) renderer.IsDimmed = false;
        }

        private void PauseSimulationUpdates()
        {
            try
            {
                simulationUpdateTimer?.Stop();
                simulationStopwatch?.Stop();
            }
            catch { }

            // keep renderer running but draw dim overlay
            if (renderer != null) renderer.IsDimmed = true;
        }

        private void UpdateStatsInUI()
        {
            if (controlManager == null) return;

            // retrieve the stats control instance (if created) without showing it
            var statsCtrl = controlManager.GetInstance("stats") as StasticsAndInfo;
            if (statsCtrl == null) return;

            if (engine == null) return;

            // populate the stats panel with complete state + stats so algorithm-specific fields are shown
            statsCtrl.PopulateFromState(engine.CurrentState, engine.CurrentStats);

            // update upcoming panel if present
            statsCtrl.UpdateUpcomingFromState(engine.CurrentState);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            // gracefully unsubscribe engine events and detach renderer reference
            try
            {
                if (engine != null)
                {
                    if (engineStateChangedHandler != null) engine.StateChanged -= engineStateChangedHandler;
                    if (engineStatsChangedHandler != null) engine.StatisticsChanged -= engineStatsChangedHandler;
                }
            }
            catch { }

            try { if (renderer != null) renderer.Engine = null; } catch { }
            try { renderer?.Dispose(); } catch { }
            try { controlManager?.Dispose(); } catch { }
            try { sharedToolTip?.Dispose(); } catch { }
            try { engine?.Dispose(); } catch { }
            try { simulationUpdateTimer?.Stop(); simulationUpdateTimer?.Dispose(); } catch { }
            try { simulationStopwatch?.Stop(); } catch { }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        // Ensure Form1_Load exists for designer wiring
        private void Form1_Load(object? sender, EventArgs e)
        {
            // Intentionally left blank; initialization is performed in the constructor.
        }
    }
}