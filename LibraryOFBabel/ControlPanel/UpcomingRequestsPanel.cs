using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LibraryOFBabel.ControlPanel
{
    /// <summary>
    /// Displays the "upcoming requests" area: previous (top), current (center), and upcoming list (bottom).
    /// Callers should update the panel via SetQueue or NotifyServed to reflect engine changes.
    /// </summary>
    public class UpcomingRequestsPanel : UserControl
    {
        private readonly Label lblPrevious;
        private readonly Label lblCurrent;
        private readonly FlowLayoutPanel pnlUpcomingList;
        private readonly Label lblUpcomingHeader;

        // last served value shown at the top (nullable)
        private int? lastServed;

        // currently targeted request (nullable)
        private int? currentRequest;

        // pending upcoming requests in arrival order (excluding current)
        private List<int> upcoming = new List<int>();

        // how many upcoming items to show by default
        public int MaxUpcomingToShow { get; set; } = 8;

        private System.Windows.Forms.Timer? animTimer;
        private float animProgress = 1f; // 0..1, 1 means no active animation
        private const int AnimMs = 220;
        private int animFromYPrev;
        private int animFromYCurrent;
        private int animFromYList;

        public UpcomingRequestsPanel()
        {
            // layout: vertical stack: previous (small), current (large, centered), upcoming header + scrollable list
            this.BackColor = SystemColors.Control;

            lblPrevious = new Label()
            {
                AutoSize = false,
                Height = 24,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular),
                ForeColor = SystemColors.GrayText,
                Text = string.Empty
            };

            lblCurrent = new Label()
            {
                AutoSize = false,
                Height = 48,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 16f, FontStyle.Bold),
                ForeColor = Color.DarkBlue,
                Text = "Idle"
            };

            lblUpcomingHeader = new Label()
            {
                AutoSize = false,
                Height = 20,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular),
                Text = "Upcoming"
            };

            pnlUpcomingList = new FlowLayoutPanel()
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4),
            };

            // Add in reverse so top is at the top
            Controls.Add(pnlUpcomingList);
            Controls.Add(lblUpcomingHeader);
            Controls.Add(lblCurrent);
            Controls.Add(lblPrevious);

            // default size
            this.Size = new Size(200, 300);

            animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            animTimer.Tick += (s, e) => AnimateTick();
        }

        /// <summary>
        /// Replace the queue display. currentRequest is shown in the middle, upcomingRequests are listed below (in arrival order).
        /// lastServed will be shown at the top (may be null).
        /// This method is safe to call from any thread.
        /// </summary>
        public void SetQueue(IEnumerable<int>? upcomingRequests, int? currentRequest, int? lastServed = null)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => SetQueue(upcomingRequests, currentRequest, lastServed)));
                return;
            }

            this.lastServed = lastServed;
            this.currentRequest = currentRequest;
            this.upcoming = upcomingRequests == null ? new List<int>() : upcomingRequests.ToList();

            UpdateVisuals();
        }

        /// <summary>
        /// Call when a request was served. Moves the current into previous and advances the queue.
        /// The caller should typically pass the value that was just served and the new pending list.
        /// </summary>
        public void NotifyServed(int servedValue, IEnumerable<int>? newPending)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => NotifyServed(servedValue, newPending)));
                return;
            }

            // start slide animation: store previous positions and replace data at end of animation
            if (animTimer != null && !animTimer.Enabled)
            {
                animProgress = 0f;
                // record initial layout Y offsets (we'll simply fade/translate visually)
                animFromYPrev = lblPrevious.Top;
                animFromYCurrent = lblCurrent.Top;
                animFromYList = pnlUpcomingList.Top;
                animTimer.Start();
            }

            // update data now so during animation new items are available to draw
            lastServed = servedValue;
            var list = (newPending ?? Enumerable.Empty<int>()).ToList();
            currentRequest = list.FirstOrDefault();
            upcoming = list.Skip(1).ToList();

            // ensure visuals reflect the new data at end of animation
        }

        private void AnimateTick()
        {
            // simple linear animation
            animProgress += 16f / AnimMs;
            if (animProgress >= 1f)
            {
                animProgress = 1f;
                animTimer?.Stop();
            }

            // compute small translate offsets
            float t = Math.Min(1f, animProgress);
            float ease = 1f - (1f - t) * (1f - t);

            // translate current upwards and previous fades in
            lblCurrent.Top = animFromYCurrent - (int)(ease * 20);
            lblPrevious.Top = animFromYPrev - (int)(ease * 20);
            pnlUpcomingList.Top = animFromYList + (int)(ease * 20);

            if (animProgress >= 1f)
            {
                // restore normal layout and refresh list
                UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            lblPrevious.Text = lastServed.HasValue ? $"Last: {lastServed.Value}" : string.Empty;
            lblCurrent.Text = currentRequest.HasValue ? currentRequest.Value.ToString() : "Idle";

            // update upcoming list items
            pnlUpcomingList.SuspendLayout();
            pnlUpcomingList.Controls.Clear();

            int countToShow = Math.Min(MaxUpcomingToShow, upcoming.Count);
            if (countToShow == 0)
            {
                var empty = CreateUpcomingItem("-- no upcoming requests --", subdued: true);
                pnlUpcomingList.Controls.Add(empty);
            }
            else
            {
                for (int i = 0; i < countToShow; i++)
                {
                    var v = upcoming[i];
                    var lbl = CreateUpcomingItem(v.ToString(), subdued: false);
                    pnlUpcomingList.Controls.Add(lbl);
                }

                if (upcoming.Count > countToShow)
                {
                    var more = CreateUpcomingItem($"... and {upcoming.Count - countToShow} more", subdued: true);
                    pnlUpcomingList.Controls.Add(more);
                }
            }

            pnlUpcomingList.ResumeLayout();
        }

        private Control CreateUpcomingItem(string text, bool subdued)
        {
            var lbl = new Label()
            {
                AutoSize = false,
                Height = 28,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Regular),
                Text = text,
                Margin = new Padding(2),
            };

            if (subdued)
            {
                lbl.ForeColor = SystemColors.GrayText;
            }
            else
            {
                lbl.ForeColor = Color.Black;
                lbl.BackColor = Color.FromArgb(240, 240, 255);
                lbl.BorderStyle = BorderStyle.FixedSingle;
            }

            return lbl;
        }
    }
}
