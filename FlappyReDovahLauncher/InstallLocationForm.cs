using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Dialog for selecting game installation directory and optional CompactOS compression.
    /// </summary>
    internal sealed class InstallLocationForm : Form
    {
        public string SelectedPath { get; private set; }
        public bool EnableCompact { get; private set; }

        private readonly GameDefinition _game;
        private readonly long _requiredBytes;

        private Label _lblTitle;
        private Label _lblSubtitle;
        private Label _lblPathHeader;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private Label _lblSpace;

        private Panel _pnlCompact;
        private CheckBox _chkCompact;
        private Label _lblCompactDesc;

        private Button _btnInstall;
        private Button _btnCancel;

        public InstallLocationForm(GameDefinition game)
        {
            _game = game ?? GameCatalog.Current;
            _requiredBytes = (_game != null && _game.IsDoom)
                ? 5L * 1024 * 1024 * 1024
                : 75L * 1024 * 1024 * 1024;

            Text = Loc.IsRu ? ("Установка " + _game.Title) : ("Install " + _game.Title);
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(520, 385);

            InitializeComponents();
            UpdateSpaceInfo();
        }

        private void InitializeComponents()
        {
            _lblTitle = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(24, 14),
                Size = new Size(472, 26),
                Text = Loc.IsRu ? "Выбор папки для установки" : "Choose Installation Folder"
            };

            _lblSubtitle = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(24, 42),
                Size = new Size(472, 34),
                Text = Loc.IsRu
                    ? ("Укажите папку для распаковки " + _game.Title + ". Лаунчер автоматически\nскопирует себя в эту папку и создаст ярлык на Рабочем столе.")
                    : ("Select destination folder for " + _game.Title + ". The launcher will\ncopy itself to this folder and create a desktop shortcut.")
            };

            _lblPathHeader = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(24, 88),
                Text = Loc.IsRu ? "Папка назначения:" : "Destination Folder:"
            };

            _txtPath = new TextBox
            {
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                Location = new Point(24, 110),
                Size = new Size(372, 25),
                Text = GetDefaultInstallPath(_game)
            };
            _txtPath.TextChanged += (s, e) => UpdateSpaceInfo();

            _btnBrowse = FlappyTheme.MakeGoldButton(Loc.IsRu ? "Обзор…" : "Browse…", new Point(404, 108), new Size(92, 27), primary: false);
            _btnBrowse.Click += OnClickBrowse;

            _lblSpace = new Label
            {
                AutoSize = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(24, 142),
                Size = new Size(472, 24)
            };

            // CompactOS Panel
            _pnlCompact = new Panel
            {
                BackColor = FlappyTheme.Panel,
                Location = new Point(24, 174),
                Size = new Size(472, 130)
            };
            _pnlCompact.Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(55, 45, 68), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, _pnlCompact.Width - 1, _pnlCompact.Height - 1);
                }
            };

            _chkCompact = new CheckBox
            {
                AutoSize = true,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(14, 10),
                Text = Loc.IsRu
                    ? "Сжать файлы игры после установки (CompactOS XPRESS8K)"
                    : "Compress game files after installation (CompactOS XPRESS8K)",
                Checked = false,
                Cursor = Cursors.Hand
            };

            _lblCompactDesc = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(34, 36),
                Size = new Size(426, 84),
                Text = Loc.IsRu
                    ? "• Экономит от 25 до 45 ГБ места на диске без удаления файлов.\n" +
                      "• Игра и моды работают обычно (прозрачная распаковка средствами NTFS).\n" +
                      "• Займёт дополнительно 15–30 минут после завершения распаковки.\n" +
                      "• Если пропустить, сжатие всегда можно запустить в Настройках лаунчера."
                    : "• Saves 25 to 45 GB of disk space without deleting any files.\n" +
                      "• Game and mods run transparently with zero performance loss.\n" +
                      "• Takes an additional 15–30 minutes after unpack.\n" +
                      "• You can always run compression later from Launcher Settings."
            };

            _pnlCompact.Controls.Add(_chkCompact);
            _pnlCompact.Controls.Add(_lblCompactDesc);

            // Action Buttons
            _btnInstall = FlappyTheme.MakeGoldButton(Loc.IsRu ? "Установить" : "Install", new Point(256, 326), new Size(130, 36), primary: true);
            _btnInstall.Click += OnClickInstall;

            _btnCancel = FlappyTheme.MakeGoldButton(Loc.IsRu ? "Отмена" : "Cancel", new Point(396, 326), new Size(100, 36), primary: false);
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            AcceptButton = _btnInstall;
            CancelButton = _btnCancel;

            Controls.Add(_lblTitle);
            Controls.Add(_lblSubtitle);
            Controls.Add(_lblPathHeader);
            Controls.Add(_txtPath);
            Controls.Add(_btnBrowse);
            Controls.Add(_lblSpace);
            Controls.Add(_pnlCompact);
            Controls.Add(_btnInstall);
            Controls.Add(_btnCancel);
        }

        private void OnClickBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = Loc.IsRu
                    ? ("Выберите папку для установки " + _game.Title)
                    : ("Select destination folder for " + _game.Title);
                dlg.ShowNewFolderButton = true;
                string current = _txtPath.Text.Trim();
                if (Directory.Exists(current))
                    dlg.SelectedPath = current;
                else
                {
                    string parent = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        dlg.SelectedPath = parent;
                }

                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                {
                    string chosen = dlg.SelectedPath.Trim();
                    // If user selected an existing parent folder (e.g. D:\Games), append game folder name
                    if (!chosen.EndsWith(_game.InstallFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = Path.Combine(chosen, _game.InstallFolderName);
                    }
                    _txtPath.Text = chosen;
                }
            }
        }

        private void UpdateSpaceInfo()
        {
            string path = (_txtPath.Text ?? "").Trim();
            if (string.IsNullOrEmpty(path))
            {
                _lblSpace.Text = "";
                return;
            }

            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root))
                {
                    _lblSpace.Text = "";
                    return;
                }

                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    double reqGb = _requiredBytes / (1024.0 * 1024 * 1024);
                    string driveName = root.TrimEnd('\\');

                    if (drive.AvailableFreeSpace < _requiredBytes)
                    {
                        _lblSpace.ForeColor = Color.FromArgb(240, 95, 95);
                        _lblSpace.Text = Loc.IsRu
                            ? string.Format("Диск {0} — Внимание: свободно {1:F1} ГБ! Требуется минимум ~{2:F0} ГБ.", driveName, freeGb, reqGb)
                            : string.Format("Drive {0} — Low space: {1:F1} GB free! Requires at least ~{2:F0} GB.", driveName, freeGb, reqGb);
                    }
                    else
                    {
                        _lblSpace.ForeColor = FlappyTheme.Gold;
                        _lblSpace.Text = Loc.IsRu
                            ? string.Format("Диск {0} ({1}) — Свободно: {2:F1} ГБ (требуется ~{3:F0} ГБ)", driveName, drive.DriveFormat, freeGb, reqGb)
                            : string.Format("Drive {0} ({1}) — Free: {2:F1} GB (requires ~{3:F0} GB)", driveName, drive.DriveFormat, freeGb, reqGb);
                    }
                    return;
                }
            }
            catch { }

            _lblSpace.ForeColor = FlappyTheme.Mute;
            _lblSpace.Text = "";
        }

        private void OnClickInstall(object sender, EventArgs e)
        {
            string path = (_txtPath.Text ?? "").Trim();
            string error;
            if (!ValidatePath(path, out error))
            {
                MessageBox.Show(this, error, Loc.IsRu ? "Некорректная папка" : "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string full = Path.GetFullPath(path);

            if (Directory.Exists(full))
            {
                try
                {
                    var entries = Directory.GetFileSystemEntries(full);
                    if (entries.Length > 0)
                    {
                        bool isGameDir = File.Exists(Path.Combine(full, "ModOrganizer.exe")) || File.Exists(Path.Combine(full, ".flappy_installed"));
                        if (!isGameDir)
                        {
                            var ask = MessageBox.Show(this,
                                Loc.IsRu
                                    ? ("Выбранная папка не пуста:\n" + full + "\n\nВы уверены, что хотите установить игру в эту папку?")
                                    : ("Selected folder is not empty:\n" + full + "\n\nAre you sure you want to install into this folder?"),
                                _game.Title,
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);
                            if (ask != DialogResult.Yes) return;
                        }
                    }
                }
                catch { }
            }

            SelectedPath = full;
            EnableCompact = _chkCompact.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool ValidatePath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = Loc.IsRu ? "Укажите путь для установки." : "Please specify an installation path.";
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                error = Loc.IsRu ? "Указан недопустимый путь." : "Invalid path specified.";
                return false;
            }

            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(full))
            {
                error = Loc.IsRu ? "Путь должен быть полным (например, D:\\Games\\Flappy)." : "Path must be absolute (e.g. D:\\Games\\Flappy).";
                return false;
            }

            if (string.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                error = Loc.IsRu
                    ? ("Нельзя устанавливать сборку в корень диска. Выберите папку, например " + Path.Combine(root, "Games", _game.InstallFolderName))
                    : "Cannot install directly into root of drive. Choose a subfolder.";
                return false;
            }

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir) && full.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
            {
                error = Loc.IsRu ? "Нельзя устанавливать сборку в системную папку Windows!" : "Cannot install into Windows system folder!";
                return false;
            }

            string pf1 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf2 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if ((!string.IsNullOrEmpty(pf1) && full.StartsWith(pf1, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(pf2) && full.StartsWith(pf2, StringComparison.OrdinalIgnoreCase)))
            {
                error = Loc.IsRu
                    ? ("Установка в «Program Files» крайне не рекомендуется:\n" +
                       "система контроля учетных записей Windows (UAC) блокирует работу скриптов Mod Organizer 2 и плагинов SKSE.\n\n" +
                       "Пожалуйста, выберите другую папку, например:\n" + Path.Combine(root, "Games", _game.InstallFolderName))
                    : "Installing into Program Files is blocked due to Windows UAC virtualization.\nPlease select a folder such as " + Path.Combine(root, "Games", _game.InstallFolderName);
                return false;
            }

            return true;
        }

        private static string GetDefaultInstallPath(GameDefinition game)
        {
            string folderName = game != null ? game.InstallFolderName : "Flappy";
            long reqBytes = (game != null && game.IsDoom) ? 5L * 1024 * 1024 * 1024 : 75L * 1024 * 1024 * 1024;

            try
            {
                string sysRoot = Path.GetPathRoot(Environment.SystemDirectory);
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .OrderByDescending(d =>
                    {
                        bool isSystem = string.Equals(d.Name, sysRoot, StringComparison.OrdinalIgnoreCase);
                        long score = d.AvailableFreeSpace;
                        // Prioritize secondary/game drives with sufficient free space
                        if (!isSystem && d.AvailableFreeSpace >= reqBytes)
                            score += 1000L * 1024 * 1024 * 1024;
                        return score;
                    })
                    .ToList();

                if (drives.Count > 0)
                {
                    string driveRoot = drives[0].RootDirectory.FullName;
                    return Path.Combine(driveRoot, "Games", folderName);
                }
            }
            catch { }

            return Path.Combine(@"C:\Games", folderName);
        }
    }
}
