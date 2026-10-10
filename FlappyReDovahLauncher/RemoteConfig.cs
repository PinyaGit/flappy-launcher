using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace FlappyReDovahLauncher
{
    internal sealed class RemoteMaintenance
    {
        public bool Enabled { get; set; }
        public string Message { get; set; }
    }

    internal sealed class RemoteBanner
    {
        public bool Enabled { get; set; }
        public string Type { get; set; } // "info", "warning", "alert"
        public string Text { get; set; }
        public string Link { get; set; }
    }

    internal sealed class RemoteLinks
    {
        public string Discord { get; set; }
        public string Boosty { get; set; }
        public string Telegram { get; set; }
    }

    internal sealed class RemoteGameConfig
    {
        public bool Available { get; set; }
        public bool RequiresAccessKey { get; set; }
    }

    internal sealed class RemoteConfigData
    {
        public RemoteMaintenance Maintenance { get; set; }
        public RemoteBanner Banner { get; set; }
        public RemoteLinks Links { get; set; }
        public Dictionary<string, RemoteGameConfig> Games { get; set; }
    }

    internal static class RemoteConfig
    {
        private static RemoteConfigData _current;
        private static readonly object _lock = new object();

        public static RemoteConfigData Current
        {
            get
            {
                lock (_lock)
                {
                    if (_current == null)
                        _current = CreateDefault();
                    return _current;
                }
            }
            private set
            {
                lock (_lock)
                {
                    _current = value;
                }
            }
        }

        public static event Action Updated;

        public static RemoteConfigData CreateDefault()
        {
            return new RemoteConfigData
            {
                Maintenance = new RemoteMaintenance
                {
                    Enabled = false,
                    Message = "Ведутся технические работы по обновлению сборки. Попробуйте позже."
                },
                Banner = new RemoteBanner
                {
                    Enabled = false,
                    Type = "info",
                    Text = "",
                    Link = ""
                },
                Links = new RemoteLinks
                {
                    Discord = Constants.DISCORD_URL,
                    Boosty = Constants.BOOSTY_URL,
                    Telegram = ""
                },
                Games = new Dictionary<string, RemoteGameConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    { "re-dovah", new RemoteGameConfig { Available = true, RequiresAccessKey = true } },
                    { "flappy", new RemoteGameConfig { Available = true, RequiresAccessKey = true } },
                    { "doom", new RemoteGameConfig { Available = true, RequiresAccessKey = false } }
                }
            };
        }

        public static async Task FetchAsync(int timeoutMs = 2500)
        {
            try
            {
                string url = Constants.REMOTE_CONFIG_URL;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                try
                {
                    req.UserAgent = "FlappyLauncher/" + LauncherSelfUpdate.GetLocalVersion();
                }
                catch { }

                using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                using (cts.Token.Register(() => req.Abort(), useSynchronizationContext: false))
                using (var resp = await req.GetResponseAsync().ConfigureAwait(false))
                using (var stream = resp.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var parsed = JsonAdapter.FromJson<RemoteConfigData>(json);
                    if (parsed != null)
                    {
                        Apply(parsed);
                        LauncherLog.Info("Remote config applied from " + url);
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Failed to fetch remote config: " + ex.Message);
            }
        }

        public static void Apply(RemoteConfigData cfg)
        {
            if (cfg == null) return;
            Current = cfg;

            // Apply links
            if (cfg.Links != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.Links.Discord))
                    Constants.DISCORD_URL = cfg.Links.Discord;
                if (!string.IsNullOrWhiteSpace(cfg.Links.Boosty))
                    Constants.BOOSTY_URL = cfg.Links.Boosty;
                if (!string.IsNullOrWhiteSpace(cfg.Links.Telegram))
                    Constants.TELEGRAM_URL = cfg.Links.Telegram;
            }

            // Apply games availability and gates
            if (cfg.Games != null)
            {
                foreach (var g in GameCatalog.Games)
                {
                    if (cfg.Games.TryGetValue(g.Id, out var gCfg))
                    {
                        g.Available = gCfg.Available;
                        g.RequiresAccessKey = gCfg.RequiresAccessKey;
                    }
                }
            }

            try
            {
                Updated?.Invoke();
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Error in RemoteConfig.Updated handler: " + ex.Message);
            }
        }
    }
}
