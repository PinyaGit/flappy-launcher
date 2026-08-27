using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.ThreadException += Application_ThreadException;

            // 0.0.4 started from 7-Zip/WinRAR (not extracted) lives in %TEMP%\7z* —
            // after exit the archiver deletes that folder and Explorer opens Documents.
            try
            {
                if (TryLeaveUnpackTemp(args))
                    return;
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("relocate: " + ex.Message);
            }

            LauncherSettings.Load();
            Loc.Initialize(LauncherSettings.Language);
            LauncherSettings.Language = Loc.Language;
            LauncherSettings.ApplyToRuntime();

            // Before UI: if a newer launcher is on CDN, download and restart into it.
            try
            {
                if (LauncherSelfUpdate.TryUpdateAndExit(args))
                    return;
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Self-update entry", ex);
                // continue to normal launch
            }

            System.Windows.Forms.Application.Run(new Application());
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            MessageBox.Show(FlappyException.FormatForUser(e.Exception), Constants.LAUNCHER_NAME);
        }

        /// <summary>
        /// If this exe was launched from an archiver temp dir, copy to %LocalAppData%\FlappyLauncher and re-run.
        /// </summary>
        private static bool TryLeaveUnpackTemp(string[] args)
        {
            string dir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));
            string temp = Path.GetFullPath(Path.GetTempPath().TrimEnd('\\', '/'));
            if (!dir.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(dir, temp, StringComparison.OrdinalIgnoreCase))
                return false;

            string leaf = Path.GetFileName(dir) ?? "";
            bool unpack =
                leaf.StartsWith("7z", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("Rar$", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("wz", StringComparison.OrdinalIgnoreCase) ||
                leaf.StartsWith("FlappyLauncherUpdate_", StringComparison.OrdinalIgnoreCase);
            if (!unpack) return false;

            string dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlappyLauncher");
            if (string.Equals(dir, dest, StringComparison.OrdinalIgnoreCase))
                return false;

            Directory.CreateDirectory(dest);
            string srcExe = System.Windows.Forms.Application.ExecutablePath;
            string destExe = Path.Combine(dest, Constants.LAUNCHER_EXE_NAME);
            File.Copy(srcExe, destExe, true);
            string srcCfg = srcExe + ".config";
            if (File.Exists(srcCfg))
                File.Copy(srcCfg, destExe + ".config", true);

            string argLine = "";
            if (args != null && args.Length > 0)
                argLine = " " + string.Join(" ", args);

            Process.Start(new ProcessStartInfo
            {
                FileName = destExe,
                Arguments = argLine.Trim(),
                WorkingDirectory = dest,
                UseShellExecute = true
            });
            LauncherLog.Info("Relocated from unpack temp to " + dest);
            return true;
        }
    }
}
