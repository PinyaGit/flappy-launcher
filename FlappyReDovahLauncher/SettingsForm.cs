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
        Uninstall,
        Rollback
    }

    /// <summary>Clean tabbed settings: [General & Network] and [Modpack Tools].</summary>
    internal sealed class SettingsForm : Form
    {
        public SettingsAction Action { get; private set; }

        private readonly bool _busy;
        private readonly bool _installed;
        private readonly bool _supportsVr;

        private Label _title;
        private Label _hint;

        // Tabs
        private Button _btnTabGeneral;
        private Button _btnTabTools;
        private Panel _pnlGeneral;
        private Panel _pnlTools;

        // Tab 1: General & Network
        private Label _langLabel;
        private Button _btnRu;
        private Button _btnEn;

        private Label _speedLabel;
        private Button _btnSpeed0;
        private Button _btnSpeed5;
        private Button _btnSpeed10;
        private Button _btnSpeed25;
        private Button _btnSpeed50;

        private Label _quickAccessLabel;
        private Button _btnOpenGameDir;
        private Button _btnOpenUserMods;
        private Button _btnOpenLog;

        // Tab 2: Modpack Tools
        private Label _maintenanceLabel;
        private Button _btnRepair;
        private Button _btnClearShaders;
        private Button _btnRollback;
        private Button _btnVr;

        private Label _supportLabel;
        private Button _btnBug;
        private Button _btnUninstall;

        // Bottom
        private Button _btnClose;

        public SettingsForm(bool busy, bool installed, bool supportsVr)
        {
            _busy = busy;
            _installed = installed;
            _supportsVr = supportsVr;
            Action = SettingsAction.None;

            Text = Constants.LAUNCHER_NAME;
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(480, 500);

            _title = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 10),
                Size = new Size(440, 28)
            };
            _hint = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 38),
                Size = new Size(440, 34)
            };

            // Tab bar
            _btnTabGeneral = FlappyTheme.MakeGoldButton("", new Point(24, 76), new Size(210, 34), primary: true);
            _btnTabTools = FlappyTheme.MakeGoldButton("", new Point(246, 76), new Size(210, 34), primary: false);
            _btnTabGeneral.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
            _btnTabTools.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);

            _btnTabGeneral.Click += (s, e) => SwitchTab(0);
            _btnTabTools.Click += (s, e) => SwitchTab(1);

            // ==================== PANEL 1: GENERAL & NETWORK ====================
            _pnlGeneral = new Panel
            {
                Location = new Point(20, 118),
                Size = new Size(440, 320),
                BackColor = Color.Transparent
            };

            _langLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 4),
                Size = new Size(430, 20)
            };
            _btnRu = FlappyTheme.MakeGoldButton("Русский", new Point(4, 26), new Size(210, 34), primary: false);
            _btnEn = FlappyTheme.MakeGoldButton("English", new Point(226, 26), new Size(210, 34), primary: false);
            _btnRu.Click += (s, e) => SetLang(Loc.Ru);
            _btnEn.Click += (s, e) => SetLang(Loc.En);

            _speedLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 72),
                Size = new Size(430, 20)
            };
            _btnSpeed0 = FlappyTheme.MakeGoldButton("∞", new Point(4, 94), new Size(98, 32), primary: false);
            _btnSpeed5 = FlappyTheme.MakeGoldButton("5 MB", new Point(108, 94), new Size(76, 32), primary: false);
            _btnSpeed10 = FlappyTheme.MakeGoldButton("10 MB", new Point(190, 94), new Size(78, 32), primary: false);
            _btnSpeed25 = FlappyTheme.MakeGoldButton("25 MB", new Point(274, 94), new Size(78, 32), primary: false);
            _btnSpeed50 = FlappyTheme.MakeGoldButton("50 MB", new Point(358, 94), new Size(78, 32), primary: false);

            _btnSpeed0.Click += (s, e) => SetSpeedLimit(0);
            _btnSpeed5.Click += (s, e) => SetSpeedLimit(5);
            _btnSpeed10.Click += (s, e) => SetSpeedLimit(10);
            _btnSpeed25.Click += (s, e) => SetSpeedLimit(25);
            _btnSpeed50.Click += (s, e) => SetSpeedLimit(50);

            _quickAccessLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 142),
                Size = new Size(430, 20)
            };
            _btnOpenGameDir = FlappyTheme.MakeGoldButton("", new Point(4, 166), new Size(210, 36), primary: false);
            _btnOpenUserMods = FlappyTheme.MakeGoldButton("", new Point(226, 166), new Size(210, 36), primary: false);
            _btnOpenLog = FlappyTheme.MakeGoldButton("", new Point(4, 210), new Size(432, 34), primary: false);

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

            _pnlGeneral.Controls.Add(_langLabel);
            _pnlGeneral.Controls.Add(_btnRu);
            _pnlGeneral.Controls.Add(_btnEn);
            _pnlGeneral.Controls.Add(_speedLabel);
            _pnlGeneral.Controls.Add(_btnSpeed0);
            _pnlGeneral.Controls.Add(_btnSpeed5);
            _pnlGeneral.Controls.Add(_btnSpeed10);
            _pnlGeneral.Controls.Add(_btnSpeed25);
            _pnlGeneral.Controls.Add(_btnSpeed50);
            _pnlGeneral.Controls.Add(_quickAccessLabel);
            _pnlGeneral.Controls.Add(_btnOpenGameDir);
            _pnlGeneral.Controls.Add(_btnOpenUserMods);
            _pnlGeneral.Controls.Add(_btnOpenLog);

            // ==================== PANEL 2: MODPACK TOOLS ====================
            _pnlTools = new Panel
            {
                Location = new Point(20, 118),
                Size = new Size(440, 320),
                BackColor = Color.Transparent,
                Visible = false
            };

            _maintenanceLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 4),
                Size = new Size(430, 20)
            };
            _btnRepair = FlappyTheme.MakeGoldButton("", new Point(4, 26), new Size(210, 38), primary: true);
            _btnClearShaders = FlappyTheme.MakeGoldButton("", new Point(226, 26), new Size(210, 38), primary: false);
            _btnRollback = FlappyTheme.MakeGoldButton("", new Point(4, 72), new Size(210, 38), primary: false);
            _btnVr = FlappyTheme.MakeGoldButton("", new Point(226, 72), new Size(210, 38), primary: true);

            _btnRepair.Click += (s, e) => Finish(SettingsAction.Repair);
            _btnRollback.Click += (s, e) => Finish(SettingsAction.Rollback);
            _btnClearShaders.Click += (s, e) =>
            {
                try
                {
                    PackageInstaller.ClearShaderCache();
                    MessageBox.Show(Loc.T("shaders_cleared_msg"), Loc.T("clear_shaders"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("clear_shaders")); }
            };
            _btnVr.Click += (s, e) => Finish(
                PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                    ? SettingsAction.GetVr
                    : SettingsAction.RemoveVr);

            _supportLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 126),
                Size = new Size(430, 20)
            };
            _btnBug = FlappyTheme.MakeGoldButton("", new Point(4, 150), new Size(432, 36), primary: false);
            _btnUninstall = FlappyTheme.MakeGoldButton("", new Point(4, 194), new Size(432, 36), primary: false);
            _btnUninstall.ForeColor = Color.FromArgb(230, 140, 120);

            _btnBug.Click += (s, e) => Finish(SettingsAction.BugReport);
            _btnUninstall.Click += (s, e) => Finish(SettingsAction.Uninstall);

            _pnlTools.Controls.Add(_maintenanceLabel);
            _pnlTools.Controls.Add(_btnRepair);
            _pnlTools.Controls.Add(_btnClearShaders);
            _pnlTools.Controls.Add(_btnRollback);
            _pnlTools.Controls.Add(_btnVr);
            _pnlTools.Controls.Add(_supportLabel);
            _pnlTools.Controls.Add(_btnBug);
            _pnlTools.Controls.Add(_btnUninstall);

            // Close button
            _btnClose = FlappyTheme.MakeGoldButton("", new Point(175, 450), new Size(130, 34), primary: false);
            _btnClose.DialogResult = DialogResult.Cancel;
            _btnClose.ForeColor = FlappyTheme.Mute;
            _btnClose.BackColor = FlappyTheme.CancelBack;

            Controls.Add(_title);
            Controls.Add(_hint);
            Controls.Add(_btnTabGeneral);
            Controls.Add(_btnTabTools);
            Controls.Add(_pnlGeneral);
            Controls.Add(_pnlTools);
            Controls.Add(_btnClose);
            CancelButton = _btnClose;

            ApplyTexts();
            UpdateSpeedButtonsHighlight();
        }

        private void SwitchTab(int tabIndex)
        {
            _pnlGeneral.Visible = (tabIndex == 0);
            _pnlTools.Visible = (tabIndex == 1);
            FlappyTheme.SetHighlight(_btnTabGeneral, tabIndex == 0);
            FlappyTheme.SetHighlight(_btnTabTools, tabIndex == 1);
        }

        private void SetLang(string lang)
        {
            Loc.SetLanguage(lang);
            LauncherSettings.Language = lang;
            LauncherSettings.Save();
            ApplyTexts();
        }

        private void SetSpeedLimit(int mbps)
        {
            LauncherSettings.DownloadSpeedLimitMBps = mbps;
            LauncherSettings.Save();
            UpdateSpeedButtonsHighlight();
            LauncherLog.Info("Speed limit set to: " + (mbps <= 0 ? "Unlimited" : mbps + " MB/s"));
        }

        private void UpdateSpeedButtonsHighlight()
        {
            int cur = LauncherSettings.DownloadSpeedLimitMBps;
            FlappyTheme.SetHighlight(_btnSpeed0, cur <= 0);
            FlappyTheme.SetHighlight(_btnSpeed5, cur == 5);
            FlappyTheme.SetHighlight(_btnSpeed10, cur == 10);
            FlappyTheme.SetHighlight(_btnSpeed25, cur == 25);
            FlappyTheme.SetHighlight(_btnSpeed50, cur == 50);
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

            _btnTabGeneral.Text = Loc.T("tab_general");
            _btnTabTools.Text = Loc.T("tab_tools");

            // Tab 1
            _langLabel.Text = Loc.T("language");
            _speedLabel.Text = Loc.T("speed_limit");
            _btnSpeed0.Text = Loc.T("speed_unlimited");
            _btnSpeed5.Text = Loc.T("speed_5mb");
            _btnSpeed10.Text = Loc.T("speed_10mb");
            _btnSpeed25.Text = Loc.T("speed_25mb");
            _btnSpeed50.Text = Loc.T("speed_50mb");

            _quickAccessLabel.Text = Loc.T("section_quick_access");
            _btnOpenGameDir.Text = Loc.T("open_game_dir");
            _btnOpenUserMods.Text = Loc.T("open_user_mods");
            _btnOpenLog.Text = Loc.T("open_log");

            // Tab 2
            _maintenanceLabel.Text = Loc.T("section_maintenance");
            _btnRepair.Text = Loc.T("repair");
            _btnClearShaders.Text = Loc.T("clear_shaders");
            _btnRollback.Text = Loc.IsRu ? "Версии / Откат" : "Versions / Rollback";
            _btnVr.Text = PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                ? Loc.T("get_vr")
                : Loc.T("remove_vr");

            _supportLabel.Text = Loc.T("section_support");
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
            _btnRollback.Enabled = toolsOk;
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
