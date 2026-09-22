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

    /// <summary>Language, repair / VR / bug report / uninstall.</summary>
    internal sealed class SettingsForm : Form
    {
        public SettingsAction Action { get; private set; }

        private readonly bool _busy;
        private readonly bool _installed;
        private readonly bool _supportsVr;

        private Label _title;
        private Label _hint;
        private Label _langLabel;
        private Label _toolsLabel;
        private Button _btnRu;
        private Button _btnEn;
        private Button _btnRepair;
        private Button _btnVr;
        private Button _btnOpenGameDir;
        private Button _btnOpenUserMods;
        private Button _btnClearShaders;
        private Button _btnOpenLog;
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
            ClientSize = new Size(460, 480);

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
            _btnRu = FlappyTheme.MakeGoldButton("Русский", new Point(30, 116), new Size(195, 38), primary: false);
            _btnEn = FlappyTheme.MakeGoldButton("English", new Point(235, 116), new Size(195, 38), primary: false);
            _btnRu.Click += (s, e) => SetLang(Loc.Ru);
            _btnEn.Click += (s, e) => SetLang(Loc.En);

            _toolsLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(30, 164),
                Size = new Size(400, 22)
            };

            // Two-column layout for fast access
            _btnRepair = FlappyTheme.MakeGoldButton("", new Point(30, 190), new Size(195, 38), primary: true);
            _btnVr = FlappyTheme.MakeGoldButton("", new Point(235, 190), new Size(195, 38), primary: true);

            _btnOpenGameDir = FlappyTheme.MakeGoldButton("", new Point(30, 236), new Size(195, 38), primary: false);
            _btnOpenUserMods = FlappyTheme.MakeGoldButton("", new Point(235, 236), new Size(195, 38), primary: false);

            _btnClearShaders = FlappyTheme.MakeGoldButton("", new Point(30, 282), new Size(195, 38), primary: false);
            _btnOpenLog = FlappyTheme.MakeGoldButton("", new Point(235, 282), new Size(195, 38), primary: false);

            _btnBug = FlappyTheme.MakeGoldButton("", new Point(30, 328), new Size(195, 38), primary: false);
            _btnUninstall = FlappyTheme.MakeGoldButton("", new Point(235, 328), new Size(195, 38), primary: false);
            _btnUninstall.ForeColor = Color.FromArgb(220, 160, 140);

            _btnClose = FlappyTheme.MakeGoldButton("", new Point(155, 400), new Size(150, 36), primary: false);
            _btnClose.DialogResult = DialogResult.Cancel;
            _btnClose.ForeColor = FlappyTheme.Mute;
            _btnClose.BackColor = FlappyTheme.CancelBack;

            _btnRepair.Click += (s, e) => Finish(SettingsAction.Repair);
            _btnVr.Click += (s, e) => Finish(
                PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                    ? SettingsAction.GetVr
                    : SettingsAction.RemoveVr);
            _btnOpenGameDir.Click += (s, e) =>
            {
                try
                {
                    string root = GameCatalog.InstallRoot;
                    if (System.IO.Directory.Exists(root))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = root, UseShellExecute = true });
                    else
                        MessageBox.Show(Loc.T("not_installed"), Loc.T("open_game_dir"));
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("open_game_dir")); }
            };
            _btnOpenUserMods.Click += (s, e) =>
            {
                try
                {
                    string dir = PackageInstaller.UserModsDir;
                    System.IO.Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("open_user_mods")); }
            };
            _btnClearShaders.Click += (s, e) =>
            {
                try
                {
                    PackageInstaller.ClearShaderCache();
                    MessageBox.Show(Loc.T("shaders_cleared_msg"), Loc.T("clear_shaders"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("clear_shaders")); }
            };
            _btnOpenLog.Click += (s, e) =>
            {
                try
                {
                    string log = LauncherLog.LogPath;
                    if (System.IO.File.Exists(log))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = log, UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("open_log")); }
            };
            _btnBug.Click += (s, e) => Finish(SettingsAction.BugReport);
            _btnUninstall.Click += (s, e) => Finish(SettingsAction.Uninstall);

            Controls.Add(_title);
            Controls.Add(_hint);
            Controls.Add(_langLabel);
            Controls.Add(_btnRu);
            Controls.Add(_btnEn);
            Controls.Add(_toolsLabel);
            Controls.Add(_btnRepair);
            Controls.Add(_btnVr);
            Controls.Add(_btnOpenGameDir);
            Controls.Add(_btnOpenUserMods);
            Controls.Add(_btnClearShaders);
            Controls.Add(_btnOpenLog);
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

        private void Finish(SettingsAction action)
        {
            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyTexts()
        {
            _title.Text = Loc.T("settings");
            if (GameCatalog.Current != null && GameCatalog.Current.IsDoom)
            {
                _hint.Text = GameCatalog.Current.Title;
            }
            else
            {
                string ch = !_installed
                    ? Loc.T("channel_none")
                    : (PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                        ? Loc.T("channel_ae_short")
                        : Loc.T("channel_full_short"));
                _hint.Text = Loc.F("settings_hint", GameCatalog.Current.Title, ch);
            }
            _langLabel.Text = Loc.T("language");
            _toolsLabel.Text = Loc.T("settings_game_tools");
            _btnRepair.Text = Loc.T("repair");
            _btnVr.Text = PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                ? Loc.T("get_vr")
                : Loc.T("remove_vr");
            _btnOpenGameDir.Text = Loc.T("open_game_dir");
            _btnOpenUserMods.Text = Loc.T("open_user_mods");
            _btnClearShaders.Text = Loc.T("clear_shaders");
            _btnOpenLog.Text = Loc.T("open_log");
            _btnBug.Text = Loc.T("bugreport");
            _btnUninstall.Text = Loc.T("uninstall");
            _btnClose.Text = Loc.T("close");

            FlappyTheme.SetHighlight(_btnRu, Loc.IsRu);
            FlappyTheme.SetHighlight(_btnEn, !Loc.IsRu);

            bool toolsOk = _installed && !_busy;
            _btnRepair.Enabled = toolsOk;
            _btnVr.Enabled = toolsOk && _supportsVr;

            bool isReDovah = GameCatalog.Current != null &&
                             (GameCatalog.Current.Id.Equals("re-dovah", StringComparison.OrdinalIgnoreCase) ||
                              GameCatalog.Current.Title.IndexOf("Re-Dovah", StringComparison.OrdinalIgnoreCase) >= 0);

            _btnVr.Visible = isReDovah;
            _btnOpenGameDir.Enabled = _installed;
            _btnOpenUserMods.Enabled = true;
            _btnClearShaders.Enabled = toolsOk;
            _btnOpenLog.Enabled = true;
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
