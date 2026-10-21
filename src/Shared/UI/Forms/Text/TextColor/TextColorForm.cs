using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ChemForge.Core.Text.TextColor;
using ChemForge.Models.Text.TextColor;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using static ChemForge.UI.Forms.Common.DialogStyle;

namespace ChemForge.UI.Forms.Text.TextColor
{
    /// <summary>对话框关闭时要去做的动作。</summary>
    public enum TextColorDialogAction
    {
        /// <summary>
        /// 取消，什么都不做。
        ///
        /// **显式取 0**：面板万一没能弹出来（<c>ShowModalDialog</c> 抛异常、异常被
        /// <c>CommandHost.ShowDialogSafely</c> 吞掉），<c>form.Action</c> 就是枚举的默认值。
        /// 默认值若是 <see cref="Apply"/>，命令会拿"用户根本没看过的默认色"直接改图 ——
        /// 同仓库的 <c>BatchAttrChangeAction</c> 也是把 Cancel 放在 0。
        /// </summary>
        Cancel = 0,

        /// <summary>确定，按当前选项执行。</summary>
        Apply = 1,

        /// <summary>要去调系统的颜色选择框，回来再弹一次本对话框。</summary>
        PickColor = 2,

        /// <summary>要去图上吸取颜色，回来再弹一次本对话框。</summary>
        Eyedropper = 3,

        /// <summary>要去图上框选文字，回来再弹一次本对话框。</summary>
        PickObjects = 4
    }

    /// <summary>打开对话框所需的信息。</summary>
    public sealed class TextColorFormContext
    {
        public int MTextCount { get; set; }

        public int TextCount { get; set; }

        public int MultiLeaderCount { get; set; }

        /// <summary>上一轮的选择，用来回填控件。</summary>
        public TextColorOptions Options { get; set; }
    }

    /// <summary>
    /// 批量修改多行文字颜色的面板。
    ///
    /// 界面刻意做成"两组控件都能直接操作、谁最后动谁算数"：
    /// 早期版本用灰显来互斥索引色与真彩色两组控件，
    /// 结果把两个入口本身也一起锁死了，用户在两组之间来回切得先退出去重来。
    /// 现在改成不互斥 —— 点了色块、列表或色号框就是索引色，
    /// 动了 RGB 就是真彩色，随层/随块各占一个按钮。
    /// </summary>
    public sealed partial class TextColorForm : Form
    {
        /// <summary>色块：短名 → 索引色号。</summary>
        private static readonly string[][] SwatchRows =
        {
            new[] { "1 红", "2 黄", "3 绿", "4 青", "5 蓝", "6 洋红" },
            new[] { "7 白/黑", "8 深灰", "9 浅灰", "随块", "随层", string.Empty },
            new[] { "250 黑", "251 暗灰", "252 中灰", "253 浅灰", "254 灰白", "255 白" }
        };

        private readonly TextColorFormContext _context;
        private readonly List<Button> _swatches = new List<Button>();
        private readonly Func<int, Color> _aciColorResolver;

        private TextColorMode _mode = TextColorMode.Aci;
        private int _aci = 1;
        private int _red = 255;
        private int _green;
        private int _blue;

        private Label _infoLabel;
        private ListBox _colorList;
        private TextBox _aciBox;
        private TextBox _redBox;
        private TextBox _greenBox;
        private TextBox _blueBox;
        private CheckBox _stripCheck;
        private CheckBox _singleLineCheck;
        private CheckBox _multiLeaderCheck;
        private CheckBox _blockCheck;
        private Label _modeLabel;

        /// <summary>回填控件时抑制事件，免得把状态又改回去。</summary>
        private bool _loading;

        public TextColorForm(TextColorFormContext context)
            : this(context, null)
        {
        }

        /// <summary>
        /// 指定色块的取色方式。
        ///
        /// 传 null 就是问 AutoCAD 色表；离线测试（排版测量、界面出图）可以传一个桩，
        /// 这样构造本窗体完全不必加载 acmgd ——
        /// <see cref="ResolveAciColorFromAutoCad"/> 里的 <c>Color.FromColorIndex</c>
        /// 是本窗体唯一碰 AutoCAD 的地方。
        /// </summary>
        public TextColorForm(TextColorFormContext context, Func<int, Color> aciColorResolver)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _context = context;
            _aciColorResolver = aciColorResolver ?? ResolveAciColorFromAutoCad;

            BuildLayout();
            LoadFrom(context.Options);
            RefreshState();
        }

        /// <summary>关闭时要去做的动作。</summary>
        public TextColorDialogAction Action { get; private set; }

        /// <summary>用户在面板上定下来的选项。</summary>
        public TextColorOptions Options { get; private set; }

    }
}
