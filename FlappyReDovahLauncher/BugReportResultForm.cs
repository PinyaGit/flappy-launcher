using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Displays the bug report upload result with Discord thread URL / share URL, copy to clipboard, and open in browser.
    /// </summary>
    internal sealed class BugReportResultForm : Form
    {
        public BugReportResultForm(string shareUrl, string localArchivePath, string discordThreadUrl = null, string discordThreadName = null)
        {
            Text = Constants.LAUNCHER_NAME + " - " + Loc.T("bugreport");
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(540, 360);

            bool hasDiscord = !string.IsNullOrEmpty(discordThreadUrl);
            string primaryUrl = hasDiscord ? discordThreadUrl : shareUrl;

            var title = new Label
            {
                Text = hasDiscord
                    ? (Loc.IsRu ? "Багрепорт успешно отправлен в Discord!" : "Bug report submitted to Discord!")
                    : (Loc.IsRu ? "Архив успешно создан и загружен!" : "Bug report created & uploaded!"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(500, 30)
            };

            var hint = new Label
            {
                Text = hasDiscord
                    ? (Loc.IsRu
                        ? "Тикет создан в канале форума bug-report. Ссылка скопирована в буфер:"
                        : "Ticket created in Discord bug-report forum. Link copied to clipboard:")
                    : (Loc.IsRu
                        ? "Ссылка на архив автоматически скопирована в буфер обмена.\nВставьте её в чат поддержки Discord:"
                        : "The link has been automatically copied to your clipboard.\nPaste it into the Discord support chat:"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(20, 48),
                Size = new Size(500, 42)
            };

            var urlBox = new TextBox
            {
                Text = primaryUrl,
                ReadOnly = true,
                Location = new Point(30, 96),
                Size = new Size(480, 28),
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.GoldHi,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center
            };
            urlBox.Click += (s, e) => urlBox.SelectAll();

            var shareHint = new Label
            {
                Text = hasDiscord
                    ? (Loc.IsRu
                        ? ("Файлы логов загружены в Flappy Share (срок 7 дней):\n" + shareUrl)
                        : ("Log archive uploaded to Flappy Share (7 days):\n" + shareUrl))
                    : (Loc.IsRu
                        ? "Срок хранения ссылки: 7 дней (Flappy Share)"
                        : "Link expiration: 7 days (Flappy Share)"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 130),
                Size = new Size(500, 40)
            };

            var btnOpenDiscord = FlappyTheme.MakeGoldButton(
                hasDiscord
                    ? (Loc.IsRu ? "💬 Открыть тикет в Discord" : "💬 Open Ticket in Discord")
                    : (Loc.IsRu ? "🌐 Открыть в браузере" : "🌐 Open in browser"),
                new Point(30, 180),
                new Size(230, 42),
                primary: true);

            btnOpenDiscord.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = primaryUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            };

            var btnCopy = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "📋 Скопировать ссылку" : "📋 Copy link",
                new Point(280, 180),
                new Size(230, 42),
                primary: false);

            btnCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(primaryUrl);
                    btnCopy.Text = Loc.IsRu ? "✅ Скопировано!" : "✅ Copied!";
                }
                catch { }
            };

            var btnLocal = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "📁 Локальный архив" : "📁 Local archive",
                new Point(30, 236),
                new Size(230, 36),
                primary: false);

            btnLocal.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(localArchivePath))
                    BugReportPacker.Reveal(localArchivePath);
            };

            var btnClose = FlappyTheme.MakeGoldButton(
                Loc.T("close"),
                new Point(280, 236),
                new Size(230, 36),
                primary: false);
            btnClose.DialogResult = DialogResult.OK;

            Controls.Add(title);
            Controls.Add(hint);
            Controls.Add(urlBox);
            Controls.Add(shareHint);
            Controls.Add(btnOpenDiscord);
            Controls.Add(btnCopy);
            Controls.Add(btnLocal);
            Controls.Add(btnClose);

            // Auto-copy link to clipboard on load
            try
            {
                Clipboard.SetText(primaryUrl);
            }
            catch { }
        }
    }
}
