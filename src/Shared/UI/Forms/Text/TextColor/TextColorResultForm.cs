using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChemForge.UI.Forms.Text.TextColor
{
    /// <summary>
    /// 批量改色跑完之后的「处理结果」提示框。
    ///
    /// 正常路径上弹的是 AutoCAD 原生提示框（<c>CommandHost.ShowAlertSafely</c>，
    /// 就是 BLKATTCHANGE 收尾那个「AutoCAD 消息」窗口）；只有原生那条路抛异常时
    /// 才会走到这个窗体，所以它得跟原生提示框一个样子：一段短文字 + 一个「确定」，
    /// 窗口大小跟着文字走 —— 不长标题区、不长 logo、不长成半屏报告。
    ///
    /// 正文是 <see cref="ChemForge.Models.Text.TextColor.TextColorReport.BuildBrief"/>
    /// 的短版（结论、目标颜色、范围、撤销）；完整报告留在命令行里。
    ///
    /// 弹出方式必须是 <c>CommandHost.ShowDialogSafely</c>（即 AutoCAD 的
    /// <c>ShowModalDialog</c>），不能直接 new 出来 Show()：命令是模态执行的。
    /// </summary>
    public sealed class TextColorResultForm : Form
    {
        /// <summary>文字与按钮四周到窗口边的留白。</summary>
        private const int EdgeMargin = 16;

        /// <summary>文字与按钮之间的间距。</summary>
        private const int Gap = 18;

        private const int ButtonWidth = 88;
        private const int ButtonHeight = 28;

        /// <summary>内容区宽度下限：一行“完成！共修改……”摆得下。</summary>
        private const int MinContentWidth = 300;

        /// <summary>内容区宽度上限：再宽就折行，别把窗口拉成一条横带。</summary>
        private const int MaxContentWidth = 520;

        /// <summary>
        /// <paramref name="message"/> 是短版报告。允许为空 ——
        /// 命令那边出意外时，宁可弹一个空窗口，也不要不弹。
        /// </summary>
        public TextColorResultForm(string message)
        {
            BuildLayout(Normalize(message));
        }

        private void BuildLayout(string message)
        {
            Text = "批量修改多行文字颜色";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            // 先量后摆：窗口宽高都跟着文字走。短结论就是个小框，
            // 带跳过统计的长结论也只在纵向上长一点，不会撑出一大片空白。
            int contentWidth = MeasureContentWidth(message);
            int textHeight = Math.Max(Font.Height, MeasureHeight(message, contentWidth));

            Button okButton = new Button
            {
                Name = "ResultOkButton",
                Text = "确定",
                Size = new Size(ButtonWidth, ButtonHeight),
                Location = new Point(EdgeMargin + contentWidth - ButtonWidth, EdgeMargin + textHeight + Gap)
            };
            okButton.Click += delegate { Close(); };

            Label messageLabel = new Label
            {
                Name = "ResultMessage",
                Text = message,
                Font = Font,
                AutoSize = false,
                Location = new Point(EdgeMargin, EdgeMargin),
                Size = new Size(contentWidth, textHeight)
            };

            ClientSize = new Size(
                EdgeMargin * 2 + contentWidth,
                EdgeMargin + textHeight + Gap + ButtonHeight + EdgeMargin);

            Controls.Add(messageLabel);
            Controls.Add(okButton);

            AcceptButton = okButton;
            CancelButton = okButton;

            // 焦点落在「确定」上：这是唯一的按钮，回车即关。
            ActiveControl = okButton;
        }

        /// <summary>
        /// 内容区宽度：取最长一行的宽度，夹在上下限之间。
        /// 长行会在上限处折行，由 <see cref="MeasureHeight"/> 补回高度。
        /// </summary>
        private static int MeasureContentWidth(string message)
        {
            int widest = 0;

            if (!string.IsNullOrEmpty(message))
            {
                foreach (string line in message.Split('\n'))
                {
                    string trimmed = line.TrimEnd('\r');
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    int width = TextRenderer.MeasureText(
                        trimmed,
                        SystemFonts.MessageBoxFont,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

                    if (width > widest)
                    {
                        widest = width;
                    }
                }
            }

            return Math.Min(Math.Max(widest, MinContentWidth), MaxContentWidth);
        }

        /// <summary>按给定宽度排版后需要多高（GDI 口径，与 Label 的实际绘制一致）。</summary>
        private static int MeasureHeight(string message, int width)
        {
            if (string.IsNullOrEmpty(message))
            {
                return 0;
            }

            return TextRenderer.MeasureText(
                message,
                SystemFonts.MessageBoxFont,
                new Size(Math.Max(width, 1), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        }

        /// <summary>
        /// 换行符归一化成 CRLF。
        ///
        /// Label 对 LF 也能换行，但报告由 TextColorReport 用 Environment.NewLine
        /// 拼出来，正常就是 CRLF；这里兜的是“报告被别处拼过一道”的情况，
        /// 顺便让窗口里的行距与命令行一致。
        /// </summary>
        private static string Normalize(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            return message.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }
    }
}
