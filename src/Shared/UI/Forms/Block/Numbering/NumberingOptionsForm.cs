using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.Geometry;
using ChemForge.Core.Block.Numbering;
using ChemForge.Models.Block.Numbering;
using static ChemForge.UI.Forms.Common.DialogStyle;

namespace ChemForge.UI.Forms.Block.Numbering
{
    /// <summary>对话框关闭后用户想要做什么。</summary>
    public enum NumberingAction
    {
        Cancel = 0,

        /// <summary>确认并写入。</summary>
        Write = 1,

        /// <summary>回到图纸中点选一个块作为样板（决定"按哪种块编号"）。</summary>
        PickSample = 2,

        /// <summary>回到图纸中框选一个区域，自动收集区域内与样板同名的块。</summary>
        PickRange = 3
    }

    /// <summary>
    /// 属性选择 + 编号参数 + 预览表格，全部在一个对话框里完成。
    /// 布局用手写代码构建（不用 Designer/resx），方便直接把源码拷进别的工程。
    /// </summary>
    public sealed class NumberingOptionsForm : Form
    {
        private static readonly string[] SortModeText =
        {
            "先上后下 · 行内从左到右",
            "先上后下 · 行内从右到左",
            "先下后上 · 行内从左到右",
            "先下后上 · 行内从右到左",
            "先左后右 · 列内从上到下",
            "先左后右 · 列内从下到上",
            "先右后左 · 列内从上到下",
            "先右后左 · 列内从下到上"
        };

        private static readonly string[] BasisText =
        {
            "块插入点", "包围盒中心", "属性文字位置"
        };

        private const int MaxPreviewRows = 2000;

        private readonly List<BlockItem> _candidates;
        private readonly List<AttributeDefInfo> _definitions;
        private readonly string _blockLabel;
        private readonly NumberOptions _options;
        private readonly Func<NumberOptions, NumberingPlan> _rebuild;
        private readonly Matrix3d _wcsToSort;
        private readonly List<string> _tagValues = new List<string>();

        private NumberingPlan _plan;
        private bool _loading;
        private System.ComponentModel.IContainer components;

        // ---------------- 控件 ----------------
        private Button _btnPickSample;
        private Button _btnPickRange;
        private Label _lblSelection;
        private ComboBox _cboTag;
        private Label _lblTagInfo;
        private NumericUpDown _numStart;
        private NumericUpDown _numStep;
        private NumericUpDown _numPad;
        private TextBox _txtPrefix;
        private TextBox _txtSuffix;
        private TextBox _lblSample;
        private ComboBox _cboSortMode;
        private ComboBox _cboBasis;
        private CheckBox _chkAutoTolerance;
        private NumericUpDown _numTolerance;
        private CheckBox _chkFillOnlyEmpty;
        private CheckBox _chkSkipXref;
        private CheckBox _chkGroupByRow;
        private DataGridView _grid;

        /// <summary>
        /// 表格当前这套列是按「聚合(true)」还是「逐块(false)」建的。
        /// 用于在绑数据前自检列形态，避免"列是聚合的、数据按逐块填"而找不到列。
        /// </summary>
        private bool _gridGrouped = true;

        /// <summary>预览表格最近一次刷新失败的原因；为空表示正常。</summary>
        private string _previewError = string.Empty;

        private Label _lblStats;
        private Button _btnRefresh;
        private Button _btnOk;
        private Button _btnCancel;
        private readonly ToolTip _tips = new ToolTip();

        /// <summary>
        /// 让提示框只在"鼠标真的停在控件上等了一会儿"才出现。
        ///
        /// 为什么必须调这个：本对话框是在图纸里拾取完**重建弹出**的，
        /// 弹出瞬间鼠标往往还压在按钮所在的位置上 —— 用默认的自动延迟（500ms）
        /// 会立刻弹出一个灰色提示框，用户观感就是"它自己跳出来个灰框"，
        /// 还得挪开鼠标才消失。调长 InitialDelay 后，用户必须刻意停留才会显示。
        /// </summary>
        private void ConfigureToolTipDelays()
        {
            _tips.AutomaticDelay = 1200;
            _tips.AutoPopDelay = 12000;
            _tips.InitialDelay = 1200;
            _tips.ReshowDelay = 400;
            _tips.ShowAlways = false;
        }

        public NumberingAction Action { get; private set; }

        /// <summary>
        /// 上一次窗体在屏幕上的位置与大小（含最大化状态），跨"重建"保留。
        ///
        /// 为什么需要它：本命令的交互循环是"对话框 → 回图纸拾取 → 重建对话框"，
        /// 每轮都会 new 一个新的 Form。如果不管，新窗体就按 StartPosition 摆回屏幕中央，
        /// 用户会看到窗口"跳"了一下 —— 位置被乱动了。
        /// 把上一轮的实际位置记在这里，重建时原样还原，窗口就像从没动过。
        ///
        /// 为什么用 HasValue 而不是默认值：第一次打开时应当居中，
        /// 只有用户真正动过窗口（或经历过一轮拾取）之后才用记忆的位置。
        /// </summary>
        private static Rectangle? _rememberedBounds;
        private static FormWindowState _rememberedState = FormWindowState.Normal;

        /// <summary>待应用的最大化状态；句柄创建后才能设置，所以在 OnShown 里落地。</summary>
        private FormWindowState _pendingWindowState = FormWindowState.Normal;

        /// <summary>清理非托管资源与容器内的组件（含 ToolTip）。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
                components = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>窗体外框（含标题栏）已经建好，这时设置最大化才生效。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (_pendingWindowState == FormWindowState.Maximized)
            {
                try
                {
                    WindowState = FormWindowState.Maximized;
                }
                catch (Exception)
                {
                }
            }
            _pendingWindowState = FormWindowState.Normal;

            // 窗口真正显示出来之后再把自己挂到 AutoCAD 主窗口名下。
            AttachToAutoCadOwnerWindow();
        }

