using System;

namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 批量修改块属性的「设置集合」。
    ///
    /// 参数校验通过后构建的一张「全局参数表」，字段一一对应：
    /// <see cref="ModifyContent"/>/<see cref="NewContent"/> → 内容区；
    /// 样式 / 对齐 / 反向 / 倒置 / 字高 / 宽度因子 / 旋转 / 倾斜 → 文字选项一组；
    /// 图层 / 颜色 / 线型 / 线宽 → 特性一组。
    ///
    /// 语义约定（这几条是硬约束，改动前务必确认）：
    /// 1) 三个「修改开关」为 false 的区**整区不动**，与其中的字段是否填写无关；
    /// 2) 开关打开时，某个字段为 null（下拉选「不修改」/ 编辑框留空）表示**保持原值**；
    /// 3) <see cref="NewContent"/> 留空 = **清空文字**（不是「不修改」，这是内容区特有的语义）。
    /// </summary>
    public sealed class BatchAttrChangeOptions
    {
        // ---------- ② 文字内容 ----------

        /// <summary>是否修改文字内容（原 MODVAL 开关）</summary>
        public bool ModifyContent { get; set; }

        /// <summary>新的文字内容；空字符串表示清空（原 NEWVAL 编辑框）</summary>
        public string NewContent { get; set; } = string.Empty;

        // ---------- ③ 文字选项 ----------

        /// <summary>是否修改文字选项（原 MODFMT 开关）</summary>
        public bool ModifyFormat { get; set; }

        /// <summary>目标文字样式名；null = 不修改（原 STYLELIST 下拉）</summary>
        public string TextStyleName { get; set; }

        /// <summary>
        /// 目标水平对正（DXF 组码 72 / 原 JUSTLIST 下拉）；null = 不修改。
        /// 注意：值为 0（左对正，默认值）时**不写入** ——
        /// 组码 72 的 0 就是「左对齐/默认」，写它等于强制拉回左对齐。
        /// </summary>
        public int? Justify { get; set; }

        /// <summary>反向（文字镜像，DXF 组码 71 的 bit 2）；null = 不修改（原 BKLIST 下拉）</summary>
        public bool? Backwards { get; set; }

        /// <summary>倒置（文字上下翻转，DXF 组码 71 的 bit 4）；null = 不修改（原 USLIST 下拉）</summary>
        public bool? UpsideDown { get; set; }

        /// <summary>字高；null = 不修改（原 NEWH 编辑框）</summary>
        public double? Height { get; set; }

        /// <summary>宽度因子；null = 不修改（原 NEWWF 编辑框）</summary>
        public double? WidthFactor { get; set; }

        /// <summary>旋转角度（度）；null = 不修改（原 NEWROT 编辑框）</summary>
        public double? Rotation { get; set; }

        /// <summary>倾斜角度（度）；null = 不修改（原 NEWOBL 编辑框）</summary>
        public double? Oblique { get; set; }

        // ---------- ④ 特性 ----------

        /// <summary>是否修改特性（原 MODPROP 开关）</summary>
        public bool ModifyProperties { get; set; }

        /// <summary>目标图层名；null = 不修改（原 LAYERLIST 下拉）</summary>
        public string LayerName { get; set; }

        /// <summary>
        /// 目标颜色号（DXF 组码 62）；null = 不修改。
        /// 0 = 随块（ByBlock）、256 = 随层（ByLayer）、1~255 = ACI 颜色号。
        /// </summary>
        public int? ColorIndex { get; set; }

        /// <summary>目标线型名；null = 不修改（原 LTYPELIST 下拉）</summary>
        public string LinetypeName { get; set; }

        /// <summary>
        /// 目标线宽（DXF 组码 370）；null = 不修改。
        /// -1 = 随层（ByLayer）、-2 = 随块（ByBlock）、其余为毫米值 × 100。
        /// </summary>
        public int? LineWeight { get; set; }

        // ---------- 范围 ----------

        /// <summary>修改范围（原对话框的 OPT_CUR / OPT_ALL 单选）</summary>
        public BatchAttrChangeScope Scope { get; set; } = BatchAttrChangeScope.SelectedOnly;

        /// <summary>
        /// 文字选项区是否至少填了一项 ——
        /// 样式 / 对齐 / 反向 / 倒置 / 字高 / 宽度因子 / 旋转 / 倾斜 任一非空即为 true。
        /// </summary>
        public bool HasAnyFormatField
        {
            get
            {
                return !string.IsNullOrEmpty(TextStyleName)
                    || Justify.HasValue
                    || Backwards.HasValue
                    || UpsideDown.HasValue
                    || Height.HasValue
                    || WidthFactor.HasValue
                    || Rotation.HasValue
                    || Oblique.HasValue;
            }
        }

        /// <summary>
        /// 特性区是否至少填了一项 ——
        /// 图层 / 颜色 / 线型 / 线宽 任一非空即为 true。
        /// </summary>
        public bool HasAnyPropertyField
        {
            get
            {
                return !string.IsNullOrEmpty(LayerName)
                    || ColorIndex.HasValue
                    || !string.IsNullOrEmpty(LinetypeName)
                    || LineWeight.HasValue;
            }
        }

        /// <summary>
        /// 「降级」逻辑：开关打开了但一项都没填，等于没开。
        ///
        /// 两段判定：
        /// <c>ModifyFormat</c> 为真但文字选项一项都没填 → <c>ModifyFormat</c> 置 false；
        /// <c>ModifyProperties</c> 为真但特性一项都没填 → <c>ModifyProperties</c> 置 false。
        /// 返回 false 表示三个区全部为空 —— 调用方应提示
        /// 「未启用任何修改项，未做任何修改。」并结束。
        /// </summary>
        /// <returns>是否还有至少一个区需要修改</returns>
        public bool Normalize()
        {
            if (ModifyFormat && !HasAnyFormatField)
            {
                ModifyFormat = false;
            }

            if (ModifyProperties && !HasAnyPropertyField)
            {
                ModifyProperties = false;
            }

            return ModifyContent || ModifyFormat || ModifyProperties;
        }

        /// <summary>
        /// 是否需要走 COM（ActiveX）补写「反向 / 倒置」。
        ///
        /// AutoCAD 托管 API（AutoCAD .NET API）的 <c>AttributeReference</c> **没有**
        /// IsMirrored / IsUpsideDown 属性（2020 与 2025 两代 SDK 均实测缺失，
        /// DBText 同样没有），DXF 组码 71 只能通过 ActiveX 的
        /// <c>AcadAttribute.Backward / UpsideDown</c> 写入 —— 见 AttributeWriter。
        /// </summary>
        public bool NeedsComMirrorPass
        {
            get { return ModifyFormat && (Backwards.HasValue || UpsideDown.HasValue); }
        }
    }
}
