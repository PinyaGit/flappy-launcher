using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// HTTP/file download with resume and chunked Range for large archives (&gt;~800MB).
    /// </summary>
    internal static class HttpDownloader
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            // .NET Framework default connection limit is 2 — kills multi-download speed.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = 32;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            try
            {
                // Prefer more connections to the CDN host
                var sp = ServicePointManager.FindServicePoint(new Uri("https://cdn.flappy.su/"));
                sp.ConnectionLimit = 32;
            }
            catch { }

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None, // archives are already compressed
                AllowAutoRedirect = true,
                MaxConnectionsPerServer = 32
            };
            var c = new HttpClient(handler);
            c.Timeout = TimeSpan.FromHours(6);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FlappyLauncher/0.0.5");
            c.DefaultRequestHeaders.ConnectionClose = false;
            return c;
        }

        public static bool IsHttp(string urlOrPath)
        {
            if (string.IsNullOrEmpty(urlOrPath)) return false;
            return urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static string ReadAllText(string urlOrPath)
        {
            if (IsHttp(urlOrPath))
            {
                try
                {
                    return Http.GetStringAsync(urlOrPath).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    throw new FlappyException(
                        "Cannot download index from CDN:\n" + urlOrPath + "\n\n" + ex.Message,
                        ex.ToString(), ex);
                }
            }
            string path = ToLocalPath(urlOrPath);
            if (!File.Exists(path))
                throw new FlappyException("File not found:\n" + path);
            return File.ReadAllText(path, System.Text.Encoding.UTF8);
        }

        public static string ToLocalPath(string urlOrPath)
        {
            if (string.IsNullOrEmpty(urlOrPath)) return urlOrPath;
            if (urlOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return new Uri(urlOrPath).LocalPath;
            return urlOrPath;
        }

        public static string CombineUrl(string baseUrl, string relative)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new FlappyException("PACKAGES_BASE_URL is empty. Check Constants.cs.");
            relative = (relative ?? "").Replace('\\', '/').TrimStart('/');
            if (IsHttp(baseUrl) || baseUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                return baseUrl.TrimEnd('/') + "/" + relative;
            return Path.Combine(baseUrl.TrimEnd('\\', '/'), relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Download to dest with optional resume and chunking. Verifies packageSha256 when provided.
        /// </summary>
        public static void Download(
            string urlOrPath,
            string dest,
            long expectedSize,
            string expectedSha256,
            Action<long, long> onProgress,
            CancellationToken cancel)
        {
            bool keep;
            AcquirePackage(urlOrPath, dest, expectedSize, expectedSha256, onProgress, cancel, out keep);
        }

        /// <summary>
        /// Prefer optional localPath (torrent), else download httpUrl to cacheDest.
        /// fromLocalBundle=true → do not delete path after extract.
        /// </summary>
        public static string AcquirePackage(
            string localPathOrNull,
            string httpUrl,
            string cacheDest,
            long expectedSize,
            string expectedSha256,
            Action<long, long> onProgress,
            CancellationToken cancel,
            out bool fromLocalBundle)
        {
            string path;
            AcquirePackageCore(localPathOrNull, httpUrl, cacheDest, expectedSize, expectedSha256, onProgress, cancel, out path, out fromLocalBundle);
            return path;
        }

        /// <summary>Backward-compatible single-source acquire (local path or HTTP url).</summary>
        public static string AcquirePackage(
            string urlOrPath,
            string cacheDest,
            long expectedSize,
            string expectedSha256,
            Action<long, long> onProgress,
            CancellationToken cancel,
            out bool fromLocalBundle)
        {
            if (IsHttp(urlOrPath))
                return AcquirePackage(null, urlOrPath, cacheDest, expectedSize, expectedSha256, onProgress, cancel, out fromLocalBundle);
            return AcquirePackage(urlOrPath, null, cacheDest, expectedSize, expectedSha256, onProgress, cancel, out fromLocalBundle);
        }

        private static void AcquirePackageCore(
            string localPathOrNull,
            string httpUrl,
            string cacheDest,
            long expectedSize,
            string expectedSha256,
            Action<long, long> onProgress,
            CancellationToken cancel,
            out string resultPath,
            out bool fromLocalBundle)
        {
            cancel.ThrowIfCancellationRequested();
            fromLocalBundle = false;
            resultPath = cacheDest;

            // 1) Local torrent / offline copy (fast path) — same unit as CDN would serve
            if (!string.IsNullOrWhiteSpace(localPathOrNull) && File.Exists(localPathOrNull))
            {
                try
                {
                    long len = new FileInfo(localPathOrNull).Length;
                    onProgress?.Invoke(0, expectedSize > 0 ? expectedSize : len);
                    VerifyPackageFile(localPathOrNull, expectedSize, expectedSha256, skipFullSha: true, cancel);
                    onProgress?.Invoke(len, expectedSize > 0 ? expectedSize : len);
                    resultPath = localPathOrNull;
                    fromLocalBundle = true;
                    LauncherLog.Info("Local package ready: " + Path.GetFileName(localPathOrNull));
                    return;
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn("Local package unusable, will try CDN: " + Path.GetFileName(localPathOrNull) + " — " + ex.Message);
                    // fall through to HTTP
                }
            }

            // 2) CDN / HTTP (Repair, Update, missing torrent files, pure online install)
            if (string.IsNullOrWhiteSpace(httpUrl) || !IsHttp(httpUrl))
            {
                if (!string.IsNullOrWhiteSpace(localPathOrNull) && !File.Exists(localPathOrNull))
                    throw new FlappyException("Package not found locally and no CDN URL:\n" + localPathOrNull);
                throw new FlappyException("Package URL is missing or invalid.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cacheDest) ?? ".");
            DownloadHttp(httpUrl, cacheDest, expectedSize, onProgress, cancel);
            cancel.ThrowIfCancellationRequested();
            VerifyPackageFile(cacheDest, expectedSize, expectedSha256, skipFullSha: false, cancel);
            resultPath = cacheDest;
            fromLocalBundle = false;
        }

        private static void VerifyPackageFile(
            string path,
            long expectedSize,
            string expectedSha256,
            bool skipFullSha,
            CancellationToken cancel)
        {
            long len = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (len <= 0)
                throw new FlappyException("Package file is empty:\n" + Path.GetFileName(path));

            if (expectedSize > 0 && len != expectedSize)
            {
                long diff = Math.Abs(len - expectedSize);
                if (diff > 64 * 1024)
                {
                    throw new FlappyException(
                        "Package size mismatch.\nExpected " + expectedSize +
                        " bytes, got " + len + ".\n\nFile: " + Path.GetFileName(path));
                }
                LauncherLog.Warn(string.Format(
                    "Size drift accepted for {0}: index={1} actual={2}",
                    Path.GetFileName(path), expectedSize, len));
            }

            // Full SHA for HTTP downloads. Local torrent: size + 7z magic (BitTorrent already verified).
            if (!skipFullSha && !string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actual = ComputeFileSha256(path, cancel);
                if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(path); } catch { }
                    throw new FlappyException(
                        "Package checksum failed (file corrupted or incomplete).\n\n" +
                        "File: " + Path.GetFileName(path) + "\n" +
                        "It was removed from download_cache — retry the install.");
                }
                LauncherLog.Info("SHA256 OK " + Path.GetFileName(path));
            }

            if (!LooksLike7z(path))
            {
                if (!skipFullSha)
                {
                    try { File.Delete(path); } catch { }
                }
                throw new FlappyException(
                    "File is not a valid 7z archive (bad header).\n\n" +
                    "File: " + Path.GetFileName(path));
            }
        }

        /// <summary>7z signature: 37 7A BC AF 27 1C</summary>
        public static bool LooksLike7z(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 6) return false;
                    byte[] m = new byte[6];
                    if (fs.Read(m, 0, 6) != 6) return false;
                    return m[0] == 0x37 && m[1] == 0x7A && m[2] == 0xBC
                        && m[3] == 0xAF && m[4] == 0x27 && m[5] == 0x1C;
                }
            }
            catch { return false; }
        }

        private static void CopyLocal(string urlOrPath, string dest, long expectedSize, Action<long, long> onProgress, CancellationToken cancel)
        {
            string src = ToLocalPath(urlOrPath);
            if (!File.Exists(src))
                throw new FlappyException("Package not found on disk:\n" + src);

            long total = new FileInfo(src).Length;
            const int bufSize = 1024 * 1024;
            byte[] buffer = new byte[bufSize];
            long copied = 0;
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, bufSize, FileOptions.SequentialScan))
            using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, bufSize))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    copied += read;
                    onProgress?.Invoke(copied, total);
                }
            }
        }

        private static void DownloadHttp(string url, string dest, long expectedSize, Action<long, long> onProgress, CancellationToken cancel)
        {
            long existing = 0;
            if (File.Exists(dest))
            {
                existing = new FileInfo(dest).Length;
                // Size-only cache hit: still re-verify via SHA256 in Download().
                // Log so "suspiciously fast" progress is explainable.
                if (expectedSize > 0 && existing == expectedSize)
                {
                    LauncherLog.Info("Cache hit (size match), will verify hash: " + Path.GetFileName(dest));
                    onProgress?.Invoke(existing, expectedSize);
                    return;
                }
                if (expectedSize > 0 && existing > expectedSize)
                {
                    File.Delete(dest);
                    existing = 0;
                }
            }

            long totalKnown = expectedSize > 0 ? expectedSize : -1;

            // Chunk only huge archives (providers may drop single multi-GB streams).
            // Small packs use one request — much faster and less overhead.
            long chunk = Constants.DOWNLOAD_CHUNK_BYTES;
            if (chunk < 64 * 1024 * 1024) chunk = 64 * 1024 * 1024;
            bool useChunks = expectedSize > chunk;

            if (!useChunks)
            {
                // Single stream (with resume if partial file already exists)
                long rangeStart = existing > 0 ? existing : 0;
                try
                {
                    DownloadRange(url, dest, rangeStart, null, totalKnown, onProgress, cancel);
                }
                catch (HttpRequestException) when (rangeStart > 0)
                {
                    // resume rejected → full restart
                    try { File.Delete(dest); } catch { }
                    DownloadRange(url, dest, 0, null, totalKnown, onProgress, cancel);
                }
                return;
            }

            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                existing = File.Exists(dest) ? new FileInfo(dest).Length : 0;
                if (expectedSize > 0 && existing >= expectedSize)
                    break;

                long rangeStart = existing;
                long end = Math.Min(existing + chunk, expectedSize) - 1;
                if (end < rangeStart) break;
                long? rangeEnd = end;

                try
                {
                    DownloadRange(url, dest, rangeStart, rangeEnd, totalKnown, onProgress, cancel);
                }
                catch (HttpRequestException ex)
                {
                    LauncherLog.Warn("Chunk download failed, retry open-ended resume: " + ex.Message);
                    DownloadRange(url, dest, existing, null, totalKnown, onProgress, cancel);
                }

                long nowLen = File.Exists(dest) ? new FileInfo(dest).Length : 0;
                if (nowLen <= existing)
                    throw new FlappyException("Download stalled (no progress). Check network / CDN.");
                if (nowLen >= expectedSize)
                    break;
            }
        }

        private static void DownloadRange(
            string url,
            string dest,
            long rangeStart,
            long? rangeEnd,
            long totalKnown,
            Action<long, long> onProgress,
            CancellationToken cancel)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (rangeStart > 0 || rangeEnd.HasValue)
                {
                    if (rangeEnd.HasValue)
                        req.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd.Value);
                    else
                        req.Headers.Range = new RangeHeaderValue(rangeStart, null);
                }

                using (var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult())
                {
                    if (rangeStart > 0 && resp.StatusCode == HttpStatusCode.OK)
                    {
                        // Server ignored Range — restart full download
                        LauncherLog.Warn("Server ignored Range, restarting full download: " + url);
                        try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                        rangeStart = 0;
                    }
                    else if (!resp.IsSuccessStatusCode && resp.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new FlappyException(
                            "Download failed (" + (int)resp.StatusCode + " " + resp.StatusCode + "):\n" + url);
                    }

                    long total = totalKnown;
                    if (total <= 0 && resp.Content.Headers.ContentLength.HasValue)
                    {
                        total = rangeStart + resp.Content.Headers.ContentLength.Value;
                    }

                    // Seek to rangeStart (more reliable than FileMode.Append when lengths mismatch)
                    using (var net = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var file = new FileStream(
                        dest,
                        rangeStart > 0 ? FileMode.OpenOrCreate : FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 256,
                        FileOptions.SequentialScan))
                    {
                        if (rangeStart > 0)
                        {
                            if (file.Length < rangeStart)
                                throw new FlappyException(
                                    "Incomplete local cache for resume (file shorter than range start).\n" +
                                    Path.GetFileName(dest));
                            file.Seek(rangeStart, SeekOrigin.Begin);
                        }

                        byte[] buffer = new byte[256 * 1024];
                        long received = rangeStart;
                        int read;
                        long lastReport = Environment.TickCount;
                        while ((read = net.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            file.Write(buffer, 0, read);
                            received += read;
                            long now = Environment.TickCount;
                            if (now - lastReport >= 250 || (total > 0 && received >= total))
                            {
                                lastReport = now;
                                onProgress?.Invoke(received, total > 0 ? total : -1);
                            }
                        }
                        // Truncate if we rewrote mid-file with shorter content
                        if (file.Length > received)
                            file.SetLength(received);
                        onProgress?.Invoke(received, total > 0 ? total : -1);
                    }
                }
            }
        }

        public static string ComputeFileSha256(string path, CancellationToken cancel)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[1024 * 1024];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, read, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>Simple full-file download (tools, small assets). Overwrites dest.</summary>
        public static void DownloadSimple(string url, string dest, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(dest))
                throw new ArgumentException("url/dest required");
            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (var resp = Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult())
            {
                if (!resp.IsSuccessStatusCode)
                    throw new FlappyException(
                        "Download failed (" + (int)resp.StatusCode + " " + resp.StatusCode + "):\n" + url);
                using (var net = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var file = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = net.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        file.Write(buffer, 0, read);
                    }
                }
            }
        }
    }
}
