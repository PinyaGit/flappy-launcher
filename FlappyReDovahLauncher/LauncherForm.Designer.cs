namespace FlappyReDovahLauncher
{
    partial class Application
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            if (disposing)
            {
                if (_splash != null) { _splash.Dispose(); _splash = null; }
                if (_iconDiscord != null) { _iconDiscord.Dispose(); _iconDiscord = null; }
                if (_iconBoosty != null) { _iconBoosty.Dispose(); _iconBoosty = null; }
                if (_frame != null) { _frame.Dispose(); _frame = null; }
                if (_premult != null) { _premult.Dispose(); _premult = null; }
                if (_edgeBarFill != null) { _edgeBarFill.Dispose(); _edgeBarFill = null; }
                if (_edgeBarFrame != null) { _edgeBarFrame.Dispose(); _edgeBarFrame = null; }
                if (_edgeBarTrack != null) { _edgeBarTrack.Dispose(); _edgeBarTrack = null; }
                if (_iconClose != null) { _iconClose.Dispose(); _iconClose = null; }
                if (_iconDiscord != null) { _iconDiscord.Dispose(); _iconDiscord = null; }
                if (_iconBoosty != null) { _iconBoosty.Dispose(); _iconBoosty = null; }
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.SuspendLayout();
            //
            // Application — portrait layered window (splash size only)
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(514, 800);
            this.DoubleBuffered = true;
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Application";
            this.ShowInTaskbar = true;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Flappy Re-Dovah";
            this.Load += new System.EventHandler(this.OnLoadApplication);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.OnFormMouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.OnFormMouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.OnFormMouseUp);
            this.ResumeLayout(false);
        }

        #endregion
    }
}
