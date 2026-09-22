using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>Closed-test phrase. Checked on the CDN, not in this exe.</summary>
    internal sealed class AccessKeyForm : Form
    {
        private readonly GameDefinition _game;
        private readonly TextBox _box;
        private readonly Label _error;
        private readonly Button _ok;

        public AccessKeyForm(GameDefinition game)
        {
            _game = game;
            Text = Constants.LAUNCHER_NAME;
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(460, 250);

            var title = new Label
            {
                Text = Loc.T("access_title"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(420, 32)
            };

            var hint = new Label
            {
                Text = Loc.F("access_hint", game != null ? game.Title : "Flappy"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 52),
                Size = new Size(420, 48)
            };

            _box = new TextBox
            {
                Location = new Point(40, 110),
                Size = new Size(380, 28),
                Font = new Font("Segoe UI", 11f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                BorderStyle = BorderStyle.FixedSingle,
                UseSystemPasswordChar = false
            };

            _error = new Label
            {
                Text = "",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(220, 120, 120),
                Location = new Point(20, 144),
                Size = new Size(420, 24)
            };

            _ok = FlappyTheme.MakeGoldButton(Loc.T("access_ok"), new Point(90, 186), new Size(140, 36), primary: true);
            var cancel = FlappyTheme.MakeGoldButton(Loc.T("cancel"), new Point(240, 186), new Size(130, 36), primary: false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.ForeColor = FlappyTheme.Mute;
            cancel.BackColor = FlappyTheme.CancelBack;

            _ok.Click += OnOk;
            _box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    OnOk(s, e);
                }
            };

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(_box);
            Controls.Add(_error);
            Controls.Add(_ok);
            Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = _ok;
        }

        private void OnOk(object sender, EventArgs e)
        {
            _ok.Enabled = false;
            _error.Text = "";
            try
            {
                var r = AccessGate.TryUnlock(_game, _box.Text);
                if (r.Ok)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }
                _error.Text = r.Error ?? Loc.T("access_bad");
            }
            finally
            {
                _ok.Enabled = true;
                _box.SelectAll();
                _box.Focus();
            }
        }
    }
}
