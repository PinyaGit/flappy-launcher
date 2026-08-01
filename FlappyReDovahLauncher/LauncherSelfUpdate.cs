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
    /// Self-update for Flappy Launcher (not a game/modpack).
    /// CDN: launcher/version.json + Flappy-Launcher.zip (exe + config only).
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
                try
                {
                    MessageBox.Show(
                        "Launcher update failed.\n\n" +
                        FlappyException.FormatForUser(ex) + "\n\n" +
                        "You can keep using this version, or download the latest zip from:\n" +
                        Constants.LAUNCHER_PACKAGE_URL + "\n" +
                        "and replace " + Constants.LAUNCHER_EXE_NAME + " (game folders stay).",
                        Constants.LAUNCHER_NAME + " — Update failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                catch { /* no UI */ }
                return false;
            }
        }

        /// <summary>Download + optional size/sha for launcher ZIP (not game 7z packages).</summary>
        private static void DownloadLauncherZip(string url, string dest, LauncherVersionManifest man)
        {
            var cancel = System.Threading.CancellationToken.None;
            HttpDownloader.DownloadSimple(url, dest, cancel);

            long len = new FileInfo(dest).Length;
            if (man != null && man.size > 0)
            {
                long diff = Math.Abs(len - man.size);
                if (diff > 64 * 1024)
                    throw new FlappyException(
                        "Launcher package size mismatch.\nExpected " + man.size +
                        " bytes, got " + len + ".");
            }

            if (man != null && !string.IsNullOrWhiteSpace(man.sha256))
            {
                string actual = HttpDownloader.ComputeFileSha256(dest, cancel);
                if (!string.Equals(actual, man.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(dest); } catch { }
                    throw new FlappyException(
                        "Launcher package checksum failed.\n\n" +
                        "CDN zip may be corrupt or version.json sha256 is stale.\n" +
                        "Re-upload " + Constants.LAUNCHER_PACKAGE_NAME + " + version.json together.");
                }
                LauncherLog.Info("Self-update SHA256 OK");
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
                    "A new " + Constants.LAUNCHER_NAME + " version is available.\n\n" +
                    "Installed:  " + FormatVer(local) + "\n" +
                    "Available:  " + FormatVer(remote) + notes + "\n\n" +
                    "Update now?\n(Game install folders will not be deleted.)",
                    Constants.LAUNCHER_NAME + " — Update",
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
                    "A required " + Constants.LAUNCHER_NAME + " update will be installed.\n\n" +
                    "Installed:  " + FormatVer(local) + "\n" +
                    "Available:  " + FormatVer(remote) + notes,
                    Constants.LAUNCHER_NAME + " — Required update",
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

                // Do NOT use HttpDownloader.Download — that path requires 7z magic (game packs).
                DownloadLauncherZip(zipUrl, zipPath, man);

                if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < 100)
                    throw new FlappyException("Downloaded launcher package is empty.");

                ZipFile.ExtractToDirectory(zipPath, extractDir);

                string payload = ResolvePayloadRoot(extractDir);
                if (!PayloadHasLauncherExe(payload))
                {
                    throw new FlappyException(
                        "Launcher package has no launcher .exe in the root.\n\nCheck the zip on CDN.");
                }

                // Prefer new product name; fall back to whatever is running / legacy.
                string startExe = PickStartExeName(payload);
                string batPath = Path.Combine(workRoot, "apply_update.bat");
                int pid = Process.GetCurrentProcess().Id;
                WriteApplyScript(batPath, pid, payload, baseDir, startExe);

                LauncherLog.Info("Self-update applying via " + batPath + " → " + startExe);
                Process.Start(new ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    WorkingDirectory = workRoot,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                return true;
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private static bool PayloadHasLauncherExe(string payload)
        {
            if (File.Exists(Path.Combine(payload, Constants.LAUNCHER_EXE_NAME))) return true;
            if (File.Exists(Path.Combine(payload, Constants.LAUNCHER_EXE_NAME_LEGACY))) return true;
            string[] exes = Directory.GetFiles(payload, "*.exe", SearchOption.TopDirectoryOnly);
            return exes.Length > 0;
        }

        private static string PickStartExeName(string payload)
        {
            if (File.Exists(Path.Combine(payload, Constants.LAUNCHER_EXE_NAME)))
                return Constants.LAUNCHER_EXE_NAME;
            if (File.Exists(Path.Combine(payload, Constants.LAUNCHER_EXE_NAME_LEGACY)))
                return Constants.LAUNCHER_EXE_NAME_LEGACY;
            string running = Path.GetFileName(System.Windows.Forms.Application.ExecutablePath);
            if (File.Exists(Path.Combine(payload, running)))
                return running;
            string[] exes = Directory.GetFiles(payload, "*.exe", SearchOption.TopDirectoryOnly);
            if (exes.Length > 0)
                return Path.GetFileName(exes[0]);
            return Constants.LAUNCHER_EXE_NAME;
        }

        private static string ResolvePayloadRoot(string extractDir)
        {
            if (PayloadHasLauncherExe(extractDir)) return extractDir;

            string[] dirs = Directory.GetDirectories(extractDir);
            foreach (var d in dirs)
            {
                if (PayloadHasLauncherExe(d)) return d;
            }
            if (dirs.Length == 1)
            {
                string[] exes = Directory.GetFiles(dirs[0], "*.exe", SearchOption.TopDirectoryOnly);
                if (exes.Length > 0) return dirs[0];
            }
            return extractDir;
        }

        private static void WriteApplyScript(string batPath, int pid, string src, string dst, string exeName)
        {
            // Wait for launcher PID to exit, copy payload files, start new exe, cleanup.
            // Does NOT delete game install folders (only overwrites files present in the zip).
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal EnableExtensions");
            sb.AppendLine("set \"PID=" + pid + "\"");
            sb.AppendLine("set \"SRC=" + src.TrimEnd('\\') + "\"");
            sb.AppendLine("set \"DST=" + dst.TrimEnd('\\') + "\"");
            sb.AppendLine("set \"EXE=" + exeName + "\"");
            sb.AppendLine("set \"LEGACY=" + Constants.LAUNCHER_EXE_NAME_LEGACY + "\"");
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
            sb.AppendLine("robocopy \"%SRC%\" \"%DST%\" /E /IS /IT /R:5 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np >nul");
            sb.AppendLine("if not exist \"%DST%\\%EXE%\" (");
            sb.AppendLine("  xcopy /Y /Q /I \"%SRC%\\*\" \"%DST%\\\" >nul");
            sb.AppendLine(")");
            // Drop legacy product name after migrate to Flappy Launcher.exe
            sb.AppendLine("if /I not \"%EXE%\"==\"%LEGACY%\" if exist \"%DST%\\%LEGACY%\" del /f /q \"%DST%\\%LEGACY%\" 2>nul");
            sb.AppendLine("if exist \"%DST%\\%EXE%\" (");
            sb.AppendLine("  start \"\" \"%DST%\\%EXE%\" " + SkipArg);
            sb.AppendLine(") else (");
            sb.AppendLine("  start \"\" explorer \"%DST%\"");
            sb.AppendLine(")");
            sb.AppendLine("ping -n 3 127.0.0.1 >nul");
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
                return Constants.LAUNCHER_PACKAGE_URL;
            urlOrRelative = urlOrRelative.Trim();
            if (urlOrRelative.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                urlOrRelative.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                urlOrRelative.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return urlOrRelative;
            return Constants.CombineCdn(urlOrRelative);
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
        /// <summary>Relative to CDN root or absolute URL.</summary>
        public string url { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
        public bool mandatory { get; set; }
        public string notes { get; set; }
    }
}
