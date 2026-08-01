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
using System.Web.Script.Serialization;
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

        /// <summary>Resolved 7-Zip CLI (LocalAppData cache / Program Files / optional legacy next to exe).</summary>
        public static string SevenZipPath
        {
            get
            {
                return SevenZipBootstrap.ResolvedPath
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7za.exe");
            }
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
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var idx = ser.Deserialize<Index>(json);
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
                throw new FlappyException(
                    "7-Zip is not available.\n\n" +
                    "The launcher downloads it from https://www.7-zip.org/ on first install.\n" +
                    "Check your network, or install 7-Zip system-wide.");

            Directory.CreateDirectory(GameRootPath);
            Directory.CreateDirectory(DownloadCacheDir);
            SaveChannel(InstallChannel.AeAndVr);

            var vrUnits = GetVrUnits(index.units);
            if (vrUnits.Count == 0)
            {
                progress(100, -1, "No VR packages in index.");
                return;
            }

            // Only scan VR units (do not re-fingerprint the whole AE set).
            progress(1, -1, "Checking VR packages…");
            var work = new List<PackageUnit>();
            for (int i = 0; i < vrUnits.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var unit = vrUnits[i];
                if (progress != null)
                    progress(1, -1, string.Format("Checking VR {0}/{1}:\n{2}", i + 1, vrUnits.Count, DisplayName(unit)));

                if (IsUserDataUnit(unit))
                {
                    string dest = ResolveExtractDir(unit);
                    if (!Directory.Exists(dest) || Directory.GetFileSystemEntries(dest).Length == 0)
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
                progress(100, -1, "VR packages already present.\nChannel: AE + VR");
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
                jobName: "Get VR");
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
                progress(ClampPct(i * 90.0 / Math.Max(1, total)), -1, "Removing VR:\n" + name);
                if (!IsRootUnit(unit))
                    WipeUnitDestination(unit);
            }

            foreach (var rel in extraPaths)
            {
                cancel.ThrowIfCancellationRequested();
                i++;
                string dest = Path.Combine(GameRootPath, rel);
                progress(ClampPct(i * 90.0 / Math.Max(1, total)), -1, "Removing VR:\n" + rel);
                if (Directory.Exists(dest))
                {
                    try { Directory.Delete(dest, true); }
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
                        try { Directory.Delete(dir, true); }
                        catch (Exception ex) { LauncherLog.Warn("remove VR mod " + leaf + ": " + ex.Message); }
                    }
                }
            }

            SaveChannel(InstallChannel.AeOnly);
            if (string.Equals(GetSavedMode(), "VR", StringComparison.OrdinalIgnoreCase))
                SaveMode("AE");

            progress(100, -1, "VR removed.\nChannel: AE only");
            LauncherLog.Info("Remove VR done, channel=AE");
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

        public static void InstallAll(Index index, ProgressHandler progress, CancellationToken cancel, InstallChannel channel)
        {
            bool update = IsInstalled();
            InstallOrRepair(index, progress, cancel,
                onlyMismatched: update,
                wipeMismatched: false,
                channel: channel,
                jobName: update ? "Update" : "Install");
        }

        public static void RepairAll(Index index, ProgressHandler progress, CancellationToken cancel, InstallChannel channel)
        {
            InstallOrRepair(index, progress, cancel,
                onlyMismatched: true,
                wipeMismatched: true,
                channel: channel,
                jobName: "Repair");
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

                if (IsUserDataUnit(unit))
                {
                    string dest = ResolveExtractDir(unit);
                    if (!Directory.Exists(dest) || Directory.GetFileSystemEntries(dest).Length == 0)
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
                if (IsVolatileFile(fi.Name))
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
            string actual;

            if (IsRootUnit(unit))
                actual = ComputeRootFilesFingerprint();
            else
            {
                string dest = ResolveExtractDir(unit);
                if (!Directory.Exists(dest)) return false;
                string folderRel = (unit.path ?? "").Replace('/', '\\').TrimEnd('\\');
                actual = ComputeFolderFingerprint(dest, folderRel);
            }

            return !string.IsNullOrEmpty(actual)
                && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
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

        private static bool IsRootUnit(PackageUnit unit)
        {
            if (unit == null) return false;
            if (string.Equals(unit.kind, "ROOT", StringComparison.OrdinalIgnoreCase)) return true;
            return string.IsNullOrEmpty(unit.path) || unit.path == ".";
        }

        private static bool IsVolatileFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return true;
            if (Regex.IsMatch(fileName, @"\.\d{4}_\d{2}_\d{2}_\d{2}_\d{2}_\d{2}$")) return true;
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ComputeRootFilesFingerprint()
        {
            if (!Directory.Exists(GameRootPath)) return null;
            var lines = new List<string>();
            foreach (string f in Directory.GetFiles(GameRootPath))
            {
                string name = Path.GetFileName(f);
                if (IsIgnoredRootFile(name)) continue;
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
                throw new FlappyException(
                    "7-Zip is not available.\n\n" +
                    "The launcher downloads it from https://www.7-zip.org/ on first install.\n" +
                    "Check your network, or install 7-Zip system-wide.");

            Directory.CreateDirectory(GameRootPath);
            Directory.CreateDirectory(DownloadCacheDir);
            SaveChannel(channel);

            var scoped = FilterByChannel(index.units, channel);
            LauncherLog.Info(jobName + " channel=" + channel + " scoped=" + scoped.Count);

            List<PackageUnit> work;
            int skipped = 0;
            if (onlyMismatched)
            {
                progress(0, -1, "Checking fingerprints…\nPlease wait");
                work = FindUnitsNeedingRepair(index, channel, msg => progress(1, -1, "Checking…\n" + msg), cancel);
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
                try { RestoreModOrder(); } catch (Exception ex) { LauncherLog.Warn("modlist: " + ex.Message); }
                progress(100, -1, jobName + " complete.\nAll packages OK (" + skipped + ")");
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
                ? ("Packages: " + work.Count + "\nLocal prefer + CDN fallback · starting…")
                : ("Downloading " + work.Count + " package(s)\n" + Constants.DOWNLOAD_PARALLELISM + " workers · starting…"));

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
                foreach (var s in slots.Values)
                {
                    if (s.Total > 0)
                    {
                        activeFrac += Math.Min(1.0, (double)s.Received / Math.Max(1, s.Total));
                        activeN++;
                    }
                    else if (s.Received > 0) activeN++;
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
                progress(ClampPct(pct), curPct, FormatDownloadStatus(finished, total, ema, slots.Values));
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

            // Extract sequentially
            int i = 0;
            foreach (var unit in work)
            {
                cancel.ThrowIfCancellationRequested();
                i++;
                string name = DisplayName(unit);
                string local;
                if (!downloaded.TryGetValue(unit.id, out local) || !File.Exists(local))
                    throw new FlappyException("Missing downloaded package for:\n" + name);

                double basePct = 85 + (i - 1) * 14.0 / total;

                if (wipeMismatched && onlyMismatched && !IsUserDataUnit(unit))
                {
                    progress(ClampPct(basePct), -1, "Clearing:\n" + name);
                    WipeUnitDestination(unit);
                }

                string dest = ResolveExtractDir(unit);
                Directory.CreateDirectory(dest);
                int extractCur = ClampPct(100.0 * i / Math.Max(1, total));
                progress(ClampPct(basePct + 5.0 / total), extractCur, "Extracting " + i + "/" + total + ":\n" + name);
                Extract7z(local, dest);
                // Never delete torrent/offline package files; only purge download_cache copies
                string idKey = unit.id ?? ShortName(name);
                if (!keepSource.ContainsKey(idKey))
                {
                    try { File.Delete(local); } catch { }
                }

                string up = (unit.path ?? "").Replace('\\', '/');
                if (up.Equals("profiles", StringComparison.OrdinalIgnoreCase) ||
                    up.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
                {
                    try { CaptureOfficialModlists(); } catch (Exception ex) { LauncherLog.Warn("capture modlist: " + ex.Message); }
                }
            }

            try { RestoreModOrder(); } catch (Exception ex) { LauncherLog.Warn("modlist merge: " + ex.Message); }

            FinishInstallFlag(index.version, jobName + " fixed=" + total + " skipped=" + skipped);
            SaveLocalVersion(index.version ?? "1.0.0");
            progress(100, -1, jobName + " complete.\n" + total + " package(s)");
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
                try { Directory.Delete(dest, true); }
                catch (Exception ex)
                {
                    throw new FlappyException("Cannot clear folder:\n" + dest + "\n\nClose MO2/game and retry.", ex.ToString(), ex);
                }
            }
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

            var psi = new ProcessStartInfo
            {
                FileName = SevenZipPath,
                Arguments = string.Format("x -y -aoa \"-o{0}\" -- \"{1}\"", destDir, archive),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
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

        // ---------- modlist restore ----------

        public static void CaptureOfficialModlists()
        {
            Directory.CreateDirectory(OfficialModlistDir);
            string profiles = Path.Combine(GameRootPath, "profiles");
            if (!Directory.Exists(profiles)) return;
            foreach (var dir in Directory.GetDirectories(profiles))
            {
                string ml = Path.Combine(dir, "modlist.txt");
                if (!File.Exists(ml)) continue;
                string name = Path.GetFileName(dir);
                string key = name.IndexOf("VR", StringComparison.OrdinalIgnoreCase) >= 0 ? "modlist_VR.txt" : "modlist_AE.txt";
                File.Copy(ml, Path.Combine(OfficialModlistDir, key), true);
            }
        }

        /// <summary>
        /// Restore official mod order; append user-added mods at the end (enabled).
        /// </summary>
        public static void RestoreModOrder()
        {
            string modsRoot = Path.Combine(GameRootPath, "mods");
            if (!Directory.Exists(modsRoot)) return;

            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in Directory.GetDirectories(modsRoot))
                installed.Add(Path.GetFileName(d));

            string profiles = Path.Combine(GameRootPath, "profiles");
            if (!Directory.Exists(profiles)) return;

            foreach (var profileDir in Directory.GetDirectories(profiles))
            {
                string profileName = Path.GetFileName(profileDir);
                bool isVr = profileName.IndexOf("VR", StringComparison.OrdinalIgnoreCase) >= 0;
                string officialPath = Path.Combine(OfficialModlistDir, isVr ? "modlist_VR.txt" : "modlist_AE.txt");
                string currentPath = Path.Combine(profileDir, "modlist.txt");

                List<string> officialLines = null;
                if (File.Exists(officialPath))
                    officialLines = new List<string>(File.ReadAllLines(officialPath, Encoding.UTF8));
                else if (File.Exists(currentPath))
                    officialLines = new List<string>(File.ReadAllLines(currentPath, Encoding.UTF8));
                else
                    continue;

                var ordered = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string raw in officialLines)
                {
                    string line = raw.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string mod = ParseModlistName(line);
                    if (mod == null) { ordered.Add(line); continue; }
                    if (!installed.Contains(mod)) continue; // removed
                    if (seen.Add(mod))
                        ordered.Add(line.StartsWith("+") || line.StartsWith("-") ? line : ("+" + mod));
                }

                // User mods: installed but not in official
                foreach (string mod in installed.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    if (seen.Contains(mod)) continue;
                    ordered.Add("+" + mod);
                    seen.Add(mod);
                }

                File.WriteAllLines(currentPath, ordered, new UTF8Encoding(false));
                LauncherLog.Info("modlist restored: " + profileName + " lines=" + ordered.Count);
            }
        }

        private static string ParseModlistName(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            line = line.Trim();
            if (line.StartsWith("#")) return null;
            if (line.StartsWith("+") || line.StartsWith("-"))
                return line.Substring(1).Trim();
            return line;
        }

        // ---------- launch ----------

        public static string GetProfileName(string mode)
        {
            return mode == "VR"
                ? "VR - Re-Dovah - 1.0.0 - alpha"
                : "AE - Re-Dovah - 1.0.0";
        }

        /// <summary>MO2 customExecutables title for one-click start (from ModOrganizer.ini.AE/VR).</summary>
        public static string GetStartExecutableTitle(string mode)
        {
            return mode == "VR" ? "Start Re-Dovah VR" : "Start Re-Dovah";
        }

        /// <summary>
        /// Play: prepare profile/registry, then run MO2 custom executable so the game starts
        /// without needing to click Start Re-Dovah in the MO2 UI.
        /// </summary>
        public static void LaunchGame(string mode)
        {
            LaunchMo(mode, openUiOnly: false);
        }

        /// <summary>Open Mod Organizer UI for modding (chosen AE/VR profile).</summary>
        public static void LaunchModOrganizer(string mode)
        {
            LaunchMo(mode, openUiOnly: true);
        }

        private static void LaunchMo(string mode, bool openUiOnly)
        {
            mode = (mode == "VR") ? "VR" : "AE";
            if (mode == "VR" && GetSavedChannel() == InstallChannel.AeOnly)
                throw new FlappyException(
                    "This install is AE-only.\n\nUse the \"Get VR\" button to download VR packages.");

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
            string execTitle = GetStartExecutableTitle(mode);

            if (!File.Exists(moExe))
                throw new FlappyException("ModOrganizer.exe not found.\nInstall or Repair the pack first.");
            if (!Directory.Exists(stockDir.TrimEnd('\\')))
                throw new FlappyException(stockSub + " folder not found.\n\nIf you chose AE-only, use AE mode.");
            if (!File.Exists(iniProfile))
                throw new FlappyException("Missing " + Path.GetFileName(iniProfile));

            EnsureInstalledPath(regPath, stockDir);
            File.Copy(iniProfile, iniMain, true);
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

        private static string FormatDownloadStatus(int finished, int total, double bytesPerSec, IEnumerable<DlSlot> slots)
        {
            var sb = new StringBuilder();
            sb.Append(finished).Append('/').Append(total);
            sb.Append("  ·  ").Append(FormatSpeed(bytesPerSec));
            // Active files (up to 3)
            var active = new List<DlSlot>();
            foreach (var s in slots)
            {
                if (s == null) continue;
                if (s.Total > 0 && s.Received >= s.Total) continue;
                if (s.Received <= 0 && s.Total <= 0) continue;
                active.Add(s);
            }
            active.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            int n = Math.Min(3, active.Count);
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
