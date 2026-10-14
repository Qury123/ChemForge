using System;
using System.Windows.Forms;

namespace ChemForge.UI.Forms.Block.Numbering
{
    /// <summary>
    /// 在「属性块批量编号」对话框的第一个分组框（"选择要编号的块（两步）"）右上角
    /// 挂一个「编号用法说明」按钮，点击弹出 <see cref="NumberingHelpForm"/>。
    ///
    /// 按钮与对话框分处两个文件：NumberingOptionsForm 的 BuildSelectionGroup() 末尾
    /// 只调用一行 Attach(box)，按钮的创建与点击逻辑全部留在本文件里 ——
    /// 面板的布局代码因此不必为这一个按钮改动。
    ///
    /// 只使用纯 WinForms API，不引用 AutoCAD 的对话框接口（见 NumberingHelpForm 的说明）。
    /// </summary>
    internal static class NumberingHelpButton
    {
        private const string ButtonText = "编号用法说明";
        private const int ButtonWidth = 112;
        private const int ButtonHeight = 28;

        // 右边距：组框内容区本身还有 8px 内边距，8 + 6 = 14px，
        // 与左边"① 在图纸中选一个样板块"的左边距（8px 内边距 + 该按钮 6px 外边距）对称。
        private const int RightMargin = 6;

        /// <summary>把「编号用法说明」按钮挂到分组框右上角。</summary>
        internal static void Attach(GroupBox box)
        {
            if (box == null)
            {
                return;
            }

            // 宿主面板 Dock=Right：Attach 是在 box.Controls.Add(flow) 之后调用的，
            // WinForms 停靠按 z 序"从后往前"布局，最后加的控件先占边，
            // 于是 flow（Dock=Fill）自己缩到左边，两者不会重叠。
            Panel host = new Panel();
            host.Dock = DockStyle.Right;
            host.Width = ButtonWidth + RightMargin + 4;
            host.Margin = new Padding(0);

            Button help = new Button();
            help.Text = ButtonText;
            help.Width = ButtonWidth;
            help.Height = ButtonHeight;
            help.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            help.Click += OnHelpClicked;
            host.Controls.Add(help);

            box.Controls.Add(host);

            // 纵向与"① 在图纸中选一个样板块"对齐。该 y 值要等组框布局完成才准确，
            // 所以既在挂载时算一次，也挂到 Layout 上随尺寸变化重算。
            box.Layout += delegate { AlignHelp(help, host, box); };
            AlignHelp(help, host, box);
        }

        private static void AlignHelp(Button help, Panel host, GroupBox box)
        {
            try
            {
                help.Left = Math.Max(0, host.ClientSize.Width - help.Width - RightMargin);

                // 坐标系要小心：help.Top 是相对 host（宿主面板）的，而"①…"按钮的位置相对 box。
                // 两边都先换算到 box 客户区再相减，否则会整整多出一个 host.Top
                //（实测：host.Top = 18px，按钮就低 18px，即用户看到的"没对齐"）。
                int top = 0; // 兜底：host 从组框内容区顶开始，贴 0 就与旁边按钮行齐平
                Button neighbour = FindFirstButton(box, help);
                if (neighbour != null)
                {
                    int neighbourTop = TopInBox(neighbour, box);
                    int hostTop = TopInBox(host, box);
                    if (neighbourTop >= 0 && hostTop >= 0)
                    {
                        top = neighbourTop - hostTop;
                    }
                }

                int maxTop = host.ClientSize.Height - help.Height;
                if (maxTop < 0)
                {
                    maxTop = 0;
                }

                help.Top = Math.Max(0, Math.Min(top, maxTop));
            }
            catch (Exception)
            {
                // 布局期间控件可能还没句柄，忽略即可，不影响正常显示
            }
        }

        /// <summary>
        /// 控件 <paramref name="child"/> 的顶边在 <paramref name="box"/> 客户区里的 y 坐标；
        /// 若它并不在 <paramref name="box"/> 里则返回 -1。用于跨容器比较纵向位置（不依赖句柄）。
        /// </summary>
        private static int TopInBox(Control child, Control box)
        {
            int top = 0;
            for (Control control = child; control != null; control = control.Parent)
            {
                if (ReferenceEquals(control, box))
                {
                    return top;
                }

                top += control.Top;
            }

            return -1;
        }

        /// <summary>找分组框里第一个按钮（跳过我们自己挂上去的那个）。</summary>
        private static Button FindFirstButton(Control parent, Control exclude)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (Control child in parent.Controls)
            {
                if (ReferenceEquals(child, exclude))
                {
                    continue;
                }

                Button button = child as Button;
                if (button != null)
                {
                    return button;
                }

                Button nested = FindFirstButton(child, exclude);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static void OnHelpClicked(object sender, EventArgs e)
        {
            Control source = sender as Control;
            NumberingHelpForm.ShowHelp(source == null ? null : source.FindForm());
        }
    }
}
