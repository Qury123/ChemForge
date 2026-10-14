using System.Collections.Generic;

namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 批量属性替换的**静态候选表**。
    ///
    /// 四张静态候选表 —— 文字对齐（DXF 72）、三态、线宽（DXF 370）、颜色（DXF 62）。
    /// 候选文本、数值、顺序都刻意保持固定，
    /// 因为用户的使用习惯（「线宽列表第 8 项」这类肌肉记忆）建立在这个顺序上。
    ///
    /// 三张「数值表」的第 0 项恒为「不修改」（Value = null）。
    /// </summary>
    public static class BatchAttrChangeTables
    {
        /// <summary>文字对齐候选（DXF 组码 72），第 0 项 = 不修改</summary>
        public static readonly IReadOnlyList<BatchAttrChangeDropDownItem> Justify =
            new List<BatchAttrChangeDropDownItem>
            {
                new BatchAttrChangeDropDownItem("（不修改对正）", null),
                new BatchAttrChangeDropDownItem("左 (Left)", 0),
                new BatchAttrChangeDropDownItem("居中 (Center)", 1),
                new BatchAttrChangeDropDownItem("右 (Right)", 2),
                new BatchAttrChangeDropDownItem("对齐 (Aligned)", 3),
                new BatchAttrChangeDropDownItem("中间 (Middle)", 4),
                new BatchAttrChangeDropDownItem("拟合 (Fit)", 5)
            };

        /// <summary>反向候选（DXF 组码 71 的 bit 2），第 0 项 = 不修改</summary>
        public static readonly IReadOnlyList<BatchAttrChangeTriStateItem> Backwards =
            new List<BatchAttrChangeTriStateItem>
            {
                new BatchAttrChangeTriStateItem("（不修改）", null),
                new BatchAttrChangeTriStateItem("是（反向）", true),
                new BatchAttrChangeTriStateItem("否（正常）", false)
            };

        /// <summary>倒置候选（DXF 组码 71 的 bit 4），第 0 项 = 不修改</summary>
        public static readonly IReadOnlyList<BatchAttrChangeTriStateItem> UpsideDown =
            new List<BatchAttrChangeTriStateItem>
            {
                new BatchAttrChangeTriStateItem("（不修改）", null),
                new BatchAttrChangeTriStateItem("是（倒置）", true),
                new BatchAttrChangeTriStateItem("否（正常）", false)
            };

        /// <summary>
        /// 线宽候选（DXF 组码 370）。AutoCAD 的线宽枚举值 = 毫米值 × 100，
        /// 例如 0.25 mm 写作 25；随层 = -1、随块 = -2。第 0 项 = 不修改。
        /// </summary>
        public static readonly IReadOnlyList<BatchAttrChangeDropDownItem> LineWeights =
            new List<BatchAttrChangeDropDownItem>
            {
                new BatchAttrChangeDropDownItem("（不修改线宽）", null),
                new BatchAttrChangeDropDownItem("随层 (ByLayer)", -1),
                new BatchAttrChangeDropDownItem("随块 (ByBlock)", -2),
                new BatchAttrChangeDropDownItem("0.13 mm", 13),
                new BatchAttrChangeDropDownItem("0.15 mm", 15),
                new BatchAttrChangeDropDownItem("0.18 mm", 18),
                new BatchAttrChangeDropDownItem("0.20 mm", 20),
                new BatchAttrChangeDropDownItem("0.25 mm", 25),
                new BatchAttrChangeDropDownItem("0.30 mm", 30),
                new BatchAttrChangeDropDownItem("0.35 mm", 35),
                new BatchAttrChangeDropDownItem("0.40 mm", 40),
                new BatchAttrChangeDropDownItem("0.50 mm", 50),
                new BatchAttrChangeDropDownItem("0.53 mm", 53),
                new BatchAttrChangeDropDownItem("0.60 mm", 60),
                new BatchAttrChangeDropDownItem("0.70 mm", 70),
                new BatchAttrChangeDropDownItem("0.80 mm", 80),
                new BatchAttrChangeDropDownItem("0.90 mm", 90),
                new BatchAttrChangeDropDownItem("1.00 mm", 100),
                new BatchAttrChangeDropDownItem("1.06 mm", 106),
                new BatchAttrChangeDropDownItem("1.20 mm", 120),
                new BatchAttrChangeDropDownItem("1.40 mm", 140),
                new BatchAttrChangeDropDownItem("1.58 mm", 158),
                new BatchAttrChangeDropDownItem("2.00 mm", 200),
                new BatchAttrChangeDropDownItem("2.11 mm", 211)
            };

        /// <summary>
        /// 颜色候选（DXF 组码 62）：0 = 随块（ByBlock）、256 = 随层（ByLayer）、
        /// 1~7 = 标准 ACI 色、最后一项 = 自定义颜色号。第 0 项 = 不修改。
        /// </summary>
        public static readonly IReadOnlyList<BatchAttrChangeDropDownItem> Colors =
            new List<BatchAttrChangeDropDownItem>
            {
                new BatchAttrChangeDropDownItem("（不修改颜色）", null),
                new BatchAttrChangeDropDownItem("随块 (ByBlock)", 0),
                new BatchAttrChangeDropDownItem("随层 (ByLayer)", 256),
                new BatchAttrChangeDropDownItem("红 Red (1)", 1),
                new BatchAttrChangeDropDownItem("黄 Yellow (2)", 2),
                new BatchAttrChangeDropDownItem("绿 Green (3)", 3),
                new BatchAttrChangeDropDownItem("青 Cyan (4)", 4),
                new BatchAttrChangeDropDownItem("蓝 Blue (5)", 5),
                new BatchAttrChangeDropDownItem("品红 Magenta (6)", 6),
                new BatchAttrChangeDropDownItem("白 White (7)", 7),
                new BatchAttrChangeDropDownItem("其他颜色号（在下方输入 1-255）", null, true)
            };

        /// <summary>颜色下拉里「自定义颜色号」那一项的固定索引</summary>
        public const int CustomColorIndex = 10;

        /// <summary>文字样式下拉的第 0 项</summary>
        public const string NoStyleLabel = "（不修改文字样式）";

        /// <summary>图层下拉的第 0 项</summary>
        public const string NoLayerLabel = "（不修改图层）";

        /// <summary>线型下拉的第 0 项</summary>
        public const string NoLinetypeLabel = "（不修改线型）";

        /// <summary>
        /// 把文字对齐数值转成报告里用的中文名；找不到返回 null。
        /// </summary>
        /// <param name="value">DXF 组码 72 的数值</param>
        /// <returns>中文名，如「居中 (Center)」</returns>
        public static string JustifyLabel(int value)
        {
            for (int i = 0; i < Justify.Count; i++)
            {
                if (Justify[i].Value.HasValue && Justify[i].Value.Value == value)
                {
                    return Justify[i].Label;
                }
            }

            return null;
        }

        /// <summary>
        /// 把线宽数值转成报告里用的显示文本（随层 / 随块 / x.xx mm）。
        /// </summary>
        /// <param name="value">DXF 组码 370 的数值</param>
        /// <returns>显示文本</returns>
        public static string LineWeightLabel(int value)
        {
            for (int i = 0; i < LineWeights.Count; i++)
            {
                if (LineWeights[i].Value.HasValue && LineWeights[i].Value.Value == value)
                {
                    return LineWeights[i].Label;
                }
            }

            // 报告里线宽直接打数值，这里保持同样的兜底
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
