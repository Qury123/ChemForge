using System.Drawing;
using System.Windows.Forms;

namespace ChemForge.UI.Forms.Common
{
    /// <summary>
    /// 「关于 ChemForge（版本信息）」的弹窗，由命令 CHEMFORGE 调用。
    /// 纯代码布局（不用设计器），因此不依赖 InitializeComponent；
    /// 版权信息与 README.md 的「版权」一节保持一致。
    /// </summary>
    internal sealed class AboutDialog : Form
    {
        /// <summary>
        /// 创建关于对话框。
        /// </summary>
        /// <param name="version">版本号文本，例如 "1.0.0"。</param>
        public AboutDialog(string version)
        {
            Text = "关于 ChemForge";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.White;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(432, 244);

            var titleLabel = new Label
            {
                Text = "ChemForge",
                Font = new Font(Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 90, 160),
                AutoSize = true,
                Location = new Point(24, 18)
            };

            var brandLabel = new Label
            {
                Text = "工艺匠造工坊",
                Font = new Font(Font.FontFamily, 11F),
                ForeColor = Color.FromArgb(90, 90, 90),
                AutoSize = true,
                Location = new Point(27, 58)
            };

            var separator = new Panel
            {
                BackColor = Color.FromArgb(222, 222, 222),
                Location = new Point(24, 88),
                Size = new Size(384, 1)
            };

            var versionLabel = new Label
            {
                Text = "版本 " + version,
                AutoSize = true,
                Location = new Point(27, 100)
            };

            var sloganLabel = new Label
            {
                Text = "锻造化工工艺，精绘设计蓝图",
                ForeColor = Color.FromArgb(60, 60, 60),
                AutoSize = true,
                Location = new Point(27, 126)
            };

            var copyrightLabel = new Label
            {
                Text = "Copyright © 2026 ChemForge Contributors.",
                ForeColor = Color.FromArgb(90, 90, 90),
                AutoSize = true,
                Location = new Point(27, 164)
            };

            var rightsLabel = new Label
            {
                Text = "保留所有权利。",
                ForeColor = Color.FromArgb(90, 90, 90),
                AutoSize = true,
                Location = new Point(27, 184)
            };

            var okButton = new Button
            {
                Text = "确定",
                Size = new Size(84, 28),
                Location = new Point(ClientSize.Width - 84 - 24, ClientSize.Height - 28 - 18),
                DialogResult = DialogResult.OK
            };

            Controls.Add(titleLabel);
            Controls.Add(brandLabel);
            Controls.Add(separator);
            Controls.Add(versionLabel);
            Controls.Add(sloganLabel);
            Controls.Add(copyrightLabel);
            Controls.Add(rightsLabel);
            Controls.Add(okButton);

            AcceptButton = okButton;
            CancelButton = okButton;
        }
    }
}
