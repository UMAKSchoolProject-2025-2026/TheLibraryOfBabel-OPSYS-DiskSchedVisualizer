using System;
using System.Linq;
using System.Windows.Forms;
using LibraryOFBabel.Simulation;
using System.Collections.Generic;

namespace LibraryOFBabel.ControlPanel
{
    public partial class ModeControl : UserControl
    {
        public event EventHandler<bool>? PlayToggled; // true == playing
        public event EventHandler<int>? SpeedChanged; // 1..10
        public event EventHandler<SchedulingAlgorithm>? AlgorithmChanged;
        public event EventHandler? StepRequested;
        public event EventHandler? ResetRequested;

        // New events for data controls
        public event EventHandler<IEnumerable<int>>? AddRequests;
        public event EventHandler<int>? GenerateRandomRequests;
        public event EventHandler? ClearRequests;
        public event EventHandler<int>? DiskSizeChanged;
        public event EventHandler<int>? HeadStartPositionChanged;

        private bool isPlaying;

        // Adjustable random count for GenerateRandom (can be set from code or UI later)
        public int RandomRequestCount { get; set; } = 10;

        // suppress programmatic changes from raising events
        private bool suppressValueChangedEvents;

        // Placeholder text shown in combo box when no valid algorithm is selected
        private const string AlgorithmPlaceholderText = "Please select an algorithm";

        public ModeControl()
        {
            InitializeComponent();
            WireInternals();

            // ensure head-start maximum matches initial disk size
            UpdateHeadStartMaximumFromDiskSize();
        }

        private void WireInternals()
        {
            // Populate algorithm choices (text must match mapping used later)
            cboBoxSchedulingAlgorithm.Items.Clear();
            // Add placeholder as first item so UI starts disabled until user selects a real algorithm
            cboBoxSchedulingAlgorithm.Items.Add(AlgorithmPlaceholderText);
            cboBoxSchedulingAlgorithm.Items.Add("FCFS");
            cboBoxSchedulingAlgorithm.Items.Add("SSTF");
            cboBoxSchedulingAlgorithm.Items.Add("SCAN");
            cboBoxSchedulingAlgorithm.Items.Add("C-SCAN");
            cboBoxSchedulingAlgorithm.Items.Add("LOOK");
            cboBoxSchedulingAlgorithm.Items.Add("C-LOOK");
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
                // If the placeholder is selected, keep the rest of the UI disabled and do not raise AlgorithmChanged
                if (text == AlgorithmPlaceholderText)
                {
                    UpdateControlsEnabledState(false);
                    return;
                }

                var alg = text switch
                {
                    "FCFS" => SchedulingAlgorithm.FCFS,
                    "SSTF" => SchedulingAlgorithm.SSTF,
                    "SCAN" => SchedulingAlgorithm.SCAN,
                    "C-SCAN" => SchedulingAlgorithm.CSCAN,
                    "LOOK" => SchedulingAlgorithm.LOOK,
                    "C-LOOK" => SchedulingAlgorithm.CLOOK,
                    _ => SchedulingAlgorithm.FCFS
                };

                // Enable all other controls now that a valid algorithm is selected
                UpdateControlsEnabledState(true);
                AlgorithmChanged?.Invoke(this, alg);
            };

            btnNextStep.Click += (s, e) => StepRequested?.Invoke(this, EventArgs.Empty);
            btnReset.Click += (s, e) => ResetRequested?.Invoke(this, EventArgs.Empty);

            // Data control buttons
            btnAddRequest.Click += (s, e) =>
            {
                var list = ParseRequestsFromText(rTxtBoxRequestList.Text);
                if (list.Any())
                {
                    AddRequests?.Invoke(this, list);
                }
            };

            btnGenRandRequest.Click += (s, e) =>
            {
                GenerateRandomRequests?.Invoke(this, RandomRequestCount);
            };

            btnClearRequest.Click += (s, e) =>
            {
                // clear the text box and raise event
                rTxtBoxRequestList.Clear();
                ClearRequests?.Invoke(this, EventArgs.Empty);
            };

            // numeric up-down changes
            nudDiskSize.ValueChanged += NudDiskSize_ValueChanged;
            nudHeadStartPos.ValueChanged += NudHeadStartPos_ValueChanged;

            // Start with everything disabled except the algorithm combo box
            UpdateControlsEnabledState(false);
        }

