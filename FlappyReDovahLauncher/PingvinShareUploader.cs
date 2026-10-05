using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Uploads bug report archives to Pingvin Share (https://share.flappy.su)
    /// </summary>
    internal static class PingvinShareUploader
    {
        public const string SHARE_BASE_URL = "https://share.flappy.su";
        private const string API_EMAIL = "bugreport@flappy.su";
        private const string API_PASS = "bDws6j3bX1QATjQi1DPiaAGJQpYOu0";
        private const string DEFAULT_EXPIRATION = "7-days";
        private const int CHUNK_SIZE = 8 * 1024 * 1024; // 8 MB chunks

        public static async Task<string> UploadAsync(
            string filePath,
            Action<int, string> onProgress,
            CancellationToken cancel)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Bug report archive missing", filePath);

            using (var handler = new HttpClientHandler())
            using (var client = new HttpClient(handler))
            {
                client.BaseAddress = new Uri(SHARE_BASE_URL);
                client.Timeout = TimeSpan.FromMinutes(5);

                if (onProgress != null)
                    onProgress(60, Loc.IsRu ? "Авторизация на Flappy Share…" : "Authenticating with Flappy Share…");

                // 1. Sign in
                string loginJson = "{\"email\":\"" + API_EMAIL + "\",\"password\":\"" + API_PASS + "\"}";
                using (var loginContent = new StringContent(loginJson, Encoding.UTF8, "application/json"))
                using (var loginRes = await client.PostAsync("/api/auth/signIn", loginContent, cancel).ConfigureAwait(false))
                {
                    if (!loginRes.IsSuccessStatusCode)
                    {
                        string err = await loginRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new Exception("Sign-in failed (" + loginRes.StatusCode + "): " + err);
                    }
                    string json = await loginRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var auth = JsonAdapter.FromJson<AuthResponse>(json);
                    if (auth == null || string.IsNullOrEmpty(auth.accessToken))
                        throw new Exception("Invalid auth response from Flappy Share.");

                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", auth.accessToken);
                }

                if (onProgress != null)
                    onProgress(65, Loc.IsRu ? "Создание ссылки на Flappy Share…" : "Creating share on Flappy Share…");

                // 2. Create share
                string shareId = "bug_" + Guid.NewGuid().ToString("N").Substring(0, 10);
                string title = GameCatalog.Current != null ? GameCatalog.Current.Title : "Flappy";
                string shareJson = "{\"id\":\"" + shareId + "\",\"recipients\":[],\"expiration\":\"" + DEFAULT_EXPIRATION + "\",\"description\":\"Bug Report - " + title + "\"}";

                using (var shareContent = new StringContent(shareJson, Encoding.UTF8, "application/json"))
                using (var shareRes = await client.PostAsync("/api/shares", shareContent, cancel).ConfigureAwait(false))
                {
                    if (!shareRes.IsSuccessStatusCode)
                    {
                        string err = await shareRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new Exception("Create share failed (" + shareRes.StatusCode + "): " + err);
                    }
                }

                // 3. Upload file in chunks
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                string fileId = Guid.NewGuid().ToString();
                int totalChunks = (int)Math.Ceiling((double)fileBytes.Length / CHUNK_SIZE);
                if (totalChunks <= 0) totalChunks = 1;

                for (int i = 0; i < totalChunks; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    int offset = i * CHUNK_SIZE;
                    int count = Math.Min(CHUNK_SIZE, fileBytes.Length - offset);
                    byte[] chunk = new byte[count];
                    Buffer.BlockCopy(fileBytes, offset, chunk, 0, count);

                    int pct = 70 + (int)(((double)(i + 1) / totalChunks) * 22);
                    if (onProgress != null)
                    {
                        string msg = Loc.IsRu
                            ? ("Загрузка на Flappy Share (" + (i + 1) + "/" + totalChunks + ")…")
                            : ("Uploading to Flappy Share (" + (i + 1) + "/" + totalChunks + ")…");
                        onProgress(pct, msg);
                    }

                    string uploadUrl = "/api/shares/" + shareId + "/files?id=" + fileId + "&name=" + Uri.EscapeDataString(fileName) + "&chunkIndex=" + i + "&totalChunks=" + totalChunks;

                    using (var byteContent = new ByteArrayContent(chunk))
                    {
                        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        using (var uploadRes = await client.PostAsync(uploadUrl, byteContent, cancel).ConfigureAwait(false))
                        {
                            if (!uploadRes.IsSuccessStatusCode)
                            {
                                string err = await uploadRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                                throw new Exception("Chunk upload failed (" + uploadRes.StatusCode + "): " + err);
                            }
                        }
                    }
                }

                // 4. Complete share
                if (onProgress != null)
                    onProgress(95, Loc.IsRu ? "Завершение публикации…" : "Finalizing share…");

                using (var completeRes = await client.PostAsync("/api/shares/" + shareId + "/complete", null, cancel).ConfigureAwait(false))
                {
                    // pingvin-share returns 200 or 202
                }

                string publicUrl = SHARE_BASE_URL + "/share/" + shareId;
                LauncherLog.Info("Bug report uploaded to Pingvin Share: " + publicUrl);
                return publicUrl;
            }
        }

        private class AuthResponse
        {
            public string accessToken { get; set; }
        }
    }
}
