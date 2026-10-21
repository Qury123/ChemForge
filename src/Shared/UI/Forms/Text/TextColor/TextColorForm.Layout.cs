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
    public sealed partial class TextColorForm
    {
        // ==================================================================
        //  界面
        // ==================================================================

        /// <summary>
        /// 最小尺寸：设计尺寸（520×604）的 96% × 93%，约 500×560。
        ///
        /// 不能在这里写死像素：<c>AutoScaleMode.Font</c> 会按字体/DPI 把 ClientSize
        /// 一起缩放（150% 下 520×604 变成 780×906），而 <c>MinimumSize</c> 是屏幕像素、
        /// 不参与缩放。写死 500×560 就等于在缩放后的窗口上允许用户把它缩到设计尺寸的一半，
        /// 组框立刻挤在一起 —— 而排版测试正是在设计尺寸上量像素的。
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // 到这里缩放已经完成，ClientSize 就是当前 DPI 下的真实客户区。
            MinimumSize = new Size(
                ClientSize.Width * 96 / 100,
                ClientSize.Height * 93 / 100);
        }

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
            Padding = new Padding(FormPadding);

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
                ForeColor = Accent,
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

    }
}
