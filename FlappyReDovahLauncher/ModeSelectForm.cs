using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>Pick AE or VR profile / start mode.</summary>
    public class ModeSelectForm : Form
    {
        public string SelectedMode { get; private set; }

        private readonly Button _btnAe;
        private readonly Button _btnVr;

        public ModeSelectForm(string lastMode = "AE")
        {
            SelectedMode = null;
            Text = Constants.LAUNCHER_NAME;
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(420, 270);

            var title = new Label
            {
                Text = Loc.T("mode_title"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 18),
                Size = new Size(380, 36)
            };

            var hint = new Label
            {
                Text = Loc.F("mode_last", string.IsNullOrEmpty(lastMode) ? Loc.T("mode_none") : lastMode),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 54),
                Size = new Size(380, 24)
            };

            _btnAe = FlappyTheme.MakeGoldButton(Loc.T("mode_ae"), new Point(30, 100), new Size(170, 90), primary: true);
            _btnVr = FlappyTheme.MakeGoldButton(Loc.T("mode_vr"), new Point(220, 100), new Size(170, 90), primary: false);
            _btnAe.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
            _btnVr.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);

            bool lastVr = string.Equals(lastMode, "VR", StringComparison.OrdinalIgnoreCase);
            FlappyTheme.SetHighlight(_btnAe, !lastVr);
            FlappyTheme.SetHighlight(_btnVr, lastVr);

            _btnAe.Click += (s, e) =>
            {
                SelectedMode = "AE";
                DialogResult = DialogResult.OK;
                Close();
            };
            _btnVr.Click += (s, e) =>
            {
                SelectedMode = "VR";
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancel = FlappyTheme.MakeGoldButton(Loc.T("cancel"), new Point(145, 215), new Size(130, 34), primary: false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.ForeColor = FlappyTheme.Mute;
            cancel.BackColor = FlappyTheme.CancelBack;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_btnAe);
            Controls.Add(_btnVr);
            Controls.Add(cancel);

            AcceptButton = lastVr ? _btnVr : _btnAe;
            CancelButton = cancel;
        }

        /// <summary>Shows dialog. Returns "AE", "VR", or null if cancelled.</summary>
        public static string ShowSelect(IWin32Window owner, string lastMode)
        {
            using (var f = new ModeSelectForm(lastMode))
            {
                var r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                if (r != DialogResult.OK) return null;
                return f.SelectedMode;
            }
        }
    }
}
