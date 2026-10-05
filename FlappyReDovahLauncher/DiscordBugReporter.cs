using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FlappyReDovahLauncher
{
    internal static class DiscordBugReporter
    {
        public sealed class ReportResult
        {
            public bool Success { get; set; }
            public string ThreadUrl { get; set; }
            public string ThreadName { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static async Task<ReportResult> SubmitAsync(
            string game,
            string title,
            string details,
            string archiveUrl,
            string launcherVersion,
            string gameVersion,
            CancellationToken cancel = default)
        {
            try
            {
                var payload = new
                {
                    game = game ?? "Launcher",
                    title = string.IsNullOrWhiteSpace(title) ? "Bug Report" : title.Trim(),
                    details = details ?? "",
                    archive_url = archiveUrl ?? "",
                    launcher_ver = launcherVersion ?? "",
                    game_ver = gameVersion ?? ""
                };

                string json = JsonAdapter.ToJson(payload);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                var req = (HttpWebRequest)WebRequest.Create(Constants.BUG_REPORT_API_URL);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = bytes.Length;
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.UserAgent = Constants.LAUNCHER_NAME;

                using (var reqStream = await req.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await reqStream.WriteAsync(bytes, 0, bytes.Length, cancel).ConfigureAwait(false);
                }

                using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string respJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var parsed = JsonAdapter.FromJson<ApiResponse>(respJson);
                    if (parsed != null && string.Equals(parsed.status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ReportResult
                        {
                            Success = true,
                            ThreadUrl = parsed.thread_url,
                            ThreadName = parsed.thread_name
                        };
                    }

                    return new ReportResult
                    {
                        Success = false,
                        ErrorMessage = parsed != null ? parsed.error : "Unknown server response"
                    };
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("DiscordBugReporter failed: " + ex.Message);
                return new ReportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private sealed class ApiResponse
        {
            public string status { get; set; }
            public string thread_id { get; set; }
            public string thread_url { get; set; }
            public string thread_name { get; set; }
            public string error { get; set; }
        }
    }
}
