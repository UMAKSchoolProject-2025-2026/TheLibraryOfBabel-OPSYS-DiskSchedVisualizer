using System;
using System.Linq;
using System.Windows.Forms;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.ControlPanel
{
    public partial class ModeControl : UserControl
    {
        public event EventHandler<bool>? PlayToggled; // true == playing
        public event EventHandler<int>? SpeedChanged; // 1..10
        public event EventHandler<SchedulingAlgorithm>? AlgorithmChanged;
        public event EventHandler? StepRequested;
        public event EventHandler? ResetRequested;

        private bool isPlaying;

        public ModeControl()
        {
            InitializeComponent();
            WireInternals();
        }

        private void WireInternals()
        {
            // Populate algorithm choices (text must match mapping used later)
            cboBoxSchedulingAlgorithm.Items.Clear();
            cboBoxSchedulingAlgorithm.Items.Add("FCFS");
            cboBoxSchedulingAlgorithm.Items.Add("SSTF");
            cboBoxSchedulingAlgorithm.Items.Add("SCAN");
            cboBoxSchedulingAlgorithm.Items.Add("C-SCAN");
            cboBoxSchedulingAlgorithm.SelectedIndex = 0;

            // Wire buttons & controls
            btnPlay.Click += (s, e) =>
            {
                isPlaying = !isPlaying;
                btnPlay.Text = isPlaying ? "Pause" : "Play";
                PlayToggled?.Invoke(this, isPlaying);
            };

            trkBarSimSpeed.ValueChanged += (s, e) =>
            {
                SpeedChanged?.Invoke(this, trkBarSimSpeed.Value);
            };

            cboBoxSchedulingAlgorithm.SelectedIndexChanged += (s, e) =>
            {
                var text = cboBoxSchedulingAlgorithm.SelectedItem?.ToString() ?? string.Empty;
                var alg = text switch
                {
                    "FCFS" => SchedulingAlgorithm.FCFS,
                    "SSTF" => SchedulingAlgorithm.SSTF,
                    "SCAN" => SchedulingAlgorithm.SCAN,
                    "C-SCAN" => SchedulingAlgorithm.CSCAN,
                    _ => SchedulingAlgorithm.FCFS
                };
                AlgorithmChanged?.Invoke(this, alg);
            };

            btnNextStep.Click += (s, e) => StepRequested?.Invoke(this, EventArgs.Empty);
            btnReset.Click += (s, e) => ResetRequested?.Invoke(this, EventArgs.Empty);
        }

        // helpers to set UI from external state
        public void SetPlaying(bool playing)
        {
            isPlaying = playing;
            btnPlay.Text = isPlaying ? "Pause" : "Play";
        }

        public void SetSpeed(int value)
        {
            if (value < trkBarSimSpeed.Minimum || value > trkBarSimSpeed.Maximum) return;
            trkBarSimSpeed.Value = value;
        }

        public void SetAlgorithm(SchedulingAlgorithm alg)
        {
            var text = alg switch
            {
                SchedulingAlgorithm.FCFS => "FCFS",
                SchedulingAlgorithm.SSTF => "SSTF",
                SchedulingAlgorithm.SCAN => "SCAN",
                SchedulingAlgorithm.CSCAN => "C-SCAN",
                _ => "FCFS"
            };
            var idx = cboBoxSchedulingAlgorithm.Items.IndexOf(text);
            if (idx >= 0) cboBoxSchedulingAlgorithm.SelectedIndex = idx;
        }

        // Designer expects this handler to exist; keep as a no-op or add startup logic here.
        private void ModeControl_Load(object? sender, EventArgs e)
        {
            // Intentionally empty - place ModeControl initialization code here if needed.
        }
    }
}
