namespace LibraryOFBabel.ControlPanel
{
    partial class ModeControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            groupBox1 = new GroupBox();
            btnClearRequest = new Button();
            btnGenRandRequest = new Button();
            btnAddRequest = new Button();
            label1 = new Label();
            rTxtBoxRequestList = new RichTextBox();
            nudDiskSize = new NumericUpDown();
            label4 = new Label();
            nudHeadStartPos = new NumericUpDown();
            label2 = new Label();
            cboBoxSchedulingAlgorithm = new ComboBox();
            label3 = new Label();
            groupBox2 = new GroupBox();
            btnPrevStep = new Button();
            chkBoxShowStepbyStep = new CheckBox();
            label5 = new Label();
            btnNextStep = new Button();
            trkBarSimSpeed = new TrackBar();
            btnReset = new Button();
            btnPlay = new Button();
            toolTip1 = new ToolTip(components);
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudDiskSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize)nudHeadStartPos).BeginInit();
            groupBox2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)trkBarSimSpeed).BeginInit();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(btnClearRequest);
            groupBox1.Controls.Add(btnGenRandRequest);
            groupBox1.Controls.Add(btnAddRequest);
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(rTxtBoxRequestList);
            groupBox1.Controls.Add(nudDiskSize);
            groupBox1.Controls.Add(label4);
            groupBox1.Controls.Add(nudHeadStartPos);
            groupBox1.Controls.Add(label2);
            groupBox1.Location = new Point(23, 95);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(323, 315);
            groupBox1.TabIndex = 3;
            groupBox1.TabStop = false;
            groupBox1.Text = "Data Controls";
            // 
            // btnClearRequest
            // 
            btnClearRequest.Location = new Point(212, 244);
            btnClearRequest.Name = "btnClearRequest";
            btnClearRequest.Size = new Size(90, 55);
            btnClearRequest.TabIndex = 12;
            btnClearRequest.Text = "Clear Requests";
            btnClearRequest.UseVisualStyleBackColor = true;
            // 
            // btnGenRandRequest
            // 
            btnGenRandRequest.Location = new Point(115, 244);
            btnGenRandRequest.Name = "btnGenRandRequest";
            btnGenRandRequest.Size = new Size(90, 55);
            btnGenRandRequest.TabIndex = 11;
            btnGenRandRequest.Text = "Generate Random";
            btnGenRandRequest.UseVisualStyleBackColor = true;
            // 
            // btnAddRequest
            // 
            btnAddRequest.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnAddRequest.Location = new Point(16, 244);
            btnAddRequest.Name = "btnAddRequest";
            btnAddRequest.Size = new Size(90, 55);
            btnAddRequest.TabIndex = 10;
            btnAddRequest.Text = "Add Request";
            btnAddRequest.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label1.Location = new Point(16, 89);
            label1.Name = "label1";
            label1.Size = new Size(251, 17);
            label1.TabIndex = 8;
            label1.Text = "Request List (Seperate using a comma\",\"):";
            // 
            // rTxtBoxRequestList
            // 
            rTxtBoxRequestList.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            rTxtBoxRequestList.Location = new Point(16, 113);
            rTxtBoxRequestList.Name = "rTxtBoxRequestList";
            rTxtBoxRequestList.ScrollBars = RichTextBoxScrollBars.Vertical;
            rTxtBoxRequestList.Size = new Size(286, 125);
            rTxtBoxRequestList.TabIndex = 7;
            rTxtBoxRequestList.Text = "";
            // 
            // nudDiskSize
            // 
            nudDiskSize.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            nudDiskSize.Location = new Point(16, 43);
            nudDiskSize.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudDiskSize.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            nudDiskSize.Name = "nudDiskSize";
            nudDiskSize.Size = new Size(107, 29);
            nudDiskSize.TabIndex = 6;
            nudDiskSize.Value = new decimal(new int[] { 50, 0, 0, 0 });
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label4.Location = new Point(161, 19);
            label4.Name = "label4";
            label4.Size = new Size(141, 17);
            label4.TabIndex = 5;
            label4.Text = "Head Starting Position:";
            // 
            // nudHeadStartPos
            // 
            nudHeadStartPos.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            nudHeadStartPos.Location = new Point(161, 43);
            nudHeadStartPos.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            nudHeadStartPos.Minimum = new decimal(new int[] { 50, 0, 0, 0 });
            nudHeadStartPos.Name = "nudHeadStartPos";
            nudHeadStartPos.Size = new Size(107, 29);
            nudHeadStartPos.TabIndex = 4;
            nudHeadStartPos.Value = new decimal(new int[] { 50, 0, 0, 0 });
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label2.Location = new Point(16, 19);
            label2.Name = "label2";
            label2.Size = new Size(75, 21);
            label2.TabIndex = 3;
            label2.Text = "Disk Size:";
            // 
            // cboBoxSchedulingAlgorithm
            // 
            cboBoxSchedulingAlgorithm.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            cboBoxSchedulingAlgorithm.FormattingEnabled = true;
            cboBoxSchedulingAlgorithm.Location = new Point(23, 42);
            cboBoxSchedulingAlgorithm.Name = "cboBoxSchedulingAlgorithm";
            cboBoxSchedulingAlgorithm.Size = new Size(323, 29);
            cboBoxSchedulingAlgorithm.TabIndex = 4;
            cboBoxSchedulingAlgorithm.Text = "First Come First Serve (FCFS)";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label3.Location = new Point(23, 18);
            label3.Name = "label3";
            label3.Size = new Size(198, 21);
            label3.TabIndex = 5;
            label3.Text = "Disk Scheduling Algorithm:";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(btnPrevStep);
            groupBox2.Controls.Add(chkBoxShowStepbyStep);
            groupBox2.Controls.Add(label5);
            groupBox2.Controls.Add(btnNextStep);
            groupBox2.Controls.Add(trkBarSimSpeed);
            groupBox2.Controls.Add(btnReset);
            groupBox2.Controls.Add(btnPlay);
            groupBox2.Location = new Point(23, 426);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(323, 291);
            groupBox2.TabIndex = 6;
            groupBox2.TabStop = false;
            groupBox2.Text = "Simulation Controls";
            // 
            // btnPrevStep
            // 
            btnPrevStep.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnPrevStep.Location = new Point(166, 226);
            btnPrevStep.Name = "btnPrevStep";
            btnPrevStep.Size = new Size(144, 32);
            btnPrevStep.TabIndex = 18;
            btnPrevStep.Text = "Previous Step";
            btnPrevStep.UseVisualStyleBackColor = true;
            // 
            // chkBoxShowStepbyStep
            // 
            chkBoxShowStepbyStep.AutoSize = true;
            chkBoxShowStepbyStep.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            chkBoxShowStepbyStep.Location = new Point(19, 195);
            chkBoxShowStepbyStep.Name = "chkBoxShowStepbyStep";
            chkBoxShowStepbyStep.Size = new Size(205, 25);
            chkBoxShowStepbyStep.TabIndex = 17;
            chkBoxShowStepbyStep.Text = "Show Step-By-Step Mode";
            chkBoxShowStepbyStep.UseVisualStyleBackColor = true;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label5.Location = new Point(16, 105);
            label5.Name = "label5";
            label5.Size = new Size(100, 21);
            label5.TabIndex = 13;
            label5.Text = "Speed Slider:";
            // 
            // btnNextStep
            // 
            btnNextStep.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnNextStep.Location = new Point(16, 226);
            btnNextStep.Name = "btnNextStep";
            btnNextStep.Size = new Size(144, 32);
            btnNextStep.TabIndex = 16;
            btnNextStep.Text = "Next Step";
            btnNextStep.UseVisualStyleBackColor = true;
            // 
            // trkBarSimSpeed
            // 
            trkBarSimSpeed.Location = new Point(19, 129);
            trkBarSimSpeed.Minimum = 1;
            trkBarSimSpeed.Name = "trkBarSimSpeed";
            trkBarSimSpeed.RightToLeft = RightToLeft.Yes;
            trkBarSimSpeed.Size = new Size(291, 45);
            trkBarSimSpeed.TabIndex = 15;
            trkBarSimSpeed.Value = 10;
            // 
            // btnReset
            // 
            btnReset.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnReset.Location = new Point(166, 31);
            btnReset.Name = "btnReset";
            btnReset.Size = new Size(144, 55);
            btnReset.TabIndex = 14;
            btnReset.Text = "Reset";
            btnReset.UseVisualStyleBackColor = true;
            // 
            // btnPlay
            // 
            btnPlay.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            btnPlay.Location = new Point(16, 31);
            btnPlay.Name = "btnPlay";
            btnPlay.Size = new Size(144, 55);
            btnPlay.TabIndex = 13;
            btnPlay.Text = "Play";
            btnPlay.UseVisualStyleBackColor = true;
            // 
            // ModeControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(cboBoxSchedulingAlgorithm);
            Controls.Add(label3);
            Name = "ModeControl";
            Size = new Size(364, 731);
            Load += ModeControl_Load;
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudDiskSize).EndInit();
            ((System.ComponentModel.ISupportInitialize)nudHeadStartPos).EndInit();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)trkBarSimSpeed).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private GroupBox groupBox1;
        private RichTextBox rTxtBoxRequestList;
        private NumericUpDown nudDiskSize;
        private Label label4;
        private NumericUpDown nudHeadStartPos;
        private Label label2;
        private ComboBox cboBoxSchedulingAlgorithm;
        private Label label3;
        private Label label1;
        private Button btnClearRequest;
        private Button btnGenRandRequest;
        private Button btnAddRequest;
        private GroupBox groupBox2;
        private TrackBar trkBarSimSpeed;
        private Button btnReset;
        private Button btnPlay;
        private Button btnPrevStep;
        private CheckBox chkBoxShowStepbyStep;
        private Label label5;
        private Button btnNextStep;
        private ToolTip toolTip1;
    }
}
