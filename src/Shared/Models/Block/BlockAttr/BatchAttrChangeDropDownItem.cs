namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 下拉框候选项 —— 标签 + 数值。
    ///
    /// 约定：<see cref="Value"/> 为 null 恒表示
    /// 「不修改」，且**永远是列表的第 0 项**。
    /// </summary>
    public sealed class BatchAttrChangeDropDownItem
    {
        /// <summary>显示文本（与 DCL popup_list 里的候选文本逐字一致）</summary>
        public string Label { get; }

        /// <summary>候选项对应的数值；null = 不修改</summary>
        public int? Value { get; }

        /// <summary>是否为「其他颜色号（在下方输入 1-255）」这一特殊项</summary>
        public bool IsCustom { get; }

        /// <summary>创建候选项</summary>
        /// <param name="label">显示文本</param>
        /// <param name="value">数值；null = 不修改</param>
        /// <param name="isCustom">是否为「自定义颜色号」项</param>
        public BatchAttrChangeDropDownItem(string label, int? value, bool isCustom = false)
        {
            Label = label;
            Value = value;
            IsCustom = isCustom;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 三态下拉框候选项 —— 「不修改 / 是 / 否」。
    /// 与面板里三态下拉框的候选表逐项一致。
    /// </summary>
    public sealed class BatchAttrChangeTriStateItem
    {
        /// <summary>显示文本（如「是（反向）」「否（正常）」）</summary>
        public string Label { get; }

        /// <summary>候选项对应的布尔值；null = 不修改</summary>
        public bool? Value { get; }

        /// <summary>创建三态候选项</summary>
        /// <param name="label">显示文本</param>
        /// <param name="value">布尔值；null = 不修改</param>
        public BatchAttrChangeTriStateItem(string label, bool? value)
        {
            Label = label;
            Value = value;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Label;
        }
    }
}