        /// <summary>
        /// 让对话框"盖住 AutoCAD 自己"，但**不影响其它软件**。
        ///
        /// 背景：用户开着 F12 动态输入时，命令提示会跟着光标浮在图纸上，
        /// 对话框不处理就会被它压住。但**不能用 `TopMost = true`** ——
        /// `TopMost` 是系统级的，对话框会盖在**所有**程序上面，
        /// 用户切到别的软件（浏览器、IDE…）也被它压着，这是不能接受的。
        ///
        /// 正确做法：把 AutoCAD 主窗口设为本窗体的**所有者（Owner）**。
        /// 所有者窗口的从属窗口永远排在其所有者的其它窗口之上，
        /// 因此对话框天然压住 AutoCAD 的浮动提示层，而 AutoCAD 自身
        /// 依然可以被其它程序正常遮挡 —— 作用范围就限制在 AutoCAD 内部。
        ///
        /// ⚠️ 但**光设 Owner 还不够**（实测确认）：设完那一刻，动态输入提示层
        /// 可能已经先画上去了，新弹出的对话框不会自动重排到它上面 ——
        /// 观感就是"对话框背后露着一个灰框"，而**切出去再切回来就好了**
        /// （重绘时 Owner 关系才真正生效）。
        ///
        /// 所以还要在挂完 Owner 后**主动把本窗体提到 AutoCAD 层级的最顶**：
        /// 用 `SetWindowPos(Handle, HWND_TOP, ...)`，且 `SWP_NOMOVE | SWP_NOSIZE`
        /// —— **只动 z-order，不挪位置、不改尺寸**。
        /// 注意 `HWND_TOP` 只在本窗体的"顶层窗口组"内排序，**不是** `HWND_TOPMOST`，
        /// 所以不会盖住用户的其它软件。
        ///
        /// 注意：**不要**用 `SetWindowPos(HWND_TOPMOST)`，那是全局置顶，
        /// 正是第七轮要修掉的行为。
        /// </summary>
        private void AttachToAutoCadOwnerWindow()
        {
            try
            {
                // 明确不置顶；靠 Owner 关系保证在 AutoCAD 内部层级最高
                TopMost = false;

                IntPtr acadMain = System.Diagnostics.Process
                    .GetCurrentProcess().MainWindowHandle;

                if (acadMain == IntPtr.Zero || Handle == IntPtr.Zero)
                {
                    return;
                }

                // GWLP_HWNDPARENT = -8：设置所有者窗口
                SetWindowLongPtr(Handle, GwlpHwndParent, acadMain);

                // 补一刀：刚弹出时动态输入提示层可能已经在上面了，主动排到本组最顶。
                // 只用 HWND_TOP（组内排序），配合 NOMOVE|NOSIZE 不碰窗口位置与大小。
                SetWindowPos(Handle, HwndTop, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }
            catch (Exception)
            {
                // 挂不上也不影响功能，最多是被动态输入提示挡住
            }
        }

        private const int GwlpHwndParent = -8;

        /// <summary>把窗口排到"其所有者窗口组"的最顶端（不是全局置顶）。</summary>
        private static readonly IntPtr HwndTop = new IntPtr(0);

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            // 32/64 位都要能用：64 位走 SetWindowLongPtr，32 位退回 SetWindowLong
            if (IntPtr.Size == 8)
            {
                return SetWindowLongPtr64(hWnd, nIndex, value);
            }
            return new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int value);

