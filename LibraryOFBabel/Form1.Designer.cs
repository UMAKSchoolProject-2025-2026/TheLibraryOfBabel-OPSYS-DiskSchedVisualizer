namespace LibraryOFBabel
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            panelVisualizer = new Panel();
            panel2 = new Panel();
            panelControlsHost = new Panel();
            label1 = new Label();
            menuStrip1 = new MenuStrip();
            controlsToolStripMenuItem = new ToolStripMenuItem();
            modeControlToolStripMenuItem = new ToolStripMenuItem();
            statsAndInfoToolStripMenuItem = new ToolStripMenuItem();
            helpToolStripMenuItem = new ToolStripMenuItem();
            panel2.SuspendLayout();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // panelVisualizer
            // 
            panelVisualizer.BorderStyle = BorderStyle.FixedSingle;
            panelVisualizer.Location = new Point(386, 27);
            panelVisualizer.Name = "panelVisualizer";
            panelVisualizer.Size = new Size(1118, 786);
            panelVisualizer.TabIndex = 0;
            // 
            // panel2
            // 
            panel2.BorderStyle = BorderStyle.FixedSingle;
            panel2.Controls.Add(panelControlsHost);
            panel2.Controls.Add(label1);
            panel2.Location = new Point(12, 27);
            panel2.Name = "panel2";
            panel2.Size = new Size(372, 786);
            panel2.TabIndex = 1;
            // 
            // panelControlsHost
            // 
            panelControlsHost.Location = new Point(3, 50);
            panelControlsHost.Name = "panelControlsHost";
            panelControlsHost.Size = new Size(364, 731);
            panelControlsHost.TabIndex = 1;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Times New Roman", 21.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            label1.Location = new Point(4, 14);
            label1.Name = "label1";
            label1.Size = new Size(363, 32);
            label1.TabIndex = 0;
            label1.Text = "THE LIBRARY OF BABEL";
            label1.Click += label1_Click;
            // 
            // menuStrip1
            // 
            menuStrip1.Items.AddRange(new ToolStripItem[] { controlsToolStripMenuItem, helpToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(1520, 24);
            menuStrip1.TabIndex = 2;
            menuStrip1.Text = "menuStrip1";
            // 
            // controlsToolStripMenuItem
            // 
            controlsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { modeControlToolStripMenuItem, statsAndInfoToolStripMenuItem });
            controlsToolStripMenuItem.Name = "controlsToolStripMenuItem";
            controlsToolStripMenuItem.Size = new Size(64, 20);
            controlsToolStripMenuItem.Text = "Controls";
            // 
            // modeControlToolStripMenuItem
            // 
            modeControlToolStripMenuItem.Name = "modeControlToolStripMenuItem";
            modeControlToolStripMenuItem.Size = new Size(180, 22);
            modeControlToolStripMenuItem.Text = "Mode Control";
            // 
            // statsAndInfoToolStripMenuItem
            // 
            statsAndInfoToolStripMenuItem.Name = "statsAndInfoToolStripMenuItem";
            statsAndInfoToolStripMenuItem.Size = new Size(180, 22);
            statsAndInfoToolStripMenuItem.Text = "Stats and Info";
            // 
            // helpToolStripMenuItem
            // 
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(44, 20);
            helpToolStripMenuItem.Text = "Help";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1520, 825);
            Controls.Add(panel2);
            Controls.Add(panelVisualizer);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "Form1";
            Text = "Form1";
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel panelVisualizer;
        private Label label1;
        private Panel panel2;
        private Panel panelControlsHost;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem controlsToolStripMenuItem;
        private ToolStripMenuItem modeControlToolStripMenuItem;
        private ToolStripMenuItem statsAndInfoToolStripMenuItem;
        private ToolStripMenuItem helpToolStripMenuItem;
    }
}
