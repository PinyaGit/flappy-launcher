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
            // If the game is not installed yet and we have a local package bundle (torrent),
            // prefer the local bundle index so file sizes and hashes match the downloaded archives.
            if (!IsInstalled() && Constants.HasLocalPackageBundle)
            {
                string localIndex = Constants.LOCAL_INDEX_PATH;
                if (!string.IsNullOrEmpty(localIndex) && File.Exists(localIndex))
                {
                    try
                    {
                        string json = File.ReadAllText(localIndex, Encoding.UTF8);
                        var idx = DeserializeIndex(json);
                        LauncherLog.Info("Index from LOCAL bundle (fresh install) v" + idx.version + " units=" + idx.units.Count);
                        return idx;
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn("Local bundle index read failed: " + ex.Message);
                    }
                }
            }

            // Prefer CDN for updates/repairs/online installs, fall back to local bundle offline.
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

            string fallbackLocalIndex = Constants.LOCAL_INDEX_PATH;
            if (!string.IsNullOrEmpty(fallbackLocalIndex) && File.Exists(fallbackLocalIndex))
            {
                try
                {
                    string json = File.ReadAllText(fallbackLocalIndex, Encoding.UTF8);
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

        public static string PinnedVersionFilePath
        {
            get { return Path.Combine(GameRootPath, ".flappy_pinned_version"); }
        }

        public static bool IsVersionPinned()
        {
            try
            {
                if (!File.Exists(PinnedVersionFilePath)) return false;
                string v = File.ReadAllText(PinnedVersionFilePath, Encoding.UTF8).Trim();
                return !string.IsNullOrEmpty(v);
            }
            catch { return false; }
        }

        public static string GetPinnedVersion()
        {
            try
            {
                if (!File.Exists(PinnedVersionFilePath)) return null;
                string v = File.ReadAllText(PinnedVersionFilePath, Encoding.UTF8).Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }
            catch { return null; }
        }

        public static void SetPinnedVersion(string versionOrNull)
        {
            try
            {
                if (string.IsNullOrEmpty(versionOrNull))
                {
                    if (File.Exists(PinnedVersionFilePath))
                        File.Delete(PinnedVersionFilePath);
                }
                else
                {
                    Directory.CreateDirectory(GameRootPath);
                    File.WriteAllText(PinnedVersionFilePath, versionOrNull.Trim(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("SetPinnedVersion failed: " + ex.Message);
            }
        }

        public static VersionsManifest FetchVersionsManifest()
        {
            try
            {
                string url = HttpDownloader.CombineUrl(GameCatalog.PackagesBaseUrl, "versions.json");
                string json = HttpDownloader.ReadAllText(url);
                return JsonAdapter.FromJson<VersionsManifest>(json);
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("FetchVersionsManifest failed: " + ex.Message);
                return null;
            }
        }

        public static Index FetchIndexForVersion(VersionEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.index))
                throw new FlappyException("Invalid version entry.");

            string url = HttpDownloader.CombineUrl(GameCatalog.PackagesBaseUrl, entry.index);
            string json = HttpDownloader.ReadAllText(url);
            var idx = DeserializeIndex(json);
            return idx;
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

            if (update && CanApplyDelta(index, channel))
            {
                try
                {
                    LauncherLog.Info("Applying single-archive delta patch from v" + index.delta.fromVersion + " to v" + index.version);
                    ApplyDeltaUpdate(index, progress, cancel, channel);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn("Delta update failed, falling back to full unit repair: " + ex.Message);
                }
            }

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

        public static void RollbackToVersion(Index index, ProgressHandler progress, CancellationToken cancel, InstallChannel channel)
        {
            InstallOrRepair(index, progress, cancel,
                onlyMismatched: true,
                wipeMismatched: true,
                channel: channel,
                jobName: Loc.IsRu ? ("Откат на v" + index.version) : ("Rollback to v" + index.version));
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

        private static string ComputeFolderFingerprint(string folderAbs, string folderRel, bool includeMetaIni, bool ignoreTimestampBackups = true)
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
                if (IsVolatileRelPath(folderRel, sub, includeMetaIni, ignoreTimestampBackups))
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
            if (!string.IsNullOrEmpty(legacy)
                && string.Equals(legacy, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Some mod archives contain packaged backup files (.backup.YYYY_MM_DD_...) that the server builder did not ignore
            string withoutBackupFilter = ComputeUnitFingerprint(unit, false, ignoreTimestampBackups: false);
            if (!string.IsNullOrEmpty(withoutBackupFilter)
                && string.Equals(withoutBackupFilter, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Some mod archives contain BOTH meta.ini and backup files that server builder hashed together
            string withMetaAndBackup = ComputeUnitFingerprint(unit, true, ignoreTimestampBackups: false);
            if (!string.IsNullOrEmpty(withMetaAndBackup)
                && string.Equals(withMetaAndBackup, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            // Root files unit: MO2 modifies ModOrganizer.ini and creates fomod.db on first launch.
            // If this root unit was already successfully applied and core binaries exist, do not trigger false repair.
            if (IsRootUnit(unit))
            {
                string applied = GetAppliedSha(unit.id);
                string want = GetUnitStamp(unit);
                if (!string.IsNullOrEmpty(applied) && string.Equals(applied, want, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(Path.Combine(GameRootPath, "ModOrganizer.exe")) &&
                        File.Exists(Path.Combine(GameRootPath, "usvfs_x64.dll")))
                        return true;
                }
            }

            return false;
        }

        private static string ComputeUnitFingerprint(PackageUnit unit, bool includeMetaIni = false, bool ignoreTimestampBackups = true)
        {
            if (IsRootUnit(unit))
                return ComputeRootFilesFingerprint(includeMetaIni, ignoreTimestampBackups);

            string dest = ResolveExtractDir(unit);
            if (!Directory.Exists(dest)) return null;
            string folderRel = (unit.path ?? "").Replace('/', '\\').TrimEnd('\\');
            return ComputeFolderFingerprint(dest, folderRel, includeMetaIni, ignoreTimestampBackups);
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

        private static bool IsVolatileFile(string fileName, bool includeMetaIni = false, bool ignoreTimestampBackups = true)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            if (ignoreTimestampBackups && Regex.IsMatch(fileName, @"\.\d{4}_\d{2}_\d{2}_\d{2}_\d{2}_\d{2}$")) return true;
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

        private static bool IsVolatileRelPath(string folderRel, string sub, bool includeMetaIni = false, bool ignoreTimestampBackups = true)
        {
            if (IsVolatileFile(Path.GetFileName(sub), includeMetaIni, ignoreTimestampBackups)) return true;
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

        private static string ComputeRootFilesFingerprint(bool includeMetaIni = false, bool ignoreTimestampBackups = true)
        {
            if (!Directory.Exists(GameRootPath)) return null;
            var lines = new List<string>();
            foreach (string f in Directory.GetFiles(GameRootPath))
            {
                string name = Path.GetFileName(f);
                if (IsIgnoredRootFile(name) || IsVolatileFile(name, includeMetaIni, ignoreTimestampBackups)) continue;
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
            if (name.Equals("fomod.db", StringComparison.OrdinalIgnoreCase)) return true;
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
            if (CurrentUpdateMode == UserModsAction.CleanCanon)
            {
                try { QuarantineUserModsForCleanCanon(index); } catch (Exception ex) { LauncherLog.Warn("QuarantineUserModsForCleanCanon on install/repair: " + ex.Message); }
            }
            else
            {
                try { RestoreUserMods(); } catch (Exception ex) { LauncherLog.Warn("RestoreUserMods on install/repair: " + ex.Message); }
            }

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
                    string oldMeta = Path.Combine(destDir, "meta.ini");
                    string newMeta = Path.Combine(tmp, "meta.ini");
                    if (File.Exists(oldMeta) && !File.Exists(newMeta))
                    {
                        try { File.Copy(oldMeta, newMeta, true); } catch { }
                    }
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
        /// Client saves stay. Official modlist/plugins/loadorder are smart merged.
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

                    string fileName = Path.GetFileName(rel);
                    if (string.Equals(fileName, "modlist.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SmartMergeModlistFile(f, target);
                    }
                    else if (string.Equals(fileName, "plugins.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SmartMergePluginsFile(f, target);
                    }
                    else if (string.Equals(fileName, "loadorder.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SmartMergeLoadorderFile(f, target);
                    }
                    else
                    {
                        File.Copy(f, target, true);
                    }
                    copied++;
                }
                LauncherLog.Info("profile overlay files=" + copied + " dest=" + destDir);
            }
            finally
            {
                try { if (Directory.Exists(tmp)) DeleteDirectoryRobust(tmp); } catch { }
            }
        }

        public static void SmartMergeModlistFile(string officialPath, string clientPath)
        {
            if (!File.Exists(clientPath))
            {
                if (File.Exists(officialPath)) File.Copy(officialPath, clientPath, true);
                return;
            }
            if (!File.Exists(officialPath)) return;

            try
            {
                var clientLines = File.ReadAllLines(clientPath);
                var clientStates = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
                foreach (var cl in clientLines)
                {
                    string t = (cl ?? "").Trim();
                    if (t.Length == 0 || t[0] == '#') continue;
                    char prefix = t[0];
                    if (prefix == '+' || prefix == '-')
                    {
                        string name = t.Substring(1).Trim();
                        if (!string.IsNullOrEmpty(name))
                            clientStates[name] = prefix;
                    }
                }

                var officialLines = File.ReadAllLines(officialPath);
                var officialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ol in officialLines)
                {
                    string t = (ol ?? "").Trim();
                    if (t.Length == 0 || t[0] == '#') continue;
                    string name = (t[0] == '+' || t[0] == '-') ? t.Substring(1).Trim() : t;
                    if (!string.IsNullOrEmpty(name))
                        officialNames.Add(name);
                }

                var lastOfficial = LoadOfficialNames();
                string modsDir = Path.Combine(GameRootPath, "mods");

                // Anchor client user entries (mods and custom separators) to the preceding official entry
                var anchoredAfter = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                anchoredAfter[""] = new List<string>();

                string lastOfficialAnchor = "";
                var clientUserEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (CurrentUpdateMode != UserModsAction.CleanCanon)
                {
                    foreach (var raw in clientLines)
                    {
                        string t = (raw ?? "").Trim();
                        if (t.Length == 0 || t[0] == '#') continue;

                        char prefix = t[0];
                        string name = (prefix == '+' || prefix == '-') ? t.Substring(1).Trim() : t;
                        if (string.IsNullOrEmpty(name)) continue;

                        bool isSeparator = name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase);

                        if (officialNames.Contains(name))
                        {
                            lastOfficialAnchor = name;
                        }
                        else
                        {
                            // Non-official entry: check if it was an obsolete official mod removed by author
                            bool wasOfficial = lastOfficial != null && lastOfficial.Contains(name);
                            if (wasOfficial)
                                continue;

                            // If not a separator, ensure folder physically exists on disk
                            if (!isSeparator && !Directory.Exists(Path.Combine(modsDir, name)))
                                continue;

                            List<string> list;
                            if (!anchoredAfter.TryGetValue(lastOfficialAnchor, out list))
                            {
                                list = new List<string>();
                                anchoredAfter[lastOfficialAnchor] = list;
                            }
                            list.Add(raw);
                            clientUserEntries.Add(name);
                        }
                    }
                }

                var merged = new List<string>(officialLines.Length + clientUserEntries.Count + 16);

                // 1. User entries anchored before the first official entry
                List<string> topEntries;
                if (anchoredAfter.TryGetValue("", out topEntries))
                {
                    merged.AddRange(topEntries);
                }

                // 2. Official entries in official order, followed by their anchored user entries
                foreach (var ol in officialLines)
                {
                    string t = (ol ?? "").Trim();
                    if (t.Length == 0 || t[0] == '#')
                    {
                        merged.Add(ol);
                        continue;
                    }

                    char prefix = t[0];
                    string name = (prefix == '+' || prefix == '-') ? t.Substring(1).Trim() : t;
                    bool isSeparator = name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase);

                    if (isSeparator || prefix == '*')
                    {
                        merged.Add(ol);
                    }
                    else if (prefix == '+' || prefix == '-')
                    {
                        char userPrefix;
                        if (clientStates.TryGetValue(name, out userPrefix))
                            merged.Add(userPrefix + name);
                        else
                            merged.Add(ol);
                    }
                    else
                    {
                        merged.Add(ol);
                    }

                    // Emit user entries anchored to this official name
                    List<string> anchored;
                    if (anchoredAfter.TryGetValue(name, out anchored))
                    {
                        merged.AddRange(anchored);
                        anchoredAfter.Remove(name);
                    }
                }

                // 3. Any remaining anchored entries that weren't matched
                foreach (var kvp in anchoredAfter)
                {
                    if (kvp.Key == "") continue;
                    merged.AddRange(kvp.Value);
                }

                // 4. Any unlisted user mods physically on disk in mods/
                if (CurrentUpdateMode != UserModsAction.CleanCanon && Directory.Exists(modsDir))
                {
                    foreach (var dir in Directory.GetDirectories(modsDir))
                    {
                        string name = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (officialNames.Contains(name)) continue;
                        if (lastOfficial != null && lastOfficial.Contains(name)) continue;
                        if (clientUserEntries.Contains(name)) continue;
                        if (name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;

                        int insertIdx = FindSafeUserModInsertionIndex(merged);
                        merged.Insert(insertIdx, "+" + name);
                        clientUserEntries.Add(name);
                        LauncherLog.Info("SmartMergeModlistFile added unlisted user mod: " + name);
                    }
                }

                File.WriteAllLines(clientPath, merged.ToArray(), new UTF8Encoding(false));
                LauncherLog.Info("SmartMergeModlistFile merged " + Path.GetFileName(clientPath) + " (preserved " + clientUserEntries.Count + " user mods/separators)");
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("SmartMergeModlistFile failed, falling back to copy: " + ex.Message);
                try { File.Copy(officialPath, clientPath, true); } catch { }
            }
        }

        private static int FindSafeUserModInsertionIndex(List<string> lines)
        {
            if (lines == null || lines.Count == 0) return 0;

            // Find the end of generated outputs / top separators
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.IndexOf("Generated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int next = i + 1;
                    while (next < lines.Count &&
                           (lines[next].IndexOf("Output", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            lines[next].IndexOf("Generated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            lines[next].IndexOf("не удалять", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        next++;
                    }
                    return Math.Min(lines.Count, next);
                }
            }

            int idx = 0;
            while (idx < lines.Count && (lines[idx].StartsWith("#") || string.IsNullOrWhiteSpace(lines[idx])))
                idx++;
            return idx;
        }

        private static HashSet<string> ScanExistingPluginNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string modsDir = Path.Combine(GameRootPath, "mods");
                if (Directory.Exists(modsDir))
                {
                    foreach (var modDir in Directory.GetDirectories(modsDir))
                    {
                        foreach (var file in Directory.GetFiles(modDir))
                        {
                            string ext = Path.GetExtension(file);
                            if (ext.Equals(".esp", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".esl", StringComparison.OrdinalIgnoreCase))
                            {
                                set.Add(Path.GetFileName(file));
                            }
                        }
                    }
                }
                foreach (var sub in new[] { "StockGame", "StockGameVR" })
                {
                    string dataDir = Path.Combine(GameRootPath, sub, "Data");
                    if (Directory.Exists(dataDir))
                    {
                        foreach (var file in Directory.GetFiles(dataDir))
                        {
                            string ext = Path.GetExtension(file);
                            if (ext.Equals(".esp", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".esl", StringComparison.OrdinalIgnoreCase))
                            {
                                set.Add(Path.GetFileName(file));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("ScanExistingPluginNames: " + ex.Message);
            }
            return set;
        }

        public static void SmartMergePluginsFile(string officialPath, string clientPath)
        {
            if (!File.Exists(clientPath))
            {
                if (File.Exists(officialPath)) File.Copy(officialPath, clientPath, true);
                return;
            }
            if (!File.Exists(officialPath)) return;

            try
            {
                var clientLines = File.ReadAllLines(clientPath);
                var clientStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var cl in clientLines)
                {
                    string t = (cl ?? "").Trim();
                    if (t.Length == 0 || t[0] == '#') continue;
                    bool enabled = t.StartsWith("*");
                    string name = (enabled ? t.Substring(1) : t).Trim();
                    if (!string.IsNullOrEmpty(name))
                        clientStates[name] = enabled;
                }

                var officialLines = File.ReadAllLines(officialPath);
                var officialSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var merged = new List<string>(officialLines.Length + clientStates.Count);

                foreach (var ol in officialLines)
                {
                    string t = (ol ?? "").Trim();
                    if (t.Length == 0 || t[0] == '#')
                    {
                        merged.Add(ol);
                        continue;
                    }

                    bool authorEnabled = t.StartsWith("*");
                    string name = (authorEnabled ? t.Substring(1) : t).Trim();
                    officialSeen.Add(name);

                    bool userEnabled;
                    if (clientStates.TryGetValue(name, out userEnabled))
                    {
                        merged.Add(userEnabled ? ("*" + name) : name);
                    }
                    else
                    {
                        merged.Add(ol);
                    }
                }

                var existingPlugins = ScanExistingPluginNames();

                foreach (var pair in clientStates)
                {
                    if (!officialSeen.Contains(pair.Key))
                    {
                        if (existingPlugins.Count == 0 || existingPlugins.Contains(pair.Key))
                        {
                            merged.Add(pair.Value ? ("*" + pair.Key) : pair.Key);
                        }
                    }
                }

                File.WriteAllLines(clientPath, merged.ToArray(), new UTF8Encoding(false));
                LauncherLog.Info("SmartMergePluginsFile merged " + Path.GetFileName(clientPath));
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("SmartMergePluginsFile failed, falling back to copy: " + ex.Message);
                try { File.Copy(officialPath, clientPath, true); } catch { }
            }
        }

        public static void SmartMergeLoadorderFile(string officialPath, string clientPath)
        {
            if (!File.Exists(clientPath))
            {
                if (File.Exists(officialPath)) File.Copy(officialPath, clientPath, true);
                return;
            }
            if (!File.Exists(officialPath)) return;

            try
            {
                var clientLines = File.ReadAllLines(clientPath);
                var officialLines = File.ReadAllLines(officialPath);
                var officialSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var merged = new List<string>(officialLines.Length + clientLines.Length);

                foreach (var ol in officialLines)
                {
                    string t = (ol ?? "").Trim();
                    if (t.Length == 0 || t[0] == '#')
                    {
                        merged.Add(ol);
                        continue;
                    }
                    officialSeen.Add(t);
                    merged.Add(ol);
                }

                var existingPlugins = ScanExistingPluginNames();

                foreach (var cl in clientLines)
                {
                    string t = (cl ?? "").Trim();
                    if (t.Length > 0 && t[0] != '#' && !officialSeen.Contains(t))
                    {
                        if (existingPlugins.Count == 0 || existingPlugins.Contains(t))
                        {
                            merged.Add(cl);
                            officialSeen.Add(t);
                        }
                    }
                }

                File.WriteAllLines(clientPath, merged.ToArray(), new UTF8Encoding(false));
                LauncherLog.Info("SmartMergeLoadorderFile merged " + Path.GetFileName(clientPath));
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("SmartMergeLoadorderFile failed: " + ex.Message);
                try { File.Copy(officialPath, clientPath, true); } catch { }
            }
        }

        private static bool IsVrRelPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            string p = rel.Replace('/', '\\').Trim('\\');
            if (p.StartsWith("StockGameVR", StringComparison.OrdinalIgnoreCase)) return true;
            if (p.IndexOf("(VR)", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (p.IndexOf("Re-Dovah VR", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (p.IndexOf("VR - Re-Dovah", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (p.StartsWith("profiles\\VR", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool CanApplyDelta(Index index, InstallChannel channel)
        {
            if (index == null || index.delta == null) return false;
            if (string.IsNullOrWhiteSpace(index.delta.package)) return false;

            var localVer = GetLocalVersion();
            if (localVer.Major == 0 && localVer.Minor == 0 && localVer.Build == 0 && localVer.Revision == 0)
                return false;

            var deltaFrom = ParseVersion(index.delta.fromVersion);
            bool verMatch = localVer.Equals(deltaFrom) ||
                string.Equals(localVer.ToString(), (index.delta.fromVersion ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            if (!verMatch) return false;

            return true;
        }

        public static void ApplyDeltaUpdate(
            Index index,
            ProgressHandler progress,
            CancellationToken cancel,
            InstallChannel channel)
        {
            if (index == null || index.delta == null)
                throw new FlappyException("No delta package information in index.");

            var delta = index.delta;
            string patchName = Path.GetFileName(delta.package);
            string downloadMsg = Loc.IsRu ? ("Обновление: скачивание " + patchName) : ("Update: downloading " + patchName);
            progress(5, -1, downloadMsg);

            EnsureSevenZip(progress, cancel);

            Directory.CreateDirectory(GameRootPath);
            Directory.CreateDirectory(DownloadCacheDir);

            // 1. Acquire delta package
            string cdnUrl = HttpDownloader.CombineUrl(GameCatalog.PackagesBaseUrl, delta.package);
            string localPath = GameCatalog.GetLocalPackagePath(delta.package);
            string cachePath = Path.Combine(DownloadCacheDir, patchName);

            bool fromLocal;
            string patchFile = HttpDownloader.AcquirePackage(
                localPath,
                cdnUrl,
                cachePath,
                delta.packageSize,
                delta.packageSha256,
                (recv, tot) =>
                {
                    int pct = (tot > 0) ? (int)Math.Min(100, Math.Max(0, (recv * 100) / tot)) : -1;
                    progress(5 + (pct >= 0 ? (int)(pct * 0.45) : 0), pct, downloadMsg);
                },
                cancel,
                out fromLocal);

            if (CurrentUpdateMode == UserModsAction.CleanCanon)
            {
                try { QuarantineUserModsForCleanCanon(index); } catch (Exception ex) { LauncherLog.Warn("QuarantineUserModsForCleanCanon before delta: " + ex.Message); }
            }
            else
            {
                try { RestoreUserMods(); } catch (Exception ex) { LauncherLog.Warn("RestoreUserMods before delta: " + ex.Message); }
            }

            // 2. Extract delta package to staging directory
            string extractMsg = Loc.IsRu ? ("Обновление: распаковка " + patchName) : ("Update: extracting " + patchName);
            progress(55, -1, extractMsg);
            string stage = Path.Combine(Path.GetTempPath(), "flappy_delta_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);

            try
            {
                Extract7z(patchFile, stage);

                // 3. Process manifest_delta.json if present
                string manifestPath = Path.Combine(stage, "manifest_delta.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        string json = File.ReadAllText(manifestPath, Encoding.UTF8);
                        var manifest = JsonAdapter.FromJson<DeltaManifest>(json);
                        if (manifest != null && manifest.deletedPaths != null && manifest.deletedPaths.Count > 0)
                        {
                            string cleanMsg = Loc.IsRu ? "Удаление устаревших файлов..." : "Removing obsolete files...";
                            progress(65, -1, cleanMsg);
                            string fullGameRoot = Path.GetFullPath(GameRootPath).TrimEnd('\\');
                            foreach (var delRel in manifest.deletedPaths)
                            {
                                cancel.ThrowIfCancellationRequested();
                                if (string.IsNullOrWhiteSpace(delRel)) continue;
                                string safeRel = delRel.Replace('/', '\\').TrimStart('\\');
                                string target = Path.GetFullPath(Path.Combine(fullGameRoot, safeRel));
                                if (!target.StartsWith(fullGameRoot, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (Directory.Exists(target))
                                {
                                    try { DeleteDirectoryRobust(target); }
                                    catch (Exception ex) { LauncherLog.Warn("Delta remove dir " + safeRel + ": " + ex.Message); }
                                }
                                else if (File.Exists(target))
                                {
                                    try { File.Delete(target); }
                                    catch (Exception ex) { LauncherLog.Warn("Delta remove file " + safeRel + ": " + ex.Message); }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn("Failed processing manifest_delta.json: " + ex.Message);
                    }
                }

                // 4. Copy updated/new files from stage into GameRootPath
                string applyMsg = Loc.IsRu ? "Применение обновлений..." : "Applying update files...";
                progress(75, -1, applyMsg);
                string[] files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
                int totalFiles = files.Length;
                int currentFile = 0;

                foreach (var srcFile in files)
                {
                    cancel.ThrowIfCancellationRequested();
                    currentFile++;
                    if (currentFile % 25 == 0)
                    {
                        int p = 75 + (int)((currentFile / (double)Math.Max(1, totalFiles)) * 20);
                        progress(p, -1, applyMsg);
                    }

                    string rel = srcFile.Substring(stage.Length).TrimStart('\\', '/');
                    if (string.Equals(rel, "manifest_delta.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // If AE-only, skip VR files
                    if (channel == InstallChannel.AeOnly && IsVrRelPath(rel))
                        continue;

                    // Never overwrite player saves
                    if (IsProfileSavePath(rel))
                        continue;

                    string destFile = Path.Combine(GameRootPath, rel);
                    string fileName = Path.GetFileName(rel);

                    if (string.Equals(fileName, "modlist.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SmartMergeModlistFile(srcFile, destFile);
                    }
                    else if (string.Equals(fileName, "plugins.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SmartMergePluginsFile(srcFile, destFile);
                    }
                    else if (string.Equals(fileName, "loadorder.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        SmartMergeLoadorderFile(srcFile, destFile);
                    }
                    else
                    {
                        string destDir = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrEmpty(destDir))
                            Directory.CreateDirectory(destDir);
                        File.Copy(srcFile, destFile, true);
                    }
                }
            }
            finally
            {
                try { if (Directory.Exists(stage)) DeleteDirectoryRobust(stage); } catch { }
                if (!fromLocal)
                {
                    try { if (File.Exists(patchFile)) File.Delete(patchFile); } catch { }
                }
            }

            // 5. Post-apply operations
            string finishMsg = Loc.IsRu ? "Завершение обновления..." : "Finishing update...";
            progress(96, -1, finishMsg);
            try { RestoreModOrder(index); } catch (Exception ex) { LauncherLog.Warn("RestoreModOrder after delta: " + ex.Message); }
            try { ClearShaderCache(); } catch { }

            SaveChannel(channel);
            FinishInstallFlag(index.version, "Delta patch " + delta.fromVersion + " -> " + index.version);
            SaveLocalVersion(index.version ?? "1.0.0");

            progress(100, -1, Loc.F("job_complete", Loc.T("job_update"), 1));
            LauncherLog.Info("Delta update applied successfully: " + delta.fromVersion + " -> " + index.version);
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
        /// Restores user mods from UserMods back to mods\, cleans removed official mods,
        /// and preserves user mods and relative positions in profiles modlist.
        /// </summary>
        public static void RestoreModOrder(Index index)
        {
            try
            {
                if (Directory.Exists(OfficialOrderDir))
                    DeleteDirectoryRobust(OfficialOrderDir);
            }
            catch { }

            // 1. Restore any previously quarantined user mods (only in Preserve mode)
            if (CurrentUpdateMode == UserModsAction.Preserve)
            {
                RestoreUserMods();
            }

            var lastOfficial = LoadOfficialNames();
            HashSet<string> newOfficial = null;
            if (index != null && index.units != null)
            {
                newOfficial = GetOfficialModNames(index);
            }

            if (newOfficial != null && newOfficial.Count > 0)
            {
                CleanRemovedOfficialMods(lastOfficial, newOfficial);
                CleanProfilesModlists(newOfficial, lastOfficial);
                if (CurrentUpdateMode == UserModsAction.Preserve)
                {
                    EnsureUserModsInProfiles(newOfficial);
                }
                SaveOfficialNames(newOfficial);
            }
        }

        private static void CleanProfilesModlists(HashSet<string> officialMods, HashSet<string> lastOfficial)
        {
            if (officialMods == null || officialMods.Count == 0) return;
            string profilesDir = Path.Combine(GameRootPath, "profiles");
            if (!Directory.Exists(profilesDir)) return;

            CleanModlistInDirectory(profilesDir, officialMods, lastOfficial);
        }

        private static void CleanModlistInDirectory(string baseDir, HashSet<string> officialMods, HashSet<string> lastOfficial)
        {
            string modsDir = Path.Combine(GameRootPath, "mods");
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
                        bool wasOfficial = lastOfficial != null && lastOfficial.Contains(entry);
                        bool isRemovedOfficial = wasOfficial && !officialMods.Contains(entry);
                        bool existsInMods = Directory.Exists(Path.Combine(modsDir, entry));

                        if (isSeparator)
                        {
                            cleanLines.Add(raw);
                        }
                        else if (officialMods.Contains(entry))
                        {
                            cleanLines.Add(raw);
                        }
                        else if (!isRemovedOfficial && existsInMods)
                        {
                            // Active user mod on disk: preserve!
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

        public static UserModsAction CurrentUpdateMode { get; set; } = UserModsAction.Preserve;

        public static List<string> DetectUserMods(Index index)
        {
            var list = new List<string>();
            try
            {
                string modsDir = Path.Combine(GameRootPath, "mods");
                if (!Directory.Exists(modsDir)) return list;

                var lastOfficial = LoadOfficialNames();
                var newOfficial = (index != null && index.units != null) ? GetOfficialModNames(index) : null;
                var official = (newOfficial != null && newOfficial.Count > 0) ? newOfficial : lastOfficial;

                foreach (var dir in Directory.GetDirectories(modsDir))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;
                    if (official != null && official.Contains(name)) continue;
                    if (lastOfficial != null && lastOfficial.Contains(name)) continue;

                    list.Add(name);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("DetectUserMods: " + ex.Message);
            }
            return list;
        }

        public static void QuarantineUserModsForCleanCanon(Index index)
        {
            try
            {
                var userMods = DetectUserMods(index);
                if (userMods.Count == 0) return;

                string modsDir = Path.Combine(GameRootPath, "mods");
                string profilesDir = Path.Combine(GameRootPath, "profiles");

                // 1. Backup modlist.txt in all profiles
                if (Directory.Exists(profilesDir))
                {
                    foreach (var prof in Directory.GetDirectories(profilesDir))
                    {
                        string ml = Path.Combine(prof, "modlist.txt");
                        if (File.Exists(ml))
                        {
                            try { File.Copy(ml, ml + ".user_backup", true); } catch { }
                        }
                    }
                }

                // 2. Move custom mods to UserModsDir
                Directory.CreateDirectory(UserModsDir);
                int moved = 0;
                foreach (var modName in userMods)
                {
                    string src = Path.Combine(modsDir, modName);
                    if (Directory.Exists(src))
                    {
                        string dest = UniqueDest(UserModsDir, modName);
                        try
                        {
                            MoveDirectory(src, dest);
                            moved++;
                            LauncherLog.Info("CleanCanon moved user mod: " + modName + " -> " + dest);
                        }
                        catch (Exception ex)
                        {
                            LauncherLog.Warn("CleanCanon move " + modName + ": " + ex.Message);
                        }
                    }
                }

                if (moved > 0)
                {
                    TryWriteReadme(UserModsDir,
                        "Пользовательские моды, перемещенные при обновлении (выбран «Чистый канон автора»).\r\n\r\n" +
                        "Лаунчер сохранил резервную копию порядка модов в profiles\\*\\modlist.txt.user_backup.\r\n" +
                        "Если вам понадобятся эти моды, скопируйте их обратно в папку mods\\ игры.\r\n");
                    LauncherLog.Info("QuarantineUserModsForCleanCanon moved " + moved + " mods to " + UserModsDir);
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("QuarantineUserModsForCleanCanon: " + ex.Message);
            }
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

        public static void RestoreUserMods()
        {
            try
            {
                string userModsDir = UserModsDir;
                if (!Directory.Exists(userModsDir)) return;

                string modsDir = Path.Combine(GameRootPath, "mods");
                if (!Directory.Exists(modsDir)) return;

                int restored = 0;
                foreach (var dir in Directory.GetDirectories(userModsDir))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;

                    string cleanName = name;
                    var match = System.Text.RegularExpressions.Regex.Match(name, @"^(.+)_\d{8}_\d{6}$");
                    if (match.Success)
                    {
                        cleanName = match.Groups[1].Value;
                    }

                    string dest = Path.Combine(modsDir, cleanName);
                    if (!Directory.Exists(dest))
                    {
                        MoveDirectory(dir, dest);
                        restored++;
                        LauncherLog.Info("restored user mod from UserMods: " + name + " -> " + dest);
                    }
                    else if (!Directory.Exists(Path.Combine(modsDir, name)))
                    {
                        MoveDirectory(dir, Path.Combine(modsDir, name));
                        restored++;
                        LauncherLog.Info("restored user mod (with suffix): " + name);
                    }
                    else
                    {
                        LauncherLog.Info("user mod already in mods/: " + name);
                    }
                }

                var remainingDirs = Directory.GetDirectories(userModsDir);
                if (remainingDirs.Length == 0)
                {
                    string readme = Path.Combine(userModsDir, "README.txt");
                    if (File.Exists(readme))
                    {
                        try { File.Delete(readme); } catch { }
                    }
                    try { Directory.Delete(userModsDir); } catch { }
                }

                if (restored > 0)
                {
                    LauncherLog.Info("RestoreUserMods: restored=" + restored);
                    EnsureUserModsInProfiles();
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("RestoreUserMods: " + ex.Message);
            }
        }

        public static void EnsureUserModsInProfiles(HashSet<string> officialMods = null)
        {
            try
            {
                string modsDir = Path.Combine(GameRootPath, "mods");
                string profilesDir = Path.Combine(GameRootPath, "profiles");
                if (!Directory.Exists(modsDir) || !Directory.Exists(profilesDir)) return;

                var lastOfficial = LoadOfficialNames();
                var official = officialMods ?? lastOfficial;

                var userModsOnDisk = new List<string>();
                foreach (var dir in Directory.GetDirectories(modsDir))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;
                    if (official != null && official.Contains(name)) continue;
                    if (lastOfficial != null && lastOfficial.Contains(name)) continue;
                    userModsOnDisk.Add(name);
                }

                if (userModsOnDisk.Count == 0) return;

                foreach (var profileDir in Directory.GetDirectories(profilesDir))
                {
                    string modlistPath = Path.Combine(profileDir, "modlist.txt");
                    if (!File.Exists(modlistPath)) continue;

                    var lines = new List<string>(File.ReadAllLines(modlistPath));
                    var existingInModlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var l in lines)
                    {
                        string t = (l ?? "").Trim();
                        if (t.Length == 0 || t[0] == '#') continue;
                        string entry = (t[0] == '+' || t[0] == '-') ? t.Substring(1).Trim() : t;
                        if (!string.IsNullOrEmpty(entry)) existingInModlist.Add(entry);
                    }

                    int added = 0;
                    int insertIdx = FindSafeUserModInsertionIndex(lines);
                    foreach (var userMod in userModsOnDisk)
                    {
                        if (!existingInModlist.Contains(userMod))
                        {
                            lines.Insert(insertIdx, "+" + userMod);
                            insertIdx++;
                            added++;
                            LauncherLog.Info("Ensured user mod in profile " + Path.GetFileName(profileDir) + ": +" + userMod);
                        }
                    }

                    if (added > 0)
                    {
                        File.WriteAllLines(modlistPath, lines.ToArray(), new UTF8Encoding(false));
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("EnsureUserModsInProfiles: " + ex.Message);
            }
        }

        /// <summary>
        /// Only deletes mods that were in lastOfficial but removed in newOfficial (pack author deletions).
        /// User-installed mods are preserved in mods\.
        /// </summary>
        private static void CleanRemovedOfficialMods(HashSet<string> lastOfficial, HashSet<string> newOfficial)
        {
            string modsDir = Path.Combine(GameRootPath, "mods");
            if (!Directory.Exists(modsDir)) return;
            if (newOfficial == null || newOfficial.Count == 0) return;

            int packDeleted = 0;
            foreach (string dir in Directory.GetDirectories(modsDir))
            {
                string name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name) || newOfficial.Contains(name))
                    continue;

                bool wasOfficial = lastOfficial != null && lastOfficial.Contains(name);
                if (wasOfficial)
                {
                    try
                    {
                        DeleteDirectoryRobust(dir);
                        packDeleted++;
                        LauncherLog.Info("pack-removed deleted: " + name);
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn("pack-removed " + name + ": " + ex.Message);
                    }
                }
                else
                {
                    LauncherLog.Info("user-mod kept in mods/: " + name);
                }
            }

            if (packDeleted > 0)
                LauncherLog.Info("clean removed official mods: deleted=" + packDeleted);
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
                Arguments = "l -slt -sccUTF-8 -- \"" + archive + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
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
            try { RestoreUserMods(); EnsureUserModsInProfiles(); } catch (Exception ex) { LauncherLog.Warn("RestoreUserMods on LaunchMo: " + ex.Message); }
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
