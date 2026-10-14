using System.Collections.Generic;

namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 打开「批量修改块属性」面板所需的全部输入。
    ///
    /// 建立对话框之后要往里面塞的那些东西：
    /// BLKINFO（当前块名）、RANGEINFO（范围说明）、ATTLIST（属性标签列表）、
    /// 三个动态下拉（文字样式 / 图层 / 线型，候选来自当前图纸），
    /// 以及从记忆里回填的值。
    ///
    /// 面板可反复弹出：① 选块 / ② 框范围 两个按钮会把面板关掉、回图纸拾取，
    /// 拾取完再新建一个面板。所以本类还要携带「这次拾取之前面板上是什么样」
    /// （见 <see cref="Session"/> 与 <see cref="RangeSource"/>）。
    /// </summary>
    public sealed class BatchAttrChangeContext
    {
        /// <summary>「当前块名：」后面那串（选中块的唯一有效块名，顿号连接）</summary>
        public string BlockLabel { get; set; } = string.Empty;

        /// <summary>本次选中的块引用数量</summary>
        public int SelectedBlockCount { get; set; }

        /// <summary>全图同名块数量（含本次所选）；用于「修改范围」的单选说明</summary>
        public int SameNameBlockCount { get; set; }

        /// <summary>
        /// 当前这批块是从哪一步来的（决定「修改范围」那行状态文字怎么写，以及
        /// <see cref="BatchAttrChangeOptions.Scope"/> 取「仅本次所选」还是「全图同名」）。
        /// </summary>
        public BatchAttrChangeRangeSource RangeSource { get; set; } = BatchAttrChangeRangeSource.None;

        /// <summary>
        /// 本次命令里**上一轮**面板的已填状态（三个修改开关 + 已填值 + 勾选的标签）；
        /// 首次打开面板时为 null。
        ///
        /// 面板是「回图纸拾取 → 重新弹出」的两步流程，每轮都会新建窗体；把它原样带回来，
        /// 用户刚勾的开关和刚填的值就不会因为"去框了个范围"而被清掉 ——
        /// 对应 图块编号面板把 <c>NumberOptions</c> 原样带回的做法。
        /// 注意它与 <see cref="RememberedMemory"/> 不是一回事：那个是跨图纸跨启动的落盘记忆。
        /// </summary>
        public BatchAttrChangeSessionState Session { get; set; }

        /// <summary>汇总去重排序后的属性标签全集（对话框列表的内容）</summary>
        public IList<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// 上次的记忆（回填用）。
        ///
        /// 这里刻意传**原始记忆**而不是转换好的 <see cref="BatchAttrChangeOptions"/>：
        /// 这里也是直接把记忆里的字符串 / 索引
        /// 塞回控件，面板才能区分「上次选的是下拉里那一项」和
        /// 「上次选的是『其他颜色号』并填了个数字」—— 转成设置对象后这个区别就丢了。
        /// 三个修改开关的勾选状态不在记忆里，面板一律按未勾选开局。
        /// </summary>
        public BatchAttrChangeMemory RememberedMemory { get; set; } = new BatchAttrChangeMemory();

        /// <summary>当前图纸的文字样式名（已排序，已剔除含通配符的匿名样式）</summary>
        public IList<string> StyleNames { get; set; } = new List<string>();

        /// <summary>当前图纸的图层名（已排序，必定含 "0"）</summary>
        public IList<string> LayerNames { get; set; } = new List<string>();

        /// <summary>当前图纸的线型名（已排序）</summary>
        public IList<string> LinetypeNames { get; set; } = new List<string>();
    }
}
