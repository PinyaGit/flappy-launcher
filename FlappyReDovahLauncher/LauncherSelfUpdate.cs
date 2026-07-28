using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Self-update for the launcher exe (not the modpack).
    /// CDN: launcher/version.json + zip with exe/7za.
    /// </summary>
    internal static class LauncherSelfUpdate
    {
        private const string SkipArg = "--skip-self-update";

        /// <summary>
        /// Returns true if the process should exit (updater will restart the new exe).
        /// Network/parse failures are soft — launcher continues.
        /// </summary>
        public static bool TryUpdateAndExit(string[] args)
        {
            if (!Constants.CHECK_LAUNCHER_UPDATES)
                return false;

            if (args != null)
            {
                foreach (var a in args)
                {
                    if (string.Equals(a, SkipArg, StringComparison.OrdinalIgnoreCase))
                    {
                        LauncherLog.Info("Self-update skipped via " + SkipArg);
                        return false;
                    }
                }
            }

            try
            {
                return RunUpdateFlow();
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Self-update failed (continuing)", ex);
                // Soft fail — do not block launch
                return false;
            }
        }

        private static bool RunUpdateFlow()
        {
            Version local = GetLocalVersion();
            LauncherLog.Info("Launcher version local=" + local);

            string jsonUrl = Constants.LAUNCHER_VERSION_URL;
            string json;
            try
            {
                // Short timeout path: use HttpDownloader which shares HttpClient (long timeout).
                // Prefer a dedicated quick check — still OK for local LAN/CDN.
                json = HttpDownloader.ReadAllText(jsonUrl);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Self-update: cannot fetch version.json — " + ex.Message);
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                LauncherLog.Warn("Self-update: empty version.json");
                return false;
            }

            var man = ParseManifest(json);
            if (man == null || string.IsNullOrWhiteSpace(man.version))
            {
                LauncherLog.Warn("Self-update: invalid version.json");
                return false;
            }

            Version remote;
            if (!Version.TryParse(NormalizeVersion(man.version), out remote))
            {
                LauncherLog.Warn("Self-update: bad remote version " + man.version);
                return false;
            }

            LauncherLog.Info("Launcher version remote=" + remote);

            if (remote <= local)
            {
                LauncherLog.Info("Self-update: up to date");
                return false;
            }

            bool mandatory = man.mandatory;
            string notes = string.IsNullOrWhiteSpace(man.notes) ? "" : "\n\n" + man.notes.Trim();

            if (!mandatory)
            {
                var r = MessageBox.Show(
                    "A new launcher version is available.\n\n" +
                    "Installed:  " + FormatVer(local) + "\n" +
                    "Available:  " + FormatVer(remote) + notes + "\n\n" +
                    "Update now?\n(Game install folder will not be deleted.)",
                    "Flappy Re-Dovah — Update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (r != DialogResult.Yes)
                {
                    LauncherLog.Info("Self-update: user declined");
                    return false;
                }
            }
            else
            {
                MessageBox.Show(
                    "A required launcher update will be installed.\n\n" +
                    "Installed:  " + FormatVer(local) + "\n" +
                    "Available:  " + FormatVer(remote) + notes,
                    "Flappy Re-Dovah — Required update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            string zipUrl = ResolvePackageUrl(man.url);
            if (string.IsNullOrEmpty(zipUrl))
                throw new FlappyException("version.json has no package url.");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string workRoot = Path.Combine(Path.GetTempPath(), "FlappyLauncherUpdate_" + Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(workRoot, "launcher.zip");
            string extractDir = Path.Combine(workRoot, "payload");
            Directory.CreateDirectory(workRoot);
            Directory.CreateDirectory(extractDir);

            try
            {
                Cursor.Current = Cursors.WaitCursor;
                LauncherLog.Info("Self-update download " + zipUrl);
                HttpDownloader.Download(
                    zipUrl,
                    zipPath,
                    man.size > 0 ? man.size : 0,
                    man.sha256,
                    null,
                    System.Threading.CancellationToken.None);

                if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < 100)
                    throw new FlappyException("Downloaded launcher package is empty.");

                ZipFile.ExtractToDirectory(zipPath, extractDir);

                // Accept zip with files at root OR single top-level folder
                string payload = ResolvePayloadRoot(extractDir);
                string localExeName = Path.GetFileName(
                    System.Windows.Forms.Application.ExecutablePath);
                if (!File.Exists(Path.Combine(payload, "Flappy Re-Dovah.exe")) &&
                    !File.Exists(Path.Combine(payload, localExeName)))
                {
                    string[] exes = Directory.GetFiles(payload, "*.exe", SearchOption.TopDirectoryOnly);
                    if (exes.Length == 0)
                        throw new FlappyException(
                            "Launcher package has no .exe in the root.\n\nCheck the zip on CDN.");
                }

                string batPath = Path.Combine(workRoot, "apply_update.bat");
                string exeName = localExeName;
                int pid = Process.GetCurrentProcess().Id;
                WriteApplyScript(batPath, pid, payload, baseDir, exeName);

                LauncherLog.Info("Self-update applying via " + batPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    WorkingDirectory = workRoot,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                return true; // exit so files can be replaced
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private static string ResolvePayloadRoot(string extractDir)
        {
            // Prefer directory that contains Flappy Re-Dovah.exe
            string direct = Path.Combine(extractDir, "Flappy Re-Dovah.exe");
            if (File.Exists(direct)) return extractDir;

            string[] dirs = Directory.GetDirectories(extractDir);
            foreach (var d in dirs)
            {
                if (File.Exists(Path.Combine(d, "Flappy Re-Dovah.exe")))
                    return d;
            }
            // single nested folder without exact name
            if (dirs.Length == 1)
            {
                string[] exes = Directory.GetFiles(dirs[0], "*.exe", SearchOption.TopDirectoryOnly);
                if (exes.Length > 0) return dirs[0];
            }
            return extractDir;
        }

        private static void WriteApplyScript(string batPath, int pid, string src, string dst, string exeName)
        {
            // Wait for launcher PID to exit, copy files, restart, cleanup.
            // Does NOT touch Flappy Re-Dovah\ game folder (only overwrites files present in payload).
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal EnableExtensions");
            sb.AppendLine("set \"PID=" + pid + "\"");
            sb.AppendLine("set \"SRC=" + src.TrimEnd('\\') + "\"");
            sb.AppendLine("set \"DST=" + dst.TrimEnd('\\') + "\"");
            sb.AppendLine("set \"EXE=" + exeName + "\"");
            sb.AppendLine("set /a TRIES=0");
            sb.AppendLine(":wait");
            sb.AppendLine("set /a TRIES+=1");
            sb.AppendLine("if %TRIES% GTR 120 goto copy");
            sb.AppendLine("tasklist /FI \"PID eq %PID%\" 2>nul | findstr /I \"%PID%\" >nul");
            sb.AppendLine("if not errorlevel 1 (");
            sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("  goto wait");
            sb.AppendLine(")");
            sb.AppendLine(":copy");
            sb.AppendLine("ping -n 2 127.0.0.1 >nul");
            // Copy only files from payload into launcher dir (no delete of game install)
            sb.AppendLine("xcopy /Y /Q /I \"%SRC%\\*\" \"%DST%\\\" >nul");
            sb.AppendLine("if exist \"%DST%\\%EXE%\" (");
            sb.AppendLine("  start \"\" \"%DST%\\%EXE%\" " + SkipArg);
            sb.AppendLine(") else (");
            sb.AppendLine("  start \"\" explorer \"%DST%\"");
            sb.AppendLine(")");
            // Clean temp work root (parent of SRC)
            sb.AppendLine("ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("rd /s /q \"%~dp0\" 2>nul");
            File.WriteAllText(batPath, sb.ToString(), Encoding.ASCII);
        }

        private static LauncherVersionManifest ParseManifest(string json)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            return ser.Deserialize<LauncherVersionManifest>(json);
        }

        private static string ResolvePackageUrl(string urlOrRelative)
        {
            if (string.IsNullOrWhiteSpace(urlOrRelative))
                return Constants.CombineBase("launcher/Flappy-Re-Dovah-Launcher.zip");
            urlOrRelative = urlOrRelative.Trim();
            if (urlOrRelative.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                urlOrRelative.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                urlOrRelative.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return urlOrRelative;
            return Constants.CombineBase(urlOrRelative);
        }

        public static Version GetLocalVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var fvi = FileVersionInfo.GetVersionInfo(asm.Location);
                if (!string.IsNullOrEmpty(fvi.FileVersion))
                {
                    Version v;
                    if (Version.TryParse(NormalizeVersion(fvi.FileVersion), out v))
                        return v;
                }
                return asm.GetName().Version ?? new Version(0, 0, 0, 0);
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }

        private static string NormalizeVersion(string v)
        {
            v = (v ?? "").Trim();
            // allow "1.0.1" or "1.0.1.0"
            var parts = v.Split('.');
            if (parts.Length == 1) return v + ".0.0.0";
            if (parts.Length == 2) return v + ".0.0";
            if (parts.Length == 3) return v + ".0";
            return v;
        }

        private static string FormatVer(Version v)
        {
            if (v == null) return "?";
            if (v.Revision > 0) return v.ToString();
            if (v.Build > 0) return v.Major + "." + v.Minor + "." + v.Build;
            return v.Major + "." + v.Minor;
        }
    }

    /// <summary>CDN launcher/version.json</summary>
    internal sealed class LauncherVersionManifest
    {
        public string version { get; set; }
        /// <summary>Relative to PACKAGES_BASE_URL or absolute URL.</summary>
        public string url { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
        public bool mandatory { get; set; }
        public string notes { get; set; }
    }
}