        /// <summary>
        /// 关闭前把当前位置/大小记下来，供下一轮重建还原 —— 这样窗口就不会"跳"。
        /// 注意这里**只记录、不移动**：绝不再用"把窗体挪到屏幕外"那套做法。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            RememberBounds();
            base.OnFormClosing(e);
        }

        public NumberOptions Options
        {
            get { return _options; }
        }

        public NumberingPlan Plan
        {
            get { return _plan; }
        }

        public NumberingOptionsForm(
            List<AttributeDefInfo> definitions,
            List<BlockItem> candidates,
            string blockLabel,
            NumberOptions initial,
            Func<NumberOptions, NumberingPlan> rebuild,
            Matrix3d wcsToSort)
        {
            _definitions = definitions ?? new List<AttributeDefInfo>();
            _candidates = candidates ?? new List<BlockItem>();
            _blockLabel = blockLabel ?? string.Empty;
            _rebuild = rebuild;
            _wcsToSort = wcsToSort;
            _options = initial != null ? initial.Clone() : new NumberOptions();
            Action = NumberingAction.Cancel;

            // 构建界面与回填参数期间屏蔽各控件的联动事件，避免半初始化状态下重算
            _loading = true;
            BuildUi();
            LoadOptionsIntoControls();
            _loading = false;

            // 回填参数时 _loading 把复选框的联动事件吞掉了，列还停在 BuildUi 的初始形态。
            // 这里按**最终**参数显式重建一次，保证"列"与紧接着要绑的"数据"是同一套。
            ApplyGridColumns();

            RestoreRememberedBounds();
            RefreshPlan();
        }

        /// <summary>
        /// 还原上一轮记住的窗口位置/大小（若有）。
        ///
        /// 必须放在 BuildUi 之后：BuildUi 里设了 StartPosition = CenterScreen，
        /// 这里再把它改成 Manual 并给出 Bounds，否则 CenterScreen 会覆盖我们的位置。
        /// </summary>
        private void RestoreRememberedBounds()
        {
            if (!_rememberedBounds.HasValue)
            {
                return;   // 第一次打开：保持居中
            }

            try
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = _rememberedBounds.Value;

                // 最大化要在窗口句柄创建并显示之后才生效，交给 OnShown 处理
                _pendingWindowState = _rememberedState;
            }
            catch (Exception)
            {
                // 还原失败就退回居中，不影响功能
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        /// <summary>记住当前位置/大小，供下一轮重建时还原。</summary>
        private void RememberBounds()
        {
            try
            {
                _rememberedState = WindowState;

                // 最大化/最小化时 Bounds 是无意义的，用 RestoreBounds 拿"还原后"的位置
                _rememberedBounds = (WindowState == FormWindowState.Normal)
                    ? Bounds
                    : RestoreBounds;
            }
            catch (Exception)
            {
                // 记不住也无妨，下一轮最多回到居中
            }
        }

        // =====================================================================
        // 界面构建
        // =====================================================================

        private void BuildUi()
        {
            // ToolTip 挂进窗体的容器，随窗体一起释放
            if (components == null)
            {
                components = new System.ComponentModel.Container();
            }
            components.Add(_tips);
            ConfigureToolTipDelays();
            SuspendLayout();

            Text = "属性块批量编号： " + (string.IsNullOrEmpty(_blockLabel) ? "（尚未选择块）" : _blockLabel);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            // 注意：这里**不设 TopMost**。TopMost 是系统级的全局置顶，
            // 会让对话框盖住用户的所有其它软件（浏览器、IDE…）。
            // 压住 AutoCAD 的 F12 动态输入提示改用"所有者窗口"方案，
            // 见 OnShown → AttachToAutoCadOwnerWindow()。
            TopMost = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(960, 760);
            MinimumSize = new Size(780, 640);
            Padding = new Padding(FormPadding);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));   // 选择对象
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));   // 目标属性
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128f));   // 编号规则（与 BuildNumberingGroup 的 128 一致）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108f));  // 排序规则
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70f));   // 写入行为
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 预览表格
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));   // 底部按钮

            root.Controls.Add(BuildSelectionGroup(), 0, 0);
            root.Controls.Add(BuildAttributeGroup(), 0, 1);
            root.Controls.Add(BuildNumberingGroup(), 0, 2);
            root.Controls.Add(BuildSortingGroup(), 0, 3);
            root.Controls.Add(BuildBehaviourGroup(), 0, 4);
            root.Controls.Add(BuildGrid(), 0, 5);
            root.Controls.Add(BuildBottomPanel(), 0, 6);

            Controls.Add(root);
            ResumeLayout(true);
        }

        /// <summary>
        /// 选择对象分组。两步走：
        ///   ① 在图纸中点选一个块作为"样板"（决定按哪种块编号）；
        ///   ② 点"选择范围"后回到图纸，用 AutoCAD 自带的框选挑一批块
        ///      （只收集与样板同名的；不选直接回车 = 整个图形）。
        /// 选完自动回到本对话框，候选块带入下方表格预览。
        /// </summary>
        private GroupBox BuildSelectionGroup()
        {
            GroupBox box = NewGroup("选择要编号的块（两步）", 76);

            _btnPickSample = NewButton("① 在图纸中选一个样板块", 210);
            _btnPickSample.Click += OnPickSampleClicked;

            _btnPickRange = NewButton("② 选择范围", 120);
            _btnPickRange.Click += OnPickRangeClicked;

            _lblSelection = new Label();
            _lblSelection.AutoSize = true;
            _lblSelection.Margin = new Padding(14, 8, 0, 0);

            FlowLayoutPanel flow = NewFlow();
            flow.Controls.Add(_btnPickSample);
            flow.Controls.Add(_btnPickRange);
            flow.Controls.Add(_lblSelection);
            box.Controls.Add(flow);
            ChemForge.UI.Forms.Block.Numbering.NumberingHelpButton.Attach(box);

            // 这两个按钮**故意不设 ToolTip**：
            // 对话框是在图纸里拾取完才重建弹出的，此时鼠标往往仍停在按钮位置上，
            // 一弹出来就会自动把灰色提示框显示出来（用户观感："又冒出个灰框"），
            // 而要它消失还得挪开鼠标。按钮文案本身已经说清了两步干什么，不需要悬停解释。
            return box;
        }

        private GroupBox BuildAttributeGroup()
        {
            GroupBox box = NewGroup("目标属性", 70);

            _cboTag = new ComboBox();
            _cboTag.DropDownStyle = ComboBoxStyle.DropDown;
            _cboTag.Width = 260;
            _cboTag.Margin = new Padding(0, 1, 14, 0);
            _cboTag.SelectedIndexChanged += OnTagChanged;
            _cboTag.TextChanged += OnTagChanged;

            _lblTagInfo = new Label();
            _lblTagInfo.AutoSize = true;
            _lblTagInfo.ForeColor = Color.DimGray;
            _lblTagInfo.Margin = new Padding(0, 5, 0, 0);
            _lblTagInfo.Text = "-";

            FlowLayoutPanel flow = NewFlow();
            flow.Controls.Add(Field("属性 Tag：", _cboTag));
            flow.Controls.Add(Field("说明：", _lblTagInfo));
            box.Controls.Add(flow);
            return box;
        }

        private GroupBox BuildNumberingGroup()
        {
            // 比原来高一些：把"效果"单独放一行，前缀很长时也有地方放下
            // 高度要放得下两行"效果"文字（单行标签换成多行文本框后需要更多纵向空间）；
            // root.RowStyles 里"编号规则"那一行必须 ≥ 这个值，否则会被 TableLayoutPanel 裁掉。
            GroupBox box = NewGroup("编号规则", 128);

            _numStart = NewNumeric(-1000000M, 1000000M, 0, 1M, 80);
            _numStart.ValueChanged += OnOptionChanged;

            _numStep = NewNumeric(-1000000M, 1000000M, 0, 1M, 70);
            _numStep.ValueChanged += OnOptionChanged;

            _numPad = NewNumeric(0M, 12M, 0, 1M, 60);
            _numPad.ValueChanged += OnOptionChanged;

            _txtPrefix = new TextBox();
            _txtPrefix.Width = 80;
            _txtPrefix.TextChanged += OnOptionChanged;

            _txtSuffix = new TextBox();
            _txtSuffix.Width = 80;
            _txtSuffix.TextChanged += OnOptionChanged;

            // "效果"标签：**不再让文本宽度决定控件宽度**。
            //
            // 以前是 AutoSize = true，前缀一长就把它所在的那个 Field 面板撑成超宽；
            // 外层 FlowLayoutPanel 换行后，这一项落到 GroupBox 可视区之外（GroupBox 高度是固定的），
            // 于是"编号效果"整块看不见 —— 用户反馈的正是这个。
            //
            // 现在改成：宽度由所在行决定（Dock=Fill），并且用**多行只读文本框**代替单行标签——
            // 单行标签放不下时只能显示省略号，前缀一长就看不全（用户反馈的正是这个）；
            // 文本框会自动换行，装不下还能滚动，全文都能看到，也能选中复制。
            // 外观上 BorderStyle=None + 控件底色，看起来仍是一行文字。
            _lblSample = new TextBox();
            _lblSample.Multiline = true;
            _lblSample.ReadOnly = true;
            _lblSample.WordWrap = true;
            // 不显示右侧滚动条（用户反馈这块多余）：4 个样例一行放得下；
            // 真装不下时仍可用滚轮/方向键滚动，悬停提示也保留全文。
            _lblSample.ScrollBars = ScrollBars.None;
            _lblSample.BorderStyle = BorderStyle.None;
            _lblSample.BackColor = SystemColors.Control;
            _lblSample.TabStop = false;
            _lblSample.Dock = DockStyle.Fill;
            _lblSample.ForeColor = Accent;
            _lblSample.Margin = new Padding(0, 2, 0, 0);
            _lblSample.Text = "-";

            // 第一行：起始值/步长/补零/前缀/后缀（照旧自动换行）
            FlowLayoutPanel fields = NewFlow();
            fields.AutoScroll = false;          // 让它自己换行，不要横向滚动条
            fields.AutoSize = true;
            fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fields.Controls.Add(Field("起始值：", _numStart));
            fields.Controls.Add(Field("步长：", _numStep));
            fields.Controls.Add(Field("补零位数（0=不补）：", _numPad));
            fields.Controls.Add(Field("前缀：", _txtPrefix));
            fields.Controls.Add(Field("后缀：", _txtSuffix));

            // 第二行："效果"独占一行、铺满可用宽度（前缀再长也只是省略号，不会被挤没）
            TableLayoutPanel sampleRow = new TableLayoutPanel();
            sampleRow.Dock = DockStyle.Fill;
            sampleRow.ColumnCount = 2;
            sampleRow.RowCount = 1;
            sampleRow.Margin = new Padding(0);
            sampleRow.Padding = new Padding(0);
            sampleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sampleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Label sampleCaption = new Label();
            sampleCaption.Text = "效果：";
            sampleCaption.AutoSize = true;
            sampleCaption.TextAlign = ContentAlignment.MiddleLeft;
            sampleCaption.Margin = new Padding(0, 3, 0, 0);
            sampleRow.Controls.Add(sampleCaption, 0, 0);
            sampleRow.Controls.Add(_lblSample, 1, 0);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.Controls.Add(fields, 0, 0);
            layout.Controls.Add(sampleRow, 0, 1);

            box.Controls.Add(layout);
            return box;
        }

        private GroupBox BuildSortingGroup()
        {
            GroupBox box = NewGroup("排序规则（默认：先上后下，同一行从左到右）", 108);

            _cboSortMode = new ComboBox();
            _cboSortMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboSortMode.Width = 200;
            _cboSortMode.Items.AddRange(SortModeText);
            _cboSortMode.SelectedIndex = 0;
            _cboSortMode.SelectedIndexChanged += OnSortOptionChanged;

            _cboBasis = new ComboBox();
            _cboBasis.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboBasis.Width = 140;
            _cboBasis.Items.AddRange(BasisText);
            _cboBasis.SelectedIndex = 0;
            _cboBasis.SelectedIndexChanged += OnSortOptionChanged;

            _chkAutoTolerance = new CheckBox();
            _chkAutoTolerance.Text = "自动（按块高度中位数估算）";
            _chkAutoTolerance.AutoSize = true;
            _chkAutoTolerance.Checked = true;
            _chkAutoTolerance.Margin = new Padding(0, 4, 4, 0);
            _chkAutoTolerance.CheckedChanged += OnAutoToleranceChanged;

            _numTolerance = NewNumeric(0M, 100000000M, 3, 0.5M, 110);
            _numTolerance.Enabled = false;
            _numTolerance.ValueChanged += OnSortOptionChanged;

            FlowLayoutPanel flow = NewFlow();
            flow.Controls.Add(Field("排序方式：", _cboSortMode));
            flow.Controls.Add(Field("排序基准：", _cboBasis));
            flow.Controls.Add(Field("行(列)容差：", _chkAutoTolerance));
            flow.Controls.Add(Field("容差值：", _numTolerance));
            box.Controls.Add(flow);
            return box;
        }

        private GroupBox BuildBehaviourGroup()
        {
            GroupBox box = NewGroup("写入行为", 70);

            _chkFillOnlyEmpty = NewCheck("只填空白（不覆盖已有值）", false);
            _chkFillOnlyEmpty.CheckedChanged += OnSortOptionChanged;

            _chkSkipXref = NewCheck("跳过外部参照(xref)块", true);
            _chkSkipXref.CheckedChanged += OnSortOptionChanged;

            _chkGroupByRow = NewCheck("预览表格按行(列)聚合", true);
            _chkGroupByRow.CheckedChanged += OnGroupByRowChanged;

            FlowLayoutPanel flow = NewFlow();
            flow.Controls.Add(Field(string.Empty, _chkFillOnlyEmpty));
            flow.Controls.Add(Field(string.Empty, _chkSkipXref));
            flow.Controls.Add(Field(string.Empty, _chkGroupByRow));

            Label note = new Label();
            note.AutoSize = true;
            note.ForeColor = Color.DimGray;
            note.Margin = new Padding(0, 7, 0, 0);
            note.Text = "（预览表格用来看结果；点“确定”才会写入图形）";
            flow.Controls.Add(note);

            box.Controls.Add(flow);

            _tips.SetToolTip(_chkGroupByRow,
                "候选块多时推荐打开：图纸上同一行(列)的块在表格里只占一行，"
                + "\n摘要显示“第 N 行 · 几个 · 序号范围”，"
                + "每个块的 旧值→新值 直接写在“明细”列里，被跳过的块会标出原因。\n"
                + "关掉则每个块单独占一行。只影响本表格的显示，不影响编号与写入。");
            return box;
        }

        /// <summary>
        /// 预览表格。两种形态：
        ///   聚合（默认）：每个行(列)占一行 —— 行(列)/数量/序号范围/…
        ///   逐块：每个块占一行 —— 序/行/句柄/块名/图层/当前值/新值/X/Y/状态
        /// 切换时整表重建列，因为列集合本身不同（DataGridView 不能只换 Header）。
        /// </summary>
        private DataGridView BuildGrid()
        {
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.Margin = new Padding(0, 0, 0, 4);
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            // 关掉单元格悬停提示：鼠标扫过表格时会弹出一个灰色小方框（"悬停查看明细"），
            // 既挡视线又要挪开鼠标才能消失，用户明确要求不要。明细改为在表格下方常驻显示。
            _grid.ShowCellToolTips = false;

            ApplyGridColumns();
            return _grid;
        }

        private void ApplyGridColumns()
        {
            if (_grid == null)
            {
                return;
            }

            // ⚠️ 判定只看 _options.PreviewGroupByRow —— 必须和 BindGrid() 用**同一个**真值源。
            //
            // 历史 bug：这里原本读的是复选框控件 _chkGroupByRow.Checked，
            // 而 BindGrid() 读的是 _options.PreviewGroupByRow。平时二者同步，
            // 但在窗体构造期（BuildUi 建列时复选框还是代码默认值，
            // LoadOptionsIntoControls 改它的动作又被 _loading 吞掉）二者会分叉：
            //   列 = 聚合形态、数据 = 按逐块填
            // 于是 row.Cells["序"] 找不到列，抛 ArgumentException 把整条命令打断。
            // 统一到 _options 之后就不会再分叉（CollectOptions() 已在刷新前把控件值同步进来）。
            bool grouped = _options != null && _options.PreviewGroupByRow;
            _gridGrouped = grouped;

            _grid.Columns.Clear();

            if (grouped)
            {
                _grid.Columns.Add(NewColumn("Row", "行(列)", 70, DataGridViewContentAlignment.MiddleRight));
                _grid.Columns.Add(NewColumn("Count", "数量", 60, DataGridViewContentAlignment.MiddleRight));
                _grid.Columns.Add(NewColumn("Range", "序号范围", 110, DataGridViewContentAlignment.MiddleLeft));
                _grid.Columns.Add(NewColumn("Old", "当前值", 150, DataGridViewContentAlignment.MiddleLeft));
                _grid.Columns.Add(NewColumn("New", "新值", 150, DataGridViewContentAlignment.MiddleLeft));
                _grid.Columns.Add(NewColumn("State", "状态", 210, DataGridViewContentAlignment.MiddleLeft));
                _grid.Columns.Add(NewColumn("Hint", "明细", 240, DataGridViewContentAlignment.MiddleLeft));

                _grid.Columns["Old"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                _grid.Columns["New"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                _grid.Columns["State"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                _grid.Columns["Hint"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                return;
            }

            _grid.Columns.Add(NewColumn("序", "序号", 50, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(NewColumn("Row", "行", 46, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(NewColumn("Handle", "句柄", 80, DataGridViewContentAlignment.MiddleLeft));
            _grid.Columns.Add(NewColumn("Block", "块名", 130, DataGridViewContentAlignment.MiddleLeft));
            _grid.Columns.Add(NewColumn("Layer", "图层", 110, DataGridViewContentAlignment.MiddleLeft));
            _grid.Columns.Add(NewColumn("Old", "当前值", 120, DataGridViewContentAlignment.MiddleLeft));
            _grid.Columns.Add(NewColumn("New", "新值", 120, DataGridViewContentAlignment.MiddleLeft));
            _grid.Columns.Add(NewColumn("X", "X", 90, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(NewColumn("Y", "Y", 90, DataGridViewContentAlignment.MiddleRight));
            _grid.Columns.Add(NewColumn("State", "状态", 150, DataGridViewContentAlignment.MiddleLeft));

            _grid.Columns["Block"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns["State"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        private Control BuildBottomPanel()
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.ColumnCount = 2;
            panel.RowCount = 1;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblStats = new Label();
            _lblStats.Dock = DockStyle.Fill;
            _lblStats.TextAlign = ContentAlignment.MiddleLeft;
            _lblStats.AutoEllipsis = true;
            _lblStats.Text = "-";

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _btnOk = NewButton("确定（写入图形）", 140);
            _btnOk.Click += OnOkClicked;

            _btnCancel = NewButton("取消", 80);
            _btnCancel.Click += OnCancelClicked;

            _btnRefresh = NewButton("刷新预览", 100);
            _btnRefresh.Click += OnRefreshClicked;

            _tips.SetToolTip(_btnRefresh,
                "按当前参数重新读取图形，刷新下方表格的预览结果。只更新本表格，不改动图纸。");
            _tips.SetToolTip(_btnOk,
                "把表格\"新值\"列写入图纸中对应块的属性。\n"
                + "写入完成后，被编号的块会自动保持为选中状态（Esc 可取消选择）。\n"
                + "写入后输入 U 可一次性撤销。");
            buttons.Controls.Add(_btnOk);
            buttons.Controls.Add(_btnCancel);
            buttons.Controls.Add(_btnRefresh);

            panel.Controls.Add(_lblStats, 0, 0);
            panel.Controls.Add(buttons, 1, 0);
            return panel;
        }

        // =====================================================================
        // 小工具
        // =====================================================================

        private static FlowLayoutPanel NewFlow()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
            flow.AutoScroll = true;
            return flow;
        }

        private static Control Field(string caption, Control editor)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.LeftToRight;
            panel.WrapContents = false;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Margin = new Padding(0, 2, 14, 2);

            if (!string.IsNullOrEmpty(caption))
            {
                Label label = new Label();
                label.Text = caption;
                label.AutoSize = true;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Margin = new Padding(0, 5, 4, 0);
                panel.Controls.Add(label);
            }

            panel.Controls.Add(editor);
            return panel;
        }

        private static NumericUpDown NewNumeric(decimal min, decimal max, int decimals, decimal increment, int width)
        {
            NumericUpDown numeric = new NumericUpDown();
            numeric.Minimum = min;
            numeric.Maximum = max;
            numeric.DecimalPlaces = decimals;
            numeric.Increment = increment;
            numeric.Width = width;
            numeric.Margin = new Padding(0, 1, 0, 0);
            return numeric;
        }

        private static CheckBox NewCheck(string text, bool isChecked)
        {
            CheckBox box = new CheckBox();
            box.Text = text;
            box.AutoSize = true;
            box.Checked = isChecked;
            box.Margin = new Padding(0, 4, 16, 0);
            return box;
        }

        private static DataGridViewTextBoxColumn NewColumn(
            string name, string header, int width, DataGridViewContentAlignment align)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.Width = width;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.DefaultCellStyle.Alignment = align;
            return column;
        }

        // =====================================================================
        // 数据同步
        // =====================================================================

        private void LoadOptionsIntoControls()
        {
            for (int i = 0; i < _definitions.Count; i++)
            {
                _tagValues.Add(_definitions[i].Tag);
                _cboTag.Items.Add(_definitions[i].Describe());
            }

            int index = -1;
            if (!string.IsNullOrEmpty(_options.Tag))
            {
                index = _tagValues.FindIndex(delegate(string t)
                {
                    return string.Equals(t, _options.Tag, StringComparison.OrdinalIgnoreCase);
                });
            }
            if (index >= 0)
            {
                _cboTag.SelectedIndex = index;
            }
            else if (_tagValues.Count > 0)
            {
                _cboTag.SelectedIndex = 0;
            }
            else
            {
                _cboTag.Text = _options.Tag;
            }
            UpdateTagInfo();

            _numStart.Value = Clamp(_numStart, _options.Start);
            _numStep.Value = Clamp(_numStep, _options.Step);
            _numPad.Value = Clamp(_numPad, _options.Pad);
            _txtPrefix.Text = _options.Prefix ?? string.Empty;
            _txtSuffix.Text = _options.Suffix ?? string.Empty;

            _cboSortMode.SelectedIndex = SortModeIndexOf(_options);
            _cboBasis.SelectedIndex = (int)_options.Basis;

            bool auto = _options.RowTolerance <= 0.0;
            _chkAutoTolerance.Checked = auto;
            _numTolerance.Enabled = !auto;
            if (!auto)
            {
                _numTolerance.Value = Clamp(_numTolerance, (decimal)_options.RowTolerance);
            }

            _chkFillOnlyEmpty.Checked = _options.FillOnlyEmpty;
            _chkSkipXref.Checked = _options.SkipXrefBlocks;
            _chkGroupByRow.Checked = _options.PreviewGroupByRow;

            UpdateSample();
        }

        private static decimal Clamp(NumericUpDown control, decimal value)
        {
            if (value < control.Minimum)
            {
                return control.Minimum;
            }
            if (value > control.Maximum)
            {
                return control.Maximum;
            }
            return value;
        }

        private void CollectOptions()
        {
            string typed = _cboTag.Text ?? string.Empty;
            if (_cboTag.SelectedIndex >= 0 && _cboTag.SelectedIndex < _tagValues.Count
                && string.Equals(typed, _cboTag.Items[_cboTag.SelectedIndex] as string, StringComparison.Ordinal))
            {
                _options.Tag = _tagValues[_cboTag.SelectedIndex];
            }
            else
            {
                _options.Tag = typed.Trim();
            }

            _options.Start = (int)_numStart.Value;
            _options.Step = (int)_numStep.Value;
            _options.Pad = (int)_numPad.Value;
            _options.Prefix = _txtPrefix.Text ?? string.Empty;
            _options.Suffix = _txtSuffix.Text ?? string.Empty;

            ApplySortMode(_cboSortMode.SelectedIndex, _options);
            _options.Basis = (SortBasis)Math.Max(0, _cboBasis.SelectedIndex);
            // 界面上不再提供坐标系选择，排序统一按世界坐标系（WCS），与视图旋转无关
            _options.Coord = CoordinateMode.Wcs;
            _options.RowTolerance = _chkAutoTolerance.Checked ? 0.0 : (double)_numTolerance.Value;

            _options.FillOnlyEmpty = _chkFillOnlyEmpty.Checked;
            _options.SkipXrefBlocks = _chkSkipXref.Checked;
            _options.PreviewGroupByRow = _chkGroupByRow.Checked;
        }

        private void OnTagChanged(object sender, EventArgs e)
        {
            UpdateTagInfo();
        }

        private void UpdateTagInfo()
        {
            if (_lblTagInfo == null)
            {
                return;
            }

            int index = _cboTag.SelectedIndex;
            if (index >= 0 && index < _definitions.Count
                && string.Equals(_cboTag.Text, _cboTag.Items[index] as string, StringComparison.Ordinal))
            {
                AttributeDefInfo info = _definitions[index];
                string text = string.IsNullOrEmpty(info.Prompt) ? "(无提示文字)" : info.Prompt;
                if (info.Constant)
                {
                    text += "    注意：常量属性，无法修改";
                }
                _lblTagInfo.Text = text;
            }
            else
            {
                _lblTagInfo.Text = "(自定义 Tag，将以实际存在的属性为准)";
            }
        }

        private void OnOptionChanged(object sender, EventArgs e)
        {
            UpdateSample();
        }

        private void UpdateSample()
        {
            if (_lblSample == null)
            {
                return;
            }
            NumberOptions probe = _options.Clone();
            probe.Start = (int)_numStart.Value;
            probe.Step = (int)_numStep.Value;
            probe.Pad = (int)_numPad.Value;
            probe.Prefix = _txtPrefix.Text ?? string.Empty;
            probe.Suffix = _txtSuffix.Text ?? string.Empty;

            // 只列 4 个样例：原来 6 个（5 个 + "..." + 末位），前缀一长就要换行/滚动才看得全；
            // 4 个正好一行放得下，也不用省略号（count 与 maxItems 都传 4）。
            string sample = NumberingService.DescribeSample(4, probe, 4);
            _lblSample.Text = sample;

            // 多行文本框已能换行/滚动看到全文，悬停提示只作兜底保留。
            // _tips 的触发延迟已在 ConfigureToolTipDelays() 里调长，不会随手一划就弹出来。
            if (_tips != null)
            {
                _tips.SetToolTip(_lblSample, sample);
            }
        }

        private void OnAutoToleranceChanged(object sender, EventArgs e)
        {
            _numTolerance.Enabled = !_chkAutoTolerance.Checked;
            if (_loading)
            {
                return;
            }
            OnSortOptionChanged(sender, e);
        }

        private void OnSortOptionChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }
            // 排序参数只影响顺序，不需要重读数据库
            ResortPlan();
        }

        /// <summary>切换"按行(列)聚合"。只影响表格，不重读数据库。</summary>
        private void OnGroupByRowChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }

            CollectOptions();
            ApplyGridColumns();
            if (_plan == null)
            {
                return;
            }

            _plan = PlanBuilder.SortAndNumber(_plan.Items, _options, _wcsToSort);
            BindGrid();
            UpdateStats();
        }

        // =====================================================================
        // 预览
        // =====================================================================

        private void RefreshPlan()
        {
            CollectOptions();

            if (_rebuild == null)
            {
                return;
            }

            try
            {
                _plan = _rebuild(_options);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取块属性失败：\r\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            BindGrid();
            UpdateStats();
        }

        private void ResortPlan()
        {
            CollectOptions();
            if (_plan == null)
            {
                return;
            }

            _plan = PlanBuilder.SortAndNumber(_plan.Items, _options, _wcsToSort);
            BindGrid();
            UpdateStats();
        }

        /// <summary>
        /// 填预览表格。
        ///
        /// 外层兜住异常：表格画不出来不该中断整条命令 —— 此时图纸还没动过，
        /// 用户仍可以调参数、或点「刷新预览」重试，不该被直接踢出流程。
        /// 失败原因写进 <see cref="_previewError"/>，由 <see cref="UpdateStats"/> 显示到状态栏。
        /// </summary>
        private void BindGrid()
        {
            try
            {
                BindGridCore();
                _previewError = string.Empty;
            }
            catch (Exception ex)
            {
                _previewError = "预览表格刷新失败：" + ex.Message;
                try { _grid.Rows.Clear(); } catch (Exception) { }
            }
        }

        private void BindGridCore()
        {
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();

                // 自愈：列形态必须与本次要绑的形态一致。
                // 任何路径漏了重建列，都在这里纠正 —— 取 Cells["序"] / Cells["Count"] 时才不会落空。
                bool grouped = _options.PreviewGroupByRow;
                if (_gridGrouped != grouped)
                {
                    ApplyGridColumns();
                }

                if (_plan == null)
                {
                    return;
                }

                List<PreviewGroup> groups = PreviewGroup.Build(_plan, grouped);
                int count = Math.Min(groups.Count, MaxPreviewRows);
                if (count > 0)
                {
                    _grid.Rows.Add(count);
                }

                for (int i = 0; i < count; i++)
                {
                    if (grouped)
                    {
                        BindGroupRow(_grid.Rows[i], groups[i]);
                    }
                    else
                    {
                        BindItemRow(_grid.Rows[i], groups[i].Items[0]);
                    }
                }
            }
            finally
            {
                _grid.ResumeLayout();
            }
        }

        /// <summary>聚合模式：一行(列)占表格一行，明细放在悬停提示里。</summary>
        private static void BindGroupRow(DataGridViewRow row, PreviewGroup group)
        {
            row.Cells["Row"].Value = group.RowNumber > 0
                ? group.RowNumber.ToString(CultureInfo.InvariantCulture) : "-";
            row.Cells["Count"].Value = group.Items.Count.ToString(CultureInfo.InvariantCulture);
            row.Cells["Range"].Value = group.DescribeOrdinalRange();
            row.Cells["Old"].Value = DescribeDistinct(group.Items, true);
            row.Cells["New"].Value = DescribeDistinct(group.Items, false);
            row.Cells["State"].Value = group.DescribeState();
            row.Cells["Hint"].Value = group.DescribeInline(PreviewGroup.MaxInlineBlocks);


            if (!group.CanWrite)
            {
                row.DefaultCellStyle.ForeColor = Color.Firebrick;
            }
            else if (group.SkippedCount > 0)
            {
                row.DefaultCellStyle.ForeColor = Color.FromArgb(0xB0, 0x60, 0x00);
            }
        }

        /// <summary>逐块模式：一个块占表格一行（与聚合前的显示一致）。</summary>
        private static void BindItemRow(DataGridViewRow row, BlockItem item)
        {
            row.Cells["序"].Value = item.Ordinal > 0
                ? item.Ordinal.ToString(CultureInfo.InvariantCulture) : "-";
            row.Cells["Row"].Value = item.Row >= 0
                ? (item.Row + 1).ToString(CultureInfo.InvariantCulture) : "-";
            row.Cells["Handle"].Value = item.Handle;
            row.Cells["Block"].Value = item.EffectiveBlockName;
            row.Cells["Layer"].Value = item.LayerName;
            row.Cells["Old"].Value = item.OldValue;
            row.Cells["New"].Value = item.NewValue;
            row.Cells["X"].Value = item.SortX.ToString("0.###", CultureInfo.InvariantCulture);
            row.Cells["Y"].Value = item.SortY.ToString("0.###", CultureInfo.InvariantCulture);
            row.Cells["State"].Value = item.CanWrite ? "可编号" : SkipReasonText.Describe(item.Skip);

            if (!item.CanWrite)
            {
                row.DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }

        /// <summary>
        /// 聚合行的"当前值 / 新值"摘要。同一行内各块的值常常一样（例如都是 "1" 或都为空），
        /// 这时只显示一个值 + 计数，比刷一屏重复内容清楚。
        /// </summary>
        private static string DescribeDistinct(List<BlockItem> items, bool oldValue)
        {
            if (items.Count == 0)
            {
                return string.Empty;
            }

            string first = Display(oldValue ? items[0].OldValue : items[0].NewValue);
            for (int i = 1; i < items.Count; i++)
            {
                string current = Display(oldValue ? items[i].OldValue : items[i].NewValue);
                if (!string.Equals(current, first, StringComparison.Ordinal))
                {
                    return "(本行内各不相同)";
                }
            }

            return first;
        }

        private static string Display(string value)
        {
            return string.IsNullOrEmpty(value) ? "(空)" : value;
        }

        private void UpdateStats()
        {
            UpdateSelectionStatus();

            // 上一次刷新若失败会把状态栏标红，这里恢复常规颜色
            _lblStats.ForeColor = SystemColors.ControlText;

            // 没有候选块时，"确定"和"刷新预览"没有意义，直接禁用
            bool hasCandidates = _candidates.Count > 0;
            _btnOk.Enabled = hasCandidates;
            _btnRefresh.Enabled = hasCandidates;

            if (_plan == null || _candidates.Count == 0)
            {
                _lblStats.Text = hasCandidates
                    ? "无候选块"
                    : "尚未收集到块：先点【① 在图纸中选一个样板块】，再点【② 选择范围】框选区域。";
                ApplyPreviewError();
                return;
            }

            string text = string.Format(CultureInfo.InvariantCulture,
                "候选 {0} 个；可编号 {1} 个；跳过 {2} 个；行(列)数 {3}；容差 {4}",
                _plan.Items.Count, _plan.WritableCount, _plan.SkippedCount,
                _plan.RowCount, _plan.DescribeTolerance());

            text += DescribeGridRows();

            if (_plan.Notes.Count > 0)
            {
                text += "！" + _plan.Notes[0];
            }

            _lblStats.Text = text;
            ApplyPreviewError();
        }

        /// <summary>
        /// 预览表格刷新失败时，把原因补到状态栏末尾并标红。
        ///
        /// 必须在 <see cref="UpdateStats"/> 的最后调用 —— 它开头会把颜色恢复成常规色，
        /// 所以错误提示要放在那之后，否则会被盖掉。
        /// 只影响表格显示，不影响编号与写入：用户仍可按「确定」继续。
        /// </summary>
        private void ApplyPreviewError()
        {
            if (string.IsNullOrEmpty(_previewError) || _lblStats == null)
            {
                return;
            }

            _lblStats.Text += "　" + _previewError;
            _lblStats.ForeColor = Color.Firebrick;
        }

        /// <summary>把"表格现在有多少行"讲清楚，免得用户以为表格漏了块。</summary>
        private string DescribeGridRows()
        {
            if (_plan.RowCount <= 0)
            {
                return string.Empty;
            }

            int rows = _options.PreviewGroupByRow ? _plan.RowCount : _plan.Items.Count;
            if (rows <= MaxPreviewRows)
            {
                return string.Format(CultureInfo.InvariantCulture, "；表格 {0} 行", rows);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "（表格只显示前 {0} 行）", MaxPreviewRows);
        }

        /// <summary>更新"选择对象"那一行的状态文字，以及第 ② 步按钮的可用性。</summary>
        private void UpdateSelectionStatus()
        {
            if (_lblSelection == null)
            {
                return;
            }

            bool hasSample = !string.IsNullOrEmpty(_blockLabel);

            // 没有样板就不知道该按哪种块收集，第 ② 步不可点
            _btnPickRange.Enabled = hasSample;

            if (!hasSample)
            {
                _lblSelection.Text = "尚未选择：请先点【① 在图纸中选一个样板块】";
                _lblSelection.ForeColor = Color.Firebrick;
                return;
            }

            if (_candidates.Count == 0)
            {
                _lblSelection.Text = "样板：" + _blockLabel
                    + " —— 再点【② 选择范围】框选区域（或点 ① 换一个样板）";
                _lblSelection.ForeColor = Color.FromArgb(0xB0, 0x60, 0x00);
                return;
            }

            // 用户真正关心的是"有多少个要编号"，所以把可编号数放在最前面说清楚；
            // 框选到多少对象只是过程量，放在括号里备查。
            int writable = _plan != null ? _plan.WritableCount : _candidates.Count;
            int skipped = _plan != null ? _plan.SkippedCount : 0;

            string text = string.Format(CultureInfo.InvariantCulture,
                "样板：{0} —— 可编号 {1} 个", _blockLabel, writable);

            if (skipped > 0)
            {
                text += string.Format(CultureInfo.InvariantCulture,
                    "（跳过 {0} 个）", skipped);
            }

            text += string.Format(CultureInfo.InvariantCulture,
                "　同名块共 {0} 个", _candidates.Count);

            _lblSelection.Text = text;
            _lblSelection.ForeColor = Color.FromArgb(0x1F, 0x6F, 0x37);
        }

        // =====================================================================
        // 按钮
        // =====================================================================

        private void OnRefreshClicked(object sender, EventArgs e)
        {
            RefreshPlan();
        }

        private void OnPickSampleClicked(object sender, EventArgs e)
        {
            CollectOptions();
            ShowPickingStatus("正在图纸中点选样板块…");
            Action = NumberingAction.PickSample;
            RememberBounds();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnPickRangeClicked(object sender, EventArgs e)
        {
            CollectOptions();
            ShowPickingStatus("正在图纸中框选范围…");
            Action = NumberingAction.PickRange;
            RememberBounds();
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// 即将回图纸拾取：把上一次的统计刷成中性提示，
        /// 避免"样板块：图签1，区域内同名块 81 个"这种旧数字留在屏幕上看不出是旧的。
        /// </summary>
        private void ShowPickingStatus(string message)
        {
            if (_lblSelection == null)
            {
                return;
            }
            _lblSelection.Text = message;
            _lblSelection.ForeColor = Color.DimGray;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            CollectOptions();

            if (_candidates.Count == 0)
            {
                MessageBox.Show(this,
                    "还没有收集到要编号的块。\r\n"
                    + "请点【① 在图纸中选一个样板块】选好样板，再点【② 选择范围】框选区域。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(_options.Tag))
            {
                MessageBox.Show(this, "请先选择或输入要写入的属性 Tag。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 关闭前再按当前图形状态重算一次，避免用户在对话框打开期间改过图形
            RefreshPlan();
            if (_plan == null)
            {
                return;
            }
            if (_plan.WritableCount == 0)
            {
                MessageBox.Show(this, "没有任何块可以编号。请检查属性 Tag 是否正确。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Action = NumberingAction.Write;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            Action = NumberingAction.Cancel;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // =====================================================================
        // 排序方式映射
        // =====================================================================

        /// <summary>
        /// 把下拉框的 8 种“视觉方向”映射到 (主轴, 主板降序, 内轴降序)。
        ///
        /// 关键点：行优先时主轴是 Y（降序 = 先上后下），列优先时主轴是 X
        /// （降序 = 先右后左）。所以“先左后右”对应 DescendingPrimary = false，
        /// 这里必须做一次换算，不能直接把视觉方向当降序标志。
        /// </summary>
        private static void ApplySortMode(int index, NumberOptions options)
        {
            switch (index)
            {
                // 先上后下 · 行内从左到右
                case 0:
                    options.Axis = PrimaryAxis.Row;
                    options.DescendingPrimary = true;
                    options.DescendingInner = false;
                    break;

                // 先上后下 · 行内从右到左
                case 1:
                    options.Axis = PrimaryAxis.Row;
                    options.DescendingPrimary = true;
                    options.DescendingInner = true;
                    break;

                // 先下后上 · 行内从左到右
                case 2:
                    options.Axis = PrimaryAxis.Row;
                    options.DescendingPrimary = false;
                    options.DescendingInner = false;
                    break;

                // 先下后上 · 行内从右到左
                case 3:
                    options.Axis = PrimaryAxis.Row;
                    options.DescendingPrimary = false;
                    options.DescendingInner = true;
                    break;

                // 先左后右 · 列内从上到下  -> 主轴 X 升序，内轴 Y 降序
                case 4:
                    options.Axis = PrimaryAxis.Column;
                    options.DescendingPrimary = false;
                    options.DescendingInner = true;
                    break;

                // 先左后右 · 列内从下到上  -> 主轴 X 升序，内轴 Y 升序
                case 5:
                    options.Axis = PrimaryAxis.Column;
                    options.DescendingPrimary = false;
                    options.DescendingInner = false;
                    break;

                // 先右后左 · 列内从上到下  -> 主轴 X 降序，内轴 Y 降序
                case 6:
                    options.Axis = PrimaryAxis.Column;
                    options.DescendingPrimary = true;
                    options.DescendingInner = true;
                    break;

                // 先右后左 · 列内从下到上  -> 主轴 X 降序，内轴 Y 升序
                case 7:
                    options.Axis = PrimaryAxis.Column;
                    options.DescendingPrimary = true;
                    options.DescendingInner = false;
                    break;

                default:
                    options.Axis = PrimaryAxis.Row;
                    options.DescendingPrimary = true;
                    options.DescendingInner = false;
                    break;
            }
        }

        /// <summary>ApplySortMode 的逆运算。</summary>
        private static int SortModeIndexOf(NumberOptions options)
        {
            if (options.Axis == PrimaryAxis.Row)
            {
                if (options.DescendingPrimary)
                {
                    return options.DescendingInner ? 1 : 0;
                }
                return options.DescendingInner ? 3 : 2;
            }

            if (options.DescendingPrimary)
            {
                return options.DescendingInner ? 6 : 7;
            }
            return options.DescendingInner ? 4 : 5;
        }
    }
}
