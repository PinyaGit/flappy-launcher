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
            Text = "Flappy Re-Dovah";
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(440, 290);

            var title = new Label
            {
                Text = "What to download?",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(400, 32)
            };

            var hint = new Label
            {
                Text = "VR packs are large (StockGameVR + (VR) mods).\nYou can reinstall later with the full channel.",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 52),
                Size = new Size(400, 48)
            };

            var btnAe = FlappyTheme.MakeGoldButton("AE only\nSkyrim AE — no VR", new Point(30, 115), new Size(180, 100), primary: true);
            var btnBoth = FlappyTheme.MakeGoldButton("AE + VR\nFull pack", new Point(230, 115), new Size(180, 100), primary: true);
            btnAe.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            btnBoth.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);

            btnAe.Click += (s, e) => { Selected = InstallChannel.AeOnly; DialogResult = DialogResult.OK; Close(); };
            btnBoth.Click += (s, e) => { Selected = InstallChannel.AeAndVr; DialogResult = DialogResult.OK; Close(); };

            var cancel = FlappyTheme.MakeGoldButton("Cancel", new Point(155, 235), new Size(130, 34), primary: false);
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
