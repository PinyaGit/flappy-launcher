using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
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
        /// <summary>
        /// Closed-test key before CDN download. No effect unless
        /// <see cref="Constants.ACCESS_GATES_ENABLED"/> and the server gate is on.
        /// Never set this on Re-Dovah.
        /// </summary>
        public bool RequiresAccessKey { get; set; }
        /// <summary>Show Get VR / channel options.</summary>
        public bool SupportsVr { get; set; }
        /// <summary>True = first install may download the full pack from CDN. Skyrim stays torrent-only.</summary>
        public bool AllowCdnFreshInstall { get; set; }
        public string Description { get; set; }
        /// <summary>"mo2" (Skyrim packs) or "doom" (Zandronum client + auto-connect).</summary>
        public string Kind { get; set; }
        /// <summary>host:port for Kind=doom. Ignored otherwise.</summary>
        public string ConnectHost { get; set; }
        /// <summary>Loose rail icon next to the exe (Assets/logo_doom.png).</summary>
        public string IconPath { get; set; }

        public bool IsDoom
        {
            get { return string.Equals(Kind, "doom", StringComparison.OrdinalIgnoreCase); }
        }

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
                    Id = "flappy",
                    Title = "Flappy",
                    ShortLabel = "Flappy",
                    CdnFolder = "flappy",
                    InstallFolderName = "Flappy",
                    SplashResource = "bg_flappy_400",
                    IconResource = "logo_flappy_400",
                    Available = true,
                    RequiresAccessKey = true,
                    SupportsVr = false,
                    Description = "Skyrim AE",
                    Kind = "mo2"
                },
                new GameDefinition
                {
                    Id = "re-dovah",
                    Title = "Flappy Re-Dovah",
                    ShortLabel = "Re-Dovah",
                    CdnFolder = "re-dovah",
                    InstallFolderName = "Re-Dovah",
                    SplashResource = "bg_re_dovah",
                    IconResource = "logo_re_dovah",
                    Available = true,
                    RequiresAccessKey = true,
                    SupportsVr = true,
                    Description = "Skyrim AE + VR modpack"
                },
                new GameDefinition
                {
                    Id = "doom",
                    Title = "Flappy Doom",
                    ShortLabel = "Doom",
                    CdnFolder = "doom",
                    InstallFolderName = "Doom",
                    SplashResource = "bg_doom",
                    IconResource = "logo_doom",
                    Available = true,
                    RequiresAccessKey = false,
                    SupportsVr = false,
                    Description = "Zandronum 3.3 · Chillax + HDOOM",
                    Kind = "doom",
                    ConnectHost = "doom.flappy.su:10666",
                    AllowCdnFreshInstall = true
                }
            };
        }

        // ---------- paths relative to current game ----------

        private static readonly Dictionary<string, string> _customInstallRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void SetCustomInstallRoot(string gameId, string path)
        {
            if (string.IsNullOrEmpty(gameId)) return;
            if (string.IsNullOrEmpty(path))
                _customInstallRoots.Remove(gameId);
            else
                _customInstallRoots[gameId] = Path.GetFullPath(path);
        }

        public static string GetInstalledPathFromRegistry(string gameId)
        {
            if (string.IsNullOrEmpty(gameId)) return null;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Flappy\" + gameId))
                {
                    if (k != null)
                    {
                        string p = k.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                            return p;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void SaveInstalledPathToRegistry(string gameId, string path)
        {
            if (string.IsNullOrEmpty(gameId) || string.IsNullOrEmpty(path)) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Flappy\" + gameId))
                {
                    if (k != null)
                        k.SetValue("InstallPath", path, RegistryValueKind.String);
                }
            }
            catch { }
        }

        public static string GetInstallRoot(GameDefinition game)
        {
            var g = game ?? Current;
            if (g == null) return AppDomain.CurrentDomain.BaseDirectory;

            // 1. Session override (e.g. chosen during this install session)
            if (_customInstallRoots.TryGetValue(g.Id, out string custom) && !string.IsNullOrEmpty(custom))
                return custom;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            // 2. Is current BaseDirectory itself the game folder?
            bool baseHasMarker = File.Exists(Path.Combine(baseDir, ".flappy_installed"))
                || File.Exists(Path.Combine(baseDir, "ModOrganizer.exe"))
                || Directory.Exists(Path.Combine(baseDir, "StockGame"))
                || (g.IsDoom && File.Exists(Path.Combine(baseDir, "zandronum.exe")));

            if (baseHasMarker)
            {
                string idFile = Path.Combine(baseDir, ".flappy_game_id");
                if (File.Exists(idFile))
                {
                    try
                    {
                        string savedId = File.ReadAllText(idFile, Encoding.UTF8).Trim();
                        if (string.Equals(savedId, g.Id, StringComparison.OrdinalIgnoreCase))
                            return baseDir;
                    }
                    catch { }
                }
                else
                {
                    if (baseDir.EndsWith(g.InstallFolderName, StringComparison.OrdinalIgnoreCase) || g == Current)
                        return baseDir;
                }
            }

            // 3. Saved path in registry HKCU\Software\Flappy\<gameId>\InstallPath
            string regPath = GetInstalledPathFromRegistry(g.Id);
            if (!string.IsNullOrEmpty(regPath))
            {
                if (File.Exists(Path.Combine(regPath, ".flappy_installed"))
                    || File.Exists(Path.Combine(regPath, "ModOrganizer.exe"))
                    || Directory.Exists(Path.Combine(regPath, "StockGame"))
                    || (g.IsDoom && File.Exists(Path.Combine(regPath, "zandronum.exe"))))
                {
                    return regPath;
                }
            }

            // 4. Default legacy path next to exe
            return Path.Combine(baseDir, g.InstallFolderName);
        }

        public static string InstallRoot
        {
            get
            {
                return GetInstallRoot(Current);
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
                string inRoot = Path.Combine(InstallRoot, "local_version_" + Current.Id + ".txt");
                if (File.Exists(inRoot)) return inRoot;
                string inBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_version_" + Current.Id + ".txt");
                if (File.Exists(inBase)) return inBase;
                return inRoot;
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
                string folder = (Current.CdnFolder ?? "").Trim().Trim('/', '\\');
                if (string.IsNullOrEmpty(folder)) return null;
                // Windows is case-insensitive: "flappy" packages would collide with game folder "Flappy".
                // Torrent layout is content\<cdnfolder>\ next to the exe; CDN URLs stay /flappy and /re-dovah.
                string[] candidates =
                {
                    Path.Combine(baseDir, "content", folder),
                    Path.Combine(baseDir, folder + "-packages")
                };
                foreach (var c in candidates)
                {
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
