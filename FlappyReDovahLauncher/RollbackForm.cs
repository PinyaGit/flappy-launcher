using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Dialog for selecting game version, rollback, or pinning version to hold updates.
    /// </summary>
    internal sealed class RollbackForm : Form
    {
        public VersionEntry SelectedVersion { get; private set; }
        public bool PinRequested { get; private set; }
        public bool UnpinAndUpgradeRequested { get; private set; }

        private readonly ComboBox _combo;
        private readonly CheckBox _chkPin;
        private readonly Button _btnApply;
        private readonly Button _btnUnpin;
        private readonly Label _statusLabel;
        private VersionsManifest _manifest;

        public RollbackForm()
        {
            Text = Constants.LAUNCHER_NAME + " - " + (Loc.IsRu ? "Выбор версии / Откат" : "Version Select / Rollback");
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(500, 360);

            var title = new Label
            {
                Text = Loc.IsRu ? "Управление версией сборки" : "Game Version Management",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(460, 30)
            };

            var localVer = PackageInstaller.GetLocalVersion();
            string localStr = localVer.ToString();
            string pinned = PackageInstaller.GetPinnedVersion();
            bool isPinned = !string.IsNullOrEmpty(pinned);

            var infoLabel = new Label
            {
                Text = Loc.IsRu
                    ? ("Установленная версия: v" + localStr + (isPinned ? " (закреплена / обновления приостановлены)" : ""))
                    : ("Installed version: v" + localStr + (isPinned ? " (pinned / updates on hold)" : "")),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = isPinned ? FlappyTheme.GoldHi : FlappyTheme.Cream,
                Location = new Point(20, 48),
                Size = new Size(460, 24)
            };

            var selectLabel = new Label
            {
                Text = Loc.IsRu ? "Выберите версию для установки / отката:" : "Select version to install / rollback:",
                AutoSize = false,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(30, 84),
                Size = new Size(440, 20)
            };

            _combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(30, 108),
                Size = new Size(440, 30),
                Font = new Font("Segoe UI", 10.5f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream
            };

            _chkPin = new CheckBox
            {
                Text = Loc.IsRu
                    ? "Придержать обновление (не обновлять автоматически до выхода новых патчей)"
                    : "Hold updates (pin this version, do not auto-update)",
                AutoSize = false,
                Checked = isPinned || true,
                Location = new Point(30, 150),
                Size = new Size(440, 42),
                Font = new Font("Segoe UI", 9f),
                ForeColor = FlappyTheme.Cream
            };

            _statusLabel = new Label
            {
                Text = Loc.IsRu ? "Загрузка списка версий…" : "Loading versions list…",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9f),
                ForeColor = FlappyTheme.Mute,
                Location = new Point(20, 200),
                Size = new Size(460, 22)
            };

            _btnApply = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "⏪ Применить выбранную версию" : "⏪ Apply Selected Version",
                new Point(30, 230),
                new Size(440, 40),
                primary: true);
            _btnApply.Enabled = false;

            _btnUnpin = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "🔄 Снять закрепление и обновиться" : "🔄 Unpin and Update to Latest",
                new Point(30, 276),
                new Size(215, 36),
                primary: false);
            _btnUnpin.Visible = isPinned;

            var btnCancel = FlappyTheme.MakeGoldButton(
                Loc.T("close"),
                new Point(isPinned ? 255 : 150, 276),
                new Size(isPinned ? 215 : 200, 36),
                primary: false);
            btnCancel.DialogResult = DialogResult.Cancel;

            Controls.Add(title);
            Controls.Add(infoLabel);
            Controls.Add(selectLabel);
            Controls.Add(_combo);
            Controls.Add(_chkPin);
            Controls.Add(_statusLabel);
            Controls.Add(_btnApply);
            Controls.Add(_btnUnpin);
            Controls.Add(btnCancel);

            _btnApply.Click += (s, e) =>
            {
                if (_combo.SelectedItem is VersionComboItem item && item.Entry != null)
                {
                    SelectedVersion = item.Entry;
                    PinRequested = _chkPin.Checked;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            _btnUnpin.Click += (s, e) =>
            {
                UnpinAndUpgradeRequested = true;
                PackageInstaller.SetPinnedVersion(null);
                DialogResult = DialogResult.OK;
                Close();
            };

            LoadVersionsAsync();
        }

        private async void LoadVersionsAsync()
        {
            try
            {
                _manifest = await System.Threading.Tasks.Task.Run(() => PackageInstaller.FetchVersionsManifest());
                if (_manifest == null || _manifest.versions == null || _manifest.versions.Count == 0)
                {
                    _statusLabel.Text = Loc.IsRu ? "История версий пока не сформирована на сервере." : "No version history found on server.";
                    return;
                }

                _combo.Items.Clear();
                foreach (var v in _manifest.versions)
                {
                    string dateStr = !string.IsNullOrEmpty(v.released) ? (" (" + v.released + ")") : "";
                    _combo.Items.Add(new VersionComboItem
                    {
                        Text = "v" + v.version + dateStr,
                        Entry = v
                    });
                }

                if (_combo.Items.Count > 0)
                {
                    _combo.SelectedIndex = 0;
                    _btnApply.Enabled = true;
                    _statusLabel.Text = Loc.IsRu
                        ? ("Доступно версий: " + _combo.Items.Count)
                        : ("Available versions: " + _combo.Items.Count);
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = Loc.IsRu ? ("Ошибка: " + ex.Message) : ("Error: " + ex.Message);
            }
        }

        private class VersionComboItem
        {
            public string Text { get; set; }
            public VersionEntry Entry { get; set; }
            public override string ToString() => Text;
        }
    }
}
