using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
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
        Rollback,
        Compact
    }

    /// <summary>Tabbed settings: [General], [Network], and [Modpack Tools].</summary>
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
        private Button _btnTabNetwork;
        private Button _btnTabTools;
        private Panel _pnlGeneral;
        private Panel _pnlNetwork;
        private Panel _pnlTools;

        // Tab 1: General
        private Label _langLabel;
        private Button _btnRu;
        private Button _btnEn;

        private Label _quickAccessLabel;
        private Button _btnOpenGameDir;
        private Button _btnOpenUserMods;
        private Button _btnSystemDoctor;

        private Label _displayLabel;
        private Button _btnPresetPerf;
        private Button _btnPresetBalanced;
        private Button _btnPresetUltra;

        // Tab 2: Network
        private Label _speedLabel;
        private Label _speedValueLabel;
        private FlappySlider _speedSlider;
        private Label _speedTicksMin;
        private Label _speedTicksMid;
        private Label _speedTicksMax;

        private Label _cacheHeaderLabel;
        private Label _cacheLabel;
        private Button _btnClearCache;

        private Label _cdnLabel;
        private Button _btnPingCdn;
        private Label _lblPingResult;

        // Tab 3: Modpack Tools
        private Label _maintenanceLabel;
        private Button _btnRepair;
        private Button _btnClearCacheAndOverwrite;
        private Button _btnRollback;
        private Button _btnCompact;
        private Button _btnVr;
        private Button _btnBackupSaves;

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
            ClientSize = new Size(500, 520);

            _title = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 10),
                Size = new Size(460, 28)
            };
            _hint = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 38),
                Size = new Size(460, 34)
            };

            // 3-tab bar
            _btnTabGeneral = FlappyTheme.MakeGoldButton("", new Point(20, 76), new Size(148, 34), primary: true);
            _btnTabNetwork = FlappyTheme.MakeGoldButton("", new Point(176, 76), new Size(148, 34), primary: false);
            _btnTabTools = FlappyTheme.MakeGoldButton("", new Point(332, 76), new Size(148, 34), primary: false);

            _btnTabGeneral.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _btnTabNetwork.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _btnTabTools.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);

            _btnTabGeneral.Click += (s, e) => SwitchTab(0);
            _btnTabNetwork.Click += (s, e) => SwitchTab(1);
            _btnTabTools.Click += (s, e) => SwitchTab(2);

            // ==================== PANEL 1: GENERAL ====================
            _pnlGeneral = new Panel
            {
                Location = new Point(20, 118),
                Size = new Size(460, 340),
                BackColor = Color.Transparent
            };

            _langLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 4),
                Size = new Size(450, 20)
            };
            _btnRu = FlappyTheme.MakeGoldButton("Русский", new Point(4, 26), new Size(220, 36), primary: false);
            _btnEn = FlappyTheme.MakeGoldButton("English", new Point(234, 26), new Size(220, 36), primary: false);
            _btnRu.Click += (s, e) => SetLang(Loc.Ru);
            _btnEn.Click += (s, e) => SetLang(Loc.En);

            _quickAccessLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 78),
                Size = new Size(450, 20)
            };
            _btnOpenGameDir = FlappyTheme.MakeGoldButton("", new Point(4, 102), new Size(220, 38), primary: false);
            _btnOpenUserMods = FlappyTheme.MakeGoldButton("", new Point(234, 102), new Size(220, 38), primary: false);
            _btnSystemDoctor = FlappyTheme.MakeGoldButton("", new Point(4, 146), new Size(450, 34), primary: false);

            _btnOpenGameDir.Click += (s, e) =>
            {
                try
                {
                    string root = GameCatalog.InstallRoot;
                    if (Directory.Exists(root))
                        Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
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
                    Directory.CreateDirectory(dir);
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("open_user_mods")); }
            };

            _btnSystemDoctor.Click += (s, e) =>
            {
                SystemDoctor.ShowDialog(this);
            };

            // Display & Presets
            _displayLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 186),
                Size = new Size(450, 20),
                Text = Loc.IsRu ? "Пресеты графики" : "Graphics Presets"
            };

            _btnPresetPerf = FlappyTheme.MakeGoldButton(Loc.T("preset_perf"), new Point(4, 208), new Size(146, 34), primary: false);
            _btnPresetBalanced = FlappyTheme.MakeGoldButton(Loc.T("preset_balanced"), new Point(156, 208), new Size(146, 34), primary: false);
            _btnPresetUltra = FlappyTheme.MakeGoldButton(Loc.T("preset_ultra"), new Point(308, 208), new Size(146, 34), primary: false);

            _btnPresetPerf.Click += (s, e) => ApplyPreset(GraphicsPreset.Performance, Loc.T("preset_perf"));
            _btnPresetBalanced.Click += (s, e) => ApplyPreset(GraphicsPreset.Balanced, Loc.T("preset_balanced"));
            _btnPresetUltra.Click += (s, e) => ApplyPreset(GraphicsPreset.Ultra, Loc.T("preset_ultra"));

            _pnlGeneral.Controls.Add(_langLabel);
            _pnlGeneral.Controls.Add(_btnRu);
            _pnlGeneral.Controls.Add(_btnEn);
            _pnlGeneral.Controls.Add(_quickAccessLabel);
            _pnlGeneral.Controls.Add(_btnOpenGameDir);
            _pnlGeneral.Controls.Add(_btnOpenUserMods);
            _pnlGeneral.Controls.Add(_btnSystemDoctor);
            _pnlGeneral.Controls.Add(_displayLabel);
            _pnlGeneral.Controls.Add(_btnPresetPerf);
            _pnlGeneral.Controls.Add(_btnPresetBalanced);
            _pnlGeneral.Controls.Add(_btnPresetUltra);

            // ==================== PANEL 2: NETWORK ====================
            _pnlNetwork = new Panel
            {
                Location = new Point(20, 118),
                Size = new Size(460, 340),
                BackColor = Color.Transparent,
                Visible = false
            };

            _speedLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 4),
                Size = new Size(220, 20)
            };
            _speedValueLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = FlappyTheme.GoldHi,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(226, 4),
                Size = new Size(228, 20)
            };
            _speedSlider = new FlappySlider
            {
                Location = new Point(4, 28),
                Size = new Size(450, 26),
                Minimum = 0,
                Maximum = 100,
                Value = LauncherSettings.DownloadSpeedLimitMBps
            };
            _speedSlider.ValueChanged += (s, e) => UpdateSpeedValueDisplay(_speedSlider.Value);
            _speedSlider.ValueCommitted += (s, e) => SetSpeedLimit(_speedSlider.Value);

            _speedTicksMin = new Label
            {
                AutoSize = false,
                Text = "0 (∞)",
                ForeColor = FlappyTheme.Mute,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                Location = new Point(4, 56),
                Size = new Size(80, 16)
            };
            _speedTicksMid = new Label
            {
                AutoSize = false,
                Text = "50",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = FlappyTheme.Mute,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                Location = new Point(190, 56),
                Size = new Size(80, 16)
            };
            _speedTicksMax = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = FlappyTheme.Mute,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                Location = new Point(344, 56),
                Size = new Size(110, 16)
            };

            _cacheHeaderLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 86),
                Size = new Size(450, 20),
                Text = Loc.IsRu ? "Управление кэшем загрузок" : "Download Cache Management"
            };
            _cacheLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.TextLight,
                Font = new Font("Segoe UI", 9f),
                Location = new Point(4, 112),
                Size = new Size(220, 36),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _btnClearCache = FlappyTheme.MakeGoldButton(Loc.T("clear_cache"), new Point(234, 110), new Size(220, 36), primary: false);
            _btnClearCache.Click += (s, e) =>
            {
                PackageInstaller.ClearDownloadCache();
                RefreshCacheSize();
                MessageBox.Show(Loc.T("cache_cleared"), Loc.T("clear_cache"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            _cdnLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 164),
                Size = new Size(450, 20),
                Text = Loc.IsRu ? "Связь с CDN сервером" : "CDN Server Connection"
            };
            _btnPingCdn = FlappyTheme.MakeGoldButton(Loc.T("test_cdn"), new Point(4, 188), new Size(220, 36), primary: false);
            _lblPingResult = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Mute,
                Font = new Font("Segoe UI Semibold", 9.5f),
                Location = new Point(234, 188),
                Size = new Size(220, 36),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _btnPingCdn.Click += (s, e) => TestCdnPing();

            _pnlNetwork.Controls.Add(_speedLabel);
            _pnlNetwork.Controls.Add(_speedValueLabel);
            _pnlNetwork.Controls.Add(_speedSlider);
            _pnlNetwork.Controls.Add(_speedTicksMin);
            _pnlNetwork.Controls.Add(_speedTicksMid);
            _pnlNetwork.Controls.Add(_speedTicksMax);
            _pnlNetwork.Controls.Add(_cacheHeaderLabel);
            _pnlNetwork.Controls.Add(_cacheLabel);
            _pnlNetwork.Controls.Add(_btnClearCache);
            _pnlNetwork.Controls.Add(_cdnLabel);
            _pnlNetwork.Controls.Add(_btnPingCdn);
            _pnlNetwork.Controls.Add(_lblPingResult);

            // ==================== PANEL 3: MODPACK TOOLS ====================
            _pnlTools = new Panel
            {
                Location = new Point(20, 118),
                Size = new Size(460, 340),
                BackColor = Color.Transparent,
                Visible = false
            };

            _maintenanceLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 2),
                Size = new Size(450, 20)
            };
            _btnRepair = FlappyTheme.MakeGoldButton("", new Point(4, 24), new Size(220, 36), primary: true);
            _btnClearCacheAndOverwrite = FlappyTheme.MakeGoldButton("", new Point(234, 24), new Size(220, 36), primary: false);
            _btnRollback = FlappyTheme.MakeGoldButton("", new Point(4, 66), new Size(220, 36), primary: false);
            _btnCompact = FlappyTheme.MakeGoldButton("", new Point(234, 66), new Size(220, 36), primary: false);
            _btnVr = FlappyTheme.MakeGoldButton("", new Point(4, 108), new Size(220, 36), primary: true);
            _btnBackupSaves = FlappyTheme.MakeGoldButton(Loc.T("backup_saves"), new Point(234, 108), new Size(220, 36), primary: false);

            _btnRepair.Click += (s, e) => Finish(SettingsAction.Repair);
            _btnRollback.Click += (s, e) => Finish(SettingsAction.Rollback);
            _btnCompact.Click += (s, e) => Finish(SettingsAction.Compact);
            _btnClearCacheAndOverwrite.Click += (s, e) =>
            {
                var confirm = MessageBox.Show(
                    Loc.T("clear_cache_ask_confirm"),
                    Loc.T("clear_cache_and_overwrite"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                try
                {
                    var res = PackageInstaller.ClearCacheAndOverwrite();
                    MessageBox.Show(
                        Loc.F("cache_overwrite_cleared_msg", res.dirsCleared, res.overwriteFilesDeleted),
                        Loc.T("clear_cache_and_overwrite"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Loc.T("clear_cache_and_overwrite"));
                }
            };
            _btnVr.Click += (s, e) => Finish(
                PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                    ? SettingsAction.GetVr
                    : SettingsAction.RemoveVr);

            _btnBackupSaves.Click += (s, e) =>
            {
                try
                {
                    int count;
                    string zip = SaveBackupManager.CreateBackup(out count);
                    if (string.IsNullOrEmpty(zip) || count == 0)
                    {
                        MessageBox.Show(Loc.T("backup_saves_none"), Loc.T("backup_saves"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show(Loc.F("backup_saves_done", count, zip), Loc.T("backup_saves"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, Loc.T("backup_saves"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            _supportLabel = new Label
            {
                AutoSize = false,
                ForeColor = FlappyTheme.Gold,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                Location = new Point(4, 160),
                Size = new Size(450, 20)
            };
            _btnBug = FlappyTheme.MakeGoldButton("", new Point(4, 184), new Size(220, 36), primary: false);
            _btnUninstall = FlappyTheme.MakeGoldButton("", new Point(234, 184), new Size(220, 36), primary: false);
            _btnUninstall.ForeColor = Color.FromArgb(230, 140, 120);

            _btnBug.Click += (s, e) => Finish(SettingsAction.BugReport);
            _btnUninstall.Click += (s, e) => Finish(SettingsAction.Uninstall);

            _pnlTools.Controls.Add(_maintenanceLabel);
            _pnlTools.Controls.Add(_btnRepair);
            _pnlTools.Controls.Add(_btnClearCacheAndOverwrite);
            _pnlTools.Controls.Add(_btnRollback);
            _pnlTools.Controls.Add(_btnCompact);
            _pnlTools.Controls.Add(_btnVr);
            _pnlTools.Controls.Add(_btnBackupSaves);
            _pnlTools.Controls.Add(_supportLabel);
            _pnlTools.Controls.Add(_btnBug);
            _pnlTools.Controls.Add(_btnUninstall);

            // Close button
            _btnClose = FlappyTheme.MakeGoldButton("", new Point(180, 468), new Size(140, 36), primary: false);
            _btnClose.DialogResult = DialogResult.Cancel;
            _btnClose.ForeColor = FlappyTheme.Mute;
            _btnClose.BackColor = FlappyTheme.CancelBack;

            Controls.Add(_title);
            Controls.Add(_hint);
            Controls.Add(_btnTabGeneral);
            Controls.Add(_btnTabNetwork);
            Controls.Add(_btnTabTools);
            Controls.Add(_pnlGeneral);
            Controls.Add(_pnlNetwork);
            Controls.Add(_pnlTools);
            Controls.Add(_btnClose);
            CancelButton = _btnClose;

            ApplyTexts();
            RefreshCacheSize();
        }

        private void SwitchTab(int tabIndex)
        {
            _pnlGeneral.Visible = (tabIndex == 0);
            _pnlNetwork.Visible = (tabIndex == 1);
            _pnlTools.Visible = (tabIndex == 2);
            FlappyTheme.SetHighlight(_btnTabGeneral, tabIndex == 0);
            FlappyTheme.SetHighlight(_btnTabNetwork, tabIndex == 1);
            FlappyTheme.SetHighlight(_btnTabTools, tabIndex == 2);

            if (tabIndex == 1)
            {
                RefreshCacheSize();
            }
        }

        private void RefreshCacheSize()
        {
            long bytes = PackageInstaller.GetDownloadCacheSizeBytes();
            string formatted = PackageInstaller.FormatBytes(bytes);
            _cacheLabel.Text = Loc.F("cache_label", formatted);
            _btnClearCache.Enabled = bytes > 0;
        }

        private static readonly HttpClient _pingClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private async void TestCdnPing()
        {
            _btnPingCdn.Enabled = false;
            _lblPingResult.Text = Loc.T("checking");
            _lblPingResult.ForeColor = FlappyTheme.Gold;
            var sw = Stopwatch.StartNew();
            try
            {
                // Ping version.json or remote-config which return 200 OK
                string pingUrl = Constants.LAUNCHER_VERSION_URL;
                using (var req = new HttpRequestMessage(HttpMethod.Head, pingUrl))
                {
                    req.Headers.UserAgent.ParseAdd(Constants.LAUNCHER_NAME);
                    using (var resp = await _pingClient.SendAsync(req).ConfigureAwait(true))
                    {
                        sw.Stop();
                        // Any valid response (200, 304, etc.) proves connectivity
                        if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotModified)
                        {
                            _lblPingResult.ForeColor = Color.FromArgb(120, 230, 140);
                            _lblPingResult.Text = Loc.F("cdn_ping_ok", sw.ElapsedMilliseconds);
                        }
                        else
                        {
                            _lblPingResult.ForeColor = Color.FromArgb(240, 120, 120);
                            _lblPingResult.Text = Loc.T("cdn_ping_fail");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                LauncherLog.Warn("CDN ping test failed: " + ex.Message);
                _lblPingResult.ForeColor = Color.FromArgb(240, 120, 120);
                _lblPingResult.Text = Loc.T("cdn_ping_fail");
            }
            finally
            {
                _btnPingCdn.Enabled = true;
            }
        }

        private void SetLang(string lang)
        {
            Loc.SetLanguage(lang);
            LauncherSettings.Language = lang;
            LauncherSettings.Save();
            ApplyTexts();
            RefreshCacheSize();
        }

        private void SetSpeedLimit(int mbps)
        {
            LauncherSettings.DownloadSpeedLimitMBps = mbps;
            LauncherSettings.Save();
            UpdateSpeedValueDisplay(mbps);
            LauncherLog.Info("Speed limit set to: " + (mbps <= 0 ? "Unlimited" : mbps + " MB/s"));
        }

        private void UpdateSpeedValueDisplay(int mbps)
        {
            if (mbps <= 0)
            {
                _speedValueLabel.Text = Loc.T("speed_unlimited");
            }
            else
            {
                int bits = mbps * 8;
                _speedValueLabel.Text = Loc.IsRu
                    ? $"{mbps} МБ/с (~{bits} Мбит/с)"
                    : $"{mbps} MB/s (~{bits} Mbps)";
            }
        }

        private void Finish(SettingsAction action)
        {
            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyPreset(GraphicsPreset preset, string title)
        {
            int updated;
            if (GameConfigManager.ApplyGraphicsPreset(preset, out updated))
            {
                MessageBox.Show(Loc.F("preset_applied", title, updated), Loc.T("graphics_preset"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(Loc.T("need_install"), Loc.T("graphics_preset"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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
            _btnTabNetwork.Text = Loc.T("tab_network");
            _btnTabTools.Text = Loc.T("tab_tools");

            // Tab 1: General
            _langLabel.Text = Loc.T("language");
            _quickAccessLabel.Text = Loc.T("section_quick_access");
            _btnOpenGameDir.Text = Loc.T("open_game_dir");
            _btnOpenUserMods.Text = Loc.T("open_user_mods");
            _btnSystemDoctor.Text = Loc.T("system_doctor");
            _displayLabel.Text = Loc.IsRu ? "Пресеты графики" : "Graphics Presets";
            _btnPresetPerf.Text = Loc.T("preset_perf");
            _btnPresetBalanced.Text = Loc.T("preset_balanced");
            _btnPresetUltra.Text = Loc.T("preset_ultra");

            // Tab 2: Network
            _speedLabel.Text = Loc.T("speed_limit");
            _speedTicksMax.Text = Loc.IsRu ? "100 МБ/с" : "100 MB/s";
            UpdateSpeedValueDisplay(_speedSlider.Value);
            _cacheHeaderLabel.Text = Loc.IsRu ? "Управление кэшем загрузок" : "Download Cache Management";
            _btnClearCache.Text = Loc.T("clear_cache");
            _cdnLabel.Text = Loc.IsRu ? "Связь с CDN сервером" : "CDN Server Connection";
            _btnPingCdn.Text = Loc.T("test_cdn");

            // Tab 3: Modpack Tools
            _maintenanceLabel.Text = Loc.T("section_maintenance");
            _btnRepair.Text = Loc.T("repair");
            _btnClearCacheAndOverwrite.Text = Loc.T("clear_cache_and_overwrite");
            _btnRollback.Text = Loc.IsRu ? "Версии / Откат" : "Versions / Rollback";
            _btnCompact.Text = Loc.T("compact_btn");
            _btnVr.Text = PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                ? Loc.T("get_vr")
                : Loc.T("remove_vr");

            _btnBackupSaves.Text = Loc.T("backup_saves");

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
            _btnClearCacheAndOverwrite.Enabled = toolsOk;
            _btnUninstall.Enabled = toolsOk;
            _btnBug.Enabled = toolsOk;
            _btnRollback.Enabled = toolsOk;
            _btnCompact.Enabled = toolsOk;
            _btnBackupSaves.Enabled = toolsOk;
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
