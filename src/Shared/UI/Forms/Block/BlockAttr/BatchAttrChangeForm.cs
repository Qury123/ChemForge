using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ChemForge.Models.Block.BlockAttr;

namespace ChemForge.UI.Forms.Block.BlockAttr
{
    /// <summary>
    /// 「确定」之后面板要干什么。
    /// 沿用 图块编号面板的 <c>Action</c> 约定：0 恒为取消，调用方用
    /// <c>if (form.Action == BatchAttrChangeAction.Cancel)</c> 一句判空。
    ///
    /// 2 / 3 两个值同样是照搬 图块编号的
    /// <c>NumberingAction.PickSample</c> / <c>PickRange</c>：
    /// 面板先关掉、回图纸拾取，再由命令层新建一个面板弹出来。
    /// </summary>
    public enum BatchAttrChangeAction
    {
        /// <summary>取消（或直接关窗），不做任何修改</summary>
        Cancel = 0,

        /// <summary>确定：按面板里的设置执行批量替换</summary>
        Apply = 1,

        /// <summary>第 ① 步：回图纸重新选择要修改的图块（可框选，决定"样板"）</summary>
        PickBlocks = 2,

        /// <summary>第 ② 步：回图纸重新框选范围（只收与①同名的块；空选回车 = 整个图形）</summary>
        PickRange = 3
    }

    /// <summary>
    /// 批量修改块属性 —— 可视化面板。
    ///
    /// 界面标题「批量修改块属性：文字内容 / 文字选项 / 特性」，
    /// 布局与配色参照本仓库既有的 图块编号面板
    /// （<c>UI\Forms\Block\Numbering\NumberingOptionsForm.cs</c>）：
    /// 全部手写代码、不用 Designer / resx，方便直接把源码拷进别的工程。
    ///
    /// ---- 两处刻意的设计取舍 ----
    /// <list type="number">
    /// <item><b>属性列表用 <see cref="CheckedListBox"/>，不是多选 ListBox。</b>
    /// 多选列表只能靠 Ctrl/Shift 多选、靠高亮表达状态，
    /// 勾选框则不然：勾选状态一眼可见，也不会误触取消已选项；代价是双击不再等于确定
    /// （双击在勾选列表里天然是切换勾选），确定改由按钮承担。</item>
    /// <item><b>「未启用任何修改项」在面板内拦截。</b>
    /// 不关掉对话框、也不回到命令行打印「未启用任何修改项，未做任何修改。」——
    /// 模态面板点了确定却什么都不发生、还得多看一行命令行，体验很差，
    /// 所以这里保持对话框打开并弹出同一句提示。</item>
    /// </list>
    ///
    /// 除此之外，候选文本、校验报错文案、记忆回填规则、降级逻辑都固定不变。
    /// </summary>
    public sealed class BatchAttrChangeForm : Form
    {
        private readonly BatchAttrChangeContext _context;

        /// <summary>初始化 / 回填期间为 true，用来抑制控件事件里的联动逻辑</summary>
        private bool _loading;

        // ---- ① 选择要修改的块（两步）----
        private Button _btnPickBlocks;
        private Button _btnPickRange;
        private Label _lblRangeInfo;

        // ---- ② 选择属性 ----
        private CheckedListBox _chkTags;
        private TextBox _txtBlockInfo;
        private Label _lblSelection;

        // ---- ③ 文字内容 ----
        private CheckBox _chkContent;
        private TextBox _txtContent;

        // ---- ④ 文字选项 ----
        private CheckBox _chkFormat;
        private ComboBox _cboStyle;
        private ComboBox _cboJustify;
        private ComboBox _cboBackwards;
        private ComboBox _cboUpsideDown;
        private TextBox _txtHeight;
        private TextBox _txtWidthFactor;
        private TextBox _txtRotation;
        private TextBox _txtOblique;

        // ---- ⑤ 特性 ----
        private CheckBox _chkProperties;
        private ComboBox _cboLayer;
        private ComboBox _cboColor;
        private TextBox _txtColorNumber;
        private ComboBox _cboLinetype;
        private ComboBox _cboLineWeight;

        // ---- 底部 ----
        private Label _lblStatus;
        private Button _btnOk;
        private Button _btnCancel;

        private ToolTip _tips;
        private System.ComponentModel.IContainer components;

        // 面板会在图纸里拾取完再弹出来，位置/大小跨多次弹出记住，
        // 但绝不使用「挪到屏幕外」那类做法 —— 见 RememberBounds 注释
        private static Rectangle? _rememberedBounds;
        private static FormWindowState _rememberedState = FormWindowState.Normal;
        private FormWindowState _pendingWindowState = FormWindowState.Normal;

        /// <summary>面板结论；默认 Cancel，未点确定时调用方无需额外判断</summary>
        public BatchAttrChangeAction Action { get; private set; }

        /// <summary>点「确定」后的返回值；取消时为 null</summary>
        public BatchAttrChangeSelection Selection { get; private set; }

        /// <summary>
        /// 点「确定」时控件状态的快照，交给命令层落盘。
        ///
        /// 记忆的快照规则：下拉存索引、动态下拉存文本、
        /// 三个修改开关不写进记忆。取消时为 null（取消不更新记忆）。
        /// </summary>
        public BatchAttrChangeMemory Memory { get; private set; }

        /// <summary>
        /// 面板当前状态快照（三个修改开关 + 已填值 + 勾选的标签），交给命令层带进下一个面板。
        ///
        /// 与 <see cref="Memory"/> 的区别：那个只在点「确定」时产生，之后落盘、跨图纸跨启动；
        /// 这一份每次点①②（回图纸拾取）都会产生，**只活在本次命令里** ——
        /// 用户回图纸框个范围，不该把刚勾的三个修改开关和刚填的值清掉。
        /// 点「确定」时本属性同样有值，方便调用方统一处理。
        /// </summary>
        public BatchAttrChangeSessionState Session { get; private set; }

