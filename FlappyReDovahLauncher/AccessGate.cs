using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Closed-test key for titles with <see cref="GameDefinition.RequiresAccessKey"/>.
    /// Server checks the phrase; the launcher never embeds it.
    /// Master switch: <see cref="Constants.ACCESS_GATES_ENABLED"/>.
    /// </summary>
    internal static class AccessGate
    {
        private static readonly HttpClient Http = CreateClient();
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> Tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> Expires = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient();
            c.Timeout = TimeSpan.FromSeconds(20);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FlappyLauncher/0.1.6");
            return c;
        }

        public static bool IsRequired(GameDefinition game)
        {
            if (!Constants.ACCESS_GATES_ENABLED) return false;
            if (game == null || !game.RequiresAccessKey) return false;
            if (Constants.HasLocalPackageBundle) return false; // torrent/offline copy
            return true;
        }

        public static bool HasSession(GameDefinition game)
        {
            if (game == null) return false;
            lock (Sync)
            {
                LoadCachedToken(game.Id);
                return HasFreshToken(game.Id);
            }
        }

        /// <summary>True if this CDN URL is under a gated game folder (Flappy /flappy/).</summary>
        public static bool UrlNeedsToken(string url)
        {
            if (!Constants.ACCESS_GATES_ENABLED) return false;
            if (string.IsNullOrEmpty(url) || !HttpDownloader.IsHttp(url)) return false;
            foreach (var g in GameCatalog.Games)
            {
                if (g == null || !g.RequiresAccessKey) continue;
                string folder = (g.CdnFolder ?? "").Trim().Trim('/');
                if (string.IsNullOrEmpty(folder)) continue;
                if (url.IndexOf("/" + folder + "/", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static void Attach(HttpRequestMessage req, string url)
        {
            if (req == null || !UrlNeedsToken(url)) return;
            string token = GetTokenForUrl(url);
            if (string.IsNullOrEmpty(token)) return;
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }

        /// <summary>
        /// UI-thread. Prompts if needed. Returns false if the player cancelled.
        /// </summary>
        public static bool EnsureUnlocked(IWin32Window owner, GameDefinition game)
        {
            if (!IsRequired(game)) return true;

            lock (Sync)
            {
                LoadCachedToken(game.Id);
                if (HasFreshToken(game.Id)) return true;
            }

            if (!ServerSaysEnabled())
            {
                LauncherLog.Info("Access gate off on server for " + game.Id);
                return true;
            }

            using (var dlg = new AccessKeyForm(game))
            {
                var r = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                return r == DialogResult.OK && HasFreshToken(game.Id);
            }
        }

        public static UnlockResult TryUnlock(GameDefinition game, string key)
        {
            var result = new UnlockResult();
            if (game == null)
            {
                result.Error = Loc.T("access_offline");
                return result;
            }

            string normalized = NormalizeKey(key);
            if (string.IsNullOrEmpty(normalized))
            {
                result.Error = Loc.T("access_bad");
                return result;
            }

            try
            {
                string body = JsonAdapter.ToJson(new Dictionary<string, string>
                {
                    { "game", game.Id },
                    { "key", normalized }
                });
                using (var req = new HttpRequestMessage(HttpMethod.Post, Constants.ACCESS_UNLOCK_URL))
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (var resp = Http.SendAsync(req).GetAwaiter().GetResult())
                    {
                        string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                        int code = (int)resp.StatusCode;
                        if (code == 429)
                        {
                            int retry = ReadRetryAfter(resp, json);
                            result.Error = Loc.F("access_rate", retry);
                            return result;
                        }
                        if (code == 401 || code == 403)
                        {
                            result.Error = Loc.T("access_bad");
                            return result;
                        }
                        if (!resp.IsSuccessStatusCode)
                        {
                            result.Error = Loc.T("access_offline");
                            return result;
                        }

                        var map = JsonAdapter.FromJson<Dictionary<string, object>>(json);
                        string token = MapStr(map, "token");
                        int expiresIn = MapInt(map, "expires_in", 7 * 24 * 3600);
                        if (string.IsNullOrEmpty(token))
                        {
                            result.Error = Loc.T("access_offline");
                            return result;
                        }

                        lock (Sync)
                        {
                            Tokens[game.Id] = token;
                            Expires[game.Id] = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
                            SaveCachedToken(game.Id);
                        }
                        LauncherLog.Info("Access gate unlocked for " + game.Id);
                        result.Ok = true;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Access unlock failed: " + ex.Message);
                result.Error = Loc.T("access_offline");
                return result;
            }
        }

        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var sb = new StringBuilder(key.Length);
            bool space = false;
            foreach (char c in key.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!space && sb.Length > 0)
                    {
                        sb.Append(' ');
                        space = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    space = false;
                }
            }
            return sb.ToString().ToLowerInvariant();
        }

        private static bool ServerSaysEnabled()
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Get, Constants.ACCESS_STATUS_URL))
                using (var resp = Http.SendAsync(req).GetAwaiter().GetResult())
                {
                    if (!resp.IsSuccessStatusCode) return true; // fail closed
                    string json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                    var map = JsonAdapter.FromJson<Dictionary<string, object>>(json);
                    if (map == null) return true;
                    object v;
                    if (!map.TryGetValue("enabled", out v) || v == null) return true;
                    if (v is bool) return (bool)v;
                    string s = Convert.ToString(v);
                    if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
                    if (s == "0") return false;
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        private static bool HasFreshToken(string gameId)
        {
            string t;
            DateTime exp;
            if (!Tokens.TryGetValue(gameId, out t) || string.IsNullOrEmpty(t)) return false;
            if (!Expires.TryGetValue(gameId, out exp)) return false;
            return DateTime.UtcNow < exp.AddMinutes(-5);
        }

        private static string GetTokenForUrl(string url)
        {
            foreach (var g in GameCatalog.Games)
            {
                if (g == null || !g.RequiresAccessKey) continue;
                string folder = (g.CdnFolder ?? "").Trim().Trim('/');
                if (string.IsNullOrEmpty(folder)) continue;
                if (url.IndexOf("/" + folder + "/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                lock (Sync)
                {
                    LoadCachedToken(g.Id);
                    string t;
                    if (Tokens.TryGetValue(g.Id, out t)) return t;
                }
            }
            return null;
        }

        private static string CachePath(string gameId)
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlappyLauncher");
            return Path.Combine(dir, "access_" + gameId + ".token");
        }

        private static void LoadCachedToken(string gameId)
        {
            if (Tokens.ContainsKey(gameId)) return;
            try
            {
                string path = CachePath(gameId);
                if (!File.Exists(path)) return;
                string json = File.ReadAllText(path, Encoding.UTF8);
                var map = JsonAdapter.FromJson<Dictionary<string, object>>(json);
                string token = MapStr(map, "token");
                string expStr = MapStr(map, "expires");
                DateTime exp;
                if (string.IsNullOrEmpty(token) || !DateTime.TryParse(expStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out exp))
                    return;
                if (DateTime.UtcNow >= exp) return;
                Tokens[gameId] = token;
                Expires[gameId] = exp.Kind == DateTimeKind.Utc ? exp : exp.ToUniversalTime();
            }
            catch { }
        }

        private static void SaveCachedToken(string gameId)
        {
            try
            {
                string dir = Path.GetDirectoryName(CachePath(gameId));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                DateTime exp;
                if (!Expires.TryGetValue(gameId, out exp)) exp = DateTime.UtcNow.AddDays(7);
                var map = new Dictionary<string, string>
                {
                    { "token", Tokens[gameId] },
                    { "expires", exp.ToUniversalTime().ToString("o") }
                };
                File.WriteAllText(CachePath(gameId), JsonAdapter.ToJson(map), Encoding.UTF8);
            }
            catch { }
        }

        private static string MapStr(Dictionary<string, object> map, string key)
        {
            if (map == null) return "";
            object v;
            if (!map.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v);
        }

        private static int MapInt(Dictionary<string, object> map, string key, int fallback)
        {
            if (map == null) return fallback;
            object v;
            if (!map.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); }
            catch { return fallback; }
        }

        private static int ReadRetryAfter(HttpResponseMessage resp, string json)
        {
            try
            {
                if (resp.Headers.RetryAfter != null && resp.Headers.RetryAfter.Delta.HasValue)
                    return Math.Max(1, (int)resp.Headers.RetryAfter.Delta.Value.TotalMinutes);
            }
            catch { }
            try
            {
                var map = JsonAdapter.FromJson<Dictionary<string, object>>(json);
                int sec = MapInt(map, "retry_after", 0);
                if (sec > 0) return Math.Max(1, (sec + 59) / 60);
            }
            catch { }
            return 20;
        }

        internal sealed class UnlockResult
        {
            public bool Ok;
            public string Error;
        }
    }
}
