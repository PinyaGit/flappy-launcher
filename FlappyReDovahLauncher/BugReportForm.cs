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

        private readonly ComboBox _comboCategory;
        private readonly TextBox _txtTitle;
        private readonly TextBox _txtDetails;

        public BugReportForm()
        {
            Text = Constants.LAUNCHER_NAME + " - " + (Loc.IsRu ? "Отправка баг-репорта" : "Submit Bug Report");
            FlappyTheme.ApplyDialog(this);
            ClientSize = new Size(540, 430);

            var titleLabel = new Label
            {
                Text = Loc.IsRu ? "Сообщить об ошибке / Bug Report" : "Report an Issue / Bug Report",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                ForeColor = FlappyTheme.GoldHi,
                Location = new Point(20, 16),
                Size = new Size(500, 30)
            };

            var lblCategory = new Label
            {
                Text = Loc.IsRu ? "С чем связана проблема?" : "What is the issue related to?",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 56),
                AutoSize = true
            };

            _comboCategory = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 78),
                Size = new Size(480, 28)
            };

            _comboCategory.Items.Add(new CategoryItem("Re-Dovah", "Re-Dovah (Skyrim VR / AE)"));
            _comboCategory.Items.Add(new CategoryItem("Flappy", "Flappy (Skyrim AE)"));
            _comboCategory.Items.Add(new CategoryItem("Launcher", Loc.IsRu ? "Лаунчер (Flappy Launcher)" : "Launcher (Flappy Launcher)"));

            // Preselect current game
            if (GameCatalog.Current != null && string.Equals(GameCatalog.Current.Id, "flappy", StringComparison.OrdinalIgnoreCase))
                _comboCategory.SelectedIndex = 1;
            else if (GameCatalog.Current != null && string.Equals(GameCatalog.Current.Id, "re-dovah", StringComparison.OrdinalIgnoreCase))
                _comboCategory.SelectedIndex = 0;
            else
                _comboCategory.SelectedIndex = 0;

            var lblTitle = new Label
            {
                Text = Loc.IsRu ? "Краткий заголовок проблемы:" : "Short summary / Title:",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 118),
                AutoSize = true
            };

            _txtTitle = new TextBox
            {
                Font = new Font("Segoe UI", 10f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 140),
                Size = new Size(480, 26),
                BorderStyle = BorderStyle.FixedSingle,
                MaxLength = 90
            };

            var lblDetails = new Label
            {
                Text = Loc.IsRu ? "Подробное описание (что случилось, шаги для повторения):" : "Details (what happened, reproduction steps):",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 180),
                AutoSize = true
            };

            _txtDetails = new TextBox
            {
                Font = new Font("Segoe UI", 9.5f),
                BackColor = FlappyTheme.Panel,
                ForeColor = FlappyTheme.Cream,
                Location = new Point(30, 204),
                Size = new Size(480, 130),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle
            };

            var btnSubmit = FlappyTheme.MakeGoldButton(
                Loc.IsRu ? "🚀 Отправить репорт" : "🚀 Submit Report",
                new Point(30, 360),
                new Size(230, 42),
                primary: true);

            var btnCancel = FlappyTheme.MakeGoldButton(
                Loc.T("close"),
                new Point(280, 360),
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
