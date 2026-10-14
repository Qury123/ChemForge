namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 当前这批待处理块引用是**从哪一步来的**。
    ///
    /// 决定两件事：
    /// <list type="number">
    /// <item>面板「① 选择要修改的块（两步）」右侧那行状态文字怎么写；</item>
    /// <item><see cref="BatchAttrChangeOptions.Scope"/> 取哪一档 —— 只有
    /// <see cref="WholeDrawing"/> 才是「全图所有同名图块」，其余都是「仅当前选中的这批」。</item>
    /// </list>
    ///
    /// 两步模型照搬本仓库既有的 图块编号面板
    /// （<c>UI\Forms\Block\Numbering\NumberingOptionsForm.cs</c> 的「选择要编号的块（两步）」）：
    /// 第 ① 步定下**样板**（决定改哪种块、属性标签有哪些），
    /// 第 ② 步在样板的约束下换一批块引用（框选一片区域；不选任何对象直接回车 = 整个图形）。
    /// </summary>
    public enum BatchAttrChangeRangeSource
    {
        /// <summary>还没选过块（刚打开面板）</summary>
        None = 0,

        /// <summary>来自第 ① 步：在图纸里点选 / 框选的一批块</summary>
        PickedBlocks = 1,

        /// <summary>来自第 ② 步：框选到的、与①同名的那些块</summary>
        PickedRange = 2,

        /// <summary>来自第 ② 步的「不选任何对象直接回车」：整个图形里的同名块</summary>
        WholeDrawing = 3
    }
}
