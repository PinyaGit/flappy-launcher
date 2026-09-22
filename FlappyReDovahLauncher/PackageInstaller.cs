using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Multi-package install / update / repair with fingerprints, parallel download, AE/VR channel.
    /// </summary>
    internal static class PackageInstaller
    {
        // Back-compat aliases used by LauncherForm
        public class Unit : PackageUnit { }
        public class Index : PackageIndex { }

        public static string GameRootPath
        {
            get { return GameCatalog.InstallRoot; }
        }

        public static string InstallFlagPath
        {
            get { return Path.Combine(GameRootPath, ".flappy_installed"); }
        }

        public static string ModeFilePath
        {
            get { return Path.Combine(GameRootPath, ".flappy_mode"); }
        }

        public static string ChannelFilePath
        {
            get { return Path.Combine(GameRootPath, ".flappy_channel"); }
        }

        public static string OfficialModlistDir
        {
            get { return Path.Combine(GameRootPath, ".flappy"); }
        }

        public static string LocalVersionPath
        {
            get { return GameCatalog.LocalVersionPath; }
        }

        public static string DownloadCacheDir
        {
            get { return GameCatalog.DownloadCacheDir; }
        }

        public static string RepairReportPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "repair_report_" + GameCatalog.Current.Id + ".txt");
            }
        }

        /// <summary>progress(overall0-100, currentArchive0-100 or -1, statusText)</summary>
        public delegate void ProgressHandler(int overallPct, int currentPct, string message);

        /// <summary>Managed 7za from LocalAppData (never system 7-Zip).</summary>
        public static string SevenZipPath
        {
            get { return SevenZipBootstrap.ResolvedPath; }
        }

        /// <summary>Download official 7-Zip tools if missing. Safe to call repeatedly.</summary>
        public static string EnsureSevenZip(ProgressHandler progress, CancellationToken cancel)
        {
            return SevenZipBootstrap.Ensure(
                msg =>
                {
                    if (progress != null) progress(0, -1, msg ?? "7-Zip…");
                },
                cancel);
        }

        public static bool IsInstalled()
        {
            if (GameCatalog.Current != null && GameCatalog.Current.IsDoom)
            {
                return File.Exists(InstallFlagPath)
                    && Directory.Exists(GameRootPath)
                    && File.Exists(Path.Combine(GameRootPath, "zandronum.exe"));
            }
            return File.Exists(InstallFlagPath)
                && Directory.Exists(GameRootPath)
                && File.Exists(Path.Combine(GameRootPath, "ModOrganizer.exe"))
                && (Directory.Exists(Path.Combine(GameRootPath, "StockGame"))
                    || Directory.Exists(Path.Combine(GameRootPath, "StockGameVR")));
        }

        public static Index FetchIndex()
        {
            // Same index for online and torrent installs: prefer CDN, fall back to local bundle offline.
            Exception cdnError = null;
            try
            {
                string json = HttpDownloader.ReadAllText(Constants.INDEX_URL);
                var idx = DeserializeIndex(json);
                LauncherLog.Info("Index from CDN v" + idx.version + " units=" + idx.units.Count);
                return idx;
            }
            catch (Exception ex)
            {
                cdnError = ex;
                LauncherLog.Warn("CDN index failed: " + ex.Message);
            }

            string localIndex = Constants.LOCAL_INDEX_PATH;
            if (!string.IsNullOrEmpty(localIndex) && File.Exists(localIndex))
            {
                try
                {
                    string json = File.ReadAllText(localIndex, Encoding.UTF8);
                    var idx = DeserializeIndex(json);
                    LauncherLog.Info("Index from LOCAL bundle v" + idx.version + " units=" + idx.units.Count);
                    return idx;
                }
                catch (Exception ex)
                {
                    throw new FlappyException(
                        "Cannot read package index from CDN or local bundle.\n\nCDN: " +
                        FlappyException.FormatForUser(cdnError) + "\nLocal: " + ex.Message,
                        ex.ToString(), ex);
                }
            }

            throw new FlappyException(
                "Cannot read package index.\n\nCheck CDN / network" +
                (cdnError != null ? ":\n" + FlappyException.FormatForUser(cdnError) : "."),
                cdnError != null ? cdnError.ToString() : null,
                cdnError);
        }

        private static Index DeserializeIndex(string json)
        {
            var idx = JsonAdapter.FromJson<Index>(json);
            if (idx == null || idx.units == null)
                throw new FlappyException("index.json is empty or invalid.");
            return idx;
        }

        public static Version ParseVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return new Version(0, 0, 0, 0);
            Version result;
            if (Version.TryParse(v.Trim(), out result)) return result;
            return new Version(0, 0, 0, 0);
        }

        public static Version GetLocalVersion()
        {
            if (File.Exists(LocalVersionPath))
                return ParseVersion(File.ReadAllText(LocalVersionPath, Encoding.UTF8));
            // Pre multi-game: single local_version.txt next to exe
            if (string.Equals(GameCatalog.Current.Id, "re-dovah", StringComparison.OrdinalIgnoreCase))
            {
                string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_version.txt");
                if (File.Exists(legacy))
                    return ParseVersion(File.ReadAllText(legacy, Encoding.UTF8));
            }
            return new Version(0, 0, 0, 0);
        }

        public static void SaveLocalVersion(string version)
        {
            string v = (version ?? "1.0.0").Trim();
            File.WriteAllText(LocalVersionPath, v, Encoding.UTF8);
            if (string.Equals(GameCatalog.Current.Id, "re-dovah", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string legacy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local_version.txt");
                    File.WriteAllText(legacy, v, Encoding.UTF8);
                }
                catch { }
            }
        }

        public static InstallChannel GetSavedChannel()
        {
            try
            {
                if (!File.Exists(ChannelFilePath)) return InstallChannel.AeAndVr;
                string t = File.ReadAllText(ChannelFilePath, Encoding.UTF8).Trim().ToUpperInvariant();
                if (t == "AE" || t == "AEONLY" || t == "AE_ONLY") return InstallChannel.AeOnly;
            }
            catch { }
            return InstallChannel.AeAndVr;
        }

        public static void SaveChannel(InstallChannel ch)
        {
            Directory.CreateDirectory(GameRootPath);
            File.WriteAllText(ChannelFilePath, ch == InstallChannel.AeOnly ? "AE" : "AEVR", Encoding.UTF8);
        }

        public static string GetSavedMode()
        {
            if (!File.Exists(ModeFilePath)) return "AE";
            string m = File.ReadAllText(ModeFilePath, Encoding.UTF8).Trim().ToUpperInvariant();
            return (m == "VR") ? "VR" : "AE";
        }

        public static void SaveMode(string mode)
        {
            Directory.CreateDirectory(GameRootPath);
            File.WriteAllText(ModeFilePath, mode == "VR" ? "VR" : "AE", Encoding.UTF8);
        }

        /// <summary>VR content: StockGameVR, (VR) mods, VR profile, Re-Dovah VR outputs.</summary>
        public static bool IsVrUnit(PackageUnit unit)
        {
            if (unit == null) return false;
            string p = (unit.path ?? "").Replace('\\', '/').Trim().Trim('/');
            if (p.Equals("StockGameVR", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.IndexOf("(VR)", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (p.IndexOf("Re-Dovah VR", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (p.IndexOf("VR - Re-Dovah", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Split profile packages: profiles/VR - Re-Dovah - …
            if (p.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
            {
                string leaf = p.Substring("profiles/".Length);
                if (leaf.IndexOf("VR", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            // root package may include VR bats — cannot split; keep for AE install
            return false;
        }

        public static List<PackageUnit> FilterByChannel(IEnumerable<PackageUnit> units, InstallChannel channel)
        {
            var list = new List<PackageUnit>();
            foreach (var u in units ?? Enumerable.Empty<PackageUnit>())
            {
                if (channel == InstallChannel.AeOnly && IsVrUnit(u))
                    continue;
                list.Add(u);
            }
            return list;
        }

        public static List<PackageUnit> GetVrUnits(IEnumerable<PackageUnit> units)
        {
            var list = new List<PackageUnit>();
            foreach (var u in units ?? Enumerable.Empty<PackageUnit>())
            {
                if (IsVrUnit(u)) list.Add(u);
            }
            return list;
        }

        /// <summary>Upgrade AE-only install: download missing VR packages and switch channel to AE+VR.</summary>
        public static void InstallVrChannel(
            Index index,
            ProgressHandler progress,
            CancellationToken cancel)
        {
            if (index == null || index.units == null)
                throw new FlappyException("index.json has no packages.");

            EnsureSevenZip(progress, cancel);
            if (string.IsNullOrEmpty(SevenZipPath) || !File.Exists(SevenZipPath))
                throw new FlappyException(Loc.T("sevenzip_missing"));

            Directory.CreateDirectory(GameRootPath);
            Directory.CreateDirectory(DownloadCacheDir);
            SaveChannel(InstallChannel.AeAndVr);

            var vrUnits = GetVrUnits(index.units);
            if (vrUnits.Count == 0)
            {
                progress(100, -1, Loc.T("no_vr_index"));
                return;
            }

            // Only scan VR units (do not re-fingerprint the whole AE set).
            progress(1, -1, Loc.T("checking_vr"));
            var work = new List<PackageUnit>();
            for (int i = 0; i < vrUnits.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var unit = vrUnits[i];
                if (progress != null)
                    progress(1, -1, Loc.F("checking_n", DisplayName(unit)));

                if (IsInstallOnceUnit(unit) && DestLooksPresent(unit))
                    continue;
                if (IsUserDataUnit(unit))
                {
                    if (ProfileNeedsOverlay(unit))
                        work.Add(unit);
                    continue;
                }
                if (!UnitMatchesLocal(unit))
                    work.Add(unit);
            }

            if (work.Count == 0)
            {
                FinishInstallFlag(index.version, "Get VR ok (already present)");
                SaveLocalVersion(index.version ?? "1.0.0");
                progress(100, -1, Loc.T("vr_already"));
                return;
            }

            // Full download pipeline on VR-only unit list (skip re-scan of AE).
            var slim = new Index
            {
                format = index.format,
                version = index.version,
                gameRoot = index.gameRoot,
                units = work
            };
            InstallOrRepair(
                slim,
                progress,
                cancel,
                onlyMismatched: false,
                wipeMismatched: false,
                channel: InstallChannel.AeAndVr,
                jobName: Loc.T("job_get_vr"));
        }

        /// <summary>Remove VR packages from disk and switch channel back to AE-only.</summary>
        public static void RemoveVrChannel(
            Index index,
            ProgressHandler progress,
            CancellationToken cancel)
        {
            if (!IsInstalled())
                throw new FlappyException("Game is not installed.");

            var vrUnits = GetVrUnits(index != null ? index.units : null);
            // Also wipe common VR paths even if index is stale
            var extraPaths = new[]
            {
                "StockGameVR",
                Path.Combine("profiles", GetProfileName("VR"))
            };

            int total = vrUnits.Count + extraPaths.Length;
            int i = 0;

            foreach (var unit in vrUnits)
            {
                cancel.ThrowIfCancellationRequested();
                i++;
                string name = DisplayName(unit);
                progress(ClampPct(i * 90.0 / Math.Max(1, total)), -1, Loc.F("removing_vr", name));
                if (!IsRootUnit(unit))
                    WipeUnitDestination(unit);
            }

            foreach (var rel in extraPaths)
            {
                cancel.ThrowIfCancellationRequested();
                i++;
                string dest = Path.Combine(GameRootPath, rel);
                progress(ClampPct(i * 90.0 / Math.Max(1, total)), -1, Loc.F("removing_vr", rel));
                if (Directory.Exists(dest))
                {
                    try { DeleteDirectoryRobust(dest); }
                    catch (Exception ex)
                    {
                        throw new FlappyException(
                            "Cannot remove:\n" + dest + "\n\nClose MO2/game and retry.",
                            ex.ToString(), ex);
                    }
                }
            }

            // Best-effort: remove (VR)* mod folders not covered if index missing
            string modsRoot = Path.Combine(GameRootPath, "mods");
            if (Directory.Exists(modsRoot))
            {
                foreach (var dir in Directory.GetDirectories(modsRoot))
                {
                    cancel.ThrowIfCancellationRequested();
                    string leaf = Path.GetFileName(dir) ?? "";
                    if (leaf.IndexOf("(VR)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        leaf.IndexOf("Re-Dovah VR", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { DeleteDirectoryRobust(dir); }
                        catch (Exception ex) { LauncherLog.Warn("remove VR mod " + leaf + ": " + ex.Message); }
                    }
                }
            }

            SaveChannel(InstallChannel.AeOnly);
            if (string.Equals(GetSavedMode(), "VR", StringComparison.OrdinalIgnoreCase))
                SaveMode("AE");

            progress(100, -1, Loc.T("vr_removed"));
            LauncherLog.Info("Remove VR done, channel=AE");
        }

        /// <summary>Delete this game's install folder + version/cache. Launcher exe stays.</summary>
        public static void UninstallCurrentGame()
        {
            string root = GameRootPath;
            if (string.IsNullOrEmpty(root))
                throw new FlappyException(Loc.T("need_install"));
            if (!Directory.Exists(root) && !File.Exists(InstallFlagPath))
                throw new FlappyException(Loc.T("need_install"));

            try
            {
                if (Directory.Exists(root))
                    DeleteDirectoryRobust(root);
            }
            catch (Exception ex)
            {
                throw new FlappyException(
                    Loc.IsRu
                        ? "Не удалось удалить папку:\n" + root + "\n\nЗакройте MO2 / игру и повторите."
                        : "Cannot remove:\n" + root + "\n\nClose MO2/game and retry.",
                    ex.ToString(), ex);
            }

            try { if (File.Exists(LocalVersionPath)) File.Delete(LocalVersionPath); } catch { }
            try
            {
                string cache = DownloadCacheDir;
                if (!string.IsNullOrEmpty(cache) && Directory.Exists(cache))
                    DeleteDirectoryRobust(cache);
            }
            catch { }

            LauncherLog.Info("Uninstalled game " + GameCatalog.Current.Id + " at " + root);
        }

        public static PlayButtonState ResolvePlayState(bool installed, Version local, Version online, bool busy)
        {
            if (busy) return PlayButtonState.Busy;
            if (!installed) return PlayButtonState.Install;
            if (online != null && local < online) return PlayButtonState.Update;
            // installed but fingerprint drift still allows Play; Update only on version
            return PlayButtonState.Play;
        }

        public static void EnsureDiskSpace(IEnumerable<PackageUnit> units)
        {
            long need = 0;
            foreach (var u in units)
                need += Math.Max(0, u.packageSize);
            // extract needs roughly package + uncompressed; use 1.15x packages + 500MB margin
            long require = (long)(need * 1.15) + 500L * 1024 * 1024;
            string root = Path.GetPathRoot(Path.GetFullPath(GameRootPath));
            if (string.IsNullOrEmpty(root)) return;
            try
            {
                var di = new DriveInfo(root);
                if (di.AvailableFreeSpace < require)
                {
                    throw new FlappyException(
                        "Not enough free disk space.\n\n" +
                        "Need about " + FormatBytes(require) + " free on " + root + "\n" +
                        "Available: " + FormatBytes(di.AvailableFreeSpace));
                }
            }
            catch (FlappyException) { throw; }
            catch (Exception ex)
            {
                LauncherLog.Warn("Disk space check failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Fresh install is allowed from the torrent bundle, from a per-game CDN flag,
        /// or from <see cref="Constants.ALLOW_CDN_FRESH_INSTALL"/>.
        /// </summary>
        public static bool CanFreshInstall()
        {
            if (IsInstalled()) return true;
            if (Constants.HasLocalPackageBundle) return true;
            if (GameCatalog.Current != null && GameCatalog.Current.AllowCdnFreshInstall)
                return true;
            return Constants.ALLOW_CDN_FRESH_INSTALL;
        }

        public static void InstallAll(Index index, ProgressHandler progress, CancellationToken cancel, InstallChannel channel)
        {
            bool update = IsInstalled();
            if (!update && !CanFreshInstall())
                throw new FlappyException(Loc.T("need_torrent"));
            InstallOrRepair(index, progress, cancel,
                onlyMismatched: update,
                wipeMismatched: false,
                channel: channel,
                jobName: update ? Loc.T("job_update") : Loc.T("job_install"));
        }

        public static void RepairAll(Index index, ProgressHandler progress, CancellationToken cancel, InstallChannel channel)
        {
            InstallOrRepair(index, progress, cancel,
                onlyMismatched: true,
                wipeMismatched: true,
                channel: channel,
                jobName: Loc.T("job_repair"));
        }

        public static List<PackageUnit> FindUnitsNeedingRepair(
            Index index,
            InstallChannel channel,
            Action<string> status,
            CancellationToken cancel)
        {
            var need = new List<PackageUnit>();
            var scoped = FilterByChannel(index.units, channel);
            int n = scoped.Count;
            for (int i = 0; i < n; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var unit = scoped[i];
                string name = DisplayName(unit);
                if (status != null)
                    status(string.Format("Checking {0}/{1}: {2}", i + 1, n, name));

                if (IsInstallOnceUnit(unit) && DestLooksPresent(unit))
                    continue;

                if (IsUserDataUnit(unit))
                {
                    if (ProfileNeedsOverlay(unit))
                        need.Add(unit);
                    continue;
                }

                if (!UnitMatchesLocal(unit))
                    need.Add(unit);
            }
            return need;
        }

        public static void WriteRepairReport(IList<PackageUnit> need, IList<PackageUnit> scoped, string note)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(Constants.LAUNCHER_NAME + " repair report — " + GameCatalog.Current.Title);
                sb.AppendLine(DateTime.Now.ToString("o"));
                sb.AppendLine(note ?? "");
                sb.AppendLine("Scoped units: " + (scoped != null ? scoped.Count : 0));
                sb.AppendLine("Need download: " + (need != null ? need.Count : 0));
                sb.AppendLine();
                if (need != null)
                {
                    foreach (var u in need)
                    {
                        sb.AppendLine(string.Format("{0}\t{1}\t{2}", u.kind, u.path, u.packageSize));
                    }
                }
                File.WriteAllText(RepairReportPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { LauncherLog.Warn("repair_report: " + ex.Message); }
        }

        // ---------- fingerprints ----------

        public static string ComputeFolderFingerprint(string folderAbs, string folderRel)
        {
            return ComputeFolderFingerprint(folderAbs, folderRel, false);
        }

        private static string ComputeFolderFingerprint(string folderAbs, string folderRel, bool includeMetaIni)
        {
            if (string.IsNullOrEmpty(folderAbs) || !Directory.Exists(folderAbs))
                return null;

            folderAbs = Path.GetFullPath(folderAbs).TrimEnd('\\', '/');
            int baseLen = folderAbs.Length;
            var lines = new List<string>();

            string[] files;
            try { files = Directory.GetFiles(folderAbs, "*", SearchOption.AllDirectories); }
            catch { return null; }

            foreach (string f in files)
            {
                FileInfo fi;
                try { fi = new FileInfo(f); if (!fi.Exists) continue; }
                catch { continue; }

                string sub = f.Substring(baseLen).TrimStart('\\', '/');
                if (IsVolatileRelPath(folderRel, sub, includeMetaIni))
                    continue;

                string rel = string.IsNullOrEmpty(folderRel)
                    ? sub
                    : folderRel.TrimEnd('\\', '/') + "\\" + sub;
                rel = rel.Replace('/', '\\');
                lines.Add(rel + "|" + fi.Length);
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return HashFingerprintLines(lines);
        }

        public static bool UnitMatchesLocal(PackageUnit unit)
        {
            if (unit == null || string.IsNullOrWhiteSpace(unit.fingerprint))
                return false;

            string expected = unit.fingerprint.Trim();
            string actual = ComputeUnitFingerprint(unit);
            if (!string.IsNullOrEmpty(actual)
                && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Older indexes hashed meta.ini; MO2 rewrites it constantly.
            string legacy = ComputeUnitFingerprint(unit, true);
            return !string.IsNullOrEmpty(legacy)
                && string.Equals(legacy, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeUnitFingerprint(PackageUnit unit, bool includeMetaIni = false)
        {
            if (IsRootUnit(unit))
                return ComputeRootFilesFingerprint(includeMetaIni);

            string dest = ResolveExtractDir(unit);
            if (!Directory.Exists(dest)) return null;
            string folderRel = (unit.path ?? "").Replace('/', '\\').TrimEnd('\\');
            return ComputeFolderFingerprint(dest, folderRel, includeMetaIni);
        }

        public static bool IsUserDataUnit(PackageUnit unit)
        {
            if (unit == null) return false;
            string p = (unit.path ?? "").Replace('\\', '/').Trim().Trim('/');
            // Whole profiles tree or split packages profiles/<name>
            if (p.Equals("profiles", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
                return true;
            return p.Equals("overwrite", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInstallOnceUnit(PackageUnit unit)
        {
            if (unit == null) return false;
            if (unit.installOnce) return true;
            string p = (unit.path ?? "").Replace('\\', '/').Trim().Trim('/');
            return p.Equals("StockGame", StringComparison.OrdinalIgnoreCase)
                || p.Equals("StockGameVR", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsModUnit(PackageUnit unit)
        {
            if (unit == null) return false;
            if (string.Equals(unit.kind, "MOD", StringComparison.OrdinalIgnoreCase)) return true;
            string p = (unit.path ?? "").Replace('\\', '/').Trim().Trim('/');
            return p.StartsWith("mods/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool DestLooksPresent(PackageUnit unit)
        {
            if (unit == null) return false;
            string dest = ResolveExtractDir(unit);
            if (string.IsNullOrEmpty(dest) || !Directory.Exists(dest)) return false;
            try { return Directory.GetFileSystemEntries(dest).Length > 0; }
            catch { return true; }
        }

        private static bool ProfileNeedsOverlay(PackageUnit unit)
        {
            if (unit == null) return false;
            if (!DestLooksPresent(unit)) return true;
            string want = GetUnitStamp(unit);
            if (string.IsNullOrEmpty(want)) return true;
            string applied = GetAppliedSha(unit.id);
            if (string.IsNullOrEmpty(applied)) return true;
            return !string.Equals(applied, want, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRootUnit(PackageUnit unit)
        {
            if (unit == null) return false;
            if (string.Equals(unit.kind, "ROOT", StringComparison.OrdinalIgnoreCase)) return true;
            return string.IsNullOrEmpty(unit.path) || unit.path == ".";
        }

        private static bool IsVolatileFile(string fileName, bool includeMetaIni = false)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            if (Regex.IsMatch(fileName, @"\.\d{4}_\d{2}_\d{2}_\d{2}_\d{2}_\d{2}$")) return true;
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Equals("imgui.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (!includeMetaIni && fileName.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Equals(".flappy_empty", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly string[] StockGameJunkDirs = { "enbcache", "default", "Profile", "Skyrim" };

        private static bool IsVolatileRelPath(string folderRel, string sub, bool includeMetaIni = false)
        {
            if (IsVolatileFile(Path.GetFileName(sub), includeMetaIni)) return true;
            string top = (folderRel ?? "").Replace('/', '\\').Trim('\\');
            int slash = top.IndexOf('\\');
            if (slash >= 0) top = top.Substring(0, slash);
            string first = (sub ?? "").Replace('/', '\\').Trim('\\');
            int s = first.IndexOf('\\');
            if (s >= 0) first = first.Substring(0, s);
            if (top.Equals("StockGame", StringComparison.OrdinalIgnoreCase) ||
                top.Equals("StockGameVR", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string junk in StockGameJunkDirs)
                {
                    if (first.Equals(junk, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            if (top.Equals("profiles", StringComparison.OrdinalIgnoreCase) ||
                top.StartsWith("profiles\\", StringComparison.OrdinalIgnoreCase))
            {
                if (first.Equals("saves", StringComparison.OrdinalIgnoreCase)) return true;
                string ext = Path.GetExtension(sub ?? "");
                if (ext.Equals(".ess", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".skse", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ComputeRootFilesFingerprint(bool includeMetaIni = false)
        {
            if (!Directory.Exists(GameRootPath)) return null;
            var lines = new List<string>();
            foreach (string f in Directory.GetFiles(GameRootPath))
            {
                string name = Path.GetFileName(f);
                if (IsIgnoredRootFile(name) || IsVolatileFile(name, includeMetaIni)) continue;
                try
                {
                    var fi = new FileInfo(f);
                    if (!fi.Exists) continue;
                    lines.Add(name + "|" + fi.Length);
                }
                catch { }
            }
            lines.Sort(StringComparer.OrdinalIgnoreCase);
            return HashFingerprintLines(lines);
        }

        private static bool IsIgnoredRootFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (name.Equals(".flappy_installed", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals(".flappy_mode", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Equals(".flappy_channel", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("Diff_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string HashFingerprintLines(List<string> lines)
        {
            using (var sha = SHA256.Create())
            {
                var enc = Encoding.UTF8;
                foreach (string line in lines)
                {
                    byte[] b = enc.GetBytes(line + "\n");
                    sha.TransformBlock(b, 0, b.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // ---------- install core ----------

        public static void InstallOrRepair(
            Index index,
            ProgressHandler progress,
            CancellationToken cancel,
            bool onlyMismatched,
            bool wipeMismatched,
            InstallChannel channel,
            string jobName)
        {
            if (index == null || index.units == null || index.units.Count == 0)
                throw new FlappyException("index.json has no packages.");

            EnsureSevenZip(progress, cancel);
            if (string.IsNullOrEmpty(SevenZipPath) || !File.Exists(SevenZipPath))
                throw new FlappyException(Loc.T("sevenzip_missing"));

            Directory.CreateDirectory(GameRootPath);
            Directory.CreateDirectory(DownloadCacheDir);
            SaveChannel(channel);

            var scoped = FilterByChannel(index.units, channel);
            LauncherLog.Info(jobName + " channel=" + channel + " scoped=" + scoped.Count);

            List<PackageUnit> work;
            int skipped = 0;
            if (onlyMismatched)
            {
                progress(0, -1, Loc.T("checking_fp"));
                work = FindUnitsNeedingRepair(index, channel, msg => progress(1, -1, Loc.F("checking_n", msg)), cancel);
                skipped = scoped.Count - work.Count;
                WriteRepairReport(work, scoped, jobName + " scan");
            }
            else
            {
                work = new List<PackageUnit>(scoped);
            }

            if (work.Count == 0)
            {
                FinishInstallFlag(index.version, jobName + " ok skipped=" + skipped);
                SaveLocalVersion(index.version ?? "1.0.0");
                try { RestoreModOrder(index); } catch (Exception ex) { LauncherLog.Warn("modlist: " + ex.Message); }
                progress(100, -1, Loc.F("job_complete_ok", jobName, skipped));
                return;
            }

            EnsureDiskSpace(work);

            // Fixed worker pool (N threads) — better than 400 Task.Run + semaphore.
            // Http connection limit is raised in HttpDownloader (default was 2!).
            var downloaded = new ConcurrentDictionary<string, string>();
            var keepSource = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var errors = new ConcurrentQueue<Exception>();
            int doneDl = 0;
            int total = work.Count;
            var gate = new object();
            long lastUi = 0;
            var slots = new ConcurrentDictionary<string, DlSlot>();
            bool localBundle = Constants.IsLocalPackagesMode;

            // Smooth speed (EMA) — does not drop to 0 between small files
            long speedBytes = 0;
            long speedTick = Environment.TickCount;
            double speedEma = 0;

            var queue = new ConcurrentQueue<PackageUnit>(work);
            int workers = Math.Max(1, Math.Min(Constants.DOWNLOAD_PARALLELISM, work.Count));
            var workerTasks = new Task[workers];

            progress(2, -1, localBundle
                ? Loc.F("dl_local", work.Count)
                : Loc.F("dl_start", work.Count, Constants.DOWNLOAD_PARALLELISM));

            long totalDlBytes = 0;
            foreach (var u in work) totalDlBytes += Math.Max(0, u.packageSize);
            long completedDlBytes = 0;

            Action reportUi = () =>
            {
                long now = Environment.TickCount;
                long prevUi;
                lock (gate) { prevUi = lastUi; }
                if (now - prevUi < 250) return;
                lock (gate) { lastUi = now; }

                long bytes;
                long tick0;
                double ema;
                int finished;
                lock (gate)
                {
                    bytes = Interlocked.Exchange(ref speedBytes, 0);
                    tick0 = speedTick;
                    speedTick = now;
                    finished = doneDl;
                    double dt = Math.Max(0.2, (now - tick0) / 1000.0);
                    double instant = bytes / dt;
                    if (speedEma <= 0) speedEma = instant;
                    else speedEma = speedEma * 0.65 + instant * 0.35;
                    if (instant < speedEma * 0.05 && finished < total)
                        speedEma = speedEma * 0.92;
                    ema = speedEma;
                }

                double activeFrac = 0;
                int activeN = 0;
                long activeRecv = 0;
                foreach (var s in slots.Values)
                {
                    if (s.Total > 0)
                    {
                        activeFrac += Math.Min(1.0, (double)s.Received / Math.Max(1, s.Total));
                        activeN++;
                        activeRecv += s.Received;
                    }
                    else if (s.Received > 0)
                    {
                        activeN++;
                        activeRecv += s.Received;
                    }
                }
                if (activeN > 0) activeFrac /= activeN;
                double pct = 2 + (finished + activeFrac) / Math.Max(1.0, total) * 83.0;
                if (pct > 85) pct = 85;
                // Current archive %: largest active download (or 100 if idle between files)
                int curPct = -1;
                double best = -1;
                foreach (var s in slots.Values)
                {
                    if (s == null || s.Total <= 0) continue;
                    double f = Math.Min(1.0, (double)s.Received / s.Total);
                    if (f > best) { best = f; curPct = ClampPct(f * 100.0); }
                }

                long doneBytes = Interlocked.Read(ref completedDlBytes) + activeRecv;
                long remainingBytes = Math.Max(0, totalDlBytes - doneBytes);
                progress(ClampPct(pct), curPct, FormatDownloadStatus(finished, total, ema, slots.Values, remainingBytes));
            };

            for (int w = 0; w < workers; w++)
            {
                workerTasks[w] = Task.Run(() =>
                {
                    PackageUnit u;
                    while (queue.TryDequeue(out u))
                    {
                        cancel.ThrowIfCancellationRequested();
                        string name = DisplayName(u);
                        string shortName = ShortName(name);
                        string id = u.id ?? shortName;
                        try
                        {
                            // Per-game CDN folder + optional local torrent file for same relative path
                            string cdnUrl = HttpDownloader.CombineUrl(GameCatalog.PackagesBaseUrl, u.package);
                            string localPath = GameCatalog.GetLocalPackagePath(u.package);
                            string cachePath = Path.Combine(DownloadCacheDir, id + ".7z");

                            var slot = new DlSlot { Name = shortName, Total = u.packageSize > 0 ? u.packageSize : -1 };
                            slots[id] = slot;
                            long lastRecv = 0;

                            bool fromLocal;
                            string local = HttpDownloader.AcquirePackage(
                                localPath,
                                cdnUrl,
                                cachePath,
                                u.packageSize,
                                u.packageSha256,
                                (recv, tot) =>
                                {
                                    long delta = recv - lastRecv;
                                    if (delta > 0)
                                    {
                                        Interlocked.Add(ref speedBytes, delta);
                                        lastRecv = recv;
                                    }
                                    slot.Received = recv;
                                    if (tot > 0) slot.Total = tot;
                                    reportUi();
                                },
                                cancel,
                                out fromLocal);

                            DlSlot removed;
                            slots.TryRemove(id, out removed);
                            downloaded[id] = local;
                            if (fromLocal) keepSource[id] = 0;
                            Interlocked.Increment(ref doneDl);
                            Interlocked.Add(ref completedDlBytes, Math.Max(0, u.packageSize));
                            reportUi();
                            LauncherLog.Info((fromLocal ? "Local " : "CDN ") + name);
                        }
                        catch (Exception ex)
                        {
                            DlSlot removed;
                            slots.TryRemove(id, out removed);
                            errors.Enqueue(ex);
                            LauncherLog.Error("Package acquire failed " + name, ex);
                        }
                    }
                }, cancel);
            }

            try { Task.WaitAll(workerTasks, cancel); }
            catch (OperationCanceledException) { throw; }

            if (!errors.IsEmpty)
            {
                Exception first;
                errors.TryPeek(out first);
                throw new FlappyException(
                    FlappyException.FormatForUser(first) + "\n\nSee launcher.log for details.",
                    first != null ? first.ToString() : null,
                    first);
            }

            ExtractUnitsParallel(
                work,
                downloaded,
                keepSource,
                wipeMismatched,
                onlyMismatched,
                total,
                progress,
                cancel);

            try { RestoreModOrder(index); } catch (Exception ex) { LauncherLog.Warn("modlist merge: " + ex.Message); }

            FinishInstallFlag(index.version, jobName + " fixed=" + total + " skipped=" + skipped);
            SaveLocalVersion(index.version ?? "1.0.0");
            progress(100, -1, Loc.F("job_complete", jobName, total));
            LauncherLog.Info(jobName + " done fixed=" + total + " skipped=" + skipped);
        }

        private static void FinishInstallFlag(string version, string note)
        {
            File.WriteAllText(InstallFlagPath,
                GameCatalog.Current.Title + " installed" + Environment.NewLine +
                "game=" + GameCatalog.Current.Id + Environment.NewLine +
                "version=" + version + Environment.NewLine +
                "date=" + DateTime.Now.ToString("o") + Environment.NewLine +
                "note=" + note + Environment.NewLine,
                Encoding.UTF8);
        }

        /// <summary>
        /// Extract acquired archives with up to EXTRACT_PARALLELISM 7-Zip processes.
        /// Units whose destination folders overlap (ROOT vs anything, parent vs child) stay serialized.
        /// </summary>
        private static void ExtractUnitsParallel(
            List<PackageUnit> work,
            ConcurrentDictionary<string, string> downloaded,
            ConcurrentDictionary<string, byte> keepSource,
            bool wipeMismatched,
            bool onlyMismatched,
            int total,
            ProgressHandler progress,
            CancellationToken cancel)
        {
            int extractWorkers = Math.Max(1, Math.Min(Constants.EXTRACT_PARALLELISM, work.Count));
            LauncherLog.Info("Extract workers=" + extractWorkers + " packages=" + work.Count);

            var pending = new List<PackageUnit>(work);
            var inflightDests = new List<string>();
            var extractErrors = new ConcurrentQueue<Exception>();
            var extractActive = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var extractGate = new object();
            var uiGate = new object();
            int extractRunning = 0;
            int doneExtract = 0;
            int captureProfiles = 0;
            long lastUi = 0;

            Action reportUi = () =>
            {
                long now = Environment.TickCount;
                lock (uiGate)
                {
                    if (now - lastUi < 200 && doneExtract < total) return;
                    lastUi = now;
                }

                int doneSnap = doneExtract;
                var names = new List<string>();
                foreach (var kv in extractActive)
                    names.Add(kv.Value);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                string shown = names.Count == 0 ? "…" : string.Join(" · ", names);
                int seq = Math.Min(total, Math.Max(1, doneSnap + names.Count));
                double pct = 85 + 14.0 * doneSnap / Math.Max(1, total);
                int extractCur = ClampPct(100.0 * seq / Math.Max(1, total));
                progress(ClampPct(pct), extractCur, Loc.F("extracting", seq, total, shown));
            };

            Action<PackageUnit> extractOne = unit =>
            {
                string name = DisplayName(unit);
                string destKey = NormalizeExtractDest(unit);
                try
                {
                    cancel.ThrowIfCancellationRequested();
                    string local;
                    if (!downloaded.TryGetValue(unit.id, out local) || !File.Exists(local))
                        throw new FlappyException("Missing downloaded package for:\n" + name);

                    if (wipeMismatched && onlyMismatched && !IsUserDataUnit(unit) && !IsInstallOnceUnit(unit))
                    {
                        progress(ClampPct(85), -1, "Clearing:\n" + name);
                        WipeUnitDestination(unit);
                    }

                    string dest = ResolveExtractDir(unit);
                    Directory.CreateDirectory(dest);

                    bool overlayProfile = IsUserDataUnit(unit) && DestLooksPresent(unit);
                    if (overlayProfile)
                        OverlayProfileArchive(local, dest);
                    else if (IsModUnit(unit))
                        Extract7zAtomic(local, dest);
                    else
                        Extract7z(local, dest);

                    if (IsModUnit(unit))
                    {
                        try { SyncDeleteExtraFiles(unit, dest, local); }
                        catch (Exception ex) { LauncherLog.Warn("sync-delete " + name + ": " + ex.Message); }
                    }

                    string idKey = unit.id ?? ShortName(name);
                    if (!keepSource.ContainsKey(idKey))
                    {
                        try { File.Delete(local); } catch { }
                    }

                    if (IsUserDataUnit(unit))
                        Interlocked.Exchange(ref captureProfiles, 1);

                    try { StampApplied(unit); }
                    catch (Exception ex) { LauncherLog.Warn("applied stamp: " + ex.Message); }
                }
                catch (Exception ex)
                {
                    extractErrors.Enqueue(ex);
                    LauncherLog.Error("extract " + name, ex);
                }
                finally
                {
                    string removed;
                    extractActive.TryRemove(unit.id ?? name, out removed);
                    lock (extractGate)
                    {
                        extractRunning--;
                        inflightDests.Remove(destKey);
                        doneExtract++;
                        Monitor.PulseAll(extractGate);
                    }
                    reportUi();
                }
            };

            while (true)
            {
                PackageUnit toStart = null;
                lock (extractGate)
                {
                    while (toStart == null)
                    {
                        if (extractRunning == 0 && (pending.Count == 0 || !extractErrors.IsEmpty))
                            break;

                        if (cancel.IsCancellationRequested)
                        {
                            if (extractRunning == 0) break;
                            Monitor.Wait(extractGate, 250);
                            continue;
                        }

                        if (!extractErrors.IsEmpty)
                        {
                            if (extractRunning == 0) break;
                            Monitor.Wait(extractGate, 250);
                            continue;
                        }

                        if (extractRunning < extractWorkers)
                        {
                            for (int i = 0; i < pending.Count; i++)
                            {
                                string destKey = NormalizeExtractDest(pending[i]);
                                bool conflict = false;
                                for (int d = 0; d < inflightDests.Count; d++)
                                {
                                    if (ExtractDestinationsOverlap(destKey, inflightDests[d]))
                                    {
                                        conflict = true;
                                        break;
                                    }
                                }
                                if (conflict) continue;

                                toStart = pending[i];
                                pending.RemoveAt(i);
                                inflightDests.Add(destKey);
                                extractRunning++;
                                string nm = DisplayName(toStart);
                                extractActive[toStart.id ?? nm] = ShortName(nm);
                                break;
                            }
                        }

                        if (toStart != null) break;
                        Monitor.Wait(extractGate, 250);
                    }
                }

                if (toStart != null)
                {
                    PackageUnit captured = toStart;
                    Task.Factory.StartNew(
                        () => extractOne(captured),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);
                    reportUi();
                    continue;
                }

                break;
            }

            cancel.ThrowIfCancellationRequested();

            if (!extractErrors.IsEmpty)
            {
                Exception first;
                extractErrors.TryPeek(out first);
                if (first is OperationCanceledException)
                    throw (OperationCanceledException)first;
                throw new FlappyException(
                    FlappyException.FormatForUser(first) + "\n\nSee launcher.log for details.",
                    first != null ? first.ToString() : null,
                    first);
            }

            if (captureProfiles != 0)
            {
                try { CaptureOfficialModlists(); }
                catch (Exception ex) { LauncherLog.Warn("capture modlist: " + ex.Message); }
            }
        }

        private static string NormalizeExtractDest(PackageUnit unit)
        {
            return Path.GetFullPath(ResolveExtractDir(unit)).TrimEnd('\\', '/') + "\\";
        }

        private static bool ExtractDestinationsOverlap(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }

        private static void WipeUnitDestination(PackageUnit unit)
        {
            if (IsRootUnit(unit)) return;
            string dest = ResolveExtractDir(unit);
            if (string.IsNullOrEmpty(dest)) return;
            string root = Path.GetFullPath(GameRootPath).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new FlappyException("Refuse to wipe path outside game root:\n" + dest);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return;
            if (Directory.Exists(dest))
            {
                try { DeleteDirectoryRobust(dest); }
                catch (Exception ex)
                {
                    throw new FlappyException("Cannot clear folder:\n" + dest + "\n\nClose MO2/game and retry.", ex.ToString(), ex);
                }
            }
        }

        /// <summary>
        /// Recursive delete that first drops ReadOnly (git objects in Tools\Synthesis\.git, etc.).
        /// .NET Directory.Delete(recursive) throws UnauthorizedAccessException on those files.
        /// </summary>
        private static void DeleteDirectoryRobust(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;
            Exception last = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    ClearReadOnlyTree(path);
                    Directory.Delete(path, true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(150 * (attempt + 1));
                }
            }
            throw last ?? new IOException("Cannot delete: " + path);
        }

        private static void ClearReadOnlyTree(string path)
        {
            var root = new DirectoryInfo(path);
            if (!root.Exists) return;
            StripReadOnly(root);
            FileSystemInfo[] entries;
            try { entries = root.GetFileSystemInfos("*", SearchOption.AllDirectories); }
            catch { return; }
            foreach (var e in entries)
                StripReadOnly(e);
        }

        private static void StripReadOnly(FileSystemInfo info)
        {
            try
            {
                info.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
        }

        public static string ResolveExtractDir(PackageUnit unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.path) || unit.path == "." ||
                string.Equals(unit.kind, "ROOT", StringComparison.OrdinalIgnoreCase))
                return GameRootPath;
            string rel = unit.path.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(GameRootPath, rel);
        }

        private static void Extract7z(string archive, string destDir)
        {
            Directory.CreateDirectory(destDir);

            if (!File.Exists(archive))
                throw new FlappyException("Archive missing:\n" + archive);
            if (!HttpDownloader.LooksLike7z(archive))
            {
                try { File.Delete(archive); } catch { }
                throw new FlappyException(
                    "Cached file is not a valid 7z archive (deleted from cache):\n" +
                    Path.GetFileName(archive) + "\n\nRetry install — it will re-download.");
            }

            string sevenZip = SevenZipPath;
            if (string.IsNullOrEmpty(sevenZip) || !File.Exists(sevenZip))
                sevenZip = SevenZipBootstrap.Ensure(null, CancellationToken.None);

            var psi = new ProcessStartInfo
            {
                FileName = sevenZip,
                WorkingDirectory = Path.GetDirectoryName(sevenZip),
                Arguments = string.Format("x -y -aoa \"-o{0}\" -- \"{1}\"", destDir, archive),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.EnvironmentVariables["PATH"] = Path.GetDirectoryName(sevenZip) + ";" + Environment.GetFolderPath(Environment.SpecialFolder.System);
            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new FlappyException("Failed to start 7za.exe");
                string stdout = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode > 1)
                {
                    // Corrupt cache (or corrupt CDN package) — drop local file so next run re-downloads
                    try { File.Delete(archive); } catch { }
                    string detail = string.IsNullOrWhiteSpace(err) ? stdout : err;
                    throw new FlappyException(
                        "Extract failed for:\n" + Path.GetFileName(archive) + "\n\n" +
                        detail.Trim() + "\n\n" +
                        "Cache file was deleted. If this repeats after a full re-download,\n" +
                        "the CDN package is corrupt — repack that mod and re-upload.",
                        "7z exit " + p.ExitCode);
                }
            }
        }

        private static void Extract7zAtomic(string archive, string destDir)
        {
            string parent = Path.GetDirectoryName(destDir);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

            string tmp = destDir + "_tmp_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            if (Directory.Exists(tmp))
            {
                try { DeleteDirectoryRobust(tmp); } catch { }
            }

            try
            {
                Extract7z(archive, tmp);
                if (Directory.Exists(destDir))
                {
                    DeleteDirectoryRobust(destDir);
                }
                Directory.Move(tmp, destDir);
            }
            catch
            {
                try { if (Directory.Exists(tmp)) DeleteDirectoryRobust(tmp); } catch { }
                throw;
            }
        }

        public static int ClearShaderCache()
        {
            int count = 0;
            string root = GameRootPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;

            string[] junkDirs = {
                Path.Combine(root, "enbcache"),
                Path.Combine(root, "ShaderCache"),
                Path.Combine(root, "StockGame", "enbcache"),
                Path.Combine(root, "StockGame", "ShaderCache"),
                Path.Combine(root, "StockGameVR", "enbcache"),
                Path.Combine(root, "StockGameVR", "ShaderCache")
            };

            foreach (var d in junkDirs)
            {
                if (Directory.Exists(d))
                {
                    try { DeleteDirectoryRobust(d); count++; } catch { }
                }
            }
            LauncherLog.Info("ClearShaderCache removed dirs: " + count);
            return count;
        }

        /// <summary>
        /// Overlay a profile archive onto an existing profile: copy packed files except saves.
        /// Client saves stay. Official modlist/plugins/loadorder come from the archive.
        /// </summary>
        private static void OverlayProfileArchive(string archive, string destDir)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "flappy_profile_" + Guid.NewGuid().ToString("N"));
            try
            {
                Extract7z(archive, tmp);
                string tmpRoot = Path.GetFullPath(tmp).TrimEnd('\\');
                int baseLen = tmpRoot.Length;
                string[] files;
                try { files = Directory.GetFiles(tmpRoot, "*", SearchOption.AllDirectories); }
                catch { return; }
                int copied = 0;
                foreach (string f in files)
                {
                    string rel = f.Substring(baseLen).TrimStart('\\', '/');
                    if (IsProfileSavePath(rel)) continue;
                    string target = Path.Combine(destDir, rel);
                    string parent = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    File.Copy(f, target, true);
                    copied++;
                }
                LauncherLog.Info("profile overlay files=" + copied + " dest=" + destDir);
            }
            finally
            {
                try { if (Directory.Exists(tmp)) DeleteDirectoryRobust(tmp); } catch { }
            }
        }

        private static bool IsProfileSavePath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            string n = rel.Replace('/', '\\').TrimStart('\\');
            if (n.StartsWith("saves\\", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("saves", StringComparison.OrdinalIgnoreCase))
                return true;
            string ext = Path.GetExtension(n);
            return ext.Equals(".ess", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".skse", StringComparison.OrdinalIgnoreCase);
        }

        // ---------- modlist restore ----------

        private static readonly string[] OfficialOrderFiles =
        {
            "modlist.txt", "plugins.txt", "loadorder.txt", "lockedorder.txt", "archives.txt"
        };

        public static string OfficialOrderDir
        {
            get { return Path.Combine(OfficialModlistDir, "official"); }
        }

        public static string AppliedStatePath
        {
            get { return Path.Combine(OfficialModlistDir, "applied.json"); }
        }

        public static void CaptureOfficialModlists()
        {
            Directory.CreateDirectory(OfficialModlistDir);
            string profiles = Path.Combine(GameRootPath, "profiles");
            if (!Directory.Exists(profiles)) return;
            foreach (var dir in Directory.GetDirectories(profiles))
            {
                string name = Path.GetFileName(dir);
                string snap = Path.Combine(OfficialOrderDir, name);
                Directory.CreateDirectory(snap);
                foreach (string file in OfficialOrderFiles)
                {
                    string src = Path.Combine(dir, file);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(snap, file), true);
                }
            }
        }

        public static HashSet<string> GetOfficialModNames(Index index)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (index == null || index.units == null) return set;
            foreach (var u in index.units)
            {
                if (IsModUnit(u))
                {
                    string p = (u.path ?? "").Replace('\\', '/').Trim().Trim('/');
                    if (p.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
                    {
                        string modName = p.Substring(5).Trim();
                        if (!string.IsNullOrEmpty(modName))
                            set.Add(modName);
                    }
                }
            }
            return set;
        }

        public static void RestoreModOrder()
        {
            RestoreModOrder(null);
        }

        /// <summary>
        /// Overlay official modlist / plugins / loadorder onto every profile.
        /// Client list and order must match the packed pack. Saves are not touched.
        /// Extra folders in mods\ are moved out so MO2 does not append them disabled.
        /// User-installed mods → UserMods next to the launcher. Pack-removed mods are deleted.
        /// </summary>
        public static void RestoreModOrder(Index index)
        {
            string profiles = Path.Combine(GameRootPath, "profiles");
            if (!Directory.Exists(profiles)) return;

            // Ensure initial official snapshot exists if missing
            if (!Directory.Exists(OfficialOrderDir) || Directory.GetDirectories(OfficialOrderDir).Length == 0)
            {
                try { CaptureOfficialModlists(); } catch { }
            }

            var lastOfficial = LoadOfficialNames();
            HashSet<string> newOfficial = null;
            if (index != null && index.units != null)
            {
                newOfficial = GetOfficialModNames(index);
            }

            // Fallback if index wasn't provided or had 0 mods: parse from official snapshots
            if (newOfficial == null || newOfficial.Count == 0)
            {
                newOfficial = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var profileDir in Directory.GetDirectories(profiles))
                {
                    string profileName = Path.GetFileName(profileDir);
                    string snap = Path.Combine(OfficialOrderDir, profileName);
                    if (Directory.Exists(snap))
                    {
                        foreach (string n in ParseModlistNames(Path.Combine(snap, "modlist.txt")))
                            newOfficial.Add(n);
                    }
                }
            }

            // Apply official order files onto each profile
            foreach (var profileDir in Directory.GetDirectories(profiles))
            {
                string profileName = Path.GetFileName(profileDir);
                string snap = Path.Combine(OfficialOrderDir, profileName);
                if (!Directory.Exists(snap))
                    continue;

                int copied = 0;
                foreach (string file in OfficialOrderFiles)
                {
                    string src = Path.Combine(snap, file);
                    if (!File.Exists(src)) continue;
                    File.Copy(src, Path.Combine(profileDir, file), true);
                    copied++;
                }
                if (copied > 0)
                    LauncherLog.Info("official order applied: " + profileName + " files=" + copied);
            }

            if (newOfficial != null && newOfficial.Count > 0)
            {
                QuarantineExtraMods(lastOfficial, newOfficial);
                CleanProfilesModlists(newOfficial);
                SaveOfficialNames(newOfficial);
            }
        }

        private static void CleanProfilesModlists(HashSet<string> officialMods)
        {
            if (officialMods == null || officialMods.Count == 0) return;
            string profilesDir = Path.Combine(GameRootPath, "profiles");
            if (!Directory.Exists(profilesDir)) return;

            CleanModlistInDirectory(profilesDir, officialMods);
            if (Directory.Exists(OfficialOrderDir))
            {
                CleanModlistInDirectory(OfficialOrderDir, officialMods);
            }
        }

        private static void CleanModlistInDirectory(string baseDir, HashSet<string> officialMods)
        {
            foreach (var profileDir in Directory.GetDirectories(baseDir))
            {
                string modlistPath = Path.Combine(profileDir, "modlist.txt");
                if (!File.Exists(modlistPath)) continue;

                try
                {
                    var lines = File.ReadAllLines(modlistPath);
                    var cleanLines = new List<string>(lines.Length);
                    int removed = 0;

                    foreach (var raw in lines)
                    {
                        string line = (raw ?? "").Trim();
                        if (line.Length == 0 || line.StartsWith("#"))
                        {
                            cleanLines.Add(raw);
                            continue;
                        }

                        string entry = line;
                        if (entry.StartsWith("+") || entry.StartsWith("-"))
                            entry = entry.Substring(1).Trim();

                        bool isSeparator = entry.EndsWith("_separator", StringComparison.OrdinalIgnoreCase);
                        if (isSeparator || officialMods.Contains(entry))
                        {
                            cleanLines.Add(raw);
                        }
                        else
                        {
                            removed++;
                        }
                    }

                    if (removed > 0)
                    {
                        File.WriteAllLines(modlistPath, cleanLines.ToArray(), new UTF8Encoding(false));
                        LauncherLog.Info("cleaned profile modlist: " + Path.GetFileName(profileDir) + " removed_obsolete=" + removed);
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn("clean modlist.txt (" + Path.GetFileName(profileDir) + "): " + ex.Message);
                }
            }
        }

        /// <summary>MO2 folder names from modlist.txt (+/- prefix stripped).</summary>
        private static HashSet<string> ParseModlistNames(string modlistPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(modlistPath)) return set;
            foreach (string raw in File.ReadAllLines(modlistPath))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (line[0] == '+' || line[0] == '-')
                    line = line.Substring(1).Trim();
                if (line.Length > 0) set.Add(line);
            }
            return set;
        }

        private static string OfficialNamesPath
        {
            get { return Path.Combine(OfficialModlistDir, "official-names.txt"); }
        }

        private static HashSet<string> LoadOfficialNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(OfficialNamesPath)) return set;
                foreach (string raw in File.ReadAllLines(OfficialNamesPath))
                {
                    string n = (raw ?? "").Trim();
                    if (n.Length > 0) set.Add(n);
                }
            }
            catch (Exception ex) { LauncherLog.Warn("official-names read: " + ex.Message); }
            return set;
        }

        private static void SaveOfficialNames(HashSet<string> names)
        {
            try
            {
                Directory.CreateDirectory(OfficialModlistDir);
                var list = new List<string>(names);
                list.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(OfficialNamesPath, list.ToArray(), new UTF8Encoding(false));
            }
            catch (Exception ex) { LauncherLog.Warn("official-names write: " + ex.Message); }
        }

        public static string UserModsDir
        {
            get
            {
                return Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "UserMods",
                    GameCatalog.Current.InstallFolderName ?? GameCatalog.Current.Id);
            }
        }

        /// <summary>
        /// Folders in mods\ that are not on the new official list would be auto-appended
        /// by MO2 as disabled. User mods go next to the launcher. Pack-removed mods are deleted.
        /// </summary>
        private static void QuarantineExtraMods(HashSet<string> lastOfficial, HashSet<string> newOfficial)
        {
            string modsDir = Path.Combine(GameRootPath, "mods");
            if (!Directory.Exists(modsDir)) return;
            if (newOfficial == null || newOfficial.Count == 0) return;

            int userMoved = 0, packDeleted = 0;
            foreach (string dir in Directory.GetDirectories(modsDir))
            {
                string name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || newOfficial.Contains(name))
                    continue;

                bool wasOfficial = lastOfficial != null && lastOfficial.Contains(name);
                try
                {
                    if (wasOfficial)
                    {
                        DeleteDirectoryRobust(dir);
                        packDeleted++;
                        LauncherLog.Info("pack-removed deleted: " + name);
                    }
                    else
                    {
                        Directory.CreateDirectory(UserModsDir);
                        string dest = UniqueDest(UserModsDir, name);
                        MoveDirectory(dir, dest);
                        userMoved++;
                        LauncherLog.Info("user-mod " + name + " -> " + dest);
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn("quarantine " + name + ": " + ex.Message);
                }
            }

            if (userMoved > 0)
            {
                TryWriteReadme(UserModsDir,
                    "Пользовательские моды, установленные вручную.\r\n\r\n" +
                    "Лаунчер переместил их сюда при обновлении/починке игры, чтобы они не ломали порядок модов и не вызывали вылетов в Mod Organizer 2.\r\n" +
                    "Если вам нужен какой-то из этих модов, скопируйте его папку обратно в директорию mods\\ игры после завершения обновления.\r\n");
            }

            if (userMoved + packDeleted > 0)
                LauncherLog.Info("quarantine user=" + userMoved + " pack-removed-deleted=" + packDeleted);
        }

        private static string UniqueDest(string root, string name)
        {
            string dest = Path.Combine(root, name);
            if (!Directory.Exists(dest) && !File.Exists(dest)) return dest;
            return Path.Combine(root, name + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        }

        private static void MoveDirectory(string src, string dest)
        {
            try
            {
                Directory.Move(src, dest);
            }
            catch (IOException)
            {
                CopyDirectory(src, dest);
                DeleteDirectoryRobust(src);
            }
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (string sub in Directory.GetDirectories(src))
                CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
        }

        private static void TryWriteReadme(string dir, string text)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                string readme = Path.Combine(dir, "README.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme, text, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string GetUnitStamp(PackageUnit unit)
        {
            if (unit == null) return "";
            if (!string.IsNullOrWhiteSpace(unit.packageSha256))
                return unit.packageSha256.Trim();
            return (unit.fingerprint ?? "").Trim();
        }

        private static Dictionary<string, string> LoadApplied()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(AppliedStatePath)) return map;
                var state = JsonAdapter.FromJson<AppliedState>(File.ReadAllText(AppliedStatePath, Encoding.UTF8));
                if (state != null && state.units != null)
                {
                    foreach (var kv in state.units)
                    {
                        if (!string.IsNullOrEmpty(kv.Key))
                            map[kv.Key] = kv.Value ?? "";
                    }
                }
            }
            catch (Exception ex) { LauncherLog.Warn("applied.json read: " + ex.Message); }
            return map;
        }

        private static void SaveApplied(Dictionary<string, string> map)
        {
            Directory.CreateDirectory(OfficialModlistDir);
            var state = new AppliedState { units = map ?? new Dictionary<string, string>() };
            File.WriteAllText(AppliedStatePath, JsonAdapter.ToJson(state), new UTF8Encoding(false));
        }

        private static string GetAppliedSha(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return "";
            string v;
            return LoadApplied().TryGetValue(unitId, out v) ? (v ?? "") : "";
        }

        private static readonly object AppliedLock = new object();

        private static void StampApplied(PackageUnit unit)
        {
            if (unit == null || string.IsNullOrEmpty(unit.id)) return;
            lock (AppliedLock)
            {
                var map = LoadApplied();
                map[unit.id] = GetUnitStamp(unit);
                SaveApplied(map);
            }
        }

        private sealed class AppliedState
        {
            public Dictionary<string, string> units { get; set; }
        }

        private static HashSet<string> ListArchiveFiles(string archive)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string sevenZip = SevenZipPath;
            if (string.IsNullOrEmpty(sevenZip) || !File.Exists(sevenZip))
                sevenZip = SevenZipBootstrap.Ensure(null, CancellationToken.None);

            var psi = new ProcessStartInfo
            {
                FileName = sevenZip,
                WorkingDirectory = Path.GetDirectoryName(sevenZip),
                Arguments = "l -slt -- \"" + archive + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.EnvironmentVariables["PATH"] = Path.GetDirectoryName(sevenZip) + ";" + Environment.GetFolderPath(Environment.SpecialFolder.System);
            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new FlappyException("Failed to start 7za.exe to list archive");
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode > 1)
                    throw new FlappyException("7z list failed for:\n" + Path.GetFileName(archive));

                bool inListing = false;
                string curPath = null;
                bool curFolder = false;
                Action flush = () =>
                {
                    if (string.IsNullOrEmpty(curPath) || curFolder) return;
                    string rel = curPath.Replace('/', '\\').TrimStart('\\');
                    if (rel.Length > 0) set.Add(rel);
                };
                foreach (string raw in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    string line = raw ?? "";
                    if (line.StartsWith("----------"))
                    {
                        inListing = true;
                        continue;
                    }
                    if (!inListing) continue;
                    if (line.Length == 0)
                    {
                        flush();
                        curPath = null;
                        curFolder = false;
                        continue;
                    }
                    if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
                    {
                        flush();
                        curPath = line.Substring(7).Trim();
                        curFolder = false;
                    }
                    else if (line.StartsWith("Folder = ", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = line.Substring(9).Trim();
                        curFolder = v == "+" || v.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (line.StartsWith("Attributes = ", StringComparison.OrdinalIgnoreCase))
                    {
                        if (line.IndexOf('D') >= 0) curFolder = true;
                    }
                }
                flush();
            }
            return set;
        }

        private static void SyncDeleteExtraFiles(PackageUnit unit, string dest, string archive)
        {
            if (string.IsNullOrEmpty(dest) || !Directory.Exists(dest)) return;
            string modsRoot = Path.GetFullPath(Path.Combine(GameRootPath, "mods")).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            if (!full.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(full, modsRoot, StringComparison.OrdinalIgnoreCase))
                return;

            var keep = ListArchiveFiles(archive);
            if (keep.Count == 0)
            {
                LauncherLog.Warn("sync-delete skipped (empty archive list): " + DisplayName(unit));
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(dest, "*", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                LauncherLog.Warn("sync-delete list: " + ex.Message);
                return;
            }

            int deleted = 0;
            int destLen = dest.TrimEnd('\\', '/').Length;
            foreach (string f in files)
            {
                string rel = f.Substring(destLen).TrimStart('\\', '/').Replace('/', '\\');
                if (IsVolatileFile(Path.GetFileName(f))) continue;
                if (keep.Contains(rel)) continue;
                try
                {
                    File.Delete(f);
                    deleted++;
                }
                catch (Exception ex) { LauncherLog.Warn("sync-delete " + rel + ": " + ex.Message); }
            }

            try
            {
                var dirs = Directory.GetDirectories(dest, "*", SearchOption.AllDirectories)
                    .OrderByDescending(d => d.Length);
                foreach (string d in dirs)
                {
                    try
                    {
                        if (Directory.GetFileSystemEntries(d).Length == 0)
                            Directory.Delete(d, false);
                    }
                    catch { }
                }
            }
            catch { }

            if (deleted > 0)
                LauncherLog.Info("sync-delete " + DisplayName(unit) + " extraFiles=" + deleted);
        }

        // ---------- launch ----------

        public static string GetProfileName(string mode)
        {
            if (string.Equals(GameCatalog.Current.Id, "flappy", StringComparison.OrdinalIgnoreCase))
                return Loc.IsRu
                    ? "2 - Flappy RU [SexLab+]"
                    : "1 - Flappy EN [SexLab+]";
            return mode == "VR"
                ? "VR - Re-Dovah"
                : "AE - Re-Dovah";
        }

        /// <summary>MO2 customExecutables title for one-click start (from ModOrganizer.ini.AE/VR).</summary>
        public static string GetStartExecutableTitle(string mode)
        {
            if (string.Equals(GameCatalog.Current.Id, "flappy", StringComparison.OrdinalIgnoreCase))
                return "Run Flappy";
            return mode == "VR" ? "Start Re-Dovah VR" : "Start Re-Dovah";
        }

        /// <summary>MO2 executable that rebuilds Pandora + BodySlide + Synthesis through VFS.</summary>
        public static string GetRebuildExecutableTitle()
        {
            return "Rebuild Outputs";
        }

        /// <summary>
        /// Play: prepare profile/registry, then run MO2 custom executable so the game starts
        /// without needing to click Start Re-Dovah in the MO2 UI.
        /// </summary>
        public static void LaunchGame(string mode)
        {
            if (GameCatalog.Current != null && GameCatalog.Current.IsDoom)
            {
                LaunchDoom();
                return;
            }
            LaunchMo(mode, openUiOnly: false);
        }

        public static void LaunchDoom()
        {
            string exe = Path.Combine(GameRootPath, "zandronum.exe");
            if (!File.Exists(exe))
                throw new FlappyException("zandronum.exe not found.\nInstall the Doom pack first.");

            var args = new StringBuilder();
            args.Append("-iwad doom2.wad");
            string order = Path.Combine(GameRootPath, "loadorder.txt");
            if (File.Exists(order))
            {
                foreach (string raw in File.ReadAllLines(order))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    args.Append(" -file \"").Append(line).Append("\"");
                }
            }
            string host = (GameCatalog.Current.ConnectHost ?? "").Trim();
            if (host.Length == 0) host = "188.235.1.72:10666";
            args.Append(" -connect ").Append(host);

            LauncherLog.Info("Start Doom " + args);
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args.ToString(),
                WorkingDirectory = GameRootPath,
                UseShellExecute = true
            });
        }

        /// <summary>Open Mod Organizer UI for modding (chosen AE/VR profile).</summary>
        public static void LaunchModOrganizer(string mode)
        {
            LaunchMo(mode, openUiOnly: true);
        }

        /// <summary>
        /// Run pack rebuild (Pandora / BodySlide / Synthesis) through MO2 VFS.
        /// Same title as ModOrganizer.ini custom executable "Rebuild Outputs".
        /// </summary>
        public static void LaunchRebuild(string mode)
        {
            LaunchMo(mode, openUiOnly: false, execTitleOverride: GetRebuildExecutableTitle());
        }

        private static void LaunchMo(string mode, bool openUiOnly, string execTitleOverride = null)
        {
            mode = (mode == "VR") ? "VR" : "AE";
            if (mode == "VR" && GetSavedChannel() == InstallChannel.AeOnly)
                throw new FlappyException(Loc.T("ae_only_vr"));

            SaveMode(mode);

            bool isVr = mode == "VR";
            string stockSub = isVr ? "StockGameVR" : "StockGame";
            string stockDir = Path.Combine(GameRootPath, stockSub);
            if (!stockDir.EndsWith("\\")) stockDir += "\\";

            string moExe = Path.Combine(GameRootPath, "ModOrganizer.exe");
            string iniProfile = Path.Combine(GameRootPath, isVr ? "ModOrganizer.ini.VR" : "ModOrganizer.ini.AE");
            string iniMain = Path.Combine(GameRootPath, "ModOrganizer.ini");
            string profileName = GetProfileName(mode);
            string gameName = isVr ? "Skyrim VR" : "Skyrim Special Edition";
            string regPath = isVr
                ? @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim VR"
                : @"SOFTWARE\WOW6432Node\Bethesda Softworks\Skyrim Special Edition";
            string execTitle = string.IsNullOrEmpty(execTitleOverride)
                ? GetStartExecutableTitle(mode)
                : execTitleOverride;

            if (!File.Exists(moExe))
                throw new FlappyException(Loc.T("mo_missing"));
            if (!Directory.Exists(stockDir.TrimEnd('\\')))
                throw new FlappyException(stockSub + " folder not found.\n\nIf you chose AE-only, use AE mode.");
            if (File.Exists(iniProfile))
                File.Copy(iniProfile, iniMain, true);
            else if (!File.Exists(iniMain))
                throw new FlappyException("Missing " + Path.GetFileName(iniProfile));

            EnsureInstalledPath(regPath, stockDir);
            PatchModOrganizerIni(iniMain, profileName, gameName, stockDir.TrimEnd('\\'));

            // UI only: -p profile
            // Play: -p profile "Start Re-Dovah" — MO runs custom executable and does not require clicking Start.
            string args = openUiOnly
                ? "-p \"" + profileName + "\""
                : "-p \"" + profileName + "\" \"" + execTitle + "\"";

            LauncherLog.Info((openUiOnly ? "Open MO2 UI " : "Start game via MO2 ") + args);

            Process.Start(new ProcessStartInfo
            {
                FileName = moExe,
                Arguments = args,
                WorkingDirectory = GameRootPath,
                UseShellExecute = true
            });
        }

        public static void EnsureInstalledPath(string relativeKey, string expectedPathWithSlash)
        {
            if (!expectedPathWithSlash.EndsWith("\\"))
                expectedPathWithSlash += "\\";

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(relativeKey, writable: true)
                    ?? Registry.LocalMachine.CreateSubKey(relativeKey))
                {
                    if (key == null)
                        throw new FlappyException("Cannot open registry key: HKLM\\" + relativeKey);
                    object cur = key.GetValue("Installed Path");
                    string current = cur as string ?? "";
                    if (string.Equals(current, expectedPathWithSlash, StringComparison.OrdinalIgnoreCase))
                        return;
                    key.SetValue("Installed Path", expectedPathWithSlash, RegistryValueKind.String);
                }
                return;
            }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }

            string data = expectedPathWithSlash.EndsWith("\\")
                ? expectedPathWithSlash + "\\"
                : expectedPathWithSlash;
            string args = "add \"HKLM\\" + relativeKey + "\" /v \"Installed Path\" /t REG_SZ /d \"" + data + "\" /f";
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        throw new FlappyException("UAC cancelled — admin rights needed once to set Skyrim path.");
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                        throw new FlappyException("Failed to set registry path (code " + p.ExitCode + "). Approve UAC.");
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new FlappyException("Administrator rights are required once to set Skyrim path (UAC cancelled).");
            }
        }

        public static void PatchModOrganizerIni(string iniPath, string profileName, string gameName, string gamePathNoTrailingSlash)
        {
            string text = File.ReadAllText(iniPath, Encoding.UTF8);
            string gp = gamePathNoTrailingSlash.Replace("\\", "\\\\");
            text = Regex.Replace(text, @"selected_profile=@ByteArray\(.*?\)",
                "selected_profile=@ByteArray(" + profileName + ")");
            text = Regex.Replace(text, @"(?m)^gameName=.*$", "gameName=" + gameName);
            text = Regex.Replace(text, @"gamePath=@ByteArray\(.*?\)", "gamePath=@ByteArray(" + gp + ")");
            File.WriteAllText(iniPath, text, new UTF8Encoding(false));
        }

        // ---------- helpers ----------

        private sealed class DlSlot
        {
            public string Name;
            public long Received;
            public long Total;
        }

        public static string DisplayName(PackageUnit unit)
        {
            if (unit == null) return "?";
            return string.IsNullOrEmpty(unit.path) ? unit.id : unit.path;
        }

        private static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "?";
            string s = path.Replace('\\', '/');
            int i = s.LastIndexOf('/');
            if (i >= 0 && i < s.Length - 1) s = s.Substring(i + 1);
            if (s.Length > 28) s = s.Substring(0, 26) + "…";
            return s;
        }

        private static string FormatLocalStatus(int finished, int total, IEnumerable<DlSlot> slots)
        {
            var sb = new StringBuilder();
            sb.Append("Local ").Append(finished).Append('/').Append(total);
            sb.Append("\nverifying packages…");
            return sb.ToString();
        }

        private static string FormatDownloadStatus(int finished, int total, double bytesPerSec, IEnumerable<DlSlot> slots, long remainingBytes = 0)
        {
            var sb = new StringBuilder();
            sb.Append(finished).Append('/').Append(total);
            sb.Append("  ·  ").Append(FormatSpeed(bytesPerSec));
            if (bytesPerSec > 10240 && remainingBytes > 0)
            {
                double sec = remainingBytes / bytesPerSec;
                string eta = FormatEta(sec);
                if (!string.IsNullOrEmpty(eta))
                    sb.Append("  ·  ETA: ").Append(eta);
            }
            var active = new List<DlSlot>();
            foreach (var s in slots)
            {
                if (s == null) continue;
                if (s.Total > 0 && s.Received >= s.Total) continue;
                if (s.Received <= 0 && s.Total <= 0) continue;
                active.Add(s);
            }
            active.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            int n = Math.Min(Math.Max(1, Constants.DOWNLOAD_PARALLELISM), active.Count);
            if (n == 0)
            {
                sb.Append("\nstarting…");
                return sb.ToString();
            }
            for (int i = 0; i < n; i++)
            {
                var s = active[i];
                sb.Append('\n');
                if (s.Total > 0)
                {
                    int pct = (int)Math.Min(100, s.Received * 100.0 / s.Total);
                    sb.Append(s.Name).Append(' ').Append(pct).Append('%');
                }
                else
                    sb.Append(s.Name).Append(' ').Append(FormatBytes(s.Received));
            }
            return sb.ToString();
        }

        private static string FormatEta(double seconds)
        {
            if (seconds <= 0 || double.IsInfinity(seconds) || double.IsNaN(seconds)) return "";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return string.Format("{0}h {1:D2}m", (int)ts.TotalHours, ts.Minutes);
            if (ts.TotalMinutes >= 1)
                return string.Format("{0}m {1:D2}s", (int)ts.TotalMinutes, ts.Seconds);
            return string.Format("{0}s", Math.Max(1, (int)ts.TotalSeconds));
        }

        private static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec < 1) return "— MB/s";
            if (bytesPerSec >= 1024 * 1024)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} MB/s", bytesPerSec / (1024 * 1024));
            if (bytesPerSec >= 1024)
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} KB/s", bytesPerSec / 1024);
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} B/s", bytesPerSec);
        }

        private static int ClampPct(double value)
        {
            if (value < 0) return 0;
            if (value > 100) return 100;
            return (int)Math.Round(value);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            const double KB = 1024.0, MB = KB * 1024, GB = MB * 1024;
            if (bytes >= GB) return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.00} GB", bytes / GB);
            if (bytes >= MB) return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / MB);
            if (bytes >= KB) return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0} KB", bytes / KB);
            return bytes + " B";
        }
    }
}
