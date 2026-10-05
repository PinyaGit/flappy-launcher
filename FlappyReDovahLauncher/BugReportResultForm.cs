using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Displays the bug report upload result with share URL, copy to clipboard, and open in browser.
    /// </summary>
    internal sealed class BugReportResultForm : Form
    {
        public BugReportResultForm(string shareUrl, string localArchivePath)
        {
            Text = Constants.LAUNCHER_NAME + " - " + Loc.T("bugreport");
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(540, 310);

            var title = new Label
            {
                Text = Loc.IsRu ? "Багрепорт успешно создан и загружен!" : "Bug report created & uploaded!",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(500, 30)
            };

            var hint = new Label
            {
                Text = Loc.IsRu
                    ? "Ссылка на архив автоматически скопирована в буфер обмена.\nВставьте её в чат поддержки Discord:"
                    : "The link has been automatically copied to your clipboard.\nPaste it into the Discord support chat:",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(20, 48),
                Size = new Size(500, 42)
            };

            var urlBox = new TextBox
            {
                Text = shareUrl,
                ReadOnly = true,
                Location = new Point(30, 98),
                Size = new Size(480, 28),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.GoldHi,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center
            };
            urlBox.Click += (s, e) => urlBox.SelectAll();

            var expireHint = new Label
            {
                Text = Loc.IsRu
                    ? "Срок хранения ссылки: 7 дней (Flappy Share)"
                    : "Link expiration: 7 days (Flappy Share)",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 130),
                Size = new Size(500, 20)
            };

            var btnCopy = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "📋 Скопировать ссылку" : "📋 Copy link",
                new Point(30, 160),
                new Size(230, 38),
                primary: true);

            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(shareUrl);
                    btnCopy.Text = Loc.IsRu ? "✅ Скопировано!" : "✅ Copied!";
                }
                catch { }
            };

            var btnOpen = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "🌐 Открыть в браузере" : "🌐 Open in browser",
                new Point(280, 160),
                new Size(230, 38),
                primary: false);

            btnOpen.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = shareUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            };

            var btnFolder = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "📁 Локальный архив" : "📁 Local file",
                new Point(30, 212),
                new Size(230, 36),
                primary: false);

            btnFolder.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(localArchivePath))
                    BugReportPacker.Reveal(localArchivePath);
            };

            var btnClose = FlappyTheme.MakeGoldButton(
                Loc.T("close"),
                new Point(280, 212),
                new Size(230, 36),
                primary: false);

            btnClose.Click += (s, e) => Close();

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(urlBox);
            Controls.Add(expireHint);
            Controls.Add(btnCopy);
            Controls.Add(btnOpen);
            Controls.Add(btnFolder);
            Controls.Add(btnClose);

            AcceptButton = btnClose;

            // Auto-copy to clipboard on load
            try { Clipboard.SetText(shareUrl); } catch { }
        }
    }
}