        /// <summary>
        /// Enable or disable all interactive controls except the algorithm combo box.
        /// Used to gray out the UI until the user picks a valid algorithm.
        /// </summary>
        private void UpdateControlsEnabledState(bool enabled)
        {
            // Controls to toggle
            btnPlay.Enabled = enabled;
            trkBarSimSpeed.Enabled = enabled;
            btnNextStep.Enabled = enabled;
            btnReset.Enabled = enabled;
            btnAddRequest.Enabled = enabled;
            btnGenRandRequest.Enabled = enabled;
            btnClearRequest.Enabled = enabled;
            rTxtBoxRequestList.Enabled = enabled;
            nudDiskSize.Enabled = enabled;
            nudHeadStartPos.Enabled = enabled;
        }

        private void NudDiskSize_ValueChanged(object? sender, EventArgs e)
        {
            if (suppressValueChangedEvents) return;

            // when disk size changes, update head-start max and notify listeners
            UpdateHeadStartMaximumFromDiskSize();
            DiskSizeChanged?.Invoke(this, (int)nudDiskSize.Value);
        }

        private void NudHeadStartPos_ValueChanged(object? sender, EventArgs e)
        {
            if (suppressValueChangedEvents) return;

            HeadStartPositionChanged?.Invoke(this, (int)nudHeadStartPos.Value);
        }

        // Expose current values
        public int DiskSize => (int)nudDiskSize.Value;
        public int HeadStartPosition => (int)nudHeadStartPos.Value;

        /// <summary>
        /// Programmatically set the disk size shown in UI without firing DiskSizeChanged.
        /// This also updates the head-start numeric maximum to remain valid.
        /// </summary>
        public void SetDiskSize(int size)
        {
            suppressValueChangedEvents = true;
            try
            {
                // clamp to control limits
                var newVal = Math.Max((int)nudDiskSize.Minimum, Math.Min((int)nudDiskSize.Maximum, size));
                nudDiskSize.Value = newVal;
                UpdateHeadStartMaximumFromDiskSize();
            }
            finally
            {
                suppressValueChangedEvents = false;
            }
        }

        /// <summary>
        /// Programmatically set the head start position shown in UI without firing HeadStartPositionChanged.
        /// Value will be clamped to the current maximum.
        /// </summary>
        public void SetHeadStartPosition(int pos)
        {
            suppressValueChangedEvents = true;
            try
            {
                var max = (int)nudHeadStartPos.Maximum;
                var min = (int)nudHeadStartPos.Minimum;
                var newVal = Math.Max(min, Math.Min(max, pos));
                nudHeadStartPos.Value = newVal;
            }
            finally
            {
                suppressValueChangedEvents = false;
            }
        }

        /// <summary>
        /// Ensure head-start numeric maximum follows disk size (max = diskSize - 1).
        /// Also clamp current head-start value to new maximum.
        /// </summary>
        private void UpdateHeadStartMaximumFromDiskSize()
        {
            // compute a valid maximum (disk size must be at least 1)
            int disk = Math.Max(1, (int)nudDiskSize.Value);
            int maxHead = Math.Max(0, disk - 1);

            // apply without triggering change events
            suppressValueChangedEvents = true;
            try
            {
                nudHeadStartPos.Maximum = maxHead;
                if (nudHeadStartPos.Value > maxHead) nudHeadStartPos.Value = maxHead;
            }
            finally
            {
                suppressValueChangedEvents = false;
            }
        }

        /// <summary>
        /// Replace the request textbox contents with the provided list (comma-separated).
        /// Used to show randomly generated requests before the user clicks Add Request.
        /// </summary>
        public void SetRequestText(IEnumerable<int> requests)
        {
            if (requests == null) rTxtBoxRequestList.Clear();
            else rTxtBoxRequestList.Text = string.Join(", ", requests);
        }

        private static IEnumerable<int> ParseRequestsFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<int>();
            var parts = text.Split(new[] { ',', ';', '\n', ' ', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<int>(parts.Length);
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out var v)) list.Add(v);
            }
            return list;
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
                SchedulingAlgorithm.LOOK => "LOOK",
                SchedulingAlgorithm.CLOOK => "C-LOOK",
                _ => "FCFS"
            };
            var idx = cboBoxSchedulingAlgorithm.Items.IndexOf(text);
            if (idx >= 0) cboBoxSchedulingAlgorithm.SelectedIndex = idx;
            // Ensure controls are enabled when setting a valid algorithm programmatically
            if (idx >= 0) UpdateControlsEnabledState(true);
        }

        // Designer expects this handler to exist; keep as a no-op or add startup logic here.
        private void ModeControl_Load(object? sender, EventArgs e)
        {
            // Intentionally empty - place ModeControl initialization code here if needed.
        }

        private void rTxtBoxRequestList_TextChanged(object sender, EventArgs e)
        {
            // could validate live, but keep it simple for now
        }
    }
}