        /// <summary>
        /// 创建面板。
        /// </summary>
        /// <param name="context">采集阶段准备好的上下文（块名、标签、候选、记忆）</param>
        public BatchAttrChangeForm(BatchAttrChangeContext context)
        {
            _context = context ?? new BatchAttrChangeContext();

            _loading = true;
            BuildUi();
            LoadContextIntoControls();
            _loading = false;

            RestoreRememberedBounds();
            UpdateRangeInfo();
            UpdateSelectionInfo();
            ApplyAvailability();
        }

        // ==================================================================
        //  构建界面
        // ==================================================================

        private void BuildUi()
        {
            if (components == null)
            {
                components = new System.ComponentModel.Container();
            }

            if (_tips == null)
            {
                _tips = new ToolTip(components);
                ConfigureToolTipDelays();
            }

            SuspendLayout();

            Text = "批量修改块属性：文字内容 / 文字选项 / 特性";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;

            // 绝不用 TopMost：它是系统级全局置顶，会让本面板盖住用户所有其它软件。
            // 需要压住 AutoCAD 的 F12 动态输入提示时，用「把自己挂到 AutoCAD 主窗口
            // 名下」的办法（见 OnShown → AttachToAutoCadOwnerWindow）。
            TopMost = false;

            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(940, 740);
            MinimumSize = new Size(780, 700);
            Padding = new Padding(8);

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 组顺序照 图块编号面板：**先选块、再勾属性**。
            // 属性标签清单本来就来自"选中了哪些块"，把它排在选块之后更顺。
            //
            // 注意：绝对高度必须 >= 对应 GroupBox 的高度（外加 6px 下边距），
            // 否则 TableLayoutPanel 会把 GroupBox 裁掉一截
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98f));   // ① 选择要修改的块（92 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // ② 选择属性（随窗口伸缩）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));   // ③ 文字内容（70 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 134f));  // ④ 文字选项（128 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114f));  // ⑤ 特性（108 + 6）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));   // 底部按钮

            root.Controls.Add(BuildScopeGroup(), 0, 0);
            root.Controls.Add(BuildTagsGroup(), 0, 1);
            root.Controls.Add(BuildContentGroup(), 0, 2);
            root.Controls.Add(BuildFormatGroup(), 0, 3);
            root.Controls.Add(BuildPropertyGroup(), 0, 4);
            root.Controls.Add(BuildBottomPanel(), 0, 5);

