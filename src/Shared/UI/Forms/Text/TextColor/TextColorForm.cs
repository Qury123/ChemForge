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
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _context = context;

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
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(470, 470);

            _infoLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(8, 6, 8, 0),
                Text = DescribeSelection()
            };

            GroupBox basicGroup = new GroupBox
            {
                Text = "基本颜色（单击=选中，双击=确定）",
                Left = 8,
                Top = 40,
                Width = 454,
                Height = 104
            };

            TableLayoutPanel basicGrid = new TableLayoutPanel
            {
                Left = 8,
                Top = 20,
                Width = 438,
                Height = 76,
                ColumnCount = 6,
                RowCount = 3
            };

            for (int column = 0; column < 6; column++)
            {
                basicGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6f));
            }

            for (int row = 0; row < 3; row++)
            {
                basicGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3f));
            }

            for (int row = 0; row < SwatchRows.Length; row++)
            {
                for (int column = 0; column < SwatchRows[row].Length; column++)
                {
                    string caption = SwatchRows[row][column];
                    if (caption.Length == 0)
                    {
                        basicGrid.Controls.Add(new Label(), column, row);
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
                    basicGrid.Controls.Add(button, column, row);
                }
            }

            basicGroup.Controls.Add(basicGrid);

            GroupBox colorGroup = new GroupBox
            {
                Text = "索引颜色 / 真彩色（双击列表=确定）",
                Left = 8,
                Top = 150,
                Width = 454,
                Height = 214
            };

            _colorList = new ListBox
            {
                Left = 10,
                Top = 20,
                Width = 434,
                Height = 120,
                IntegralHeight = false,
                Font = new Font("Consolas", 9f)
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

            Label aciLabel = new Label
            {
                Text = "颜色号：",
                Left = 10,
                Top = 150,
                Width = 58,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _aciBox = new TextBox { Left = 68, Top = 148, Width = 52 };

            Label rgbLabel = new Label
            {
                Text = "真彩色 RGB：",
                Left = 132,
                Top = 150,
                Width = 86,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _redBox = new TextBox { Left = 220, Top = 148, Width = 46 };
            _greenBox = new TextBox { Left = 270, Top = 148, Width = 46 };
            _blueBox = new TextBox { Left = 320, Top = 148, Width = 46 };

            _aciBox.TextChanged += delegate { OnAciTyped(); };
            _redBox.TextChanged += delegate { OnRgbTyped(); };
            _greenBox.TextChanged += delegate { OnRgbTyped(); };
            _blueBox.TextChanged += delegate { OnRgbTyped(); };

            Button pickButton = new Button
            {
                Text = "更多颜色...",
                Left = 10,
                Top = 178,
                Width = 150,
                Height = 26
            };

            Button eyedButton = new Button
            {
                Text = "图上吸取...",
                Left = 168,
                Top = 178,
                Width = 150,
                Height = 26
            };

            pickButton.Click += delegate { Action = TextColorDialogAction.PickColor; Close(); };
            eyedButton.Click += delegate { Action = TextColorDialogAction.Eyedropper; Close(); };

            colorGroup.Controls.Add(_colorList);
            colorGroup.Controls.Add(aciLabel);
            colorGroup.Controls.Add(_aciBox);
            colorGroup.Controls.Add(rgbLabel);
            colorGroup.Controls.Add(_redBox);
            colorGroup.Controls.Add(_greenBox);
            colorGroup.Controls.Add(_blueBox);
            colorGroup.Controls.Add(pickButton);
            colorGroup.Controls.Add(eyedButton);

            GroupBox optionGroup = new GroupBox
            {
                Text = "处理选项",
                Left = 8,
                Top = 370,
                Width = 454,
                Height = 60
            };

            _stripCheck = new CheckBox
            {
                Text = "清除文字内部的颜色覆盖代码",
                Left = 12,
                Top = 18,
                Width = 220
            };

            _singleLineCheck = new CheckBox
            {
                Text = "同时处理单行文字",
                Left = 240,
                Top = 18,
                Width = 200
            };

            _multiLeaderCheck = new CheckBox
            {
                Text = "同时处理多重引线",
                Left = 12,
                Top = 36,
                Width = 200
            };

            _blockCheck = new CheckBox
            {
                Text = "深入块定义内部（影响所有同名块参照）",
                Left = 240,
                Top = 36,
                Width = 210
            };

            _stripCheck.CheckedChanged += delegate { RefreshState(); };
            _singleLineCheck.CheckedChanged += delegate { RefreshState(); };
            _multiLeaderCheck.CheckedChanged += delegate { RefreshState(); };
            _blockCheck.CheckedChanged += delegate { RefreshState(); };

            optionGroup.Controls.Add(_stripCheck);
            optionGroup.Controls.Add(_singleLineCheck);
            optionGroup.Controls.Add(_multiLeaderCheck);
            optionGroup.Controls.Add(_blockCheck);

            _modeLabel = new Label
            {
                Left = 12,
                Top = 434,
                Width = 330,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Button okButton = new Button
            {
                Text = "确定",
                Left = 350,
                Top = 432,
                Width = 52,
                Height = 26
            };

            Button cancelButton = new Button
            {
                Text = "取消",
                Left = 410,
                Top = 432,
                Width = 52,
                Height = 26
            };

            okButton.Click += delegate { Accept(); };
            cancelButton.Click += delegate { Action = TextColorDialogAction.Cancel; Close(); };

            Controls.Add(_infoLabel);
            Controls.Add(basicGroup);
            Controls.Add(colorGroup);
            Controls.Add(optionGroup);
            Controls.Add(_modeLabel);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
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

        private static Color SwatchColor(int aci)
        {
            if (aci == AciColorNames.ByBlockValue)
            {
                return Color.FromArgb(90, 90, 90);
            }

            if (aci == AciColorNames.ByLayerValue)
            {
                return Color.FromArgb(200, 200, 200);
            }

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
            TextColorOptions options = new TextColorOptions
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

                return;
            }

            Options = options;
            Action = TextColorDialogAction.Apply;
            Close();
        }
    }
}
