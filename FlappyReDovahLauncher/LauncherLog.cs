using System;
using System.IO;
using System.Text;

namespace FlappyReDovahLauncher
{
    internal static class LauncherLog
    {
        private static readonly object Gate = new object();

        public static string LogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher.log"); }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message + (ex != null ? " | " + ex.Message : ""));
            if (ex != null && !string.IsNullOrEmpty(ex.StackTrace))
                Write("ERROR", ex.StackTrace);
        }

        private static void Write(string level, string message)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                    // Keep log from growing forever (~2 MB)
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    {
                        string bak = LogPath + ".old";
                        try { if (File.Exists(bak)) File.Delete(bak); File.Move(LogPath, bak); } catch { }
                    }
                }
            }
            catch { /* never break UI for logging */ }
        }
    }
}
