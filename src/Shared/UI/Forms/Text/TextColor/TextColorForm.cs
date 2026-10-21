using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ChemForge.Core.Text.TextColor;
using ChemForge.Models.Text.TextColor;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace ChemForge.UI.Forms.Text.TextColor
{
    /// <summary>对话框关闭时要去做的动作。</summary>
    public enum TextColorDialogAction
    {
        /// <summary>确定，按当前选项执行。</summary>
        Apply,

        /// <summary>要去调系统的颜色选择框，回来再弹一次本对话框。</summary>
        PickColor,

        /// <summary>要去图上吸取颜色，回来再弹一次本对话框。</summary>
        Eyedropper,

        /// <summary>要去图上框选文字，回来再弹一次本对话框。</summary>
        PickObjects,

        /// <summary>取消，什么都不做。</summary>
        Cancel
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
    public sealed class TextColorForm : Form
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

        /// <summary>品牌蓝：与另两个面板的强调色一致（选择信息、标题）。</summary>
        private static readonly Color AccentColor = Color.FromArgb(0, 90, 160);

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

        // ==================================================================
        //  界面
        // ==================================================================

        private void BuildLayout()
        {
            Text = "批量修改多行文字颜色";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;

            // 排版体例照 BLKATTCHANGE（BatchAttrChangeForm）与图块编号面板（NumberingOptionsForm）：
            // 窗体留 8px 外边距，一个根 TableLayoutPanel 纵排各组框，组框之间 6px；
            // 按钮 28 高，"确定" 100 / "取消" 90 靠右下，状态与提示文字走 DimGray。
            // 三组控件都不再写死 Left/Top —— 组框 Dock=Fill，窗体拉大时铺满、拖动即可调整。
            ClientSize = new Size(520, 604);
            MinimumSize = new Size(500, 560);
            Padding = new Padding(8);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f));  // ① 选择（104 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f));  // 基本颜色（104 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 索引色（吃掉剩下的高度）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f));  // 处理选项（104 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // 当前选择 + 确定/取消

            Button okButton;
            Button cancelButton;

            root.Controls.Add(BuildSelectionGroup(), 0, 0);
            root.Controls.Add(BuildBasicColorGroup(), 0, 1);
            root.Controls.Add(BuildColorListGroup(), 0, 2);
            root.Controls.Add(BuildOptionGroup(), 0, 3);
            root.Controls.Add(BuildBottomPanel(out okButton, out cancelButton), 0, 4);

            Controls.Add(root);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        /// <summary>组框的统一外观：Dock=Fill、与下一组留 6px、内边距 8/2/8/4。</summary>
        private static GroupBox NewGroup(string caption)
        {
            return new GroupBox
            {
                Text = caption,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 2, 8, 4)
            };
        }

        private static Button NewButton(string caption, int width)
        {
            return new Button
            {
                Text = caption,
                Width = width,
                Height = 28,
                Margin = new Padding(6, 0, 0, 0)
            };
        }

        private static Label NewCaption(string caption, int leftSpacing)
        {
            return new Label
            {
                Text = caption,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(leftSpacing, 7, 4, 0)
            };
        }

        /// <summary>① 选择要改色的文字：按钮 + 当前计数 + 一行说明。</summary>
        private Control BuildSelectionGroup()
        {
            GroupBox group = NewGroup("① 选择要改色的文字");

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // 命令先弹面板，选择由用户在面板上发起（照 BLKATTCHANGE 的做法）：
            // 点它就关掉面板去图上框选，选完命令会带着新的计数再弹一次本面板。
            Button pickObjectsButton = new Button
            {
                Name = "PickObjectsButton",
                Text = "① 在图纸中选择要改色的文字",
                Width = 240,
                Height = 28,
                Margin = new Padding(0)
            };

            pickObjectsButton.Click += delegate { AcceptPickObjects(); };

            FlowLayoutPanel buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            buttonRow.Controls.Add(pickObjectsButton);
            layout.Controls.Add(buttonRow, 0, 0);

            _infoLabel = new Label
            {
                Name = "SelectionInfo",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = AccentColor,
                AutoEllipsis = true,
                Text = DescribeSelection()
            };

            layout.Controls.Add(_infoLabel, 0, 1);

            Label hint = new Label
            {
                Name = "SelectionHint",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoEllipsis = true,
                Text = "只处理多行文字 / 单行文字 / 多重引线；框选完成后计数会跟着刷新。"
            };

            layout.Controls.Add(hint, 0, 2);

            group.Controls.Add(layout);
            return group;
        }

        /// <summary>基本颜色：6 列 × 3 行的色块。</summary>
        private Control BuildBasicColorGroup()
        {
            GroupBox group = NewGroup("基本颜色（单击=选中，双击=确定）");

            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            for (int column = 0; column < 6; column++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6f));
            }

            for (int row = 0; row < 3; row++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3f));
            }

            for (int row = 0; row < SwatchRows.Length; row++)
            {
                for (int column = 0; column < SwatchRows[row].Length; column++)
                {
                    string caption = SwatchRows[row][column];
                    if (caption.Length == 0)
                    {
                        grid.Controls.Add(new Label(), column, row);
                        continue;
                    }

                    Button button = new Button
                    {
                        Text = caption,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(2),
                        FlatStyle = FlatStyle.Flat,
                        Tag = SwatchAci(row, column)
                    };

                    int aci = SwatchAci(row, column);
                    button.BackColor = SwatchColor(aci);
                    button.ForeColor = ContrastText(button.BackColor);

                    button.Click += delegate { OnSwatchClicked(aci); };
                    button.DoubleClick += delegate
                    {
                        OnSwatchClicked(aci);
                        Accept();
                    };

                    _swatches.Add(button);
                    grid.Controls.Add(button, column, row);
                }
            }

            group.Controls.Add(grid);
            return group;
        }

        /// <summary>索引色列表 + 色号/真彩色输入 + 取色按钮。</summary>
        private Control BuildColorListGroup()
        {
            GroupBox group = NewGroup("索引颜色 / 真彩色（双击列表=确定）");

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

            _colorList = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                Font = new Font("Consolas", 9f),
                Margin = new Padding(0, 2, 0, 2)
            };

            // 列表顺序是"随层、随块、然后 1..255"，与颜色号本身不是同一个下标，
            // 所以每一项都把色号存在 Tag 里，不去做下标换算。
            _colorList.Items.Add(DescribeListEntry(AciColorNames.ByLayerValue));
            _colorList.Items.Add(DescribeListEntry(AciColorNames.ByBlockValue));
            for (int aci = AciColorNames.MinIndexedColor; aci <= AciColorNames.MaxIndexedColor; aci++)
            {
                _colorList.Items.Add(DescribeListEntry(aci));
            }

            _colorList.SelectedIndexChanged += delegate
            {
                if (_loading || _colorList.SelectedIndex < 0)
                {
                    return;
                }

                OnListSelected(_colorList.SelectedIndex);
            };

            _colorList.DoubleClick += delegate
            {
                if (_colorList.SelectedIndex >= 0)
                {
                    OnListSelected(_colorList.SelectedIndex);
                    Accept();
                }
            };

            layout.Controls.Add(_colorList, 0, 0);

            FlowLayoutPanel numberRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            _aciBox = new TextBox { Width = 56, Margin = new Padding(0, 3, 18, 0) };
            _redBox = new TextBox { Width = 48, Margin = new Padding(0, 3, 8, 0) };
            _greenBox = new TextBox { Width = 48, Margin = new Padding(0, 3, 8, 0) };
            _blueBox = new TextBox { Width = 48, Margin = new Padding(0, 3, 0, 0) };

            _aciBox.TextChanged += delegate { OnAciTyped(); };
            _redBox.TextChanged += delegate { OnRgbTyped(); };
            _greenBox.TextChanged += delegate { OnRgbTyped(); };
            _blueBox.TextChanged += delegate { OnRgbTyped(); };

            numberRow.Controls.Add(NewCaption("颜色号：", 8));
            numberRow.Controls.Add(_aciBox);
            numberRow.Controls.Add(NewCaption("真彩色 RGB：", 10));
            numberRow.Controls.Add(_redBox);
            numberRow.Controls.Add(_greenBox);
            numberRow.Controls.Add(_blueBox);

            layout.Controls.Add(numberRow, 0, 1);

            FlowLayoutPanel actionRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            Button pickButton = new Button
            {
                Text = "更多颜色...",
                Width = 140,
                Height = 28,
                Margin = new Padding(0)
            };

            Button eyedButton = new Button
            {
                Text = "图上吸取...",
                Width = 140,
                Height = 28,
                Margin = new Padding(8, 0, 0, 0)
            };

            pickButton.Click += delegate { Action = TextColorDialogAction.PickColor; Close(); };
            eyedButton.Click += delegate { Action = TextColorDialogAction.Eyedropper; Close(); };

            actionRow.Controls.Add(pickButton);
            actionRow.Controls.Add(eyedButton);
            layout.Controls.Add(actionRow, 0, 2);

            group.Controls.Add(layout);
            return group;
        }

        /// <summary>处理选项：两列三行，最长的一条独占整行（怎么都不会折行）。</summary>
        private Control BuildOptionGroup()
        {
            GroupBox group = NewGroup("处理选项");

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));

            // 四个复选框一律 AutoSize=false：高度由行高定死，
            // 文字宽度由 TextColorFormLayoutTests 用 TextRenderer 量着锁住。
            // 早先不设 AutoSize 又把最长的一条塞进 210px 的右列，它折成两行、
            // 第二行被分组框裁掉还压到下面一行 —— 用户截图里那团重影。
            _stripCheck = NewCheck("清除文字内部的颜色覆盖代码");
            _singleLineCheck = NewCheck("同时处理单行文字");
            _multiLeaderCheck = NewCheck("同时处理多重引线");
            _blockCheck = NewCheck("深入块定义内部（影响所有同名块参照）");

            _stripCheck.CheckedChanged += delegate { RefreshState(); };
            _singleLineCheck.CheckedChanged += delegate { RefreshState(); };
            _multiLeaderCheck.CheckedChanged += delegate { RefreshState(); };
            _blockCheck.CheckedChanged += delegate { RefreshState(); };

            layout.Controls.Add(_stripCheck, 0, 0);
            layout.Controls.Add(_singleLineCheck, 1, 0);
            layout.Controls.Add(_multiLeaderCheck, 0, 1);
            layout.Controls.Add(_blockCheck, 0, 2);
            layout.SetColumnSpan(_blockCheck, 2);

            group.Controls.Add(layout);
            return group;
        }

        private static CheckBox NewCheck(string caption)
        {
            return new CheckBox
            {
                Text = caption,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 22,
                Margin = new Padding(0, 1, 12, 1),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        /// <summary>最下面一行：左边"当前选择"，右边确定/取消。</summary>
        private Control BuildBottomPanel(out Button okButton, out Button cancelButton)
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _modeLabel = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 0, 12, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                AutoEllipsis = true
            };

            panel.Controls.Add(_modeLabel, 0, 0);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            okButton = NewButton("确定", 100);
            cancelButton = NewButton("取消", 90);

            okButton.Click += delegate { Accept(); };
            cancelButton.Click += delegate { Action = TextColorDialogAction.Cancel; Close(); };

            buttons.Controls.Add(okButton);
            buttons.Controls.Add(cancelButton);
            panel.Controls.Add(buttons, 1, 0);

            return panel;
        }

        private static int SwatchAci(int row, int column)
        {
            if (row == 0)
            {
                return column + 1;
            }

            if (row == 1)
            {
                // 7 白/黑、8 深灰、9 浅灰、随块、随层
                int[] map = { 7, 8, 9, AciColorNames.ByBlockValue, AciColorNames.ByLayerValue, -1 };
                return map[column];
            }

            return 250 + column;
        }

        private static string DescribeListEntry(int aci)
        {
            return AciColorNames.ListItem(aci);
        }

        private Color SwatchColor(int aci)
        {
            if (aci == AciColorNames.ByBlockValue)
            {
                return Color.FromArgb(90, 90, 90);
            }

            if (aci == AciColorNames.ByLayerValue)
            {
                return Color.FromArgb(200, 200, 200);
            }

            return _aciColorResolver(aci);
        }

        /// <summary>
        /// 默认取色：问 AutoCAD 的色表。
        ///
        /// 单独成一个方法，是为了让不想加载 acmgd 的调用方（测试）能整体绕开它：
        /// 只有真的走默认取色时，这个方法才会被 JIT。
        /// </summary>
        private static Color ResolveAciColorFromAutoCad(int aci)
        {
            try
            {
                return AcColor.FromColorIndex(AcColorMethod.ByAci, (short)aci).ColorValue;
            }
            catch (Exception)
            {
                return Color.White;
            }
        }

        private static Color ContrastText(Color background)
        {
            int luminance = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return luminance > 140 ? Color.Black : Color.White;
        }

        private string DescribeSelection()
        {
            int total = _context.MTextCount + _context.TextCount + _context.MultiLeaderCount;
            if (total == 0)
            {
                return "尚未选择文字：请点上面的「① 在图纸中选择要改色的文字」。";
            }

            return "已选文字：多行文字 "
                + _context.MTextCount.ToString(CultureInfo.InvariantCulture)
                + " 个 / 单行文字 "
                + _context.TextCount.ToString(CultureInfo.InvariantCulture)
                + " 个 / 多重引线 "
                + _context.MultiLeaderCount.ToString(CultureInfo.InvariantCulture)
                + " 个";
        }

        // ==================================================================
        //  状态
        // ==================================================================

        private void LoadFrom(TextColorOptions options)
        {
            if (options != null)
            {
                _mode = options.Mode;
                _aci = options.AciIndex;
                _red = options.Red;
                _green = options.Green;
                _blue = options.Blue;
            }

            _loading = true;
            try
            {
                _aciBox.Text = _aci.ToString(CultureInfo.InvariantCulture);
                _redBox.Text = _red.ToString(CultureInfo.InvariantCulture);
                _greenBox.Text = _green.ToString(CultureInfo.InvariantCulture);
                _blueBox.Text = _blue.ToString(CultureInfo.InvariantCulture);

                _stripCheck.Checked = options == null || options.StripColorCodes;
                _singleLineCheck.Checked = options == null || options.IncludeSingleLineText;
                _multiLeaderCheck.Checked = options != null && options.IncludeMultiLeader;
                _blockCheck.Checked = options != null && options.IncludeBlockDefinitions;
            }
            finally
            {
                _loading = false;
            }

            SyncListToAci();
        }

        /// <summary>把列表光标挪到当前色号上。</summary>
        private void SyncListToAci()
        {
            if (_mode != TextColorMode.Aci
                || _aci < AciColorNames.MinIndexedColor
                || _aci > AciColorNames.MaxIndexedColor)
            {
                return;
            }

            _loading = true;
            try
            {
                _colorList.SelectedIndex = AciColorNames.AciToListIndex(_aci);
            }
            finally
            {
                _loading = false;
            }
        }

        private void RefreshState()
        {
            string mode;
            switch (_mode)
            {
                case TextColorMode.Aci:
                    mode = "索引色 " + _aci.ToString(CultureInfo.InvariantCulture)
                        + "（" + AciColorNames.Name(_aci) + "）";
                    break;
                case TextColorMode.TrueColor:
                    mode = "真彩色 RGB(" + _red.ToString(CultureInfo.InvariantCulture)
                        + "," + _green.ToString(CultureInfo.InvariantCulture)
                        + "," + _blue.ToString(CultureInfo.InvariantCulture) + ")";
                    break;
                case TextColorMode.ByLayer:
                    mode = "随层 ByLayer";
                    break;
                default:
                    mode = "随块 ByBlock";
                    break;
            }

            _modeLabel.Text = "当前选择：" + mode;

            foreach (Button button in _swatches)
            {
                int aci = (int)button.Tag;
                bool selected = _mode == TextColorMode.Aci && aci == _aci
                    || _mode == TextColorMode.ByBlock && aci == AciColorNames.ByBlockValue
                    || _mode == TextColorMode.ByLayer && aci == AciColorNames.ByLayerValue;

                button.FlatAppearance.BorderSize = selected ? 3 : 1;
                button.FlatAppearance.BorderColor = selected ? Color.DodgerBlue : Color.Gray;
            }
        }

        // ==================================================================
        //  控件事件
        // ==================================================================

        private void OnSwatchClicked(int aci)
        {
            if (aci < 0)
            {
                return;
            }

            if (aci == AciColorNames.ByLayerValue)
            {
                _mode = TextColorMode.ByLayer;
            }
            else if (aci == AciColorNames.ByBlockValue)
            {
                _mode = TextColorMode.ByBlock;
            }
            else
            {
                _mode = TextColorMode.Aci;
                _aci = aci;
                _loading = true;
                try
                {
                    _aciBox.Text = aci.ToString(CultureInfo.InvariantCulture);
                }
                finally
                {
                    _loading = false;
                }

                SyncListToAci();
            }

            RefreshState();
        }

        private void OnListSelected(int index)
        {
            int aci = AciColorNames.ListIndexToAci(index);

            if (aci == AciColorNames.ByLayerValue)
            {
                _mode = TextColorMode.ByLayer;
            }
            else if (aci == AciColorNames.ByBlockValue)
            {
                _mode = TextColorMode.ByBlock;
            }
            else
            {
                _mode = TextColorMode.Aci;
                _aci = aci;
                _loading = true;
                try
                {
                    _aciBox.Text = aci.ToString(CultureInfo.InvariantCulture);
                }
                finally
                {
                    _loading = false;
                }
            }

            RefreshState();
        }

        private void OnAciTyped()
        {
            if (_loading)
            {
                return;
            }

            // 只在输入能构成合法索引色时才认账：
            // 用户清空编辑框准备重打的过程中会短暂出现空串，
            // 那时候若把模式切走，光标所在的这一组控件会突然失效。
            string text = _aciBox.Text;
            if (!TextColorContent.IsDigitsOnly(text))
            {
                return;
            }

            int value = TextColorContent.ParseByte(text, _aci);
            if (value < AciColorNames.MinIndexedColor || value > AciColorNames.MaxIndexedColor)
            {
                return;
            }

            _mode = TextColorMode.Aci;
            _aci = value;
            SyncListToAci();
            RefreshState();
        }

        private void OnRgbTyped()
        {
            if (_loading)
            {
                return;
            }

            if (!TextColorContent.IsDigitsOnly(_redBox.Text)
                && !TextColorContent.IsDigitsOnly(_greenBox.Text)
                && !TextColorContent.IsDigitsOnly(_blueBox.Text))
            {
                return;
            }

            _red = TextColorContent.ParseByte(_redBox.Text, _red);
            _green = TextColorContent.ParseByte(_greenBox.Text, _green);
            _blue = TextColorContent.ParseByte(_blueBox.Text, _blue);
            _mode = TextColorMode.TrueColor;
            RefreshState();
        }

        private void Accept()
        {
            TextColorOptions options;
            if (!TryBuildOptions(out options))
            {
                return;
            }

            Options = options;
            Action = TextColorDialogAction.Apply;
            Close();
        }

        /// <summary>
        /// 「① 在图纸中选择要改色的文字」：把面板上的选项先交回命令，
        /// 再由命令关掉面板去框选，选完带着新的计数再弹回来 ——
        /// 去选一次文字，已经调好的颜色不会丢。
        /// </summary>
        private void AcceptPickObjects()
        {
            TextColorOptions options;
            if (!TryBuildOptions(out options))
            {
                return;
            }

            Options = options;
            Action = TextColorDialogAction.PickObjects;
            Close();
        }

        /// <summary>
        /// 把面板上的选择拼成一份选项。色号非法时弹框拦住并返回 false ——
        /// 「确定」与「① 在图纸中选择…」都走这一条，规则保持一致。
        /// </summary>
        private bool TryBuildOptions(out TextColorOptions options)
        {
            options = new TextColorOptions
            {
                Mode = _mode,
                AciIndex = _aci,
                Red = _red,
                Green = _green,
                Blue = _blue,
                StripColorCodes = _stripCheck.Checked,
                IncludeSingleLineText = _singleLineCheck.Checked,
                IncludeMultiLeader = _multiLeaderCheck.Checked,
                IncludeBlockDefinitions = _blockCheck.Checked
            };

            options.Normalize();

            if (options.Mode == TextColorMode.Aci && !options.HasValidAciIndex())
            {
                MessageBox.Show(
                    this,
                    "索引色号必须是 1-255 之间的整数。",
                    "批量修改多行文字颜色",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return false;
            }

            return true;
        }
    }
}
