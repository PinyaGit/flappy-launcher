using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using FlappyReDovahLauncher.Properties;

namespace FlappyReDovahLauncher
{
    /// <summary>One installable title in Flappy Launcher (library entry).</summary>
    internal sealed class GameDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string ShortLabel { get; set; }
        /// <summary>CDN folder under base URL, e.g. "re-dovah" → https://cdn…/re-dovah/</summary>
        public string CdnFolder { get; set; }
        /// <summary>Install directory name next to the launcher exe.</summary>
        public string InstallFolderName { get; set; }
        /// <summary>Optional splash override (file path). Null = default resource.</summary>
        public string SplashPath { get; set; }
        /// <summary>Embedded rail icon resource name (Assets → Resources.resx), e.g. re_logo.</summary>
        public string IconResource { get; set; }
        /// <summary>False = stub / coming soon (UI only).</summary>
        public bool Available { get; set; }
        /// <summary>Show Get VR / channel options.</summary>
        public bool SupportsVr { get; set; }
        public string Description { get; set; }

        /// <summary>Load embedded logo bitmap (do not dispose — owned by ResourceManager).</summary>
        public Bitmap GetIconBitmap()
        {
            if (string.IsNullOrEmpty(IconResource)) return null;
            try
            {
                return Resources.ResourceManager.GetObject(IconResource) as Bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Registry of Flappy titles + current selection (Steam-style library).</summary>
    internal static class GameCatalog
    {
        public static readonly IReadOnlyList<GameDefinition> Games = BuildList();

        private static GameDefinition _current;

        public static GameDefinition Current
        {
            get
            {
                if (_current == null)
                    _current = Games.FirstOrDefault(g => g.Available) ?? Games[0];
                return _current;
            }
            private set { _current = value; }
        }

        public static event Action CurrentChanged;

        public static void Select(string id)
        {
            var g = Games.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (g == null || ReferenceEquals(g, Current)) return;
            Current = g;
            LauncherLog.Info("Selected game: " + g.Id + " (" + g.Title + ")");
            var h = CurrentChanged;
            if (h != null) h();
        }

        public static void Select(GameDefinition g)
        {
            if (g == null) return;
            Select(g.Id);
        }

        private static List<GameDefinition> BuildList()
        {
            // Stub splash for games without art yet (optional file next to exe)
            string stubSplash = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "black.png");
            if (!File.Exists(stubSplash))
                stubSplash = null;

            return new List<GameDefinition>
            {
                new GameDefinition
                {
                    Id = "re-dovah",
                    Title = "Flappy Re-Dovah",
                    ShortLabel = "Re-Dovah",
                    // Empty = CDN root (current live layout). Later: "re-dovah"
                    CdnFolder = "",
                    InstallFolderName = "Flappy Re-Dovah",
                    SplashPath = null, // default bg_LBackground
                    IconResource = "re_logo",
                    Available = true,
                    SupportsVr = true,
                    Description = "Skyrim AE + VR modpack"
                },
                new GameDefinition
                {
                    Id = "flappy-ru",
                    Title = "FlappyRU",
                    ShortLabel = "RU",
                    CdnFolder = "flappy-ru",
                    InstallFolderName = "FlappyRU",
                    SplashPath = stubSplash,
                    IconResource = "ru_logo",
                    Available = false, // stub until packaged
                    SupportsVr = false,
                    Description = "Coming soon — Russian build"
                },
                new GameDefinition
                {
                    Id = "flappy-en",
                    Title = "FlappyEN",
                    ShortLabel = "EN",
                    CdnFolder = "flappy-en",
                    InstallFolderName = "FlappyEN",
                    SplashPath = stubSplash,
                    IconResource = "en_logo",
                    Available = false,
                    SupportsVr = false,
                    Description = "Coming soon — English build"
                }
            };
        }

        // ---------- paths relative to current game ----------

        public static string InstallRoot
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Current.InstallFolderName);
            }
        }

        public static string PackagesBaseUrl
        {
            get
            {
                string folder = (Current.CdnFolder ?? "").Trim().Trim('/');
                string baseUrl = Constants.CDN_PACKAGES_BASE_URL.TrimEnd('/') + "/";
                if (string.IsNullOrEmpty(folder)) return baseUrl;
                return baseUrl + folder + "/";
            }
        }

        public static string IndexUrl
        {
            get { return PackagesBaseUrl.TrimEnd('/') + "/index.json"; }
        }

        public static string LocalVersionPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_version_" + Current.Id + ".txt");
            }
        }

        public static string DownloadCacheDir
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "download_cache", Current.Id);
            }
        }

        /// <summary>Local torrent/offline tree for this game, if present.</summary>
        public static string LocalBundleDirectory
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                // Preferred: next to exe under game folder name or cdn folder
                string[] candidates =
                {
                    Path.Combine(baseDir, Current.CdnFolder ?? ""),
                    Path.Combine(baseDir, Current.InstallFolderName + "-packages"),
                    // Legacy single-game layout (root index.json) only for re-dovah
                    string.Equals(Current.Id, "re-dovah", StringComparison.OrdinalIgnoreCase) ? baseDir : null
                };
                foreach (var c in candidates)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    if (File.Exists(Path.Combine(c, "index.json")) && Directory.Exists(Path.Combine(c, "packages")))
                        return Path.GetFullPath(c);
                }
                return null;
            }
        }

        public static string GetLocalPackagePath(string packageRelative)
        {
            string bundle = LocalBundleDirectory;
            if (string.IsNullOrEmpty(bundle) || string.IsNullOrWhiteSpace(packageRelative))
                return null;
            string rel = packageRelative.Replace('/', Path.DirectorySeparatorChar).TrimStart('\\', '/');
            string path = Path.Combine(bundle, rel);
            return File.Exists(path) ? path : null;
        }

        public static string CombinePackages(string relative)
        {
            relative = (relative ?? "").Replace('\\', '/').TrimStart('/');
            return PackagesBaseUrl.TrimEnd('/') + "/" + relative;
        }
    }
}
