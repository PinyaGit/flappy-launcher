using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace FlappyReDovahLauncher
{
    public enum DoctorStatus
    {
        Ok,
        Warning,
        Error
    }

    public sealed class DoctorCheckItem
    {
        public string Title { get; set; }
        public DoctorStatus Status { get; set; }
        public string Details { get; set; }
        public string Recommendation { get; set; }
    }

    /// <summary>
    /// Pre-flight system diagnostics: disk space, path encoding, VC++ runtimes, and DirectX.
    /// </summary>
    internal static class SystemDoctor
    {
        public static List<DoctorCheckItem> RunAllChecks()
        {
            var results = new List<DoctorCheckItem>();

            // 1. Path encoding and characters
            results.Add(CheckGamePath());

            // 2. Free disk space
            results.Add(CheckDiskSpace());

            // 3. Visual C++ 2015-2022 Redistributable
            results.Add(CheckVisualCpp());

            // 4. DirectX runtime
            results.Add(CheckDirectX());

            // 5. Antivirus / Write access
            results.Add(CheckWriteAccess());

            // 6. OneDrive synchronization check for Documents
            results.Add(CheckOneDriveDocuments());

            return results;
        }

        private static DoctorCheckItem CheckGamePath()
        {
            string root = GameCatalog.InstallRoot;
            var item = new DoctorCheckItem
            {
                Title = Loc.IsRu ? "Путь установки игры" : "Game Installation Path"
            };

            if (string.IsNullOrEmpty(root))
            {
                item.Status = DoctorStatus.Warning;
                item.Details = Loc.IsRu ? "Путь ещё не определён" : "Path not defined yet";
                return item;
            }

            bool hasNonAscii = root.Any(c => c > 127);
            if (hasNonAscii)
            {
                item.Status = DoctorStatus.Error;
                item.Details = (Loc.IsRu ? "Путь содержит кириллицу или спецсимволы: " : "Path contains non-ASCII characters: ") + root;
                item.Recommendation = Loc.IsRu
                    ? "Mod Organizer 2, SKSE и многие DLL не поддерживают русские буквы в путях. Переместите игру в путь из английских букв (например, D:\\Games\\Flappy)."
                    : "Mod Organizer 2 and SKSE plugins crash when installed in non-ASCII paths. Move the game to an English path (e.g. D:\\Games\\Flappy).";
                return item;
            }

            item.Status = DoctorStatus.Ok;
            item.Details = root + (Loc.IsRu ? " (корректный ASCII-путь)" : " (valid ASCII path)");
            return item;
        }

        private static DoctorCheckItem CheckDiskSpace()
        {
            var item = new DoctorCheckItem
            {
                Title = Loc.IsRu ? "Свободное место на диске" : "Free Disk Space"
            };

            try
            {
                string root = GameCatalog.InstallRoot;
                string driveName = Path.GetPathRoot(Path.GetFullPath(root));
                var drive = new DriveInfo(driveName);

                long freeGb = drive.AvailableFreeSpace / (1024L * 1024 * 1024);
                item.Details = string.Format("{0}: {1} GB {2}", driveName, freeGb, Loc.IsRu ? "свободно" : "free");

                if (freeGb < 15)
                {
                    item.Status = DoctorStatus.Error;
                    item.Recommendation = Loc.IsRu
                        ? "Критически мало места! Для распаковки и работы игры требуется минимум 25-30 ГБ свободного пространства."
                        : "Critically low disk space! At least 25-30 GB required for smooth gameplay and updates.";
                }
                else if (freeGb < 30)
                {
                    item.Status = DoctorStatus.Warning;
                    item.Recommendation = Loc.IsRu
                        ? "Места на диске маловато. При крупных обновлениях может потребоваться больше места."
                        : "Low disk space. Future delta updates might require additional storage.";
                }
                else
                {
                    item.Status = DoctorStatus.Ok;
                }
            }
            catch (Exception ex)
            {
                item.Status = DoctorStatus.Warning;
                item.Details = Loc.IsRu ? ("Не удалось определить: " + ex.Message) : ("Check failed: " + ex.Message);
            }

            return item;
        }

        private static DoctorCheckItem CheckVisualCpp()
        {
            var item = new DoctorCheckItem
            {
                Title = "Visual C++ 2015-2022 x64"
            };

            bool found = false;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (k != null && Convert.ToInt32(k.GetValue("Installed", 0)) == 1)
                        found = true;
                }
            }
            catch { }

            if (!found)
            {
                try
                {
                    using (var k = Registry.ClassesRoot.OpenSubKey(@"Installer\Dependencies\VC,redist.x64,amd64,14.38,bundle"))
                    {
                        if (k != null) found = true;
                    }
                }
                catch { }
            }

            if (found)
            {
                item.Status = DoctorStatus.Ok;
                item.Details = Loc.IsRu ? "Установлен (x64 runtime активен)" : "Installed (x64 runtime active)";
            }
            else
            {
                item.Status = DoctorStatus.Warning;
                item.Details = Loc.IsRu ? "Не обнаружен в реестре" : "Not detected in registry";
                item.Recommendation = Loc.IsRu
                    ? "Установите Visual C++ Redistributable 2015-2022 x64 с сайта Microsoft, чтобы избежать ошибок 'VCRUNTIME140.dll' или крашей SKSE."
                    : "Install Microsoft Visual C++ 2015-2022 x64 to prevent 'VCRUNTIME140.dll' errors or SKSE crashes.";
            }

            return item;
        }

        private static DoctorCheckItem CheckDirectX()
        {
            var item = new DoctorCheckItem
            {
                Title = "DirectX Runtimes (D3DX9 / XAudio)"
            };

            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string d3d9 = Path.Combine(sys32, "d3dx9_43.dll");
            string xaudio = Path.Combine(sys32, "x3daudio1_7.dll");

            bool hasD3D = File.Exists(d3d9);
            bool hasXAudio = File.Exists(xaudio);

            if (hasD3D && hasXAudio)
            {
                item.Status = DoctorStatus.Ok;
                item.Details = Loc.IsRu ? "Библиотеки DirectX найдены" : "DirectX libraries verified";
            }
            else
            {
                item.Status = DoctorStatus.Warning;
                item.Details = Loc.IsRu ? "Отсутствуют старые библиотеки DirectX 9" : "Legacy DirectX 9 components missing";
                item.Recommendation = Loc.IsRu
                    ? "Рекомендуется установить пакет 'DirectX End-User Runtimes (June 2010)' для корректной работы ENB и звуковых движков."
                    : "Install 'DirectX End-User Runtimes (June 2010)' for ENB and audio mods compatibility.";
            }

            return item;
        }

        private static DoctorCheckItem CheckWriteAccess()
        {
            var item = new DoctorCheckItem
            {
                Title = Loc.IsRu ? "Права на запись в папку игры" : "Folder Write Permissions"
            };

            string root = GameCatalog.InstallRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                item.Status = DoctorStatus.Ok;
                item.Details = Loc.IsRu ? "Папка будет создана при установке" : "Folder will be created on install";
                return item;
            }

            try
            {
                string testFile = Path.Combine(root, ".flappy_write_test_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(testFile, "test", Encoding.UTF8);
                File.Delete(testFile);

                item.Status = DoctorStatus.Ok;
                item.Details = Loc.IsRu ? "Полный доступ на чтение и запись" : "Full read/write access";
            }
            catch (Exception ex)
            {
                item.Status = DoctorStatus.Error;
                item.Details = Loc.IsRu ? ("Ограничен доступ на запись: " + ex.Message) : ("Write permission denied: " + ex.Message);
                item.Recommendation = Loc.IsRu
                    ? "Лаунчер не может записывать файлы в папку. Запустите лаунчер от имени администратора или снимите блокировку антивируса."
                    : "The launcher cannot write to the directory. Run as administrator or add folder to antivirus exclusions.";
            }

            return item;
        }

        public static bool IsOneDriveDocuments(out string docsPath)
        {
            docsPath = "";
            try
            {
                docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docsPath) && docsPath.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static DoctorCheckItem CheckOneDriveDocuments()
        {
            var item = new DoctorCheckItem
            {
                Title = Loc.IsRu ? "Синхронизация OneDrive (Документы)" : "OneDrive Documents Sync"
            };

            string docs;
            if (IsOneDriveDocuments(out docs))
            {
                item.Status = DoctorStatus.Warning;
                item.Details = (Loc.IsRu ? "Папка «Документы» находится в OneDrive: " : "Documents folder is inside OneDrive: ") + docs;
                item.Recommendation = Loc.IsRu
                    ? "Синхронизация OneDrive может блокировать файлы сохранений и логов (CrashLogger, SKSE), вызывая зависания и вылеты игры. Рекомендуется отключить синхронизацию папки 'Документы\\My Games' в параметрах OneDrive."
                    : "OneDrive cloud sync can lock Skyrim save files and crash logs, causing freezes. Consider disabling sync for 'Documents\\My Games' in OneDrive settings.";
            }
            else
            {
                item.Status = DoctorStatus.Ok;
                item.Details = Loc.IsRu ? "Документы не синхронизируются с OneDrive" : "Documents folder is not in OneDrive";
            }

            return item;
        }

        public static void ShowDialog(System.Windows.Forms.IWin32Window owner)
        {
            var checks = RunAllChecks();
            var f = new System.Windows.Forms.Form
            {
                Text = Loc.T("system_doctor"),
                ClientSize = new System.Drawing.Size(560, 480),
                StartPosition = System.Windows.Forms.FormStartPosition.CenterParent,
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            FlappyTheme.ApplyDialog(f);

            var title = new System.Windows.Forms.Label
            {
                Text = Loc.IsRu ? "Диагностика системы" : "System Diagnostics",
                Font = new System.Drawing.Font("Segoe UI Semibold", 12f, System.Drawing.FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new System.Drawing.Point(20, 16),
                Size = new System.Drawing.Size(520, 26)
            };

            var sb = new StringBuilder();
            bool allGood = true;
            foreach (var check in checks)
            {
                string icon = check.Status == DoctorStatus.Ok ? "✔ [OK]" :
                              check.Status == DoctorStatus.Warning ? "⚠ [WARN]" : "✖ [FAIL]";
                if (check.Status != DoctorStatus.Ok) allGood = false;

                sb.AppendLine($"{icon} {check.Title}");
                sb.AppendLine($"   {check.Details}");
                if (!string.IsNullOrEmpty(check.Recommendation))
                {
                    sb.AppendLine($"   → {check.Recommendation}");
                }
                sb.AppendLine();
            }

            if (allGood)
            {
                sb.AppendLine(Loc.IsRu
                    ? "Все проверки пройдены успешно! Система готова к запуску сборки."
                    : "All checks passed! Your system is ready to run the modpack.");
            }

            var txt = new System.Windows.Forms.TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = System.Windows.Forms.ScrollBars.Vertical,
                Location = new System.Drawing.Point(20, 50),
                Size = new System.Drawing.Size(520, 350),
                BackColor = System.Drawing.Color.FromArgb(20, 20, 24),
                ForeColor = FlappyTheme.TextLight,
                Font = new System.Drawing.Font("Consolas", 9.5f),
                Text = sb.ToString()
            };

            var btnClose = FlappyTheme.MakeGoldButton(
                Loc.T("close"),
                new System.Drawing.Point(210, 420),
                new System.Drawing.Size(140, 36),
                primary: true);
            btnClose.DialogResult = System.Windows.Forms.DialogResult.OK;

            f.Controls.Add(title);
            f.Controls.Add(txt);
            f.Controls.Add(btnClose);
            f.AcceptButton = btnClose;

            f.ShowDialog(owner);
        }
    }
}
