using System;
using System.IO;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// App-wide launcher identity + CDN. Per-game paths live in <see cref="GameCatalog"/>.
    /// This is a multi-game library launcher — not bound to a single title version.
    /// </summary>
    internal static class Constants
    {
        /// <summary>Product name shown in UI / window title.</summary>
        public static readonly string LAUNCHER_NAME = "Flappy Launcher";

        /// <summary>Primary executable file name (shipped in CDN zip).</summary>
        public const string LAUNCHER_EXE_NAME = "Flappy Launcher.exe";

        /// <summary>Legacy exe from Re-Dovah-only builds (self-update migration).</summary>
        public const string LAUNCHER_EXE_NAME_LEGACY = "Flappy Re-Dovah.exe";

        /// <summary>CDN zip file name under launcher/.</summary>
        public const string LAUNCHER_PACKAGE_NAME = "Flappy-Launcher.zip";

        /// <summary>Online CDN root (per-game folders hang under this).</summary>
        public const string CDN_PACKAGES_BASE_URL = "https://cdn.flappy.su/";

        // ---------- back-compat aliases (current game) ----------

        public static string GAME_TITLE
        {
            get { return GameCatalog.Current.Title; }
        }

        public static string DESTINATION_PATH
        {
            get { return GameCatalog.InstallRoot; }
        }

        public static string PACKAGES_BASE_URL
        {
            get { return GameCatalog.PackagesBaseUrl; }
        }

        public static bool HasLocalPackageBundle
        {
            get { return !string.IsNullOrEmpty(GameCatalog.LocalBundleDirectory); }
        }

        public static bool IsLocalPackagesMode
        {
            get { return HasLocalPackageBundle; }
        }

        public static string LocalBundleDirectory
        {
            get { return GameCatalog.LocalBundleDirectory; }
        }

        public static string INDEX_URL
        {
            get { return GameCatalog.IndexUrl; }
        }

        public static string LOCAL_INDEX_PATH
        {
            get
            {
                string b = GameCatalog.LocalBundleDirectory;
                return string.IsNullOrEmpty(b) ? null : Path.Combine(b, "index.json");
            }
        }

        public static string APPLICATION_ICON_URL
        {
            get { return CombineCdn("favicon.ico"); }
        }

        /// <summary>Launcher self-update manifest (shared, not per-game).</summary>
        public static string LAUNCHER_VERSION_URL
        {
            get { return CombineCdn("launcher/version.json"); }
        }

        public static string LAUNCHER_PACKAGE_URL
        {
            get { return CombineCdn("launcher/" + LAUNCHER_PACKAGE_NAME); }
        }

        public static string GAME_EXECUTABLE_PATH
        {
            get { return Path.Combine(DESTINATION_PATH, "ModOrganizer.exe"); }
        }

        public static readonly string BOOSTY_URL = "https://boosty.to/flappyae";
        public static readonly string DISCORD_URL = "https://discord.com/invite/XqPmTqSyqF";

        public static bool SHOW_VERSION_TEXT = true;
        public static bool AUTOMATICALLY_BEGIN_UPDATING = false;
        public static bool AUTOMATICALLY_LAUNCH_GAME_AFTER_UPDATING = false;
        public static bool CHECK_LAUNCHER_UPDATES = true;

        /// <summary>
        /// False = no full game download from CDN. First install is torrent unpack only.
        /// Update / Repair still use CDN. Flip to true to restore online install.
        /// </summary>
        public static bool ALLOW_CDN_FRESH_INSTALL = false;

        /// <summary>
        /// Master switch for closed-test keys. False = never prompt, never send Bearer.
        /// Server <c>CDN_GATE_ENABLED=0</c> also opens /flappy/ without a launcher rebuild.
        /// </summary>
        public static bool ACCESS_GATES_ENABLED = false;

        public static string ACCESS_UNLOCK_URL
        {
            get { return CombineCdn("auth/unlock"); }
        }

        public static string ACCESS_STATUS_URL
        {
            get { return CombineCdn("auth/status"); }
        }

        /// <summary>Fixed CDN workers (no Settings toggle). Matches nginx per-IP connection cap.</summary>
        public static int DOWNLOAD_PARALLELISM = 2;
        public static long DOWNLOAD_CHUNK_BYTES = 800L * 1024 * 1024;

        /// <summary>Parallel 7-Zip extract processes after download/torrent acquire.</summary>
        public static int EXTRACT_PARALLELISM = 3;

        public static string GetLocalPackagePath(string packageRelative)
        {
            return GameCatalog.GetLocalPackagePath(packageRelative);
        }

        public static string CombineCdn(string relative)
        {
            relative = (relative ?? "").Replace('\\', '/').TrimStart('/');
            return CDN_PACKAGES_BASE_URL.TrimEnd('/') + "/" + relative;
        }

        public static string CombineBase(string relative)
        {
            return GameCatalog.CombinePackages(relative);
        }
    }
}
