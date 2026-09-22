using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Own 7-Zip CLI in LocalAppData. Never uses Program Files / PATH 7-Zip —
    /// a system install is often older than the archives this launcher ships.
    /// </summary>
    internal static class SevenZipBootstrap
    {
        // Pinned stable release (also used as fallback if GitHub API is blocked).
        // Official mirrors: https://www.7-zip.org/download.html  /  https://github.com/ip7z/7zip/releases
        public const string PinnedVersion = "26.02";
        public const string PinnedExtraUrl = "https://www.7-zip.org/a/7z2602-extra.7z";
        public const string PinnedExtraUrlGh = "https://github.com/ip7z/7zip/releases/download/26.02/7z2602-extra.7z";
        public const string Pinned7zrUrl = "https://www.7-zip.org/a/7zr.exe";
        public const string Pinned7zrUrlGh = "https://github.com/ip7z/7zip/releases/download/26.02/7zr.exe";

        private static readonly object Gate = new object();
        private static string _resolvedPath;

        public static string ToolsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FlappyLauncher", "tools", "7zip");
            }
        }

        private static string VersionStampPath
        {
            get { return Path.Combine(ToolsDirectory, "version.txt"); }
        }

        /// <summary>Path to managed 7za. May be null before Ensure.</summary>
        public static string ResolvedPath
        {
            get { return _resolvedPath ?? FindManagedCli(); }
        }

        /// <summary>7za from our bundled/embedded resources or LocalAppData cache only (never system 7-Zip).</summary>
        public static string FindManagedCli()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                // 1. Next to the launcher itself
                Path.Combine(baseDir, "7za.exe"),
                Path.Combine(baseDir, "tools", "7za.exe"),
                Path.Combine(baseDir, "tools", "7zip", "x64", "7za.exe"),
                // 2. Managed LocalAppData cache
                Path.Combine(ToolsDirectory, "x64", "7za.exe"),
                Path.Combine(ToolsDirectory, "7za.exe")
            };
            foreach (var p in candidates)
            {
                if (File.Exists(p) && new FileInfo(p).Length > 50 * 1024)
                {
                    if (CacheMatchesPinned() || p.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }

            // 3. Extract embedded 7-Zip tools if cache is empty or stale
            if (TryExtractEmbeddedTools())
            {
                string p = Path.Combine(ToolsDirectory, "x64", "7za.exe");
                if (File.Exists(p) && new FileInfo(p).Length > 50 * 1024)
                    return p;
            }

            return null;
        }

        public static bool TryExtractEmbeddedTools()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                string targetDir = Path.Combine(ToolsDirectory, "x64");
                Directory.CreateDirectory(targetDir);

                string[] toolFiles = { "7za.exe", "7za.dll", "7zxa.dll" };
                bool anyExtracted = false;

                foreach (string file in toolFiles)
                {
                    string resName = null;
                    foreach (var n in names)
                    {
                        if (n.EndsWith("." + file, StringComparison.OrdinalIgnoreCase) ||
                            n.Equals(file, StringComparison.OrdinalIgnoreCase))
                        {
                            resName = n;
                            break;
                        }
                    }

                    if (resName != null)
                    {
                        string outPath = Path.Combine(targetDir, file);
                        using (var s = asm.GetManifestResourceStream(resName))
                        {
                            if (s != null)
                            {
                                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                                {
                                    s.CopyTo(fs);
                                }
                                anyExtracted = true;
                            }
                        }
                    }
                }

                if (anyExtracted)
                {
                    File.WriteAllText(VersionStampPath, PinnedVersion);
                    LauncherLog.Info("7-Zip: extracted embedded tools to " + targetDir);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("7-Zip: embedded extraction failed: " + ex.Message);
            }
            return false;
        }

        private static bool CacheMatchesPinned()
        {
            try
            {
                if (!File.Exists(VersionStampPath)) return false;
                return string.Equals(
                    File.ReadAllText(VersionStampPath).Trim(),
                    PinnedVersion,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsManagedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/') + "\\";
                string root = Path.GetFullPath(ToolsDirectory).TrimEnd('\\', '/') + "\\";
                string full = Path.GetFullPath(path);
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                       full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Ensure pinned 7za is in LocalAppData. First extracts embedded 7za;
        /// downloads Extra only as fallback. Never falls back to a system 7-Zip install.
        /// </summary>
        public static string Ensure(Action<string> status, CancellationToken cancel)
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_resolvedPath)
                    && File.Exists(_resolvedPath)
                    && IsManagedPath(_resolvedPath)
                    && CacheMatchesPinned())
                    return _resolvedPath;

                string existing = FindManagedCli();
                if (!string.IsNullOrEmpty(existing) && (CacheMatchesPinned() || IsManagedPath(existing)))
                {
                    _resolvedPath = existing;
                    LauncherLog.Info("7-Zip: using managed " + existing);
                    return existing;
                }

                // 1) Try extracting from launcher's own embedded resources first
                if (TryExtractEmbeddedTools())
                {
                    string embedded = Path.Combine(ToolsDirectory, "x64", "7za.exe");
                    if (File.Exists(embedded))
                    {
                        _resolvedPath = embedded;
                        LauncherLog.Info("7-Zip: using embedded " + embedded);
                        return embedded;
                    }
                }

                if (!string.IsNullOrEmpty(existing))
                    LauncherLog.Info("7-Zip: cache stale or unpinned, redownloading " + PinnedVersion);

                if (status != null) status("Downloading 7-Zip…\nfrom 7-zip.org");
                LauncherLog.Info("7-Zip: not found, downloading official Extra package");

                Directory.CreateDirectory(ToolsDirectory);
                string tools = ToolsDirectory;
                string sevenZr = Path.Combine(tools, "7zr.exe");
                string extra = Path.Combine(tools, "extra.7z");

                try
                {
                    // 1) Bootstrap: 7zr.exe is a single PE (no DLL) — extracts .7z only
                    DownloadFirstOk(new[] { Pinned7zrUrl, Pinned7zrUrlGh }, sevenZr, cancel, status, "7zr.exe");
                    if (!File.Exists(sevenZr) || new FileInfo(sevenZr).Length < 50 * 1024)
                        throw new FlappyException("Downloaded 7zr.exe looks invalid.");

                    // 2) Full Extra (x86 + x64 7za + DLLs)
                    DownloadFirstOk(new[] { PinnedExtraUrl, PinnedExtraUrlGh }, extra, cancel, status, "7-Zip Extra");

                    if (status != null) status("Installing 7-Zip tools…\nextracting");
                    RunProcess(sevenZr, string.Format("x -y \"-o{0}\" -- \"{1}\"", tools, extra), cancel);

                    string x64 = Path.Combine(tools, "x64", "7za.exe");
                    string x86 = Path.Combine(tools, "7za.exe");
                    string resolved = File.Exists(x64) ? x64 : (File.Exists(x86) ? x86 : null);

                    if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
                        throw new FlappyException(
                            "7-Zip Extra extracted but 7za.exe was not found in:\n" + tools);

                    try { File.Delete(extra); } catch { }

                    try { File.WriteAllText(VersionStampPath, PinnedVersion); } catch { }

                    _resolvedPath = resolved;
                    LauncherLog.Info("7-Zip: installed managed " + resolved + " v" + PinnedVersion);
                    if (status != null) status("7-Zip ready");
                    return resolved;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (FlappyException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new FlappyException(
                        "Could not download 7-Zip tools for the launcher.\n\n" +
                        "Check your network. The launcher does not use a system 7-Zip install.\n\n" +
                        ex.Message,
                        ex.ToString(), ex);
                }
            }
        }

        private static void DownloadFirstOk(string[] urls, string dest, CancellationToken cancel, Action<string> status, string label)
        {
            Exception last = null;
            string tmp = dest + ".part";
            foreach (var url in urls)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    if (status != null) status("Downloading " + label + "…\n" + HostOf(url));
                    LauncherLog.Info("7-Zip download: " + url);
                    if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
                    HttpDownloader.DownloadSimple(url, tmp, cancel);
                    if (!File.Exists(tmp) || new FileInfo(tmp).Length < 1024)
                        throw new IOException("Empty download from " + url);
                    if (File.Exists(dest)) try { File.Delete(dest); } catch { }
                    File.Move(tmp, dest);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    LauncherLog.Warn("7-Zip download failed (" + url + "): " + ex.Message);
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
            throw new FlappyException(
                "Failed to download " + label + " from official mirrors.\n\n" +
                (last != null ? last.Message : "No URLs tried."),
                last != null ? last.ToString() : null, last);
        }

        private static string HostOf(string url)
        {
            try { return new Uri(url).Host; }
            catch { return url; }
        }

        private static void RunProcess(string fileName, string args, CancellationToken cancel)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                if (p == null)
                    throw new FlappyException("Failed to start " + Path.GetFileName(fileName));
                string stdout = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                while (!p.WaitForExit(200))
                {
                    if (cancel.IsCancellationRequested)
                    {
                        try { p.Kill(); } catch { }
                        cancel.ThrowIfCancellationRequested();
                    }
                }
                if (p.ExitCode > 1)
                {
                    string detail = string.IsNullOrWhiteSpace(err) ? stdout : err;
                    throw new FlappyException(
                        "7zr extract failed (exit " + p.ExitCode + "):\n" + detail.Trim());
                }
            }
        }
    }
}
