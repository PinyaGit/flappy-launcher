using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>Shared Edge-style colors for launcher dialogs.</summary>
    internal static class FlappyTheme
    {
        public static readonly Color Back = Color.FromArgb(18, 14, 22);
        public static readonly Color Panel = Color.FromArgb(32, 26, 40);
        public static readonly Color PanelHover = Color.FromArgb(48, 40, 62);
        public static readonly Color PanelActive = Color.FromArgb(48, 42, 70);
        public static readonly Color Gold = Color.FromArgb(214, 186, 110);
        public static readonly Color GoldHi = Color.FromArgb(236, 214, 150);
        public static readonly Color Cream = Color.FromArgb(236, 228, 210);
        public static readonly Color Mute = Color.FromArgb(160, 148, 168);
        public static readonly Color CancelBack = Color.FromArgb(40, 34, 48);

        public static void ApplyDialog(Form f)
        {
            f.BackColor = Back;
            f.ForeColor = Cream;
            f.Font = new Font("Segoe UI", 11f, FontStyle.Regular);
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
        }

        public static Button MakeGoldButton(string text, Point loc, Size size, bool primary)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? PanelActive : Panel,
                ForeColor = primary ? GoldHi : Cream,
                Font = new Font("Segoe UI Semibold", primary ? 11f : 10f, FontStyle.Bold),
                Location = loc,
                Size = size,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Gold;
            b.FlatAppearance.MouseOverBackColor = PanelHover;
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(28, 22, 36);
            return b;
        }

        public static void SetHighlight(Button btn, bool on)
        {
            if (on)
            {
                btn.FlatAppearance.BorderColor = GoldHi;
                btn.BackColor = PanelActive;
                btn.ForeColor = GoldHi;
            }
            else
            {
                btn.FlatAppearance.BorderColor = Color.FromArgb(120, Gold.R, Gold.G, Gold.B);
                btn.BackColor = Panel;
                btn.ForeColor = Cream;
            }
        }
    }
}
