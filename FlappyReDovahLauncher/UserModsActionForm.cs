using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    public enum UserModsAction
    {
        Preserve,
        CleanCanon
    }

    /// <summary>
    /// Modal dialog asking the player whether to preserve custom user mods (smart merge)
    /// or apply clean official author canon (quarantining user mods to UserMods).
    /// </summary>
    internal sealed class UserModsActionForm : Form
    {
        public UserModsAction SelectedAction { get; private set; }

        public UserModsActionForm(int modCount)
        {
            SelectedAction = UserModsAction.Preserve;
            Text = Constants.LAUNCHER_NAME;
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(500, 310);

            var title = new Label
            {
                Text = Loc.T("usermods_title"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 14),
                Size = new Size(460, 30)
            };

            var hint = new Label
            {
                Text = Loc.F("usermods_hint", modCount),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 48),
                Size = new Size(460, 52)
            };

            var btnPreserve = FlappyTheme.MakeGoldButton(Loc.T("usermods_preserve"), new Point(25, 110), new Size(215, 115), primary: true);
            var btnCanon = FlappyTheme.MakeGoldButton(Loc.T("usermods_canon"), new Point(260, 110), new Size(215, 115), primary: true);

            btnPreserve.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
            btnCanon.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);

            btnPreserve.Click += (s, e) =>
            {
                SelectedAction = UserModsAction.Preserve;
                DialogResult = DialogResult.OK;
                Close();
            };

            btnCanon.Click += (s, e) =>
            {
                SelectedAction = UserModsAction.CleanCanon;
                DialogResult = DialogResult.OK;
                Close();
            };

            var cancel = FlappyTheme.MakeGoldButton(Loc.T("cancel"), new Point(185, 248), new Size(130, 34), primary: false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.ForeColor = FlappyTheme.Mute;
            cancel.BackColor = FlappyTheme.CancelBack;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(btnPreserve);
            Controls.Add(btnCanon);
            Controls.Add(cancel);

            CancelButton = cancel;
            AcceptButton = btnPreserve;
        }

        public static UserModsAction? ShowPrompt(IWin32Window owner, int modCount)
        {
            using (var f = new UserModsActionForm(modCount))
            {
                var r = (owner != null) ? f.ShowDialog(owner) : f.ShowDialog();
                if (r != DialogResult.OK) return null;
                return f.SelectedAction;
            }
        }
    }
}
