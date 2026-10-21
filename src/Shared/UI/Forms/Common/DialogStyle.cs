using System.Drawing;
using System.Windows.Forms;

namespace ChemForge.UI.Forms.Common
{
    /// <summary>
    /// 各个面板共用的一套观感：强调色、组框与按钮的体格。
    ///
    /// 这几个值原先在 TCOLOR、BLKATTCHANGE、BLOCKNUMBER 三个面板里各抄了一份，
    /// 抄的时候是一字不差，可只要有人改其中一个，三个窗口就会慢慢长歪 ——
    /// 而"三个窗口长得像一家人"恰恰是用户唯一能直接看出来的东西。
    ///
    /// 这里只放**确实一模一样**的东西：可以用一个值加参数表达完的。
    /// 每个面板自己的排版（行高、列宽、控件顺序）仍然留在各自的窗体里。
    /// </summary>
    internal static class DialogStyle
    {
        /// <summary>品牌蓝：所有强调文字（选择信息、样例、范围提示）都用它。</summary>
        internal static readonly Color Accent = Color.FromArgb(0, 90, 160);

        /// <summary>辅助说明文字的颜色（"以上为预览""不影响已插入的编号"之类）。</summary>
        internal static readonly Color Hint = Color.DimGray;

        /// <summary>窗体四周的留白。</summary>
        internal const int FormPadding = 8;

        /// <summary>组框之间、窗体与组框之间的竖向间距。</summary>
        internal const int GroupGap = 6;

        /// <summary>按钮统一高度（三个面板都是 28）。</summary>
        internal const int ButtonHeight = 28;

        /// <summary>
        /// 组框的统一外观：撑满容器、与下一组留 <see cref="GroupGap"/>、内边距 8/2/8/4。
        ///
        /// <paramref name="height"/> 给 0 表示不指定高度、由所在容器分配（TCOLOR 用这种方式）；
        /// 给定高度则用于「按内容行数排好的一组」（BLKATTCHANGE、BLOCKNUMBER 用这种方式）。
        /// </summary>
        internal static GroupBox NewGroup(string caption, int height = 0)
        {
            GroupBox box = new GroupBox
            {
                Text = caption,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, GroupGap),
                Padding = new Padding(8, 2, 8, 4)
            };

            if (height > 0)
            {
                box.Height = height;
            }

            return box;
        }

        /// <summary>按钮的统一外观：高 <see cref="ButtonHeight"/>、与左边控件留 6px。</summary>
        internal static Button NewButton(string caption, int width)
        {
            return new Button
            {
                Text = caption,
                Width = width,
                Height = ButtonHeight,
                Margin = new Padding(6, 0, 0, 0)
            };
        }

        /// <summary>一组控件前面的小标题：自适应宽度、基线对齐、可按需留左间距。</summary>
        internal static Label NewCaption(string caption, int leftSpacing)
        {
            return new Label
            {
                Text = caption,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(leftSpacing, 7, 4, 0)
            };
        }
    }
}
