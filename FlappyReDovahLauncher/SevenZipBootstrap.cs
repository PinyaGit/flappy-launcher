using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Resolves 7-Zip CLI without shipping binaries next to the launcher.
    /// Order: LocalAppData cache → installed 7-Zip → download official Extra + 7zr bootstrap.
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

        /// <summary>Path to 7za/7z/7zr that can extract packages. May be null before Ensure.</summary>
        public static string ResolvedPath
        {
            get { return _resolvedPath ?? FindExisting(); }
        }

        public static string FindExisting()
        {
            // Preferred: cached x64 standalone from Extra package
            string[] cache =
            {
                Path.Combine(ToolsDirectory, "x64", "7za.exe"),
                Path.Combine(ToolsDirectory, "7za.exe"),
                Path.Combine(ToolsDirectory, "7zr.exe")
            };
            foreach (var p in cache)
                if (File.Exists(p) && new FileInfo(p).Length > 50 * 1024)
                    return p;

            // System install
            string[] system =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe"
            };
            foreach (var p in system)
                if (File.Exists(p)) return p;

            // Legacy: next to exe (old publishes) — still works, not required
            string next = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7za.exe");
            if (File.Exists(next)) return next;

            return null;
        }

        /// <summary>
        /// Ensure a working 7-Zip CLI is available. Downloads from 7-zip.org / GitHub if needed.
        /// Call before first extract (Install/Repair/Update).
        /// </summary>
        public static string Ensure(Action<string> status, CancellationToken cancel)
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_resolvedPath) && File.Exists(_resolvedPath))
                    return _resolvedPath;

                string existing = FindExisting();
                if (!string.IsNullOrEmpty(existing))
                {
                    _resolvedPath = existing;
                    LauncherLog.Info("7-Zip: using " + existing);
                    return existing;
                }

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
                    string resolved = File.Exists(x64) ? x64 : (File.Exists(x86) ? x86 : sevenZr);

                    if (!File.Exists(resolved))
                        throw new FlappyException(
                            "7-Zip Extra extracted but 7za.exe was not found in:\n" + tools);

                    try { File.Delete(extra); } catch { }

                    _resolvedPath = resolved;
                    LauncherLog.Info("7-Zip: installed " + resolved);
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
                        "Could not download 7-Zip tools.\n\n" +
                        "Install 7-Zip from https://www.7-zip.org/ or check your network.\n\n" +
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
