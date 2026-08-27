using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FlappyReDovahLauncher
{
    /// <summary>Persisted next to the exe (language, download workers).</summary>
    internal static class LauncherSettings
    {
        public static string Language { get; set; }
        public static int DownloadParallelism { get; set; }

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.txt"); }
        }

        public static void Load()
        {
            Language = "";
            DownloadParallelism = 3;
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
                    else if (key == "downloads" || key == "parallel")
                    {
                        int n;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                            DownloadParallelism = ClampWorkers(n);
                    }
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
                string body =
                    "lang=" + (Language ?? "") + Environment.NewLine +
                    "downloads=" + ClampWorkers(DownloadParallelism) + Environment.NewLine;
                File.WriteAllText(FilePath, body, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("settings save: " + ex.Message);
            }
        }

        public static int ClampWorkers(int n)
        {
            if (n < 1) return 1;
            if (n > 4) return 4;
            return n;
        }

        public static void ApplyToRuntime()
        {
            Constants.DOWNLOAD_PARALLELISM = ClampWorkers(DownloadParallelism);
        }
    }
}
