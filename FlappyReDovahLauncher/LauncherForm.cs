using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Portrait launcher: per-pixel alpha splash (no black bar), own-drawn UI over art.
    /// </summary>
    public partial class Application : Form
    {
        private Bitmap _splash;
        private Bitmap _iconDiscord;
        private Bitmap _iconBoosty;
        private Bitmap _iconClose;
        private Bitmap _iconChrome;
        private Bitmap _edgeBarFill;
        private Bitmap _edgeBarFrame;
        private Bitmap _edgeBarTrack;
        private Bitmap _frame;
        private Bitmap _defaultSplash;
        private readonly Dictionary<string, Bitmap> _gameIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private Rectangle _rcProgressOverall;
        private Rectangle _rcProgressCurrent;
        private Rectangle _rcStatus;
        // legacy alias used in a few places
        private Rectangle _rcProgress { get { return _rcProgressOverall; } set { _rcProgressOverall = value; } }

        private PackageInstaller.Index _cachedIndex;
        public Version OnlineVersion { get; private set; }

        private bool _isReady;
        private bool _isInstalled;
        private bool _busy;
        private PlayButtonState _playState = PlayButtonState.Install;
        private string _playText = "Install";
        private string _versionText = "-";
        private string _statusText = "";
        private int _progressOverall;
        private int _progressCurrent = -1; // current archive; -1 = hide secondary fill
        private bool _showProgress;
        private bool _showReady = true;

        private Rectangle _rcPlay;
        private Rectangle _rcMo;
        private Rectangle _rcCancel;
        private Rectangle _rcDiscord;
        private Rectangle _rcBoosty;
        private Rectangle _rcClose;
        private Rectangle _rcGear;
        private Rectangle _rcTray;
        private Rectangle[] _rcGames; // left library rail
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;

        private CancellationTokenSource _jobCts;

        private readonly object _drawLock = new object();
        private Bitmap _premult; // reused buffer for UpdateLayeredWindow
        private int _lastRedrawTick;
        private const int RedrawMinIntervalMs = 80; // ~12 fps max while downloading

        public bool IsReady
        {
            get { return _isReady; }
            set
            {
                _isReady = value;
                _isInstalled = PackageInstaller.IsInstalled();
                RefreshPlayState();
                if (value && !_busy)
                {
                    _statusText = GameCatalog.Current.Available ? Loc.T("ready") : Loc.GameBlurb(GameCatalog.Current);
                    _progressOverall = 0;
                    _progressCurrent = -1;
                    _showProgress = false;
                    _showReady = true;
                }
                RedrawLayered(force: true);
            }
        }

        private bool ShowMoButton
        {
            get { return GameCatalog.Current.Available && _isInstalled && !_busy; }
        }

        private bool ShowCancelButton
        {
            get { return _busy; }
        }

        private void RefreshPlayState()
        {
            if (!GameCatalog.Current.Available)
            {
                _isInstalled = false;
                _playState = PlayButtonState.Install;
                _playText = Loc.T("soon");
                _showProgress = false;
                _showReady = false;
                return;
            }

            _isInstalled = PackageInstaller.IsInstalled();
            var local = PackageInstaller.GetLocalVersion();
            _playState = PackageInstaller.ResolvePlayState(_isInstalled, local, OnlineVersion, _busy);
            switch (_playState)
            {
                case PlayButtonState.Install: _playText = Loc.T("install"); break;
                case PlayButtonState.Update: _playText = Loc.T("update"); break;
                case PlayButtonState.Busy: _playText = "…"; break;
                default: _playText = Loc.T("play"); break;
            }
            _showProgress = _busy || _playState == PlayButtonState.Install || _playState == PlayButtonState.Update;
            if (_busy) _showReady = false;
            else _showReady = _playState == PlayButtonState.Play;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeWinAPI.WS_EX_LAYERED;
                return cp;
            }
        }

        public Application()
        {
            InitializeComponent();
            LoadAssets();
            ApplyGameSplash(GameCatalog.Current);
            LayoutHitRects();
            InitTray();
            GameCatalog.CurrentChanged += OnGameCatalogChanged;
            Loc.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnLanguageChanged));
                return;
            }
            RefreshPlayState();
            RefreshVersionText();
            RebuildTrayMenu();
            RefreshStatusText();
            RedrawLayered(force: true);
        }

        /// <summary>Re-translate idle status after language change (progress text stays until next tick).</summary>
        private void RefreshStatusText()
        {
            if (_busy) return;
            if (!GameCatalog.Current.Available)
                _statusText = Loc.GameBlurb(GameCatalog.Current);
            else if (!_isInstalled || _playState == PlayButtonState.Install)
                _statusText = Loc.T("ready_install");
            else if (_playState == PlayButtonState.Update)
                _statusText = Loc.T("update_available");
            else
                _statusText = Loc.T("ready");
        }

        private void OnGameCatalogChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnGameCatalogChanged));
                return;
            }
            if (_busy) return;
            ApplyGameSplash(GameCatalog.Current);
            LayoutHitRects();
            InitializePackageMode();
        }

        private void LoadAssets()
        {
            // Default splash = Re-Dovah branch art (also used as layout size reference)
            _defaultSplash = new Bitmap(Properties.Resources.bg_re_dovah);
            if (_defaultSplash.PixelFormat != PixelFormat.Format32bppArgb)
            {
                var converted = new Bitmap(_defaultSplash.Width, _defaultSplash.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(converted))
                    g.DrawImage(_defaultSplash, 0, 0, _defaultSplash.Width, _defaultSplash.Height);
                _defaultSplash.Dispose();
                _defaultSplash = converted;
            }
            _splash = (Bitmap)_defaultSplash.Clone();
            LoadGameRailIcons();

            // Assets/discord.png + boosty.png embedded via Resources.resx
            _iconDiscord = new Bitmap(Properties.Resources.discord);
            using (var rawBoosty = new Bitmap(Properties.Resources.boosty))
                _iconBoosty = MakeSoftRoundedIcon(rawBoosty, cornerRadius: 0, feather: 0);

            try { _iconClose = new Bitmap(Properties.Resources.cross); }
            catch
            {
                _iconClose = LoadBitmapResource("cross", "cross.png");
            }
            try { _iconChrome = FlattenChromeButton(new Bitmap(Properties.Resources.chrome_btn)); }
            catch
            {
                var raw = LoadBitmapResource("chrome_btn", "button.png");
                _iconChrome = raw != null ? FlattenChromeButton(raw) : null;
            }

            _edgeBarFill = LoadBitmapResource("edge_bar_fill", "edge_bar_fill.png");
            // Crop empty padding — SVG content sits in top ~33px of 58px canvas
            _edgeBarFrame = CropToOpaque(LoadBitmapResource("edge_bar_frame", "edge_bar_frame.png"));
            _edgeBarTrack = CropToOpaque(LoadBitmapResource("edge_bar_track", "edge_bar_track.png"));

            ClientSize = new Size(_splash.Width, _splash.Height);
        }

        // Must match LayoutHitRects railTile so icons draw 1:1 (no second soft scale).
        private const int RailIconPx = 52;

        private void LoadGameRailIcons()
        {
            foreach (var kv in _gameIcons)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _gameIcons.Clear();

            foreach (var game in GameCatalog.Games)
            {
                Bitmap bmp = null;
                try
                {
                    // Embedded Assets/* via Resources.resx — change PNG + rebuild to update UI
                    Bitmap embedded = game.GetIconBitmap();
                    if (embedded != null)
                        bmp = ResizeSharp(embedded, RailIconPx);
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn("game icon " + game.Id + ": " + ex.Message);
                }
                if (bmp == null)
                    bmp = new Bitmap(RailIconPx, RailIconPx);
                _gameIcons[game.Id] = bmp;
            }
        }

        /// <summary>Resize for UI: NearestNeighbor when upscaling; HQ when downscaling large logos.</summary>
        private static Bitmap ResizeSharp(Image src, int targetPx)
        {
            if (src == null) return new Bitmap(targetPx, targetPx);
            int sw = src.Width, sh = src.Height;
            if (sw == targetPx && sh == targetPx && src is Bitmap b0 && b0.PixelFormat == PixelFormat.Format32bppArgb)
                return new Bitmap(b0);

            var dst = new Bitmap(targetPx, targetPx, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                bool upscale = sw < targetPx || sh < targetPx;
                g.InterpolationMode = upscale
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = upscale ? SmoothingMode.None : SmoothingMode.HighQuality;

                float scale = Math.Min((float)targetPx / sw, (float)targetPx / sh);
                int dw = Math.Max(1, (int)Math.Round(sw * scale));
                int dh = Math.Max(1, (int)Math.Round(sh * scale));
                int dx = (targetPx - dw) / 2;
                int dy = (targetPx - dh) / 2;
                g.DrawImage(src, new Rectangle(dx, dy, dw, dh));
            }
            return dst;
        }

        private static Bitmap LoadSplashBitmapFromFile(string path)
        {
            var next = new Bitmap(path);
            if (next.PixelFormat == PixelFormat.Format32bppArgb)
                return next;
            var c = new Bitmap(next.Width, next.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(c))
                g.DrawImage(next, 0, 0, next.Width, next.Height);
            next.Dispose();
            return c;
        }

        /// <summary>Keep window/layout size stable across branch posters.</summary>
        private Bitmap NormalizeSplashSize(Bitmap next)
        {
            if (next == null) return null;
            if (next.PixelFormat != PixelFormat.Format32bppArgb)
            {
                var c = new Bitmap(next.Width, next.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(c))
                    g.DrawImage(next, 0, 0, next.Width, next.Height);
                next.Dispose();
                next = c;
            }
            if (_defaultSplash != null &&
                (next.Width != _defaultSplash.Width || next.Height != _defaultSplash.Height))
            {
                var scaled = new Bitmap(_defaultSplash.Width, _defaultSplash.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.Clear(Color.FromArgb(255, 8, 8, 10));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(next, 0, 0, scaled.Width, scaled.Height);
                }
                next.Dispose();
                next = scaled;
            }
            return next;
        }

        private void ApplyGameSplash(GameDefinition game)
        {
            Bitmap next = null;
            try
            {
                // 1) Loose file override (dev)
                if (game != null && !string.IsNullOrEmpty(game.SplashPath) && File.Exists(game.SplashPath))
                    next = LoadSplashBitmapFromFile(game.SplashPath);
                // 2) Embedded splash for this game branch/version
                if (next == null && game != null)
                    next = game.GetSplashBitmapClone();
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("splash: " + ex.Message);
                if (next != null) { try { next.Dispose(); } catch { } next = null; }
            }

            if (next != null)
                next = NormalizeSplashSize(next);

            if (next == null && _defaultSplash != null)
                next = (Bitmap)_defaultSplash.Clone();

            if (next != null)
            {
                if (_splash != null && !ReferenceEquals(_splash, _defaultSplash))
                {
                    try { _splash.Dispose(); } catch { }
                }
                _splash = next;
                ClientSize = new Size(_splash.Width, _splash.Height);
            }
            Text = Constants.LAUNCHER_NAME + " — " + (game != null ? game.Title : "");
        }

        /// <summary>
        /// Rounded corners + soft alpha fade at edges (removes hard square halo on logos).
        /// </summary>
        private static Bitmap MakeSoftRoundedIcon(Bitmap src, int cornerRadius, int feather)
        {
            int s = Math.Max(src.Width, src.Height);
            // ~22% radius → friendly rounded square; soft outer fade
            int r = cornerRadius > 0 ? cornerRadius : Math.Max(8, s * 22 / 100);
            int f = feather > 0 ? feather : Math.Max(4, s * 8 / 100);
            if (r * 2 > s) r = s / 2;

            var dst = new Bitmap(s, s, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceOver;

                float scale = Math.Min((float)s / src.Width, (float)s / src.Height);
                int dw = Math.Max(1, (int)Math.Round(src.Width * scale));
                int dh = Math.Max(1, (int)Math.Round(src.Height * scale));
                var destRect = new Rectangle((s - dw) / 2, (s - dh) / 2, dw, dh);

                using (var path = RoundedRect(destRect, r))
                using (var tb = new TextureBrush(src, WrapMode.Clamp))
                {
                    // Map texture into destRect
                    var m = new Matrix();
                    m.Translate(destRect.X, destRect.Y);
                    m.Scale(destRect.Width / (float)src.Width, destRect.Height / (float)src.Height);
                    tb.Transform = m;
                    g.FillPath(tb, path);
                }
            }

            // Feather alpha toward transparent at outer edges
            for (int y = 0; y < dst.Height; y++)
            {
                for (int x = 0; x < dst.Width; x++)
                {
                    Color c = dst.GetPixel(x, y);
                    if (c.A == 0) continue;

                    float dx = Math.Min(x + 0.5f, dst.Width - 0.5f - x);
                    float dy = Math.Min(y + 0.5f, dst.Height - 0.5f - y);
                    float edgeDist = Math.Min(dx, dy);

                    if (dx < r && dy < r)
                    {
                        float ccx = x < dst.Width / 2f ? r : dst.Width - r;
                        float ccy = y < dst.Height / 2f ? r : dst.Height - r;
                        float dist = (float)Math.Sqrt((x + 0.5f - ccx) * (x + 0.5f - ccx) + (y + 0.5f - ccy) * (y + 0.5f - ccy));
                        edgeDist = Math.Max(0f, r - dist);
                    }

                    float t = Math.Min(1f, edgeDist / f);
                    t = t * t * (3f - 2f * t);
                    int a = (int)Math.Round(c.A * t);
                    if (a <= 0)
                        dst.SetPixel(x, y, Color.Transparent);
                    else if (a < c.A)
                        dst.SetPixel(x, y, Color.FromArgb(a, c.R, c.G, c.B));
                }
            }

            return dst;
        }

        private static Bitmap LoadBitmapResource(string resName, string fileName)
        {
            try
            {
                object obj = Properties.Resources.ResourceManager.GetObject(resName);
                if (obj is Bitmap bmp)
                    return new Bitmap(bmp);
            }
            catch { }

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                if (!File.Exists(path))
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);
                if (File.Exists(path))
                    return new Bitmap(path);
            }
            catch { }
            return null;
        }

        /// <summary>Composite button.png over a solid disc so chrome buttons are not see-through.</summary>
        private static Bitmap FlattenChromeButton(Bitmap src)
        {
            if (src == null) return null;
            try
            {
                var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingMode = CompositingMode.SourceOver;
                    float m = src.Width * 0.07f;
                    using (var br = new SolidBrush(Color.FromArgb(255, 16, 14, 18)))
                        g.FillEllipse(br, m, m, src.Width - m * 2, src.Height - m * 2);
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                }
                return dst;
            }
            finally
            {
                src.Dispose();
            }
        }

        /// <summary>Trim fully transparent margins so frame/track align with fill channel.</summary>
        private static Bitmap CropToOpaque(Bitmap src)
        {
            if (src == null) return null;
            int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    if (src.GetPixel(x, y).A > 16)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX) return src;
            var rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            var cropped = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
            }
            src.Dispose();
            return cropped;
        }

        private void LayoutHitRects()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // Top-right chrome: tray · gear · close (button.png round plate)
            const int closeSize = 32;
            const int chromeGap = 4;
            _rcClose = new Rectangle(w - 12 - closeSize, 12, closeSize, closeSize);
            _rcGear = new Rectangle(_rcClose.X - chromeGap - closeSize, 12, closeSize, closeSize);
            _rcTray = new Rectangle(_rcGear.X - chromeGap - closeSize, 12, closeSize, closeSize);

            // Social logos — bottom-left, stacked, no ring
            const int iconSize = 52;
            const int iconPad = 14;
            const int iconGap = 12;
            _rcDiscord = new Rectangle(iconPad, h - iconPad - iconSize, iconSize, iconSize);
            _rcBoosty = new Rectangle(iconPad, h - iconPad - iconSize * 2 - iconGap, iconSize, iconSize);

            // Left library rail (Steam-style): icon tiles above social
            const int railTile = 52;
            const int railGap = 10;
            const int railLabel = 14;
            int n = GameCatalog.Games.Count;
            _rcGames = new Rectangle[n];
            int railTop = 48;
            for (int i = 0; i < n; i++)
            {
                int y = railTop + i * (railTile + railLabel + railGap);
                _rcGames[i] = new Rectangle(iconPad, y, railTile, railTile + railLabel);
            }

            // Footer (right column): Play, Modding above it (Cancel replaces Modding while busy)
            const int playW = 128;
            const int playH = 44;
            const int smallH = 32;
            const int gap = 8;
            int leftCol = iconPad + iconSize + gap + 8;
            _rcPlay = new Rectangle(w - gap - playW, h - 14 - playH, playW, playH);
            _rcMo = new Rectangle(_rcPlay.X, _rcPlay.Top - gap - smallH, playW, smallH);
            _rcCancel = _rcMo;

            // Dual progress: current archive (top) + overall (bottom), left of Play
            int barRight = _rcPlay.Left - gap;
            int barW = Math.Max(120, barRight - leftCol);
            int barH = Math.Max(12, (int)(barW * 28.0 / 527.0));
            int barGap = 4;
            int barsBlockH = barH * 2 + barGap;
            int barsTop = _rcPlay.Top + (_rcPlay.Height - barsBlockH) / 2;
            _rcProgressCurrent = new Rectangle(leftCol, barsTop, barW, barH);
            _rcProgressOverall = new Rectangle(leftCol, barsTop + barH + barGap, barW, barH);

            const int statusH = 84;
            _rcStatus = new Rectangle(
                leftCol,
                Math.Max(8, _rcProgressCurrent.Top - 6 - statusH),
                barW,
                statusH);
        }

        private void OnLoadApplication(object sender, EventArgs e)
        {
            Name = Constants.LAUNCHER_NAME;
            Text = Constants.LAUNCHER_NAME + " — " + GameCatalog.Current.Title;
            try { LoadApplicationIcon(); } catch { }
            try
            {
                Visible = true;
                NativeWinAPI.ShowWindow(Handle, NativeWinAPI.SW_RESTORE);
                NativeWinAPI.ShowWindow(Handle, NativeWinAPI.SW_SHOW);
            }
            catch { }
            if (_tray != null && Icon != null)
            {
                try { _tray.Icon = Icon; } catch { }
            }

            RedrawLayered(force: true);
            InitializePackageMode();
        }

        private void InitializePackageMode()
        {
            try
            {
                var game = GameCatalog.Current;
                LauncherLog.Info("Game page: " + game.Id + " launcher v" + FormatShortVersion(LauncherSelfUpdate.GetLocalVersion()));
                _cachedIndex = null;
                OnlineVersion = null;

                if (!game.Available)
                {
                    _isInstalled = false;
                    _playText = Loc.T("soon");
                    _playState = PlayButtonState.Install;
                    _showProgress = false;
                    _showReady = false;
                    _statusText = Loc.GameBlurb(game);
                    RefreshVersionText(indexError: true);
                    IsReady = false;
                    RedrawLayered(force: true);
                    return;
                }

                // Clear stub/previous-game status before CDN work
                _statusText = Loc.T("checking");
                _showProgress = false;
                _showReady = false;

                _cachedIndex = PackageInstaller.FetchIndex();
                OnlineVersion = PackageInstaller.ParseVersion(_cachedIndex.version);
                var local = PackageInstaller.GetLocalVersion();
                RefreshVersionText();

                _isInstalled = PackageInstaller.IsInstalled();
                bool upToDate = _isInstalled && local >= OnlineVersion;
                RefreshPlayState();
                if (upToDate)
                {
                    _statusText = Loc.T("ready");
                    IsReady = true;
                }
                else
                {
                    _statusText = _isInstalled ? Loc.T("update_available") : Loc.T("ready_install");
                    IsReady = false;
                    RedrawLayered(force: true);
                }

                bool autoInstall = !upToDate && (
                    Constants.AUTOMATICALLY_BEGIN_UPDATING ||
                    Constants.HasLocalPackageBundle);
                if (autoInstall)
                {
                    LauncherLog.Info(Constants.HasLocalPackageBundle
                        ? "Local package bundle present — auto Install/Update"
                        : "Auto-update enabled — starting");
                    StartPackageInstallAsync();
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error("index", ex);
                RefreshVersionText(indexError: true);
                _isInstalled = PackageInstaller.IsInstalled();
                IsReady = _isInstalled;
                if (!_isInstalled && GameCatalog.Current.Available)
                    MessageBox.Show(FlappyException.FormatForUser(ex), GameCatalog.Current.Title);
                RedrawLayered(force: true);
            }
        }

        /// <summary>
        /// Game/modpack version (CDN index + local_version) and launcher app version (assembly).
        /// </summary>
        private void RefreshVersionText(bool indexError = false)
        {
            string launcher = FormatShortVersion(LauncherSelfUpdate.GetLocalVersion());
            if (indexError)
            {
                _versionText = Loc.F("ver_game_dash", launcher);
                return;
            }

            var localGame = PackageInstaller.GetLocalVersion();
            bool installed = PackageInstaller.IsInstalled();
            if (!installed || localGame == null || localGame == new Version(0, 0, 0, 0))
            {
                _versionText = OnlineVersion != null
                    ? Loc.F("ver_not_installed", FormatShortVersion(OnlineVersion), launcher)
                    : Loc.F("ver_game_dash_only", launcher);
            }
            else if (OnlineVersion != null && localGame != OnlineVersion)
            {
                _versionText = Loc.F("ver_upgrade", FormatShortVersion(localGame), FormatShortVersion(OnlineVersion), launcher);
            }
            else
            {
                _versionText = Loc.F("ver_ok", FormatShortVersion(localGame), launcher);
            }
        }

        private static string FormatShortVersion(Version v)
        {
            if (v == null) return "?";
            if (v.Build > 0 || v.Revision > 0)
            {
                if (v.Revision > 0) return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            return v.Major + "." + v.Minor;
        }

        private void LoadApplicationIcon()
        {
            string localIco = "app_icon.ico";
            if (!TryDownloadToCache(Constants.APPLICATION_ICON_URL, localIco))
                TryDownloadToCache(Constants.PACKAGES_BASE_URL.TrimEnd('/') + "/app_icon.ico", localIco);

            if (File.Exists(localIco) && new FileInfo(localIco).Length > 0)
            {
                using (var fs = new FileStream(localIco, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var ico = new Icon(fs))
                    Icon = (Icon)ico.Clone();
            }
        }

        private bool TryDownloadToCache(string url, string localName)
        {
            try
            {
                if (File.Exists(localName) && new FileInfo(localName).Length > 0)
                    return true;

                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(url))
                    {
                        File.Copy(url, localName, true);
                        return true;
                    }
                    return false;
                }

                using (var client = new WebClient())
                    client.DownloadFile(url, localName);

                return File.Exists(localName) && new FileInfo(localName).Length > 0;
            }
            catch
            {
                try { if (File.Exists(localName)) File.Delete(localName); } catch { }
                return false;
            }
        }

        // ---------- Layered per-pixel draw ----------

        private void RedrawLayered(bool force = false)
        {
            if (!IsHandleCreated || _splash == null) return;
            if (WindowState == FormWindowState.Minimized) return;

            int now = Environment.TickCount;
            if (!force && (now - _lastRedrawTick) < RedrawMinIntervalMs)
                return;
            _lastRedrawTick = now;

            lock (_drawLock)
            {
                int w = _splash.Width;
                int h = _splash.Height;

                if (_frame == null || _frame.Width != w || _frame.Height != h)
                {
                    if (_frame != null) _frame.Dispose();
                    _frame = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                }

                using (var g = Graphics.FromImage(_frame))
                {
                    g.Clear(Color.Transparent);
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                    // Full splash with its alpha (soft edges, no underlay)
                    g.DrawImage(_splash, 0, 0, w, h);

                    DrawGameRail(g);

                    // Social icons without rings — plain logos
                    DrawSocialIcon(g, _iconBoosty, _rcBoosty);
                    DrawSocialIcon(g, _iconDiscord, _rcDiscord);

                    DrawStatus(g, w, h);
                    if (ShowCancelButton)
                        DrawEdgeButton(g, _rcCancel, Loc.T("cancel"), 9.5f, muted: false);
                    else if (ShowMoButton)
                        DrawEdgeButton(g, _rcMo, Loc.T("modding"), 9.5f, muted: false);
                    DrawPlayButton(g);
                    DrawTrayButton(g);
                    DrawGearButton(g);
                    DrawCloseButton(g);
                }

                ApplyLayeredBitmap(_frame);
            }
        }

        /// <summary>Draw logo without plate/ring; contain-fit into bounds.</summary>
        private static void DrawSocialIcon(Graphics g, Image icon, Rectangle bounds)
        {
            if (icon == null || bounds.Width <= 0 || bounds.Height <= 0) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = Math.Min(
                (float)bounds.Width / icon.Width,
                (float)bounds.Height / icon.Height);
            int dw = Math.Max(1, (int)Math.Round(icon.Width * scale));
            int dh = Math.Max(1, (int)Math.Round(icon.Height * scale));
            int dx = bounds.X + (bounds.Width - dw) / 2;
            int dy = bounds.Y + (bounds.Height - dh) / 2;
            g.DrawImage(icon, new Rectangle(dx, dy, dw, dh));
        }

        // Edge UI palette — thin gold / dark glass
        private static readonly Color EdgeGold = Color.FromArgb(255, 214, 186, 110);
        private static readonly Color EdgeGoldHi = Color.FromArgb(255, 236, 214, 150);
        private static readonly Color EdgeCream = Color.FromArgb(240, 230, 220, 195);
        private static readonly Color EdgeMute = Color.FromArgb(190, 150, 140, 120);
        private static readonly Color EdgeGlass = Color.FromArgb(200, 12, 12, 14);

        private void DrawStatus(Graphics g, int w, int h)
        {
            using (var fontStatus = new Font("Segoe UI", 7.75f, FontStyle.Regular))
            using (var fontVer = new Font("Segoe UI", 7.25f, FontStyle.Regular))
            using (var brushStatus = new SolidBrush(EdgeCream))
            using (var brushVer = new SolidBrush(EdgeMute))
            {
                string main;
                if (_showProgress)
                    main = string.IsNullOrEmpty(_statusText) ? Loc.T("updating") : _statusText;
                else if (!GameCatalog.Current.Available)
                    main = string.IsNullOrEmpty(_statusText)
                        ? Loc.GameBlurb(GameCatalog.Current)
                        : _statusText;
                else if (_showReady)
                    main = Loc.T("ready");
                else
                    main = _statusText ?? "";

                if (!string.IsNullOrEmpty(main))
                {
                    // Multi-line status (speed + parallel files); bottom-aligned above bar
                    var sf = new StringFormat(StringFormatFlags.LineLimit)
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Far,
                        Trimming = StringTrimming.EllipsisCharacter
                    };
                    using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                    {
                        var shadowArea = _rcStatus;
                        shadowArea.Offset(1, 1);
                        g.DrawString(main, fontStatus, shadow, shadowArea, sf);
                    }
                    g.DrawString(main, fontStatus, brushStatus, _rcStatus, sf);
                }

                if (_showProgress)
                {
                    // Top = current archive; bottom = overall
                    DrawEdgeProgressBar(g, _rcProgressCurrent, _progressCurrent >= 0 ? _progressCurrent : 0);
                    DrawEdgeProgressBar(g, _rcProgressOverall, _progressOverall);
                }

                if (Constants.SHOW_VERSION_TEXT && !string.IsNullOrEmpty(_versionText))
                {
                    // Game + Launcher under the bars
                    var verArea = new Rectangle(
                        _rcProgressOverall.X,
                        Math.Min(h - 16, _rcProgressOverall.Bottom + 2),
                        _rcProgressOverall.Width,
                        16);
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter
                    };
                    g.DrawString(_versionText, fontVer, brushVer, verArea, sf);
                }
            }
        }

        /// <summary>
        /// Edge UI progress: track → chain fill (clipped inside) → rim.
        /// Fill uses the same pointed-capsule geometry as the frame so it cannot stick out.
        /// </summary>
        private void DrawEdgeProgressBar(Graphics g, Rectangle bounds, int percent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingMode = CompositingMode.SourceOver;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            percent = Math.Max(0, Math.Min(100, percent));

            // 1) Dark track
            if (_edgeBarTrack != null)
                g.DrawImage(_edgeBarTrack, bounds);
            else
            {
                using (var path = EdgeBarCapsulePath(bounds, outer: true))
                using (var br = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                    g.FillPath(br, path);
            }

            // 2) Fill inside inner capsule only
            if (percent > 0)
            {
                // Inner channel from SVG ratios (relative to cropped outer content)
                // outer height ~31.4 units, inner y 6.9..24.25 → top 22%, bottom 23%
                float padX = bounds.Width * 0.035f;
                float padY = bounds.Height * 0.22f;
                var innerBounds = RectangleF.FromLTRB(
                    bounds.X + padX,
                    bounds.Y + padY,
                    bounds.Right - padX,
                    bounds.Bottom - padY);

                float fillW = Math.Max(2f, innerBounds.Width * (percent / 100f));
                var fillRect = new RectangleF(innerBounds.X, innerBounds.Y, fillW, innerBounds.Height);

                var state = g.Save();
                using (var innerClip = EdgeBarCapsulePath(Rectangle.Round(innerBounds), outer: false))
                {
                    g.SetClip(innerClip);
                    g.SetClip(Rectangle.Round(fillRect), CombineMode.Intersect);

                    // Soft gold base
                    using (var gold = new LinearGradientBrush(
                        Rectangle.Round(innerBounds),
                        Color.FromArgb(230, 150, 110, 45),
                        Color.FromArgb(240, 220, 180, 100),
                        LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(gold, Rectangle.Round(fillRect));
                    }

                    if (_edgeBarFill != null)
                    {
                        int tileH = Math.Max(6, (int)Math.Round(innerBounds.Height));
                        float scale = tileH / (float)Math.Max(1, _edgeBarFill.Height);
                        int tileW = Math.Max(10, (int)Math.Round(_edgeBarFill.Width * scale));
                        int x0 = (int)Math.Floor(innerBounds.X);
                        int x1 = (int)Math.Ceiling(fillRect.Right);
                        int y0 = (int)Math.Round(innerBounds.Y);

                        for (int x = x0; x < x1; x += Math.Max(1, tileW - 1))
                        {
                            int drawW = Math.Min(tileW, x1 - x);
                            if (drawW <= 0) break;
                            float srcW = _edgeBarFill.Width * (drawW / (float)tileW);
                            g.DrawImage(
                                _edgeBarFill,
                                new Rectangle(x, y0, drawW, tileH),
                                new RectangleF(0, 0, srcW, _edgeBarFill.Height),
                                GraphicsUnit.Pixel);
                        }
                    }
                }
                g.Restore(state);
            }

            // 3) Rim on top
            if (_edgeBarFrame != null)
                g.DrawImage(_edgeBarFrame, bounds);
            else
            {
                using (var path = EdgeBarCapsulePath(bounds, outer: true))
                using (var pen = new Pen(Color.FromArgb(200, 210, 180, 110), 1.5f))
                    g.DrawPath(pen, path);
            }
        }

        /// <summary>Pointed capsule. outer=true for frame silhouette; false = tighter inner channel.</summary>
        private static GraphicsPath EdgeBarCapsulePath(Rectangle r, bool outer)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            float midY = y + h * 0.5f;
            // Outer tips are sharper; inner channel less pointed so fill stays clear of ends
            float tip = outer
                ? Math.Min(h * 0.9f, w * 0.055f)
                : Math.Min(h * 0.55f, w * 0.04f);
            float insetY = outer ? h * 0.06f : h * 0.08f;

            var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(x + tip, y + insetY),
                new PointF(x + w - tip, y + insetY),
                new PointF(x + w, midY),
                new PointF(x + w - tip, y + h - insetY),
                new PointF(x + tip, y + h - insetY),
                new PointF(x, midY)
            });
            return path;
        }

        /// <summary>Edge-style glass button: dark plate, thin gold line, cream label.</summary>
        private void DrawPlayButton(Graphics g)
        {
            string label = GameCatalog.Current.Available ? _playText : Loc.T("soon");
            DrawEdgeButton(g, _rcPlay, label, 12f, muted: _busy || !GameCatalog.Current.Available);
        }

        private void DrawGameRail(Graphics g)
        {
            if (_rcGames == null || _rcGames.Length == 0) return;
            // Icons are pre-sized to RailIconPx — draw 1:1, no second bicubic pass.
            var prevInterp = g.InterpolationMode;
            var prevSmooth = g.SmoothingMode;
            var prevPixel = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using (var font = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            using (var brushMute = new SolidBrush(EdgeMute))
            using (var brushSel = new SolidBrush(Color.FromArgb(220, 214, 186, 110)))
            {
                for (int i = 0; i < GameCatalog.Games.Count && i < _rcGames.Length; i++)
                {
                    var game = GameCatalog.Games[i];
                    var rc = _rcGames[i];
                    bool sel = string.Equals(game.Id, GameCatalog.Current.Id, StringComparison.OrdinalIgnoreCase);
                    // Same as Discord/Boosty: plain image, no plate/frame
                    var iconRc = new Rectangle(rc.X, rc.Y, rc.Width, rc.Width);

                    Bitmap icon;
                    if (!_gameIcons.TryGetValue(game.Id, out icon) || icon == null)
                        icon = null;

                    if (icon != null)
                    {
                        // Dim only via alpha — do not shrink unselected icons (that re-blurred them).
                        float dim = (!game.Available) ? 0.45f : (sel ? 1f : 0.88f);
                        DrawRailIcon(g, icon, iconRc, dim);
                    }

                    if (sel)
                    {
                        int uy = iconRc.Bottom - 1;
                        using (var pen = new Pen(EdgeGold, 2f))
                            g.DrawLine(pen, iconRc.X + 6, uy, iconRc.Right - 6, uy);
                    }

                    var labelRc = new Rectangle(rc.X - 4, iconRc.Bottom + 1, rc.Width + 8, 14);
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Near,
                        Trimming = StringTrimming.EllipsisCharacter
                    };
                    g.DrawString(game.ShortLabel ?? game.Title, font, sel ? brushSel : brushMute, labelRc, sf);
                }
            }

            g.InterpolationMode = prevInterp;
            g.SmoothingMode = prevSmooth;
            g.PixelOffsetMode = prevPixel;
        }

        /// <summary>Plain icon 1:1 into bounds (pre-sized bitmaps). Alpha for dimmed state only.</summary>
        private static void DrawRailIcon(Graphics g, Image icon, Rectangle bounds, float alpha)
        {
            if (icon == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            alpha = Math.Max(0.05f, Math.Min(1f, alpha));

            // Center without resampling when icon already matches tile (or fits inside).
            int dw = Math.Min(icon.Width, bounds.Width);
            int dh = Math.Min(icon.Height, bounds.Height);
            // Prefer exact match — avoid fractional scale that blurs.
            if (icon.Width == bounds.Width && icon.Height == bounds.Height)
            {
                dw = bounds.Width;
                dh = bounds.Height;
            }
            else if (icon.Width > bounds.Width || icon.Height > bounds.Height)
            {
                // Unexpected: only then downscale once with HQ
                float scale = Math.Min((float)bounds.Width / icon.Width, (float)bounds.Height / icon.Height);
                dw = Math.Max(1, (int)Math.Round(icon.Width * scale));
                dh = Math.Max(1, (int)Math.Round(icon.Height * scale));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            }
            else
            {
                // Smaller icon: pixel-perfect center, no stretch
                dw = icon.Width;
                dh = icon.Height;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
            }

            int dx = bounds.X + (bounds.Width - dw) / 2;
            int dy = bounds.Y + (bounds.Height - dh) / 2;
            var dest = new Rectangle(dx, dy, dw, dh);

            if (icon.Width == dw && icon.Height == dh)
                g.InterpolationMode = InterpolationMode.NearestNeighbor;

            if (alpha >= 0.99f)
            {
                g.DrawImage(icon, dest, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel);
                return;
            }
            var cm = new ColorMatrix { Matrix33 = alpha };
            using (var ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(icon, dest, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private void DrawEdgeButton(Graphics g, Rectangle r, string label, float fontSize, bool muted)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var shadow = r;
            shadow.Offset(1, 2);
            using (var pathSh = RoundedRect(shadow, 3))
            using (var brSh = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
                g.FillPath(brSh, pathSh);

            Color glass = muted
                ? Color.FromArgb(160, 20, 20, 24)
                : EdgeGlass;
            Color gold = muted
                ? Color.FromArgb(160, 140, 120, 80)
                : EdgeGold;
            Color goldHi = muted
                ? Color.FromArgb(180, 180, 160, 110)
                : EdgeGoldHi;

            using (var path = RoundedRect(r, 3))
            using (var body = new SolidBrush(glass))
            using (var penOuter = new Pen(gold, 1.4f))
            using (var penInner = new Pen(Color.FromArgb(100, 255, 230, 170), 1f))
            {
                g.FillPath(body, path);
                g.DrawPath(penOuter, path);
                var inner = Rectangle.Inflate(r, -3, -3);
                using (var pathIn = RoundedRect(inner, 2))
                    g.DrawPath(penInner, pathIn);
            }

            using (var pen = new Pen(goldHi, 1.2f))
            {
                int c = 7;
                g.DrawLines(pen, new[] { new Point(r.Left + 3, r.Top + c), new Point(r.Left + 3, r.Top + 3), new Point(r.Left + c, r.Top + 3) });
                g.DrawLines(pen, new[] { new Point(r.Right - 3, r.Top + c), new Point(r.Right - 3, r.Top + 3), new Point(r.Right - c, r.Top + 3) });
                g.DrawLines(pen, new[] { new Point(r.Left + 3, r.Bottom - c), new Point(r.Left + 3, r.Bottom - 3), new Point(r.Left + c, r.Bottom - 3) });
                g.DrawLines(pen, new[] { new Point(r.Right - 3, r.Bottom - c), new Point(r.Right - 3, r.Bottom - 3), new Point(r.Right - c, r.Bottom - 3) });
            }

            using (var font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold))
            using (var text = new SolidBrush(goldHi))
            using (var textShadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            {
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                var tr = r;
                tr.Offset(1, 1);
                g.DrawString(label, font, textShadow, tr, sf);
                g.DrawString(label, font, text, r, sf);
            }
        }

        private void DrawChromePlate(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Opaque disc so splash does not show through button.png alpha
            float pad = r.Width * 0.08f;
            using (var plate = new SolidBrush(Color.FromArgb(255, 16, 14, 18)))
                g.FillEllipse(plate, r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2);
            if (_iconChrome != null)
                g.DrawImage(_iconChrome, r);
        }

        private static readonly Color ChromeGlyph = Color.FromArgb(235, 228, 214, 186);

        private void DrawTrayButton(Graphics g)
        {
            DrawChromePlate(g, _rcTray);
            using (var pen = new Pen(ChromeGlyph, 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                int inset = 10;
                int y = _rcTray.Y + _rcTray.Height * 18 / 32;
                g.DrawLine(pen, _rcTray.Left + inset, y, _rcTray.Right - inset, y);
            }
        }

        private void DrawGearButton(Graphics g)
        {
            DrawChromePlate(g, _rcGear);
            float cx = _rcGear.X + _rcGear.Width / 2f;
            float cy = _rcGear.Y + _rcGear.Height / 2f;
            using (var br = new SolidBrush(ChromeGlyph))
            using (var pen = new Pen(ChromeGlyph, 1.7f))
            {
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                for (int i = 0; i < 6; i++)
                {
                    g.RotateTransform(60f);
                    g.FillRectangle(br, -1.7f, -8.6f, 3.4f, 3.6f);
                }
                g.Restore(state);
                g.DrawEllipse(pen, cx - 4.4f, cy - 4.4f, 8.8f, 8.8f);
            }
        }

        private void DrawCloseButton(Graphics g)
        {
            DrawChromePlate(g, _rcClose);
            using (var pen = new Pen(ChromeGlyph, 2.1f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            })
            {
                int m = 10;
                g.DrawLine(pen, _rcClose.Left + m, _rcClose.Top + m, _rcClose.Right - m, _rcClose.Bottom - m);
                g.DrawLine(pen, _rcClose.Right - m, _rcClose.Top + m, _rcClose.Left + m, _rcClose.Bottom - m);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyLayeredBitmap(Bitmap bitmap)
        {
            // UpdateLayeredWindow needs premultiplied alpha (PArgb).
            // Reuse buffer — recreating 514×800 every progress tick caused white freezes while dragging.
            if (_premult == null || _premult.Width != bitmap.Width || _premult.Height != bitmap.Height)
            {
                if (_premult != null) _premult.Dispose();
                _premult = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb);
            }

            using (var g = Graphics.FromImage(_premult))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
            }

            IntPtr screenDc = NativeWinAPI.GetDC(IntPtr.Zero);
            IntPtr memDc = NativeWinAPI.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                hBitmap = _premult.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeWinAPI.SelectObject(memDc, hBitmap);

                var size = new NativeWinAPI.SIZE(_premult.Width, _premult.Height);
                var pointSource = new NativeWinAPI.POINT(0, 0);
                // Pass null destination position so Windows keeps current window pos
                // (avoids fighting with OS drag / SetWindowPos).
                var blend = new NativeWinAPI.BLENDFUNCTION
                {
                    BlendOp = NativeWinAPI.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeWinAPI.AC_SRC_ALPHA
                };

                // pptDst = null → don't change position; only refresh pixels
                NativeWinAPI.UpdateLayeredWindow(
                    Handle, screenDc, IntPtr.Zero, ref size,
                    memDc, ref pointSource, 0, ref blend, NativeWinAPI.ULW_ALPHA);
            }
            finally
            {
                NativeWinAPI.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    NativeWinAPI.SelectObject(memDc, oldBitmap);
                    NativeWinAPI.DeleteObject(hBitmap);
                }
                NativeWinAPI.DeleteDC(memDc);
            }
        }

        // ---------- Input ----------

        private void OnFormMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Point p = e.Location;

            if (_rcClose.Contains(p))
            {
                Environment.Exit(0);
                return;
            }
            if (_rcGear.Contains(p))
            {
                OnClickSettings();
                return;
            }
            if (_rcTray.Contains(p))
            {
                MinimizeToTray();
                return;
            }
            if (!_busy && _rcGames != null)
            {
                for (int i = 0; i < _rcGames.Length && i < GameCatalog.Games.Count; i++)
                {
                    if (_rcGames[i].Contains(p))
                    {
                        GameCatalog.Select(GameCatalog.Games[i]);
                        return;
                    }
                }
            }
            if (_rcPlay.Contains(p))
            {
                if (!_busy) OnClickPlay();
                return;
            }
            if (ShowCancelButton && _rcCancel.Contains(p))
            {
                OnClickCancel();
                return;
            }
            if (ShowMoButton && _rcMo.Contains(p))
            {
                OnClickModding();
                return;
            }
            if (_rcDiscord.Contains(p))
            {
                OpenUrl(Constants.DISCORD_URL);
                return;
            }
            if (_rcBoosty.Contains(p))
            {
                OpenUrl(Constants.BOOSTY_URL);
                return;
            }
            // Native caption drag — keeps layered content intact (no white flash).
            NativeWinAPI.ReleaseCapture();
            NativeWinAPI.SendMessage(Handle, NativeWinAPI.WM_NCLBUTTONDOWN, NativeWinAPI.HT_CAPTION, 0);
        }

        private void OnFormMouseMove(object sender, MouseEventArgs e) { }

        private void OnFormMouseUp(object sender, MouseEventArgs e) { }

        private void OnClickSettings()
        {
            var action = SettingsForm.ShowSettings(this, _busy);
            switch (action)
            {
                case SettingsAction.Repair:
                    OnClickRepair();
                    break;
                case SettingsAction.GetVr:
                case SettingsAction.RemoveVr:
                    OnClickVrChannel();
                    break;
                case SettingsAction.BugReport:
                    OnClickBugReport();
                    break;
                case SettingsAction.Uninstall:
                    OnClickUninstall();
                    break;
            }
        }

        private void InitTray()
        {
            _trayMenu = new ContextMenuStrip();
            _tray = new NotifyIcon
            {
                Visible = false,
                Text = Loc.T("tray_tip")
            };
            try { _tray.Icon = Icon; } catch { }
            if (_tray.Icon == null)
            {
                try { _tray.Icon = SystemIcons.Application; } catch { }
            }
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            RebuildTrayMenu();
            _tray.ContextMenuStrip = _trayMenu;
        }

        private void RebuildTrayMenu()
        {
            if (_trayMenu == null) return;
            _trayMenu.Items.Clear();
            _trayMenu.Items.Add(Loc.T("tray_open"), null, (s, e) => RestoreFromTray());
            _trayMenu.Items.Add(Loc.T("tray_exit"), null, (s, e) => Environment.Exit(0));
            if (_tray != null) _tray.Text = Loc.T("tray_tip");
        }

        private void MinimizeToTray()
        {
            if (_tray == null) return;
            _tray.Visible = true;
            ShowInTaskbar = false;
            Hide();
        }

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            if (_tray != null) _tray.Visible = false;
            Activate();
            RedrawLayered(force: true);
        }

        private void OnClickUninstall()
        {
            if (_busy)
            {
                MessageBox.Show(Loc.T("uninstall_busy"), Loc.T("uninstall"));
                return;
            }
            if (!PackageInstaller.IsInstalled())
            {
                MessageBox.Show(Loc.T("need_install"), Loc.T("uninstall"));
                return;
            }
            var r = MessageBox.Show(
                Loc.F("uninstall_ask", GameCatalog.Current.Title, GameCatalog.InstallRoot),
                Loc.T("uninstall"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            try
            {
                PackageInstaller.UninstallCurrentGame();
                _isInstalled = false;
                RefreshPlayState();
                RefreshVersionText();
                _statusText = Loc.T("ready_install");
                RedrawLayered(force: true);
            }
            catch (Exception ex)
            {
                LauncherLog.Error("uninstall", ex);
                MessageBox.Show(FlappyException.FormatForUser(ex), Loc.T("fail_uninstall"));
            }
        }

        private void OnClickBugReport()
        {
            if (_busy) return;
            if (!PackageInstaller.IsInstalled())
            {
                MessageBox.Show(Loc.T("need_install"), Loc.T("bugreport"));
                return;
            }
            _busy = true;
            RefreshPlayState();
            _showProgress = true;
            _showReady = false;
            _statusText = Loc.T("bugreport_wait");
            _progressOverall = 0;
            _progressCurrent = -1;
            if (_jobCts != null) { try { _jobCts.Dispose(); } catch { } }
            _jobCts = new CancellationTokenSource();
            var token = _jobCts.Token;
            RedrawLayered(force: true);

            var bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.DoWork += (o, args) =>
            {
                var worker = (BackgroundWorker)o;
                args.Result = BugReportPacker.Run(
                    (overall, current, msg) => worker.ReportProgress(0, UiProgress.Make(overall, current, msg)),
                    token);
            };
            bw.ProgressChanged += (o, args) =>
            {
                ApplyUiProgress(args.UserState);
                RedrawLayered(force: false);
            };
            bw.RunWorkerCompleted += (o, args) =>
            {
                _busy = false;
                RefreshPlayState();
                if (args.Error != null)
                {
                    MessageBox.Show(FlappyException.FormatForUser(args.Error), Loc.T("fail_bugreport"));
                    IsReady = PackageInstaller.IsInstalled();
                    return;
                }
                string archive = args.Result as string;
                _statusText = Loc.T("ready");
                IsReady = PackageInstaller.IsInstalled();
                if (!string.IsNullOrEmpty(archive))
                {
                    MessageBox.Show(
                        Loc.F("bugreport_done", archive) + Environment.NewLine + Environment.NewLine + Loc.T("bugreport_open_folder"),
                        Loc.T("bugreport_done_title"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    BugReportPacker.Reveal(archive);
                }
            };
            bw.RunWorkerAsync();
        }

        private void OnClickPlay()
        {
            if (!GameCatalog.Current.Available)
            {
                MessageBox.Show(
                    Loc.F("coming_soon_named", GameCatalog.Current.Title),
                    GameCatalog.Current.Title);
                return;
            }
            RefreshPlayState();
            if (_playState == PlayButtonState.Play)
                LaunchGamePackageMode();
            else
                StartPackageInstallAsync();
        }

        private void OnClickCancel()
        {
            try
            {
                if (_jobCts != null && !_jobCts.IsCancellationRequested)
                {
                    _jobCts.Cancel();
                    _statusText = Loc.T("cancelling");
                    RedrawLayered(force: true);
                    LauncherLog.Info("User cancelled job");
                }
            }
            catch { }
        }

        private void OnClickModding()
        {
            if (_busy || !PackageInstaller.IsInstalled()) return;
            try
            {
                string last = PackageInstaller.GetSavedMode();
                string mode = ModeSelectForm.ShowSelect(this, last);
                if (string.IsNullOrEmpty(mode))
                    return;
                // Open full MO2 UI for the chosen profile (no auto-start game)
                PackageInstaller.LaunchModOrganizer(mode);
            }
            catch (Exception ex)
            {
                LauncherLog.Error("modding", ex);
                MessageBox.Show(FlappyException.FormatForUser(ex), Loc.T("mo"));
            }
        }

        private void OnClickVrChannel()
        {
            if (_busy || !PackageInstaller.IsInstalled()) return;

            if (PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly)
            {
                var r = MessageBox.Show(
                    Loc.T("get_vr_ask"),
                    Loc.T("get_vr"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;

                StartPackageJob(
                    Loc.T("get_vr_status"),
                    (index, report, cancel) => PackageInstaller.InstallVrChannel(index, report, cancel),
                    Loc.T("fail_get_vr"),
                    launchAfter: false);
            }
            else
            {
                var r = MessageBox.Show(
                    Loc.T("remove_vr_ask"),
                    Loc.T("remove_vr"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                StartPackageJob(
                    Loc.T("remove_vr_status"),
                    (index, report, cancel) => PackageInstaller.RemoveVrChannel(index, report, cancel),
                    Loc.T("fail_remove_vr"),
                    launchAfter: false);
            }
        }

        private void OnClickRepair()
        {
            if (_busy || !PackageInstaller.IsInstalled()) return;

            var r = MessageBox.Show(
                Loc.T("repair_ask"),
                Loc.T("repair"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            StartRepairAsync();
        }

        private InstallChannel ResolveChannelForJob(bool allowPrompt)
        {
            if (PackageInstaller.IsInstalled())
                return PackageInstaller.GetSavedChannel();

            if (!allowPrompt)
                return InstallChannel.AeAndVr;

            var ch = ChannelSelectForm.ShowSelect(this);
            if (ch == null)
                throw new OperationCanceledException();
            return ch.Value;
        }

        private void StartPackageInstallAsync()
        {
            InstallChannel channel;
            try { channel = ResolveChannelForJob(allowPrompt: true); }
            catch (OperationCanceledException) { return; }

            StartPackageJob(
                channel == InstallChannel.AeOnly ? Loc.T("preparing_ae") : Loc.T("preparing_vr"),
                (index, report, cancel) => PackageInstaller.InstallAll(index, report, cancel, channel),
                Loc.T("fail_install"),
                launchAfter: Constants.AUTOMATICALLY_LAUNCH_GAME_AFTER_UPDATING);
        }

        private void StartRepairAsync()
        {
            if (_busy) return;
            var channel = PackageInstaller.GetSavedChannel();

            _busy = true;
            RefreshPlayState();
            _showProgress = true;
            _showReady = false;
            _statusText = Loc.T("scan_fp");
            _progressOverall = 0;
            _progressCurrent = -1;
            if (_jobCts != null) { try { _jobCts.Dispose(); } catch { } }
            _jobCts = new CancellationTokenSource();
            var token = _jobCts.Token;
            RedrawLayered(force: true);

            var bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.DoWork += (o, args) =>
            {
                var worker = (BackgroundWorker)o;
                var index = _cachedIndex ?? PackageInstaller.FetchIndex();
                _cachedIndex = index;
                var need = PackageInstaller.FindUnitsNeedingRepair(
                    index,
                    channel,
                    msg => worker.ReportProgress(0, UiProgress.Make(1, -1, Loc.F("checking_n", msg))),
                    token);
                PackageInstaller.WriteRepairReport(need,
                    PackageInstaller.FilterByChannel(index.units, channel),
                    "repair scan");
                args.Result = need;
            };
            bw.ProgressChanged += (o, args) =>
            {
                ApplyUiProgress(args.UserState);
                RedrawLayered(force: false);
            };
            bw.RunWorkerCompleted += (o, args) =>
            {
                if (args.Error != null)
                {
                    _busy = false;
                    RefreshPlayState();
                    MessageBox.Show(FlappyException.FormatForUser(args.Error), Loc.T("fail_repair"));
                    IsReady = PackageInstaller.IsInstalled();
                    return;
                }

                var need = args.Result as System.Collections.Generic.List<PackageUnit>
                           ?? new System.Collections.Generic.List<PackageUnit>();

                if (need.Count == 0)
                {
                    _busy = false;
                    _statusText = Loc.T("repair_not_needed");
                    _progressOverall = 100;
                    _progressCurrent = -1;
                    IsReady = true;
                    MessageBox.Show(
                        Loc.F("repair_ok", GameCatalog.Current.Id),
                        Loc.T("repair"));
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine(Loc.F("repair_to_dl", need.Count));
                sb.AppendLine(Loc.T("settings_hint").Contains("{1}")
                    ? Loc.F("settings_hint", GameCatalog.Current.Title,
                        channel == InstallChannel.AeOnly ? Loc.T("channel_ae_short") : Loc.T("channel_full_short"))
                    : "");
                sb.AppendLine();
                long bytes = 0;
                int show = Math.Min(15, need.Count);
                for (int i = 0; i < show; i++)
                {
                    var u = need[i];
                    sb.AppendLine("• " + PackageInstaller.DisplayName(u));
                    bytes += Math.Max(0, u.packageSize);
                }
                if (need.Count > show)
                    sb.AppendLine(Loc.F("repair_more", need.Count - show));
                if (bytes > 0)
                    sb.AppendLine().AppendLine(Loc.F("repair_approx", FormatApproxSize(bytes)));
                sb.AppendLine().AppendLine(Loc.F("repair_details", GameCatalog.Current.Id));
                sb.AppendLine().Append(Loc.T("repair_go"));

                var r = MessageBox.Show(sb.ToString(), Loc.T("repair"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    _busy = false;
                    _statusText = Loc.T("ready");
                    RefreshPlayState();
                    RedrawLayered(force: true);
                    return;
                }

                _statusText = Loc.F("repairing", need.Count);
                _progressOverall = 0;
                _progressCurrent = -1;
                RedrawLayered(force: true);

                if (_jobCts != null) { try { _jobCts.Dispose(); } catch { } }
                _jobCts = new CancellationTokenSource();
                var token2 = _jobCts.Token;

                var bw2 = new BackgroundWorker();
                bw2.WorkerReportsProgress = true;
                bw2.DoWork += (o2, a2) =>
                {
                    var worker2 = (BackgroundWorker)o2;
                    PackageInstaller.RepairAll(
                        _cachedIndex,
                        (overall, current, msg) => worker2.ReportProgress(0, UiProgress.Make(overall, current, msg)),
                        token2,
                        channel);
                };
                bw2.ProgressChanged += (o2, a2) =>
                {
                    ApplyUiProgress(a2.UserState);
                    RedrawLayered(force: false);
                };
                bw2.RunWorkerCompleted += (o2, a2) =>
                {
                    _busy = false;
                    _isInstalled = PackageInstaller.IsInstalled();
                    if (a2.Error != null)
                    {
                        MessageBox.Show(FlappyException.FormatForUser(a2.Error), Loc.T("fail_repair"));
                        IsReady = _isInstalled;
                        return;
                    }
                    _statusText = Loc.T("ready");
                    RefreshVersionText();
                    IsReady = true;
                };
                bw2.RunWorkerAsync();
            };
            bw.RunWorkerAsync();
        }

        private static string FormatApproxSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " GB";
            if (bytes >= 1024L * 1024)
                return (bytes / (1024.0 * 1024)).ToString("0.0") + " MB";
            return (bytes / 1024.0).ToString("0") + " KB";
        }

        private sealed class UiProgress
        {
            public int Overall;
            public int Current;
            public string Text;
            public static UiProgress Make(int overall, int current, string text)
            {
                return new UiProgress
                {
                    Overall = Math.Max(0, Math.Min(100, overall)),
                    Current = current,
                    Text = text
                };
            }
        }

        private void ApplyUiProgress(object userState)
        {
            var p = userState as UiProgress;
            if (p != null)
            {
                _progressOverall = p.Overall;
                _progressCurrent = p.Current;
                if (!string.IsNullOrEmpty(p.Text)) _statusText = p.Text;
                return;
            }
            if (userState is string s && !string.IsNullOrEmpty(s))
                _statusText = s;
        }

        private void StartPackageJob(
            string startStatus,
            Action<PackageInstaller.Index, PackageInstaller.ProgressHandler, CancellationToken> work,
            string errorTitle,
            bool launchAfter)
        {
            if (_busy) return;
            if (!GameCatalog.Current.Available)
            {
                MessageBox.Show(Loc.GameBlurb(GameCatalog.Current), GameCatalog.Current.Title);
                return;
            }
            _busy = true;
            RefreshPlayState();
            _showProgress = true;
            _showReady = false;
            _statusText = startStatus;
            _progressOverall = 0;
            _progressCurrent = -1;
            if (_jobCts != null) { try { _jobCts.Dispose(); } catch { } }
            _jobCts = new CancellationTokenSource();
            var token = _jobCts.Token;
            RedrawLayered(force: true);

            var bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.DoWork += (o, args) =>
            {
                var index = _cachedIndex ?? PackageInstaller.FetchIndex();
                _cachedIndex = index;
                var worker = (BackgroundWorker)o;
                work(
                    index,
                    (overall, current, msg) => worker.ReportProgress(0, UiProgress.Make(overall, current, msg)),
                    token);
            };
            bw.ProgressChanged += (o, args) =>
            {
                ApplyUiProgress(args.UserState);
                RedrawLayered(force: false);
            };
            bw.RunWorkerCompleted += (o, args) =>
            {
                _busy = false;
                _isInstalled = PackageInstaller.IsInstalled();
                if (args.Error != null)
                {
                    MessageBox.Show(FlappyException.FormatForUser(args.Error), errorTitle);
                    IsReady = _isInstalled;
                    return;
                }
                _statusText = Loc.T("ready");
                _progressCurrent = -1;
                if (_cachedIndex != null)
                    OnlineVersion = PackageInstaller.ParseVersion(_cachedIndex.version);
                RefreshVersionText();
                IsReady = true;
                if (launchAfter && Constants.AUTOMATICALLY_LAUNCH_GAME_AFTER_UPDATING)
                    LaunchGamePackageMode();
            };
            bw.RunWorkerAsync();
        }

        private void LaunchGamePackageMode()
        {
            try
            {
                string last = PackageInstaller.GetSavedMode();
                string mode = ModeSelectForm.ShowSelect(this, last);
                if (string.IsNullOrEmpty(mode))
                    return;
                PackageInstaller.LaunchGame(mode);
            }
            catch (Exception ex)
            {
                LauncherLog.Error("launch", ex);
                MessageBox.Show(FlappyException.FormatForUser(ex), Loc.T("launch_failed"));
                IsReady = false;
            }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("link_failed", ex.Message), Constants.LAUNCHER_NAME);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // After un-minimize the layered surface must be pushed again.
            if (WindowState == FormWindowState.Normal && IsHandleCreated && _splash != null)
                RedrawLayered(force: true);
        }
    }
}
