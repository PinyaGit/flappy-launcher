using System;
using System.IO;
using System.Text;

namespace FlappyReDovahLauncher
{
    /// <summary>Persisted next to the exe (language). Download workers are fixed at 2.</summary>
    internal static class LauncherSettings
    {
        public static string Language { get; set; }
        public static int DownloadParallelism { get { return 2; } }

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.txt"); }
        }

        public static void Load()
        {
            Language = "";
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.IndexOf('=') < 0) continue;
                    int eq = line.IndexOf('=');
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "lang" || key == "language")
                        Language = Loc.Normalize(val);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("settings load: " + ex.Message);
            }
        }

        public static void Save()
        {
            try
            {
                string body = "lang=" + (Language ?? "") + Environment.NewLine;
                File.WriteAllText(FilePath, body, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("settings save: " + ex.Message);
            }
        }

        public static int ClampWorkers(int n)
        {
            return 2;
        }

        public static void ApplyToRuntime()
        {
            Constants.DOWNLOAD_PARALLELISM = 2;
        }
    }
}
