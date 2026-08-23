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
        /// <summary>CDN folder under base URL, e.g. "flappy" → https://cdn…/flappy/</summary>
        public string CdnFolder { get; set; }
        /// <summary>Install directory name next to the launcher exe.</summary>
        public string InstallFolderName { get; set; }
        /// <summary>Optional splash override (loose file path). Null = use <see cref="SplashResource"/>.</summary>
        public string SplashPath { get; set; }
        /// <summary>Embedded splash resource (Resources.resx), e.g. bg_re_dovah / bg_flappy_400.</summary>
        public string SplashResource { get; set; }
        /// <summary>Embedded rail icon resource, e.g. logo_re_dovah / logo_flappy_400.</summary>
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

        /// <summary>Load embedded splash (caller owns the returned Bitmap clone).</summary>
        public Bitmap GetSplashBitmapClone()
        {
            if (string.IsNullOrEmpty(SplashResource)) return null;
            try
            {
                var src = Resources.ResourceManager.GetObject(SplashResource) as Bitmap;
                if (src == null) return null;
                return new Bitmap(src);
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
            // Two library entries: Re-Dovah branch + Flappy 4.0.0 branch (RU/EN unified).
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
                    SplashResource = "bg_re_dovah",
                    IconResource = "logo_re_dovah",
                    Available = true,
                    SupportsVr = true,
                    Description = "Skyrim AE + VR modpack"
                },
                new GameDefinition
                {
                    Id = "flappy",
                    Title = "Flappy",
                    ShortLabel = "Flappy",
                    CdnFolder = "flappy",
                    InstallFolderName = "Flappy",
                    SplashResource = "bg_flappy_400",
                    IconResource = "logo_flappy_400",
                    Available = false, // stub until 4.0.0 packages on CDN
                    SupportsVr = false,
                    Description = "Coming soon — Flappy 4.0.0"
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
