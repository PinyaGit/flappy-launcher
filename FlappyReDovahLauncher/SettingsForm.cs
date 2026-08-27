using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    internal enum SettingsAction
    {
        None,
        Repair,
        GetVr,
        RemoveVr,
        BugReport,
        Uninstall
    }

    /// <summary>Language, downloads, repair / VR / bugreport / uninstall.</summary>
    internal sealed class SettingsForm : Form
    {
        public SettingsAction Action { get; private set; }

        private readonly bool _busy;
        private readonly bool _installed;
        private readonly bool _supportsVr;

        private Label _title;
        private Label _hint;
        private Label _langLabel;
        private Label _dlLabel;
        private Label _toolsLabel;
        private Button _btnRu;
        private Button _btnEn;
        private readonly Button[] _dlBtns = new Button[4];
        private Button _btnRepair;
        private Button _btnVr;
        private Button _btnBug;
        private Button _btnUninstall;
        private Button _btnClose;

        public SettingsForm(bool busy, bool installed, bool supportsVr)
        {
            _busy = busy;
            _installed = installed;
            _supportsVr = supportsVr;
            Action = SettingsAction.None;

            Text = Constants.LAUNCHER_NAME;
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(460, 560);

            _title = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 12),
                Size = new Size(420, 30)
            };
            _hint = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 44),
                Size = new Size(420, 40)
            };
            _langLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(30, 92),
                Size = new Size(400, 22)
            };
            _btnRu = FlappyTheme.MakeGoldButton("Русский", new Point(30, 116), new Size(190, 40), primary: false);
            _btnEn = FlappyTheme.MakeGoldButton("English", new Point(240, 116), new Size(190, 40), primary: false);
            _btnRu.Click += (s, e) => SetLang(Loc.Ru);
            _btnEn.Click += (s, e) => SetLang(Loc.En);

            _dlLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(30, 168),
                Size = new Size(400, 22)
            };
            int dlX = 30;
            for (int i = 0; i < 4; i++)
            {
                int n = i + 1;
                var b = FlappyTheme.MakeGoldButton(n.ToString(), new Point(dlX, 192), new Size(90, 36), primary: false);
                int captured = n;
                b.Click += (s, e) => SetWorkers(captured);
                _dlBtns[i] = b;
                dlX += 100;
            }

            _toolsLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(30, 244),
                Size = new Size(400, 22)
            };

            _btnRepair = FlappyTheme.MakeGoldButton("", new Point(30, 270), new Size(400, 40), primary: true);
            _btnVr = FlappyTheme.MakeGoldButton("", new Point(30, 318), new Size(400, 40), primary: true);
            _btnBug = FlappyTheme.MakeGoldButton("", new Point(30, 366), new Size(400, 40), primary: true);
            _btnUninstall = FlappyTheme.MakeGoldButton("", new Point(30, 414), new Size(400, 40), primary: false);
            _btnUninstall.ForeColor = Color.FromArgb(220, 160, 140);
            _btnClose = FlappyTheme.MakeGoldButton("", new Point(155, 478), new Size(150, 36), primary: false);
            _btnClose.DialogResult = DialogResult.Cancel;
            _btnClose.ForeColor = FlappyTheme.Mute;
            _btnClose.BackColor = FlappyTheme.CancelBack;

            _btnRepair.Click += (s, e) => Finish(SettingsAction.Repair);
            _btnVr.Click += (s, e) => Finish(
                PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                    ? SettingsAction.GetVr
                    : SettingsAction.RemoveVr);
            _btnBug.Click += (s, e) => Finish(SettingsAction.BugReport);
            _btnUninstall.Click += (s, e) => Finish(SettingsAction.Uninstall);

            Controls.Add(_title);
            Controls.Add(_hint);
            Controls.Add(_langLabel);
            Controls.Add(_btnRu);
            Controls.Add(_btnEn);
            Controls.Add(_dlLabel);
            foreach (var b in _dlBtns) Controls.Add(b);
            Controls.Add(_toolsLabel);
            Controls.Add(_btnRepair);
            Controls.Add(_btnVr);
            Controls.Add(_btnBug);
            Controls.Add(_btnUninstall);
            Controls.Add(_btnClose);
            CancelButton = _btnClose;

            ApplyTexts();
        }

        private void SetLang(string lang)
        {
            Loc.SetLanguage(lang);
            LauncherSettings.Language = lang;
            LauncherSettings.Save();
            ApplyTexts();
        }

        private void SetWorkers(int n)
        {
            n = LauncherSettings.ClampWorkers(n);
            LauncherSettings.DownloadParallelism = n;
            LauncherSettings.Save();
            LauncherSettings.ApplyToRuntime();
            ApplyTexts();
        }

        private void Finish(SettingsAction action)
        {
            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyTexts()
        {
            _title.Text = Loc.T("settings");
            string ch = !_installed
                ? Loc.T("channel_none")
                : (PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                    ? Loc.T("channel_ae_short")
                    : Loc.T("channel_full_short"));
            _hint.Text = Loc.F("settings_hint", GameCatalog.Current.Title, ch);
            _langLabel.Text = Loc.T("language");
            _dlLabel.Text = Loc.T("downloads");
            _toolsLabel.Text = Loc.T("settings_game_tools");
            _btnRepair.Text = Loc.T("repair");
            _btnVr.Text = PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                ? Loc.T("get_vr")
                : Loc.T("remove_vr");
            _btnBug.Text = Loc.T("bugreport");
            _btnUninstall.Text = Loc.T("uninstall");
            _btnClose.Text = Loc.T("close");

            FlappyTheme.SetHighlight(_btnRu, Loc.IsRu);
            FlappyTheme.SetHighlight(_btnEn, !Loc.IsRu);
            int w = LauncherSettings.ClampWorkers(LauncherSettings.DownloadParallelism);
            for (int i = 0; i < 4; i++)
                FlappyTheme.SetHighlight(_dlBtns[i], (i + 1) == w);

            bool toolsOk = _installed && !_busy;
            _btnRepair.Enabled = toolsOk;
            _btnVr.Enabled = toolsOk && _supportsVr;
            _btnUninstall.Enabled = toolsOk;
            _btnBug.Enabled = toolsOk;
        }

        public static SettingsAction ShowSettings(IWin32Window owner, bool busy)
        {
            using (var f = new SettingsForm(
                busy,
                PackageInstaller.IsInstalled(),
                GameCatalog.Current != null && GameCatalog.Current.SupportsVr))
            {
                var r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                if (r != DialogResult.OK) return SettingsAction.None;
                return f.Action;
            }
        }
    }
}