            Controls.Add(root);
            ResumeLayout(true);
        }

        /// <summary>
        /// ② 选择属性：块名、标签勾选列表、当前选中说明、两条使用说明。
        /// 对应 DCL 里的 BLKINFO / ATTLIST / SELINFO 与两段 text。
        /// </summary>
        private Control BuildTagsGroup()
        {
            GroupBox group = NewGroup("② 选择属性", 0);
            group.Dock = DockStyle.Fill;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            layout.Controls.Add(new Label
            {
                Text = "勾选要修改的属性标签（可多选）：",
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 90, 160),
                Margin = new Padding(0, 2, 0, 4)
            }, 0, 0);

            _txtBlockInfo = NewReadOnlyText(Color.FromArgb(0, 90, 160));
            _txtBlockInfo.Dock = DockStyle.Fill;
            layout.Controls.Add(_txtBlockInfo, 0, 1);

            _chkTags = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 2, 0, 2)
            };
            _chkTags.ItemCheck += OnTagItemCheck;
            layout.Controls.Add(_chkTags, 0, 2);

            _lblSelection = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 4, 0, 2)
            };
            layout.Controls.Add(_lblSelection, 0, 3);

            TextBox notes = NewReadOnlyText(Color.DimGray);
            notes.Dock = DockStyle.Fill;
            notes.Text = "说明：勾选哪个区才改哪个区；未勾选或留空的项保持原值，内容留空即清空文字。"
                + Environment.NewLine
                + "程序已记住上次确定时填写的值并自动带回；修改开关的勾选状态不记忆。";
            layout.Controls.Add(notes, 0, 4);

            group.Controls.Add(layout);
            return group;
        }

        /// <summary>
        /// ① 选择要修改的块（两步）：① 回图纸选块（决定"样板"）② 回图纸框范围。
        ///
        /// 这是原 DCL 里 boxed_radio_column（OPT_CUR / OPT_ALL）的替代品 ——
        /// 两个单选按钮改成两个按钮 + 一行状态文字，做法照搬 图块编号面板的
        /// <c>BuildSelectionGroup()</c>（`NumberingOptionsForm.cs:415-440`）。
        ///
        /// 为什么不再用单选：「仅本次选中」只能在"命令启动时选好的那一批"和"全图同名"之间挑，
        /// 想换个范围就得整个命令重来。改成按钮后可以在面板里反复换，
        /// 而且框范围时只收集与①同名的块 —— 不会再出现"框到什么就改什么"。
        /// </summary>
        private Control BuildScopeGroup()
        {
            GroupBox group = NewGroup("① 选择要修改的块（两步）", 92);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            FlowLayoutPanel flow = NewFlow();
            flow.WrapContents = false;

            _btnPickBlocks = NewButton("① 在图纸中选择要修改的图块", 210);
            _btnPickBlocks.Click += OnPickBlocksClicked;

            _btnPickRange = NewButton("② 重新框选范围", 120);
            _btnPickRange.Click += OnPickRangeClicked;

            _lblRangeInfo = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 90, 160),
                Margin = new Padding(14, 8, 0, 0)
            };

            flow.Controls.Add(_btnPickBlocks);
            flow.Controls.Add(_btnPickRange);
            flow.Controls.Add(_lblRangeInfo);
            layout.Controls.Add(flow, 0, 0);

            Label hint = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(2, 2, 0, 0),
                Text = "只收集与①所选块同名的块；②不选任何对象直接回车 = 整个图形。"
            };
            layout.Controls.Add(hint, 0, 1);

            // 这两个按钮**故意不设 ToolTip**（照 图块编号的做法，见其 `NumberingOptionsForm.cs:435-438`）：
            // 面板是在图纸里拾取完才重建弹出的，鼠标往往仍停在这个按钮上，
            // 一弹出就自动显示灰色提示框，用户观感是"又冒出个灰框"。

            group.Controls.Add(layout);
            return group;
        }

        /// <summary>③ 文字内容：一个开关 + 一个编辑框（留空 = 清空文字）。</summary>
        private Control BuildContentGroup()
        {
            GroupBox group = NewGroup("③ 文字内容", 70);
            FlowLayoutPanel flow = NewFlow();

            _chkContent = NewGroupCheck("修改文字内容");
            _chkContent.CheckedChanged += OnToggleChanged;

            _txtContent = NewText(420);
            _txtContent.TextChanged += OnAnyInputChanged;

            flow.Controls.Add(_chkContent);
            flow.Controls.Add(Field("改成文字：", _txtContent));

            group.Controls.Add(flow);
            return group;
        }

        /// <summary>
        /// ④ 文字选项：样式 / 对齐 / 反向 / 倒置 / 字高 / 宽度因子 / 旋转 / 倾斜。
        /// 候选文本固定不变（含全角空格对齐的「反　　向：」这类标签）。
        /// </summary>
        private Control BuildFormatGroup()
        {
            GroupBox group = NewGroup("④ 文字选项", 128);
            FlowLayoutPanel flow = NewFlow();

            _chkFormat = NewGroupCheck("修改文字选项");
            _chkFormat.CheckedChanged += OnToggleChanged;

            _cboStyle = NewCombo(200);
            _cboJustify = NewCombo(150);
            _cboBackwards = NewCombo(130);
            _cboUpsideDown = NewCombo(130);

            _txtHeight = NewText(90);
            _txtWidthFactor = NewText(90);
            _txtRotation = NewText(90);
            _txtOblique = NewText(90);

            foreach (TextBox box in new[] { _txtHeight, _txtWidthFactor, _txtRotation, _txtOblique })
            {
                box.TextChanged += OnAnyInputChanged;
            }

            foreach (ComboBox combo in new[] { _cboStyle, _cboJustify, _cboBackwards, _cboUpsideDown })
            {
                combo.SelectedIndexChanged += OnAnyInputChanged;
            }

            flow.Controls.Add(_chkFormat);
            flow.Controls.Add(Field("字体样式：", _cboStyle));
            flow.Controls.Add(Field("文字对齐：", _cboJustify));
            flow.Controls.Add(Field("反　　向：", _cboBackwards));
            flow.Controls.Add(Field("倒　　置：", _cboUpsideDown));
            flow.Controls.Add(Field("字　　高：", _txtHeight));
            flow.Controls.Add(Field("宽度因子：", _txtWidthFactor));
            flow.Controls.Add(Field("旋转角度：", _txtRotation));
            flow.Controls.Add(Field("倾斜角度：", _txtOblique));

            group.Controls.Add(flow);
            return group;
        }

        /// <summary>
        /// ⑤ 特性：图层 / 颜色 / 颜色号码 / 线型 / 线宽。
        /// 「颜色号码」只在颜色选了「其他颜色号」时才启用。
        /// </summary>
        private Control BuildPropertyGroup()
        {
            GroupBox group = NewGroup("⑤ 特性", 108);
            FlowLayoutPanel flow = NewFlow();

            _chkProperties = NewGroupCheck("修改特性");
            _chkProperties.CheckedChanged += OnToggleChanged;

            _cboLayer = NewCombo(200);
            _cboColor = NewCombo(230);
            _cboLinetype = NewCombo(180);
            _cboLineWeight = NewCombo(160);
            _txtColorNumber = NewText(80);
            _txtColorNumber.Enabled = false;

            foreach (ComboBox combo in new[] { _cboLayer, _cboColor, _cboLinetype, _cboLineWeight })
            {
                combo.SelectedIndexChanged += OnAnyInputChanged;
            }

            _cboColor.SelectedIndexChanged += OnColorChanged;
            _txtColorNumber.TextChanged += OnAnyInputChanged;

            flow.Controls.Add(_chkProperties);
            flow.Controls.Add(Field("图　　层：", _cboLayer));
            flow.Controls.Add(Field("颜　　色：", _cboColor));
            flow.Controls.Add(Field("颜色号码：", _txtColorNumber));
            flow.Controls.Add(Field("线　　型：", _cboLinetype));
            flow.Controls.Add(Field("线　　宽：", _cboLineWeight));

            if (_tips != null)
            {
                _tips.SetToolTip(_txtColorNumber, "仅在「颜色」选了「其他颜色号」时生效，取值范围 1~255。");
            }

            group.Controls.Add(flow);
            return group;
        }

        /// <summary>底部：左边状态文字，右边「确定 / 取消」。</summary>
        private Control BuildBottomPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Color.DimGray
            };

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            _btnOk = NewButton("确定", 100);
            _btnOk.Click += OnOkClick;
            _btnCancel = NewButton("取消", 90);
            _btnCancel.Click += OnCancelClick;

            // 面板是在图纸里拾取完才弹出来的，鼠标常还压在按钮上；
            // 给按钮挂 ToolTip 会立刻弹出一个灰框，观感像「它自己跳出来的」，
            // 所以按钮一律不挂提示（与 图块编号面板一致）
            buttons.Controls.Add(_btnOk);
            buttons.Controls.Add(_btnCancel);

            panel.Controls.Add(_lblStatus, 0, 0);
            panel.Controls.Add(buttons, 1, 0);
            return panel;
        }

        // ==================================================================
        //  回填：记忆 + 上下文
        // ==================================================================

        /// <summary>
        /// 把上下文与记忆填进控件。
        ///
        /// 记忆回填规则：
        /// 三个修改开关默认**未勾选**（勾选状态不落盘记忆），其余控件回显上次的值；
        /// 下拉索引越界一律回落为 0（= 不修改）。
        ///
        /// 例外：本次命令里"上一轮面板"的状态（<see cref="BatchAttrChangeContext.Session"/>）
        /// 优先级高于落盘记忆 —— 用户点①/②回图纸拾取一趟，回来时界面必须还是走之前那样，
        /// 只有首次打开面板（Session 为 null）才用落盘记忆。
        /// </summary>
        private void LoadContextIntoControls()
        {
            bool hasBlocks = _context.Tags != null && _context.Tags.Count > 0;

            _txtBlockInfo.Text = hasBlocks
                ? "当前块名：" + (_context.BlockLabel ?? string.Empty)
                : "当前块名：（尚未选择图块）";

            BatchAttrChangeMemory memory = _context.Session != null && _context.Session.Memory != null
                ? _context.Session.Memory
                : (_context.RememberedMemory ?? new BatchAttrChangeMemory());

            // ---- 标签列表：勾上上次选过的那些（已不存在的标签自然勾不上）----
            _chkTags.Items.Clear();
            HashSet<string> rememberedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (memory.Tags != null)
            {
                foreach (string tag in memory.Tags)
                {
                    if (!string.IsNullOrEmpty(tag))
                    {
                        rememberedTags.Add(tag);
                    }
                }
            }

            if (_context.Tags != null)
            {
                foreach (string tag in _context.Tags)
                {
                    int index = _chkTags.Items.Add(tag);
                    if (rememberedTags.Contains(tag))
                    {
                        _chkTags.SetItemChecked(index, true);
                    }
                }
            }

            // ---- 静态下拉：第 0 项恒为「不修改」，记忆里存的是索引 ----
            FillCombo(_cboJustify, BatchAttrChangeTables.Justify);
            FillCombo(_cboBackwards, BatchAttrChangeTables.Backwards);
            FillCombo(_cboUpsideDown, BatchAttrChangeTables.UpsideDown);
            FillCombo(_cboLineWeight, BatchAttrChangeTables.LineWeights);
            FillCombo(_cboColor, BatchAttrChangeTables.Colors);

            // ---- 动态下拉：候选来自当前图纸，记忆里存的是文本 ----
            FillTextCombo(_cboStyle, BatchAttrChangeTables.NoStyleLabel, _context.StyleNames);
            FillTextCombo(_cboLayer, BatchAttrChangeTables.NoLayerLabel, _context.LayerNames);
            FillTextCombo(_cboLinetype, BatchAttrChangeTables.NoLinetypeLabel, _context.LinetypeNames);

            _txtContent.Text = memory.Content ?? string.Empty;
            _txtHeight.Text = memory.Height ?? string.Empty;
            _txtWidthFactor.Text = memory.WidthFactor ?? string.Empty;
            _txtRotation.Text = memory.Rotation ?? string.Empty;
            _txtOblique.Text = memory.Oblique ?? string.Empty;
            _txtColorNumber.Text = memory.ColorNumber ?? string.Empty;

            _cboJustify.SelectedIndex =
                BatchAttrChangeMemory.SafeIndex(memory.JustifyIndex, _cboJustify.Items.Count);
            _cboBackwards.SelectedIndex =
                BatchAttrChangeMemory.SafeIndex(memory.BackwardsIndex, _cboBackwards.Items.Count);
            _cboUpsideDown.SelectedIndex =
                BatchAttrChangeMemory.SafeIndex(memory.UpsideDownIndex, _cboUpsideDown.Items.Count);
            _cboLineWeight.SelectedIndex =
                BatchAttrChangeMemory.SafeIndex(memory.LineWeightIndex, _cboLineWeight.Items.Count);
            _cboColor.SelectedIndex =
                BatchAttrChangeMemory.SafeIndex(memory.ColorIndex, _cboColor.Items.Count);

            _cboStyle.SelectedIndex = BatchAttrChangeMemory.SafeIndex(
                BatchAttrChangeMemory.TextToIndex(ComboTexts(_cboStyle), memory.StyleName),
                _cboStyle.Items.Count);
            _cboLayer.SelectedIndex = BatchAttrChangeMemory.SafeIndex(
                BatchAttrChangeMemory.TextToIndex(ComboTexts(_cboLayer), memory.LayerName),
                _cboLayer.Items.Count);
            _cboLinetype.SelectedIndex = BatchAttrChangeMemory.SafeIndex(
                BatchAttrChangeMemory.TextToIndex(ComboTexts(_cboLinetype), memory.LinetypeName),
                _cboLinetype.Items.Count);

            // 三个修改开关：本次命令里"上一轮"勾过就保持勾着（回图纸拾取一趟不该丢状态）；
            // 首次打开面板时一律不勾 —— 这是既定行为。
            _chkContent.Checked = _context.Session != null && _context.Session.ModifyContent;
            _chkFormat.Checked = _context.Session != null && _context.Session.ModifyFormat;
            _chkProperties.Checked = _context.Session != null && _context.Session.ModifyProperties;

            OnColorChanged(this, EventArgs.Empty);
            UpdateRangeInfo();
            UpdateToggleVisuals();
        }

        /// <summary>
        /// 把当前控件状态快照成记忆。
        ///
        /// 下拉存**索引**、动态下拉存**文本**，取舍的原因：
        /// 静态表跨图纸稳定，动态候选来自当前图纸、存索引换张图就错位。
        /// 三个修改开关不写进记忆。
        /// </summary>
        /// <returns>记忆体</returns>
        private BatchAttrChangeMemory BuildMemory()
        {
            BatchAttrChangeMemory memory = new BatchAttrChangeMemory
            {
                Content = _txtContent.Text ?? string.Empty,
                Height = _txtHeight.Text ?? string.Empty,
                WidthFactor = _txtWidthFactor.Text ?? string.Empty,
                Rotation = _txtRotation.Text ?? string.Empty,
                Oblique = _txtOblique.Text ?? string.Empty,
                ColorNumber = _txtColorNumber.Text ?? string.Empty,
                JustifyIndex = _cboJustify.SelectedIndex,
                BackwardsIndex = _cboBackwards.SelectedIndex,
                UpsideDownIndex = _cboUpsideDown.SelectedIndex,
                ColorIndex = _cboColor.SelectedIndex,
                LineWeightIndex = _cboLineWeight.SelectedIndex,
                StyleName = _cboStyle.SelectedIndex > 0
                    ? Convert.ToString(_cboStyle.SelectedItem, CultureInfo.CurrentCulture)
                    : string.Empty,
                LayerName = _cboLayer.SelectedIndex > 0
                    ? Convert.ToString(_cboLayer.SelectedItem, CultureInfo.CurrentCulture)
                    : string.Empty,
                LinetypeName = _cboLinetype.SelectedIndex > 0
                    ? Convert.ToString(_cboLinetype.SelectedItem, CultureInfo.CurrentCulture)
                    : string.Empty
            };

            List<string> tags = new List<string>();
            for (int i = 0; i < _chkTags.Items.Count; i++)
            {
                if (_chkTags.GetItemChecked(i))
                {
                    tags.Add(Convert.ToString(_chkTags.Items[i], CultureInfo.CurrentCulture));
                }
            }

            memory.Tags = tags;
            return memory;
        }

        private void FillCombo<T>(ComboBox combo, IReadOnlyList<T> items)
        {
            combo.Items.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                combo.Items.Add(items[i]);
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private static void FillTextCombo(ComboBox combo, string firstItem, IList<string> names)
        {
            combo.Items.Clear();
            combo.Items.Add(firstItem);

            if (names != null)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    string name = names[i];
                    if (!string.IsNullOrEmpty(name) && !string.Equals(name, firstItem, StringComparison.Ordinal))
                    {
                        combo.Items.Add(name);
                    }
                }
            }

            combo.SelectedIndex = 0;
        }

        private static List<string> ComboTexts(ComboBox combo)
        {
            List<string> list = new List<string>(combo.Items.Count);
            for (int i = 0; i < combo.Items.Count; i++)
            {
                list.Add(Convert.ToString(combo.Items[i], CultureInfo.CurrentCulture));
            }

            return list;
        }

        // ==================================================================
        //  控件事件
        // ==================================================================

        private void OnTagItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading)
            {
                return;
            }

            // ItemCheck 触发时勾选状态还没落定，延到消息队列空闲后再刷新说明
            BeginInvoke(new Action(UpdateSelectionInfo));
        }

        /// <summary>
        /// ① 回图纸重新选块（这次选的块决定"样板"名）：先把当前界面状态存进
        /// <see cref="Session"/>，关掉面板回图纸，命令层拾取完再新建一个面板弹出来。
        /// 做法照搬 图块编号面板的 <c>OnPickSampleClicked</c>
        /// （`NumberingOptionsForm.cs:1365-1373`）。
        /// </summary>
        private void OnPickBlocksClicked(object sender, EventArgs e)
        {
            CaptureSession();
            Action = BatchAttrChangeAction.PickBlocks;
            RememberBounds();
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// ② 回图纸重新框选范围（只收与①同名的块；不选任何对象直接回车 = 整个图形）。
        /// </summary>
        private void OnPickRangeClicked(object sender, EventArgs e)
        {
            CaptureSession();
            Action = BatchAttrChangeAction.PickRange;
            RememberBounds();
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// 把当前界面状态存成快照，由命令层带进下一个面板。
        ///
        /// 复用 <see cref="BuildMemory"/>（它已经含三个静态下拉索引、三个动态下拉文本、
        /// 五个编辑框与勾选的标签），三个修改开关额外单列 —— 它们不落盘进跨图纸记忆，
        /// 但**本次命令内必须保持**。
        ///
        /// 注意 <c>DialogResult = OK</c> 只代表"面板正常关闭"，真正的结论看
        /// <see cref="Action"/>；所以拾取分支绝不能顺手写成 Apply。
        /// </summary>
        private void CaptureSession()
        {
            Session = new BatchAttrChangeSessionState
            {
                Memory = BuildMemory(),
                ModifyContent = _chkContent.Checked,
                ModifyFormat = _chkFormat.Checked,
                ModifyProperties = _chkProperties.Checked
            };
        }

        private void OnToggleChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }

            UpdateToggleVisuals();
        }

        private void OnColorChanged(object sender, EventArgs e)
        {
            BatchAttrChangeDropDownItem item = _cboColor.SelectedItem as BatchAttrChangeDropDownItem;
            bool isCustom = item != null && item.IsCustom;

            _txtColorNumber.Enabled = isCustom;
            if (!isCustom)
            {
                _txtColorNumber.Text = string.Empty;
            }

            OnAnyInputChanged(sender, e);
        }

        private void OnAnyInputChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }

            UpdateStatusText();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            Action = BatchAttrChangeAction.Cancel;
            Selection = null;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // ==================================================================
        //  确定：取值 → 校验 → 收尾
        // ==================================================================

        private void OnOkClick(object sender, EventArgs e)
        {
            BatchAttrChangeSelection selection = GatherAndValidate();
            if (selection == null)
            {
                // 校验失败：对话框保持打开，用户改完再点确定
                return;
            }

            Action = BatchAttrChangeAction.Apply;
            Selection = selection;
            Memory = BuildMemory();
            Session = new BatchAttrChangeSessionState
            {
                Memory = Memory,
                ModifyContent = _chkContent.Checked,
                ModifyFormat = _chkFormat.Checked,
                ModifyProperties = _chkProperties.Checked
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// 从控件取值并按固定规则校验；校验失败时弹提示并返回 null。
        /// 取值与校验规则见 <see cref="BatchAttrChangeOptions"/> 里的语义约定。
        /// </summary>
        private BatchAttrChangeSelection GatherAndValidate()
        {
            List<string> selectedTags = new List<string>();
            for (int i = 0; i < _chkTags.Items.Count; i++)
            {
                if (_chkTags.GetItemChecked(i))
                {
                    selectedTags.Add(Convert.ToString(_chkTags.Items[i], CultureInfo.CurrentCulture));
                }
            }

            if (selectedTags.Count == 0)
            {
                Alert("未选择任何属性，程序结束。");
                return null;
            }

            BatchAttrChangeOptions options = new BatchAttrChangeOptions
            {
                ModifyContent = _chkContent.Checked,
                NewContent = _txtContent.Text ?? string.Empty,
                ModifyFormat = _chkFormat.Checked,
                ModifyProperties = _chkProperties.Checked,
                Scope = _context.RangeSource == BatchAttrChangeRangeSource.WholeDrawing
                    ? BatchAttrChangeScope.AllSameName
                    : BatchAttrChangeScope.SelectedOnly
            };

            if (options.ModifyFormat)
            {
                options.TextStyleName = SelectedText(_cboStyle, BatchAttrChangeTables.NoStyleLabel);
                options.Justify = SelectedValue(_cboJustify);
                options.Backwards = SelectedTriState(_cboBackwards);
                options.UpsideDown = SelectedTriState(_cboUpsideDown);

                // 校验顺序固定：字高 → 宽度因子 → 旋转角 → 倾斜角，命中即止
                double? height;
                if (!TryReadPositive(_txtHeight.Text, "字高必须是正数（如 3.5）！", out height))
                {
                    return null;
                }

                double? widthFactor;
                if (!TryReadPositive(_txtWidthFactor.Text, "宽度因子必须是正数（如 3.5）！", out widthFactor))
                {
                    return null;
                }

                double? rotation;
                if (!TryReadNumber(_txtRotation.Text, "旋转角必须是数值！", out rotation))
                {
                    return null;
                }

                double? oblique;
                if (!TryReadNumber(_txtOblique.Text, "倾斜角必须是数值！", out oblique))
                {
                    return null;
                }

                options.Height = height;
                options.WidthFactor = widthFactor;
                options.Rotation = rotation;
                options.Oblique = oblique;
            }

            if (options.ModifyProperties)
            {
                options.LayerName = SelectedText(_cboLayer, BatchAttrChangeTables.NoLayerLabel);
                options.LinetypeName = SelectedText(_cboLinetype, BatchAttrChangeTables.NoLinetypeLabel);
                options.LineWeight = SelectedValue(_cboLineWeight);

                BatchAttrChangeDropDownItem colorItem = _cboColor.SelectedItem as BatchAttrChangeDropDownItem;
                if (colorItem != null && colorItem.IsCustom)
                {
                    int colorNumber;
                    if (!TryReadColorNumber(_txtColorNumber.Text, out colorNumber))
                    {
                        Alert("颜色号必须是 1~255 的整数！");
                        return null;
                    }

                    options.ColorIndex = colorNumber;
                }
                else
                {
                    options.ColorIndex = SelectedValue(_cboColor);
                }
            }

            // 降级：开关开着但一项没填 = 没开
            if (!options.Normalize())
            {
                Alert("未启用任何修改项，未做任何修改。");
                return null;
            }

            return new BatchAttrChangeSelection
            {
                Options = options,
                SelectedTags = selectedTags
            };
        }

        private static string SelectedText(ComboBox combo, string noModifyLabel)
        {
            if (combo.SelectedIndex <= 0)
            {
                return null;
            }

            string text = Convert.ToString(combo.SelectedItem, CultureInfo.CurrentCulture);
            if (string.IsNullOrEmpty(text) || string.Equals(text, noModifyLabel, StringComparison.Ordinal))
            {
                return null;
            }

            return text;
        }

        private static int? SelectedValue(ComboBox combo)
        {
            BatchAttrChangeDropDownItem item = combo.SelectedItem as BatchAttrChangeDropDownItem;
            return item == null ? null : item.Value;
        }

        private static bool? SelectedTriState(ComboBox combo)
        {
            BatchAttrChangeTriStateItem item = combo.SelectedItem as BatchAttrChangeTriStateItem;
            return item == null ? null : item.Value;
        }

        /// <summary>
        /// 读取一个「必须是正数」的可选数值。
        ///
        /// 留空 = 不修改，返回 true 且 <paramref name="value"/> 为 null ——
        /// 这一点必须保持，否则「留空保持原值」会变成「把字高写成 0」。
        /// </summary>
        private bool TryReadPositive(string text, string errorMessage, out double? value)
        {
            value = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                return true;
            }

            double parsed;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || parsed <= 0d)
            {
                Alert(errorMessage);
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>读取一个可选数值（可为负、可为 0）；留空 = 不修改。</summary>
        private bool TryReadNumber(string text, string errorMessage, out double? value)
        {
            value = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                return true;
            }

            double parsed;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                Alert(errorMessage);
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadColorNumber(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                return false;
            }

            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            return value >= 1 && value <= 255;
        }

        /// <summary>校验失败的提示（模态弹窗）</summary>
        private void Alert(string message)
        {
            MessageBox.Show(this, message, "批量修改块属性", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ==================================================================
        //  说明文字刷新
        // ==================================================================

        private void UpdateSelectionInfo()
        {
            List<string> checkedTags = new List<string>();
            for (int i = 0; i < _chkTags.Items.Count; i++)
            {
                if (_chkTags.GetItemChecked(i))
                {
                    checkedTags.Add(Convert.ToString(_chkTags.Items[i], CultureInfo.CurrentCulture));
                }
            }

            _lblSelection.Text = checkedTags.Count == 0
                ? "当前选中：（未选择）"
                : "当前选中（" + checkedTags.Count.ToString(CultureInfo.InvariantCulture) + "项）："
                    + string.Join("、", checkedTags.ToArray());

            UpdateStatusText();
        }

        /// <summary>
        /// 刷新①那一行的范围说明。四种来源各说各的话，
        /// 让人一眼看出"这批块是怎么来的"，而不是只看到一个数字。
        /// </summary>
        private void UpdateRangeInfo()
        {
            bool hasBlocks = _context.Tags != null && _context.Tags.Count > 0;

            if (!hasBlocks)
            {
                _lblRangeInfo.ForeColor = Color.DimGray;
                _lblRangeInfo.Text = "尚未选择图块 —— 请点①回到图纸里选。";
            }
            else if (_context.RangeSource == BatchAttrChangeRangeSource.WholeDrawing)
            {
                _lblRangeInfo.ForeColor = Color.FromArgb(0, 90, 160);
                _lblRangeInfo.Text = "全图所有同名图块，共 "
                    + _context.SameNameBlockCount.ToString(CultureInfo.InvariantCulture)
                    + " 个（②里不选任何对象直接回车的结果）";
            }
            else if (_context.RangeSource == BatchAttrChangeRangeSource.PickedRange)
            {
                _lblRangeInfo.ForeColor = Color.FromArgb(0, 90, 160);
                _lblRangeInfo.Text = "本次框选的 "
                    + _context.SelectedBlockCount.ToString(CultureInfo.InvariantCulture)
                    + " 个图块（与①同名）";
            }
            else
            {
                _lblRangeInfo.ForeColor = Color.FromArgb(0, 90, 160);
                _lblRangeInfo.Text = "① 选中的 "
                    + _context.SelectedBlockCount.ToString(CultureInfo.InvariantCulture)
                    + " 个图块";
            }

            _lblRangeInfo.Visible = true;
            UpdateStatusText();
        }

        /// <summary>
        /// 按"有没有选中块"决定按钮可用性 —— 没有块就没什么可改的。
        ///
        /// 照 图块编号面板的做法（其 <c>NumberingOptionsForm.cs</c> 里
        /// <c>_btnOk.Enabled = hasCandidates;</c>、<c>_btnPickRange.Enabled = hasSample;</c>）：
        /// **禁用而不是隐藏**，用户能看见按钮存在、知道下一步该干什么。
        /// </summary>
        private void ApplyAvailability()
        {
            bool hasBlocks = _context.Tags != null && _context.Tags.Count > 0;

            _btnPickRange.Enabled = hasBlocks;
            _btnOk.Enabled = hasBlocks;
        }

        private void UpdateToggleVisuals()
        {
            // 开关没勾时把整区灰掉：一眼就能看出「哪个区会改」，避免误改
            SetEnabledDeep(_cboStyle, _chkFormat.Checked);
            SetEnabledDeep(_cboJustify, _chkFormat.Checked);
            SetEnabledDeep(_cboBackwards, _chkFormat.Checked);
            SetEnabledDeep(_cboUpsideDown, _chkFormat.Checked);
            SetEnabledDeep(_txtHeight, _chkFormat.Checked);
            SetEnabledDeep(_txtWidthFactor, _chkFormat.Checked);
            SetEnabledDeep(_txtRotation, _chkFormat.Checked);
            SetEnabledDeep(_txtOblique, _chkFormat.Checked);

            SetEnabledDeep(_cboLayer, _chkProperties.Checked);
            SetEnabledDeep(_cboColor, _chkProperties.Checked);
            SetEnabledDeep(_cboLinetype, _chkProperties.Checked);
            SetEnabledDeep(_cboLineWeight, _chkProperties.Checked);

            // 颜色号码还额外受「其他颜色号」约束
            BatchAttrChangeDropDownItem colorItem = _cboColor.SelectedItem as BatchAttrChangeDropDownItem;
            bool customColor = _chkProperties.Checked && colorItem != null && colorItem.IsCustom;
            SetEnabledDeep(_txtColorNumber, customColor);

            SetEnabledDeep(_txtContent, _chkContent.Checked);

            UpdateStatusText();
        }

        private static void SetEnabledDeep(Control control, bool enabled)
        {
            if (control != null && control.Enabled != enabled)
            {
                control.Enabled = enabled;
            }
        }

        private void UpdateStatusText()
        {
            if (_lblStatus == null)
            {
                return;
            }

            List<string> sections = new List<string>();
            if (_chkContent.Checked)
            {
                sections.Add("文字内容");
            }

            if (_chkFormat.Checked)
            {
                sections.Add("文字选项");
            }

            if (_chkProperties.Checked)
            {
                sections.Add("特性");
            }

            string scope = _context.RangeSource == BatchAttrChangeRangeSource.WholeDrawing ? "全图同名块" : "仅当前所选";
            string sectionsText = sections.Count == 0 ? "尚未勾选任何修改区" : string.Join(" + ", sections.ToArray());

            _lblStatus.Text = "将修改：" + sectionsText + "　·　范围：" + scope;
        }

        // ==================================================================
        //  窗口行为：所有者窗口 + 位置记忆
        // ==================================================================

        /// <summary>
        /// 提示框延迟整体调大。
        ///
        /// 本面板是在图纸里拾取完**紧接着**弹出的，弹出瞬间鼠标常常还压在某个控件上，
        /// ToolTip 默认 500ms 就会自己冒出一个灰框，用户观感是「它自己跳出来个灰框」。
        /// 这里把初始延迟提到 1.2 秒，并让它在 12 秒后自动消失。
        /// </summary>
        private void ConfigureToolTipDelays()
        {
            _tips.AutomaticDelay = 1200;
            _tips.AutoPopDelay = 12000;
            _tips.InitialDelay = 1200;
            _tips.ReshowDelay = 400;
            _tips.ShowAlways = false;
        }

        /// <inheritdoc />
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 最大化必须在句柄创建之后设置，否则会被 Form 的布局流程吃掉
            if (_pendingWindowState != FormWindowState.Normal)
            {
                WindowState = _pendingWindowState;
            }

            AttachToAutoCadOwnerWindow();
        }

        /// <inheritdoc />
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            RememberBounds();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// 把本面板挂到 AutoCAD 主窗口名下（设置 Owner），再在本组窗口里排到最前。
        ///
        /// 为什么不用 <c>TopMost = true</c>：那是系统级全局置顶，会让面板盖住用户
        /// 所有其它软件，属于严重干扰。把 AutoCAD 主窗口设成 Owner 之后，
        /// 面板天然会压在 AutoCAD 之上，同时不会影响其它程序。
        /// <c>SetWindowPos</c> 这里只传 <c>HWND_TOP</c>（本组内排序），
        /// **绝不传 <c>HWND_TOPMOST</c>**。
        /// </summary>
        private void AttachToAutoCadOwnerWindow()
        {
            try
            {
                TopMost = false;

                IntPtr autoCadMainWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (autoCadMainWindow == IntPtr.Zero || Handle == IntPtr.Zero)
                {
                    return;
                }

                SetWindowLongPtr(Handle, GwlpHwndParent, autoCadMainWindow);
                SetWindowPos(
                    Handle,
                    HwndTop,
                    0,
                    0,
                    0,
                    0,
                    SwpNoSize | SwpNoMove | SwpNoActivate);
            }
            catch (System.Exception)
            {
                // 挂不上只是「可能被 AutoCAD 主窗口挡住」，不影响功能
            }
        }

        /// <summary>
        /// 记住窗口位置 / 大小，下次弹出时还原。
        /// 只记录，绝不移动窗口 —— 不采用「先挪到屏幕外再挪回来」那类做法。
        /// </summary>
        private void RememberBounds()
        {
            try
            {
                _rememberedState = WindowState;
                _rememberedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>
        /// 还原上次的位置 / 大小；第一次打开时保持 <c>CenterScreen</c>。
        /// 必须在 <c>BuildUi()</c> 之后调用 —— BuildUi 设了 CenterScreen，
        /// 要改成 Manual 才能赋值 Bounds。
        /// </summary>
        private void RestoreRememberedBounds()
        {
            if (!_rememberedBounds.HasValue)
            {
                return;
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = _rememberedBounds.Value;
            _pendingWindowState = _rememberedState;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
                components = null;
            }

            base.Dispose(disposing);
        }

        // ==================================================================
        //  小工具
        // ==================================================================

        private const int GwlpHwndParent = -8;
        private static readonly IntPtr HwndTop = new IntPtr(0);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        private static GroupBox NewGroup(string text, int height)
        {
            return new GroupBox
            {
                Text = text,
                Dock = DockStyle.Fill,
                Height = height,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 2, 8, 4)
            };
        }

        private static FlowLayoutPanel NewFlow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true
            };
        }

        private static Control Field(string caption, Control editor)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 2, 14, 2)
            };

            if (!string.IsNullOrEmpty(caption))
            {
                panel.Controls.Add(new Label
                {
                    Text = caption,
                    AutoSize = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 5, 4, 0)
                });
            }

            panel.Controls.Add(editor);
            return panel;
        }

        private static TextBox NewText(int width)
        {
            return new TextBox
            {
                Width = width,
                Margin = new Padding(0, 1, 0, 0)
            };
        }

        private static TextBox NewReadOnlyText(Color foreColor)
        {
            // 用只读多行 TextBox 代替 Label：长文本能自动换行，而且可以选中复制
            return new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.None,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                TabStop = false,
                ForeColor = foreColor
            };
        }

        private static ComboBox NewCombo(int width)
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                Margin = new Padding(0, 1, 0, 0)
            };
        }

        /// <summary>区开关：加粗，和普通选项区分开，一眼看出「这个勾决定整区改不改」</summary>
        private static CheckBox NewGroupCheck(string text)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Margin = new Padding(0, 4, 16, 0)
            };
        }

        private static Button NewButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 28,
                Margin = new Padding(6, 0, 0, 0)
            };
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
