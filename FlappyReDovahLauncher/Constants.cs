using System;
using System.IO;

namespace FlappyReDovahLauncher
{
    /// <summary>App configuration (not a Form).</summary>
    internal static class Constants
    {
        public static readonly string GAME_TITLE = "Flappy Re-Dovah";
        public static readonly string LAUNCHER_NAME = "Flappy Re-Dovah";

        public static readonly string DESTINATION_PATH = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Flappy Re-Dovah");

        /// <summary>Online CDN (used when no local torrent bundle next to the exe).</summary>
        public const string CDN_PACKAGES_BASE_URL = "https://cdn.flappy.su/";

        private static string _packagesBaseResolved;
        private static bool _packagesBaseResolvedDone;

        /// <summary>
        /// Package root: local folder (torrent / offline) if index.json+packages exist next to exe,
        /// otherwise CDN. Local mode = install from disk without re-downloading.
        /// </summary>
        public static string PACKAGES_BASE_URL
        {
            get
            {
                if (!_packagesBaseResolvedDone)
                {
                    _packagesBaseResolved = ResolvePackagesBase();
                    _packagesBaseResolvedDone = true;
                }
                return _packagesBaseResolved;
            }
        }

        public static bool IsLocalPackagesMode
        {
            get
            {
                string b = PACKAGES_BASE_URL ?? "";
                return !(b.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                         || b.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                         || b.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
            }
        }

        public static string INDEX_URL
        {
            get { return CombineBase("index.json"); }
        }

        public static string APPLICATION_ICON_URL
        {
            get { return CombineBase("favicon.ico"); }
        }

        /// <summary>Launcher self-update manifest on CDN (online only).</summary>
        public static string LAUNCHER_VERSION_URL
        {
            get { return CDN_PACKAGES_BASE_URL.TrimEnd('/') + "/launcher/version.json"; }
        }

        public static readonly string GAME_EXECUTABLE_PATH = Path.Combine(DESTINATION_PATH, "ModOrganizer.exe");

        public static readonly string BOOSTY_URL = "https://boosty.to/flappyae";
        public static readonly string DISCORD_URL = "https://discord.com/invite/XqPmTqSyqF";

        public static bool SHOW_VERSION_TEXT = true;

        /// <summary>If true, start Install/Update automatically when not ready.</summary>
        public static bool AUTOMATICALLY_BEGIN_UPDATING = false;

        public static bool AUTOMATICALLY_LAUNCH_GAME_AFTER_UPDATING = false;

        /// <summary>Self-update only when using online CDN (not torrent offline bundle).</summary>
        public static bool CHECK_LAUNCHER_UPDATES
        {
            get { return !IsLocalPackagesMode; }
        }

        /// <summary>Parallel package acquires (HTTP downloads or local verify).</summary>
        public static int DOWNLOAD_PARALLELISM = 3;

        public static long DOWNLOAD_CHUNK_BYTES = 800L * 1024 * 1024;

        private static string ResolvePackagesBase()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Torrent layout: index.json + packages\ next to Flappy Re-Dovah.exe
                string indexBeside = Path.Combine(baseDir, "index.json");
                string packagesBeside = Path.Combine(baseDir, "packages");
                if (File.Exists(indexBeside) && Directory.Exists(packagesBeside))
                {
                    LauncherLog.Info("Packages mode: LOCAL torrent bundle @ " + baseDir);
                    return Path.GetFullPath(baseDir);
                }
            }
            catch (Exception ex)
            {
                try { LauncherLog.Warn("ResolvePackagesBase: " + ex.Message); } catch { }
            }

            LauncherLog.Info("Packages mode: CDN " + CDN_PACKAGES_BASE_URL);
            return CDN_PACKAGES_BASE_URL;
        }

        public static string CombineBase(string relative)
        {
            string b = PACKAGES_BASE_URL ?? "";
            relative = (relative ?? "").Replace('\\', '/').TrimStart('/');
            if (b.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                b.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                b.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return b.TrimEnd('/') + "/" + relative;
            }
            return Path.Combine(b.TrimEnd('\\', '/'), relative.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
