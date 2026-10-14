namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 批量属性替换的「修改范围」。
    ///
    /// 「修改范围」的两个取值：
    /// 仅本次选中的图块（默认）/ 全图所有同名图块。
    /// </summary>
    public enum BatchAttrChangeScope
    {
        /// <summary>仅本次选中的图块（默认）</summary>
        SelectedOnly = 0,

        /// <summary>全图所有同名图块（按「有效块名」匹配，见 BlockEffectiveNameResolver）</summary>
        AllSameName = 1
    }
}
