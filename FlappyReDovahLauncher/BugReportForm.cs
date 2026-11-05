using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Dialog for selecting bug report category, title, and detailed description before sending.
    /// </summary>
    internal sealed class BugReportForm : Form
    {
        public string SelectedCategory { get; private set; }
        public string ReportTitle { get; private set; }
        public string ReportDetails { get; private set; }
        public bool PublishToDiscord => _chkPublishDiscord.Checked;

        private readonly ComboBox _comboCategory;
        private readonly TextBox _txtTitle;
        private readonly TextBox _txtDetails;
        private readonly CheckBox _chkPublishDiscord;

        public BugReportForm()
        {
            Text = Constants.LAUNCHER_NAME + " - " + (Loc.IsRu ? "Отправка баг-репорта" : "Submit Bug Report");
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(540, 520);

            var titleLabel = new Label
            {
                Text = Loc.IsRu ? "Сообщить об ошибке / Bug Report" : "Report an Issue / Bug Report",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 14),
                Size = new Size(500, 26)
            };

            var lblCategory = new Label
            {
                Text = Loc.IsRu ? "С чем связана проблема?" : "What is the issue related to?",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 46),
                AutoSize = true
            };

            _comboCategory = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 68),
                Size = new Size(480, 28)
            };

            _comboCategory.Items.Add(new CategoryItem("Flappy", "Flappy (Skyrim AE)"));
            _comboCategory.Items.Add(new CategoryItem("Re-Dovah", "Re-Dovah (Skyrim VR / AE)"));
            _comboCategory.Items.Add(new CategoryItem("Launcher", Loc.IsRu ? "Лаунчер (Flappy Launcher)" : "Launcher (Flappy Launcher)"));

            // Preselect current game
            if (GameCatalog.Current != null && string.Equals(GameCatalog.Current.Id, "re-dovah", StringComparison.OrdinalIgnoreCase))
                _comboCategory.SelectedIndex = 1;
            else if (GameCatalog.Current != null && string.Equals(GameCatalog.Current.Id, "flappy", StringComparison.OrdinalIgnoreCase))
                _comboCategory.SelectedIndex = 0;
            else
                _comboCategory.SelectedIndex = 0;

            var lblTitle = new Label
            {
                Text = Loc.IsRu ? "Краткий заголовок проблемы:" : "Short summary / Title:",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 102),
                AutoSize = true
            };

            _txtTitle = new TextBox
            {
                Font = new Font("Segoe UI", 10f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 124),
                Size = new Size(480, 26),
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 90
            };

            var lblDetails = new Label
            {
                Text = Loc.IsRu ? "Подробное описание (что случилось, шаги для повторения):" : "Details (what happened, reproduction steps):",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 156),
                AutoSize = true
            };

            _txtDetails = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 178),
                Size = new Size(480, 100),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            // Mandatory Checklist
            var lblChecklist = new Label
            {
                Text = Loc.IsRu ? "Обязательные проверки перед отправкой:" : "Required verification before submitting:",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(30, 288),
                AutoSize = true
            };

            var chk1 = new CheckBox
            {
                Text = Loc.IsRu ? "1. Установлена актуальная версия сборки (проверено в лаунчере)" : "1. Latest modpack version is installed (checked in launcher)",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(32, 312),
                Size = new Size(480, 22),
                AutoSize = false
            };

            var chk2 = new CheckBox
            {
                Text = Loc.IsRu ? "2. Выполнена «Починка игры» в лаунчере, но это не решило проблему" : "2. Executed 'Repair' in launcher, but it did not resolve the issue",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(32, 336),
                Size = new Size(480, 22),
                AutoSize = false
            };

            var chk3 = new CheckBox
            {
                Text = Loc.IsRu ? "3. Не устанавливались старые моды для Skyrim LE (Oldrim), несовместимые с AE/SE" : "3. No incompatible Skyrim LE (Oldrim) mods were installed",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(32, 360),
                Size = new Size(480, 22),
                AutoSize = false
            };

            var chk4 = new CheckBox
            {
                Text = Loc.IsRu ? "4. Проблема воспроизводится на чистом сохранении или приложен краш-лог" : "4. Issue reproducible on a clean save or crash log is included",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(32, 384),
                Size = new Size(480, 22),
                AutoSize = false
            };

            _chkPublishDiscord = new CheckBox
            {
                Text = Loc.IsRu ? "Опубликовать новый тред в Discord (снимите, если прикрепляете архив к существующему треду)" : "Create a new Discord thread (uncheck if attaching to existing thread)",
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(32, 412),
                Size = new Size(480, 24),
                AutoSize = false,
                Checked = true
            };

            var btnSubmit = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "🚀 Сформировать репорт" : "🚀 Generate Report",
                new Point(30, 448),
                new Size(230, 42),
                primary: true);
            btnSubmit.Enabled = false;

            EventHandler updateSubmitState = (s, e) =>
            {
                btnSubmit.Enabled = chk1.Checked && chk2.Checked && chk3.Checked && chk4.Checked;
            };

            chk1.CheckedChanged += updateSubmitState;
            chk2.CheckedChanged += updateSubmitState;
            chk3.CheckedChanged += updateSubmitState;
            chk4.CheckedChanged += updateSubmitState;

            var btnCancel = FlappyTheme.MakeGoldButton(
                Loc.T("close"),
                new Point(280, 448),
                new Size(230, 42),
                primary: false);
            btnCancel.DialogResult = DialogResult.Cancel;

            btnSubmit.Click += (s, e) =>
            {
                var catItem = _comboCategory.SelectedItem as CategoryItem;
                SelectedCategory = catItem != null ? catItem.Code : "Launcher";
                ReportTitle = _txtTitle.Text.Trim();
                if (string.IsNullOrWhiteSpace(ReportTitle))
                {
                    ReportTitle = (Loc.IsRu ? "Багрепорт " : "Bug report ") + SelectedCategory;
                }
                ReportDetails = _txtDetails.Text.Trim();
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(titleLabel);
            Controls.Add(lblCategory);
            Controls.Add(_comboCategory);
            Controls.Add(lblTitle);
            Controls.Add(_txtTitle);
            Controls.Add(lblDetails);
            Controls.Add(_txtDetails);
            Controls.Add(lblChecklist);
            Controls.Add(chk1);
            Controls.Add(chk2);
            Controls.Add(chk3);
            Controls.Add(chk4);
            Controls.Add(_chkPublishDiscord);
            Controls.Add(btnSubmit);
            Controls.Add(btnCancel);
        }

        private sealed class CategoryItem
        {
            public string Code { get; }
            public string Display { get; }
            public CategoryItem(string code, string display)
            {
                Code = code;
                Display = display;
            }
            public override string ToString() => Display;
        }
    }
}
