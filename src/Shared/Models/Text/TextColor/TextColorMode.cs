namespace ChemForge.Models.Text.TextColor
{
    /// <summary>
    /// 文字颜色的目标模式。
    /// </summary>
    public enum TextColorMode
    {
        /// <summary>
        /// 随层：文字颜色跟随所在图层（索引色号 256）。
        /// </summary>
        ByLayer,

        /// <summary>
        /// 随块：文字颜色跟随所属块参照（索引色号 0）。
        /// </summary>
        ByBlock,

        /// <summary>
        /// 索引色：使用 AutoCAD 索引色号 1-255。
        /// </summary>
        Aci,

        /// <summary>
        /// 真彩色：使用 24 位 RGB。
        /// </summary>
        TrueColor
    }
}
