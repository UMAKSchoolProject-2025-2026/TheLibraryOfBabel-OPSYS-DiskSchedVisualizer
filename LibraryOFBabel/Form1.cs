using System;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using LibraryOFBabel.UI;
using LibraryOFBabel.Visualization;
using LibraryOFBabel.ControlPanel;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel
{
    public partial class Form1 : Form
    {
        private DiskVisualizationRenderer? renderer;
        private ControlManager? controlManager;
        private ToolTip? sharedToolTip;
        private SimulationEngine? engine;
        private System.Windows.Forms.Timer? simulationUpdateTimer;
        private Stopwatch? simulationStopwatch;
        private double lastSimulationTime = 0.0;
        private double simulationSpeedMultiplier = 1.0; // 1..10 from trackbar
        private double simulationAccumulator = 0.0;
        private const double StepsPerSecondBase = 5.0; // base step rate (steps/sec) when multiplier == 1

        public Form1()
        {
            InitializeComponent();
            InitializeVisualization();
            InitializeControlHost();
            InitializeSimulationEngine();
        }

        private void InitializeVisualization()
        {
            renderer = new DiskVisualizationRenderer();

            // Enable diagnostics optionally
            renderer.EnableDiagnostics = false;

            panelVisualizer.Controls.Add(renderer.GetVisualizationControl());
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

            // wire menu items to swap controls (this does not affect the SKGLControl)
            modeControlToolStripMenuItem.Click += (s, e) => controlManager.Show("mode");
            statsAndInfoToolStripMenuItem.Click += (s, e) => controlManager.Show("stats");

            // show default
            controlManager.Show("mode");
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
                simulationSpeedMultiplier = modeCtrl.Controls.OfType<TrackBar>().FirstOrDefault()?.Value ?? 5;

                modeCtrl.PlayToggled += (s, playing) =>
                {
                    if (playing) StartSimulationUpdates();
                    else PauseSimulationUpdates();
                };

                modeCtrl.SpeedChanged += (s, speed) =>
                {
                    // speed slider 1..10 maps directly to multiplier (simple mapping)
                    simulationSpeedMultiplier = Math.Max(0.001, speed);
                    // if timer running, nothing else required; multiplier will affect updates on next tick
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

                // scale by UI speed multiplier
                var scaledDelta = delta * simulationSpeedMultiplier;

                // accumulate and run discrete Step() calls at a rate determined by StepsPerSecondBase * multiplier
                simulationAccumulator += scaledDelta;
                double stepInterval = 1.0 / (StepsPerSecondBase * simulationSpeedMultiplier);
                // guard: don't spin forever if accumulator huge
                int guard = 0;
                while (simulationAccumulator >= stepInterval && guard < 100)
                {
                    engine.Step();
                    simulationAccumulator -= stepInterval;
                    guard++;
                }

                // update stats UI if visible (no forced control switches)
                UpdateStatsInUI();

                lastSimulationTime = now;
            };

            // wire engine events to update UI
            engine.StateChanged += (s, st) => { /* optionally: react */ };
            engine.StatisticsChanged += (s, st) => UpdateStatsInUI();
        }

        private void StartSimulationUpdates()
        {
            if (simulationUpdateTimer == null || simulationStopwatch == null) return;
            lastSimulationTime = 0.0;
            simulationAccumulator = 0.0;
            simulationStopwatch.Restart();
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
            statsCtrl.UpdateHeadPosition(engine.CurrentState.HeadPosition);
            statsCtrl.UpdateTotalSeekTime(engine.CurrentStats.TotalSeekDistance);
            statsCtrl.UpdateAverageSeekTime(engine.CurrentStats.AverageSeek);
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            try { renderer?.Dispose(); } catch { }
            try { controlManager?.Dispose(); } catch { }
            try { sharedToolTip?.Dispose(); } catch { }
            try { engine?.Dispose(); } catch { }
            try { simulationUpdateTimer?.Dispose(); } catch { }
            try { simulationStopwatch?.Stop(); } catch { }
        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }
    }
}