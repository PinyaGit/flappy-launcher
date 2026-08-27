using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Same job as 1_FlappyTools_bugreport.bat: My Games + MO2 profiles/logs/ini → .7z
    /// then open the folder so the user can send the archive on Discord.
    /// </summary>
    internal static class BugReportPacker
    {
        public static string Run(PackageInstaller.ProgressHandler progress, CancellationToken cancel)
        {
            if (progress != null) progress(2, -1, Loc.T("bugreport_wait"));

            string seven = PackageInstaller.EnsureSevenZip(progress, cancel);
            if (string.IsNullOrEmpty(seven) || !File.Exists(seven))
                throw new FlappyException(Loc.T("sevenzip_missing"));

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs) || !Directory.Exists(docs))
                docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
            string myGames = Path.Combine(docs, "My Games");

            string staging = Path.Combine(Path.GetTempPath(), "FlappyBugReport_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            int copied = 0;
            try
            {
                copied += CopyDir(Path.Combine(myGames, "Skyrim Special Edition"), Path.Combine(staging, "My Games", "Skyrim Special Edition"));
                copied += CopyDir(Path.Combine(myGames, "Skyrim VR"), Path.Combine(staging, "My Games", "Skyrim VR"));
                copied += CopyDir(Path.Combine(myGames, "Skyrim.INI"), Path.Combine(staging, "My Games", "Skyrim.INI"));

                string game = GameCatalog.InstallRoot;
                if (Directory.Exists(game))
                {
                    copied += CopyDir(Path.Combine(game, "profiles"), Path.Combine(staging, "profiles"));
                    copied += CopyDir(Path.Combine(game, "logs"), Path.Combine(staging, "logs"));
                    copied += CopyFile(Path.Combine(game, "ModOrganizer.ini"), Path.Combine(staging, "MO2", "ModOrganizer.ini"));
                    copied += CopyFile(Path.Combine(game, "ModOrganizer.ini.AE"), Path.Combine(staging, "MO2", "ModOrganizer.ini.AE"));
                    copied += CopyFile(Path.Combine(game, "ModOrganizer.ini.VR"), Path.Combine(staging, "MO2", "ModOrganizer.ini.VR"));
                    WriteHint(Path.Combine(game, "ModOrganizer.ini"), Path.Combine(staging, "MO2", "LAST_LAUNCH_HINT.txt"));
                }

                string launcherLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log");
                copied += CopyFile(launcherLog, Path.Combine(staging, "launcher", "launcher.log"));

                if (copied == 0)
                    throw new FlappyException(Loc.IsRu
                        ? "Нечего архивировать — нет папок My Games / установки."
                        : "Nothing to archive — no My Games or install folders found.");

                cancel.ThrowIfCancellationRequested();

                string destDir = Directory.Exists(game)
                    ? game
                    : AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string archive = Path.Combine(destDir, "bugreport_" + stamp + ".7z");
                if (File.Exists(archive)) File.Delete(archive);

                if (progress != null) progress(60, -1, Loc.T("bugreport_wait"));

                var psi = new ProcessStartInfo
                {
                    FileName = seven,
                    Arguments = "a -t7z -mx5 -mmt=on -y -- \"" + archive + "\" *",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = staging
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) throw new FlappyException("7-Zip failed to start.");
                    p.WaitForExit();
                    if (p.ExitCode > 1)
                        throw new FlappyException("7-Zip failed (exit " + p.ExitCode + ").");
                }

                if (!File.Exists(archive))
                    throw new FlappyException(Loc.IsRu ? "Архив не создан." : "Archive was not created.");

                if (progress != null) progress(100, -1, Loc.T("bugreport_done_title"));
                LauncherLog.Info("Bug report: " + archive);
                return archive;
            }
            finally
            {
                try { Directory.Delete(staging, true); } catch { }
            }
        }

        public static void Reveal(string archive)
        {
            if (string.IsNullOrEmpty(archive) || !File.Exists(archive)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + archive + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("reveal archive: " + ex.Message);
                try { Process.Start(new ProcessStartInfo { FileName = Path.GetDirectoryName(archive), UseShellExecute = true }); }
                catch { }
            }
        }

        private static int CopyDir(string src, string dst)
        {
            if (!Directory.Exists(src)) return 0;
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(src.Length).TrimStart('\\', '/');
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length).TrimStart('\\', '/');
                string to = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(file, to, true);
            }
            return 1;
        }

        private static int CopyFile(string src, string dst)
        {
            if (!File.Exists(src)) return 0;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            return 1;
        }

        private static void WriteHint(string iniPath, string dest)
        {
            if (!File.Exists(iniPath)) return;
            try
            {
                string game = "", profile = "", path = "";
                foreach (var line in File.ReadAllLines(iniPath))
                {
                    if (line.StartsWith("gameName=", StringComparison.OrdinalIgnoreCase))
                        game = line.Substring("gameName=".Length);
                    else if (line.StartsWith("selected_profile=", StringComparison.OrdinalIgnoreCase))
                        profile = line.Substring("selected_profile=".Length);
                    else if (line.StartsWith("gamePath=", StringComparison.OrdinalIgnoreCase))
                        path = line.Substring("gamePath=".Length);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                var sb = new StringBuilder();
                sb.AppendLine("Flappy Launcher — last MO2 session");
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine("gameName=" + game);
                sb.AppendLine("selected_profile=" + profile);
                sb.AppendLine("gamePath=" + path);
                File.WriteAllText(dest, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("bugreport hint: " + ex.Message);
            }
        }
    }
}
