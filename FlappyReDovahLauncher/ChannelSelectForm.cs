using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>Choose AE-only (skip VR packs) or full AE+VR download.</summary>
    internal sealed class ChannelSelectForm : Form
    {
        public InstallChannel Selected { get; private set; }

        public ChannelSelectForm()
        {
            Selected = InstallChannel.AeAndVr;
            Text = Constants.LAUNCHER_NAME;
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(440, 290);

            var title = new Label
            {
                Text = Loc.T("channel_title"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(400, 32)
            };

            var hint = new Label
            {
                Text = Loc.T("channel_hint"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 52),
                Size = new Size(400, 48)
            };

            var btnAe = FlappyTheme.MakeGoldButton(Loc.T("channel_ae"), new Point(30, 115), new Size(180, 100), primary: true);
            var btnBoth = FlappyTheme.MakeGoldButton(Loc.T("channel_both"), new Point(230, 115), new Size(180, 100), primary: true);
            btnAe.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            btnBoth.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);

            btnAe.Click += (s, e) => { Selected = InstallChannel.AeOnly; DialogResult = DialogResult.OK; Close(); };
            btnBoth.Click += (s, e) => { Selected = InstallChannel.AeAndVr; DialogResult = DialogResult.OK; Close(); };

            var cancel = FlappyTheme.MakeGoldButton(Loc.T("cancel"), new Point(155, 235), new Size(130, 34), primary: false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.ForeColor = FlappyTheme.Mute;
            cancel.BackColor = FlappyTheme.CancelBack;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(btnAe);
            Controls.Add(btnBoth);
            Controls.Add(cancel);
            CancelButton = cancel;
            AcceptButton = btnBoth;
        }

        public static InstallChannel? ShowSelect(IWin32Window owner)
        {
            using (var f = new ChannelSelectForm())
            {
                var r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                if (r != DialogResult.OK) return null;
                return f.Selected;
            }
        }
    }
}
