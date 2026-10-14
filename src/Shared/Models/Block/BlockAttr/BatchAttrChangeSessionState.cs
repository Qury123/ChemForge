namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 一次 BLKATTCHANGE 命令执行过程中、**面板与面板之间**传递的「用户已经填好的东西」。
    ///
    /// 为什么需要它：面板改成「回图纸拾取 → 重新弹出」的两步流程之后，每一轮都要新建窗体
    /// （绝不能让模态窗体横跨图纸拾取），重建时若只按落盘记忆回填，用户刚勾的三个
    /// 「修改开关」就会被无声清掉。图块编号面板是靠把 <c>NumberOptions</c>
    /// 原样带回解决这个问题的（见 <c>Commands\Block\Numbering\BlockNumberingCommands.cs</c> 里
    /// "参数经 NumberOptions 原样带回，所以界面状态不会丢"），这里是等价的一份快照。
    ///
    /// 与 <see cref="BatchAttrChangeMemory"/> 的分工要分清：
    /// <list type="bullet">
    /// <item><see cref="BatchAttrChangeMemory"/> 是**上次点确定时落盘**的记忆，跨图纸、跨启动；</item>
    /// <item>本类只活在**本次命令**里 —— 回图纸框个范围，不该把面板上刚填的值清掉。</item>
    /// </list>
    /// </summary>
    public sealed class BatchAttrChangeSessionState
    {
        /// <summary>
        /// 面板上的已填值 + 已勾选的属性标签 + 下拉索引，结构与落盘记忆完全相同。
        /// </summary>
        public BatchAttrChangeMemory Memory { get; set; } = new BatchAttrChangeMemory();

        /// <summary>
        /// 「修改文字内容」开关的勾选状态。
        /// 单独放一个字段，是因为三个开关**按既定行为不进记忆**
        /// （回填时恒把 MODVAL / MODFMT / MODPROP 置 "0"），
        /// 而跨轮拾取时必须带回来。
        /// </summary>
        public bool ModifyContent { get; set; }

        /// <summary>「修改文字选项」开关的勾选状态</summary>
        public bool ModifyFormat { get; set; }

        /// <summary>「修改特性」开关的勾选状态</summary>
        public bool ModifyProperties { get; set; }
    }
}
