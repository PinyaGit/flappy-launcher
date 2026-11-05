using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Checks for running or hung game/MO2 processes before launch and provides 1-click termination.
    /// </summary>
    internal static class GameProcessGuard
    {
        private static readonly string[] MonitoredProcesses = {
            "SkyrimSE",
            "SkyrimVR",
            "skse64_loader",
            "skyrimvr_loader",
            "ModOrganizer",
            "zandronum"
        };

        public static bool IsGameRunning()
        {
            foreach (var name in MonitoredProcesses)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                        return true;
                }
                catch { }
            }
            return false;
        }

        public static bool EnsureReadyToLaunch(IWin32Window owner)
        {
            try
            {
                Process hung = null;
                foreach (var name in MonitoredProcesses)
                {
                    var procs = Process.GetProcessesByName(name);
                    if (procs.Length > 0)
                    {
                        hung = procs[0];
                        break;
                    }
                }

                if (hung == null) return true;

                string procName = hung.ProcessName;
                string msg = Loc.IsRu
                    ? $"Процесс игры или Mod Organizer ({procName}.exe) уже запущен или завис в памяти.\n\nЗавершить его перед новым запуском?"
                    : $"A game or Mod Organizer process ({procName}.exe) is already running or hung.\n\nTerminate it before launching?";

                string title = Loc.IsRu ? "Игра уже запущена" : "Process Already Running";

                var res = MessageBox.Show(owner, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes)
                {
                    KillAllGameProcesses();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("GameProcessGuard error: " + ex.Message);
                return true;
            }
        }

        public static int KillAllGameProcesses()
        {
            int killed = 0;
            foreach (var name in MonitoredProcesses)
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            p.Kill();
                            p.WaitForExit(1500);
                            killed++;
                            LauncherLog.Info("GameProcessGuard killed hung process: " + name);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return killed;
        }
    }
}
