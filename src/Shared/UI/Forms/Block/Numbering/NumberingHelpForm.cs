using System.Drawing;
using System.Windows.Forms;

namespace ChemForge.UI.Forms.Block.Numbering
{
    /// <summary>
    /// 「编号用法说明」弹窗：显示 <see cref="NumberingHelpText"/> 里的同一份文案。
    ///
    /// 由「属性块批量编号」对话框右上角的「编号用法说明」按钮调起（见 <see cref="NumberingHelpButton"/>）。
    /// 刻意只用纯 WinForms 弹窗，不碰 AutoCAD 的对话框 API（AcadApp.ShowModalDialog /
    /// ShowAlertDialog 需要宿主 AcWindows.dll，在 accoreconsole 里 JIT 到会直接崩进程）。
    /// </summary>
    internal sealed class NumberingHelpForm : Form
    {
        private const int ButtonWidth = 92;
        private const int ButtonHeight = 28;

        private readonly TextBox _body;

        private NumberingHelpForm()
        {
            Text = "属性块批量编号 —— 编号用法说明";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(820, 620);
            MinimumSize = new Size(520, 320);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = true;

            _body = new TextBox();
            _body.Multiline = true;
            _body.ReadOnly = true;
            _body.WordWrap = false;                 // 文案自带排版，不要自动折行
            _body.ScrollBars = ScrollBars.Both;
            _body.Dock = DockStyle.Fill;
            _body.BorderStyle = BorderStyle.FixedSingle;
            _body.BackColor = SystemColors.Window;  // ReadOnly 默认灰底，这里保持白底
            _body.Font = new Font("Consolas", 10F); // 等宽，保留原文的对齐留白
            _body.Text = NumberingHelpText.DialogText;

            Button close = new Button();
            close.Text = "关闭";
            close.Width = ButtonWidth;
            close.Height = ButtonHeight;
            close.Margin = new Padding(6, 0, 0, 0);
            close.Click += delegate { Close(); };

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.WrapContents = false;
            bottom.AutoSize = false;
            bottom.Height = ButtonHeight + 16;
            bottom.Padding = new Padding(8, 8, 8, 0);
            bottom.Controls.Add(close);

            Panel pad = new Panel();
            pad.Dock = DockStyle.Fill;
            pad.Padding = new Padding(10, 10, 10, 6);
            pad.Controls.Add(_body);

            Controls.Add(pad);
            Controls.Add(bottom);

            AcceptButton = close;
            CancelButton = close;
        }

        /// <summary>以模态弹窗显示用法说明；<paramref name="owner"/> 可为 null。</summary>
        internal static void ShowHelp(IWin32Window owner)
        {
            using (NumberingHelpForm form = new NumberingHelpForm())
            {
                if (owner != null)
                {
                    form.ShowDialog(owner);
                }
                else
                {
                    form.ShowDialog();
                }
            }
        }
    }
}
