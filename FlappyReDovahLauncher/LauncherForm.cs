using System;
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
        private Bitmap _edgeBarFill;
        private Bitmap _edgeBarFrame;
        private Bitmap _edgeBarTrack;
        private Bitmap _frame;
        private Rectangle _rcProgress;
        private Rectangle _rcStatus;

        private PackageInstaller.Index _cachedIndex;
        public Version OnlineVersion { get; private set; }

        private bool _isReady;
        private bool _isInstalled;
        private bool _busy;
        private PlayButtonState _playState = PlayButtonState.Install;
        private string _playText = "Install";
        private string _versionText = "-";
        private string _statusText = "";
        private int _progressPct;
        private bool _showProgress;
        private bool _showReady = true;

        private Rectangle _rcPlay;
        private Rectangle _rcRepair;
        private Rectangle _rcMo;
        private Rectangle _rcVrChannel; // Get VR (AE-only) or Remove VR (AE+VR)
        private Rectangle _rcCancel;
        private Rectangle _rcDiscord;
        private Rectangle _rcBoosty;
        private Rectangle _rcClose;

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
                    _statusText = "Ready to play";
                    _progressPct = 0;
                    _showProgress = false;
                    _showReady = true;
                }
                RedrawLayered(force: true);
            }
        }

        private bool ShowRepairButton
        {
            get { return _isInstalled && !_busy; }
        }

        private bool ShowMoButton
        {
            get { return _isInstalled && !_busy; }
        }

        /// <summary>Get VR when AE-only; Remove VR when full channel.</summary>
        private bool ShowVrChannelButton
        {
            get { return _isInstalled && !_busy; }
        }

        private string VrChannelButtonText
        {
            get
            {
                return PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly
                    ? "Get VR"
                    : "Remove VR";
            }
        }

        private bool ShowCancelButton
        {
            get { return _busy; }
        }

        private void RefreshPlayState()
        {
            _isInstalled = PackageInstaller.IsInstalled();
            var local = PackageInstaller.GetLocalVersion();
            _playState = PackageInstaller.ResolvePlayState(_isInstalled, local, OnlineVersion, _busy);
            switch (_playState)
            {
                case PlayButtonState.Install: _playText = "Install"; break;
                case PlayButtonState.Update: _playText = "Update"; break;
                case PlayButtonState.Busy: _playText = "…"; break;
                default: _playText = "Play"; break;
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
            LayoutHitRects();
        }

        private void LoadAssets()
        {
            _splash = new Bitmap(Properties.Resources.bg_LBackground);
            if (_splash.PixelFormat != PixelFormat.Format32bppArgb)
            {
                var converted = new Bitmap(_splash.Width, _splash.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(converted))
                    g.DrawImage(_splash, 0, 0, _splash.Width, _splash.Height);
                _splash.Dispose();
                _splash = converted;
            }

            _iconDiscord = new Bitmap(Properties.Resources.icon_discord);
            // Boosty logo is a hard square — soften with rounded corners + edge fade
            using (var rawBoosty = new Bitmap(Properties.Resources.icon_boosty))
                _iconBoosty = MakeSoftRoundedIcon(rawBoosty, cornerRadius: 0, feather: 0); // radius/feather auto from size

            try { _iconClose = new Bitmap(Properties.Resources.cross); }
            catch
            {
                _iconClose = LoadBitmapResource("cross", "cross.png");
            }

            _edgeBarFill = LoadBitmapResource("edge_bar_fill", "edge_bar_fill.png");
            // Crop empty padding — SVG content sits in top ~33px of 58px canvas
            _edgeBarFrame = CropToOpaque(LoadBitmapResource("edge_bar_frame", "edge_bar_frame.png"));
            _edgeBarTrack = CropToOpaque(LoadBitmapResource("edge_bar_track", "edge_bar_track.png"));

            ClientSize = new Size(_splash.Width, _splash.Height);
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
                if (!File.Exists(path))
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", fileName);
                if (File.Exists(path))
                    return new Bitmap(path);
            }
            catch { }
            return null;
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

            // Close only (no minimize) — top-right
            const int closeSize = 28;
            _rcClose = new Rectangle(w - 12 - closeSize, 12, closeSize, closeSize);

            // Social logos — bottom-left, stacked, no ring
            const int iconSize = 52;
            const int iconPad = 14;
            const int iconGap = 12;
            _rcDiscord = new Rectangle(iconPad, h - iconPad - iconSize, iconSize, iconSize);
            _rcBoosty = new Rectangle(iconPad, h - iconPad - iconSize * 2 - iconGap, iconSize, iconSize);

            // Footer (right column), bottom → top:
            //   [ Play ]
            //   [ Repair ]
            //   [ Modding ]  (MO2 UI)
            //   [ Get VR / Remove VR ]
            //   [ Cancel ] when busy
            const int playW = 128;
            const int playH = 44;
            const int smallH = 32;
            const int gap = 8;
            int leftCol = iconPad + iconSize + gap;
            _rcPlay = new Rectangle(w - gap - playW, h - 14 - playH, playW, playH);
            _rcRepair = new Rectangle(_rcPlay.X, _rcPlay.Top - gap - smallH, playW, smallH);
            _rcMo = new Rectangle(_rcPlay.X, _rcRepair.Top - gap - smallH, playW, smallH);
            _rcVrChannel = new Rectangle(_rcPlay.X, _rcMo.Top - gap - smallH, playW, smallH);
            _rcCancel = new Rectangle(_rcPlay.X, _rcVrChannel.Top, playW, smallH);

            int barRight = _rcPlay.Left - gap;
            int barW = Math.Max(120, barRight - leftCol);
            int barH = Math.Max(16, (int)(barW * 33.0 / 527.0));
            // Align progress with Play (not under Repair)
            _rcProgress = new Rectangle(
                leftCol,
                _rcPlay.Top + (_rcPlay.Height - barH) / 2,
                barW,
                barH);

            const int statusH = 64;
            _rcStatus = new Rectangle(
                leftCol,
                Math.Max(8, _rcProgress.Top - 6 - statusH),
                barW,
                statusH);
        }

        private void OnLoadApplication(object sender, EventArgs e)
        {
            Name = Constants.GAME_TITLE;
            Text = Constants.LAUNCHER_NAME;
            try { LoadApplicationIcon(); } catch { }

            RedrawLayered(force: true);
            InitializePackageMode();
        }

        private void InitializePackageMode()
        {
            try
            {
                LauncherLog.Info("Launcher start v" + FormatShortVersion(LauncherSelfUpdate.GetLocalVersion()));
                _cachedIndex = PackageInstaller.FetchIndex();
                OnlineVersion = PackageInstaller.ParseVersion(_cachedIndex.version);
                var local = PackageInstaller.GetLocalVersion();
                RefreshVersionText();

                _isInstalled = PackageInstaller.IsInstalled();
                bool upToDate = _isInstalled && local >= OnlineVersion;
                IsReady = upToDate;
                RefreshPlayState();

                // Torrent/offline bundle: start unpack immediately (packages already on disk).
                // Online CDN: only if AUTOMATICALLY_BEGIN_UPDATING is enabled.
                bool autoInstall = !upToDate && (
                    Constants.AUTOMATICALLY_BEGIN_UPDATING ||
                    Constants.IsLocalPackagesMode);
                if (autoInstall)
                {
                    LauncherLog.Info(Constants.IsLocalPackagesMode
                        ? "Local bundle detected — auto Install/Update"
                        : "Auto-update enabled — starting");
                    StartPackageInstallAsync();
                }
                else
                    RedrawLayered(force: true);
            }
            catch (Exception ex)
            {
                LauncherLog.Error("index", ex);
                RefreshVersionText(indexError: true);
                _isInstalled = PackageInstaller.IsInstalled();
                IsReady = _isInstalled;
                if (!IsReady)
                    MessageBox.Show(FlappyException.FormatForUser(ex), "Flappy Re-Dovah");
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
                _versionText = "Game —  ·  Launcher " + launcher;
                return;
            }

            var localGame = PackageInstaller.GetLocalVersion();
            bool installed = PackageInstaller.IsInstalled();
            string gamePart;
            if (!installed || localGame == null || localGame == new Version(0, 0, 0, 0))
            {
                gamePart = OnlineVersion != null
                    ? "Game " + FormatShortVersion(OnlineVersion) + " (not installed)"
                    : "Game —";
            }
            else if (OnlineVersion != null && localGame != OnlineVersion)
            {
                gamePart = "Game " + FormatShortVersion(localGame) + " → " + FormatShortVersion(OnlineVersion);
            }
            else
            {
                gamePart = "Game " + FormatShortVersion(localGame);
            }

            _versionText = gamePart + "  ·  Launcher " + launcher;
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

                    // Social icons without rings — plain logos
                    DrawSocialIcon(g, _iconBoosty, _rcBoosty);
                    DrawSocialIcon(g, _iconDiscord, _rcDiscord);

                    DrawStatus(g, w, h);
                    if (ShowCancelButton)
                        DrawEdgeButton(g, _rcCancel, "Cancel", 9.5f, muted: false);
                    else
                    {
                        if (ShowVrChannelButton)
                            DrawEdgeButton(g, _rcVrChannel, VrChannelButtonText, 9.5f, muted: false);
                        if (ShowMoButton)
                            DrawEdgeButton(g, _rcMo, "Modding", 9.5f, muted: false);
                        if (ShowRepairButton)
                            DrawRepairButton(g);
                    }
                    DrawPlayButton(g);
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
                string main = _showProgress
                    ? (string.IsNullOrEmpty(_statusText) ? "Updating…" : _statusText)
                    : (_showReady ? "Ready to play" : "");

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
                    DrawEdgeProgressBar(g, _rcProgress, _progressPct);

                if (Constants.SHOW_VERSION_TEXT && !string.IsNullOrEmpty(_versionText))
                {
                    // Game + Launcher under the bar (one line, may ellipsis if tight)
                    var verArea = new Rectangle(
                        _rcProgress.X,
                        Math.Min(h - 16, _rcProgress.Bottom + 2),
                        _rcProgress.Width,
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
            DrawEdgeButton(g, _rcPlay, _playText, 12f, muted: _busy);
        }

        private void DrawRepairButton(Graphics g)
        {
            DrawEdgeButton(g, _rcRepair, "Repair", 9.5f, muted: false);
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

        private void DrawCloseButton(Graphics g)
        {
            if (_iconClose == null)
            {
                // Fallback X
                using (var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2f))
                {
                    int m = 7;
                    g.DrawLine(pen, _rcClose.Left + m, _rcClose.Top + m, _rcClose.Right - m, _rcClose.Bottom - m);
                    g.DrawLine(pen, _rcClose.Right - m, _rcClose.Top + m, _rcClose.Left + m, _rcClose.Bottom - m);
                }
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // Soft hit plate so X stays readable on light splash areas
            using (var plate = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(plate, Rectangle.Inflate(_rcClose, 2, 2));
            g.DrawImage(_iconClose, _rcClose);
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
            if (ShowVrChannelButton && _rcVrChannel.Contains(p))
            {
                OnClickVrChannel();
                return;
            }
            if (ShowMoButton && _rcMo.Contains(p))
            {
                OnClickModding();
                return;
            }
            if (ShowRepairButton && _rcRepair.Contains(p))
            {
                OnClickRepair();
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

        private void OnClickPlay()
        {
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
                    _statusText = "Cancelling…\nPlease wait";
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
                MessageBox.Show(FlappyException.FormatForUser(ex), "Mod Organizer");
            }
        }

        private void OnClickVrChannel()
        {
            if (_busy || !PackageInstaller.IsInstalled()) return;

            if (PackageInstaller.GetSavedChannel() == InstallChannel.AeOnly)
            {
                var r = MessageBox.Show(
                    "Download VR packages (StockGameVR, (VR) mods, VR profile)?\n\n" +
                    "This can take a long time and needs extra disk space.\n" +
                    "AE content already installed will be kept.",
                    "Get VR",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;

                StartPackageJob(
                    "Get VR…\nDownloading VR packages",
                    (index, report, cancel) => PackageInstaller.InstallVrChannel(index, report, cancel),
                    "Get VR failed",
                    launchAfter: false);
            }
            else
            {
                var r = MessageBox.Show(
                    "Remove all VR packages from this install?\n\n" +
                    "Deletes StockGameVR, (VR) mods, and the VR profile.\n" +
                    "AE content stays. You can use Get VR later to re-download.",
                    "Remove VR",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                StartPackageJob(
                    "Removing VR…\nPlease wait",
                    (index, report, cancel) => PackageInstaller.RemoveVrChannel(index, report, cancel),
                    "Remove VR failed",
                    launchAfter: false);
            }
        }

        private void OnClickRepair()
        {
            if (_busy || !PackageInstaller.IsInstalled()) return;

            var r = MessageBox.Show(
                "Repair compares local folders to CDN fingerprints (path|size).\n\n" +
                "• Unchanged packages are skipped\n" +
                "• profiles (MO2 user data) are kept if present\n" +
                "• Incomplete/changed packages are re-downloaded\n" +
                "• A report is written to repair_report.txt\n\n" +
                "Scan now?",
                "Repair",
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
                "Preparing…\n" + (channel == InstallChannel.AeOnly ? "AE only" : "AE + VR"),
                (index, report, cancel) => PackageInstaller.InstallAll(index, report, cancel, channel),
                "Install / Update failed",
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
            _statusText = "Scanning packages…\nFingerprints";
            _progressPct = 0;
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
                    msg => worker.ReportProgress(0, "Checking…\n" + msg),
                    token);
                PackageInstaller.WriteRepairReport(need,
                    PackageInstaller.FilterByChannel(index.units, channel),
                    "repair scan");
                args.Result = need;
            };
            bw.ProgressChanged += (o, args) =>
            {
                if (args.UserState is string s) _statusText = s;
                RedrawLayered(force: false);
            };
            bw.RunWorkerCompleted += (o, args) =>
            {
                if (args.Error != null)
                {
                    _busy = false;
                    RefreshPlayState();
                    MessageBox.Show(FlappyException.FormatForUser(args.Error), "Repair failed");
                    IsReady = PackageInstaller.IsInstalled();
                    return;
                }

                var need = args.Result as System.Collections.Generic.List<PackageUnit>
                           ?? new System.Collections.Generic.List<PackageUnit>();

                if (need.Count == 0)
                {
                    _busy = false;
                    _statusText = "Repair not needed\nAll packages match";
                    _progressPct = 100;
                    IsReady = true;
                    MessageBox.Show(
                        "All packages match the CDN fingerprints.\nNothing to download.\n\nSee repair_report.txt",
                        "Repair");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Packages to re-download: " + need.Count);
                sb.AppendLine("Channel: " + (channel == InstallChannel.AeOnly ? "AE only" : "AE + VR"));
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
                    sb.AppendLine("• … and " + (need.Count - show) + " more");
                if (bytes > 0)
                    sb.AppendLine().AppendLine("Approx. download: " + FormatApproxSize(bytes));
                sb.AppendLine().AppendLine("Details: repair_report.txt");
                sb.AppendLine().Append("Download and repair these packages?");

                var r = MessageBox.Show(sb.ToString(), "Repair", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    _busy = false;
                    _statusText = "Ready to play";
                    RefreshPlayState();
                    RedrawLayered(force: true);
                    return;
                }

                _statusText = "Repairing…\n" + need.Count + " packages";
                _progressPct = 0;
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
                        (pct, msg) => worker2.ReportProgress(Math.Max(0, Math.Min(100, pct)), msg),
                        token2,
                        channel);
                };
                bw2.ProgressChanged += (o2, a2) =>
                {
                    if (a2.UserState is string s) _statusText = s;
                    _progressPct = Math.Min(100, Math.Max(0, a2.ProgressPercentage));
                    RedrawLayered(force: false);
                };
                bw2.RunWorkerCompleted += (o2, a2) =>
                {
                    _busy = false;
                    _isInstalled = PackageInstaller.IsInstalled();
                    if (a2.Error != null)
                    {
                        MessageBox.Show(FlappyException.FormatForUser(a2.Error), "Repair failed");
                        IsReady = _isInstalled;
                        return;
                    }
                    _statusText = "Ready to play";
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

        private void StartPackageJob(
            string startStatus,
            Action<PackageInstaller.Index, Action<int, string>, CancellationToken> work,
            string errorTitle,
            bool launchAfter)
        {
            if (_busy) return;
            _busy = true;
            RefreshPlayState();
            _showProgress = true;
            _showReady = false;
            _statusText = startStatus;
            _progressPct = 0;
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
                    (pct, msg) => worker.ReportProgress(Math.Max(0, Math.Min(100, pct)), msg),
                    token);
            };
            bw.ProgressChanged += (o, args) =>
            {
                if (args.UserState is string s && !string.IsNullOrEmpty(s))
                    _statusText = s;
                _progressPct = Math.Min(100, Math.Max(0, args.ProgressPercentage));
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
                _statusText = "Ready to play";
                // Refresh online version from cache if install updated local_version
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
                MessageBox.Show(FlappyException.FormatForUser(ex), "Launch failed");
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
                MessageBox.Show("Cannot open link:\n" + ex.Message, "Flappy Launcher");
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
