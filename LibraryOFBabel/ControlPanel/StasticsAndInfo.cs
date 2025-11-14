using System;
using System.Windows.Forms;
using LibraryOFBabel.Simulation;

namespace LibraryOFBabel.ControlPanel
{
    public partial class StasticsAndInfo : UserControl
    {
        public StasticsAndInfo()
        {
            InitializeComponent();
        }

        public void UpdateHeadPosition(int pos)
        {
            txtBoxCurrentHeadPos.Text = pos.ToString();
        }

        public void UpdateTotalSeekTime(long totalSeekDistance)
        {
            // display as units moved (simple placeholder) — later we can convert to ms
            txtBoxTotalSeekTime.Text = totalSeekDistance.ToString();
        }

        public void UpdateAverageSeekTime(double average)
        {
            txtBoxAverageSeekTime.Text = average.ToString("F2");
        }

        // Designer expects this handler to exist; keep as a no-op or add startup logic here.
        private void StasticsAndInfo_Load(object? sender, EventArgs e)
        {
            // Intentionally empty - place StasticsAndInfo initialization code here if needed.
        }
    }
}
