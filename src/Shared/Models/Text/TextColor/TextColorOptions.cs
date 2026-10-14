using System.Globalization;

namespace ChemForge.Models.Text.TextColor
{
    /// <summary>
    /// 一次批量改色的用户设置。
    /// </summary>
    public sealed class TextColorOptions
    {
        /// <summary>目标颜色模式。</summary>
        public TextColorMode Mode { get; set; } = TextColorMode.ByLayer;

        /// <summary>索引色号，仅 <see cref="TextColorMode.Aci"/> 下有效。</summary>
        public int AciIndex { get; set; } = AciColorNames.MinIndexedColor;

        /// <summary>真彩色红分量，仅 <see cref="TextColorMode.TrueColor"/> 下有效。</summary>
        public int Red { get; set; } = 255;

        /// <summary>真彩色绿分量，仅 <see cref="TextColorMode.TrueColor"/> 下有效。</summary>
        public int Green { get; set; }

        /// <summary>真彩色蓝分量，仅 <see cref="TextColorMode.TrueColor"/> 下有效。</summary>
        public int Blue { get; set; }

        /// <summary>
        /// 是否清除文字内容里内嵌的颜色覆盖码。
        /// 这一项才是「改了颜色没反应」的解药，默认打开。
        /// </summary>
        public bool StripColorCodes { get; set; } = true;

        /// <summary>是否同时处理单行文字 TEXT。</summary>
        public bool IncludeSingleLineText { get; set; } = true;

        /// <summary>是否同时处理多重引线 MULTILEADER。</summary>
        public bool IncludeMultiLeader { get; set; }

        /// <summary>
        /// 是否深入块定义内部处理。
        /// 打开后改的是块定义本身，所有同名块参照会一起变。
        /// </summary>
        public bool IncludeBlockDefinitions { get; set; }

        /// <summary>
        /// 把越界的分量收进合法范围。
        /// 界面上输入框可以随便敲，落到执行前必须归一，
        /// 否则真彩色打包出来的值会溢出成别的颜色。
        /// </summary>
        public void Normalize()
        {
            if (AciIndex < AciColorNames.MinIndexedColor)
            {
                AciIndex = AciColorNames.MinIndexedColor;
            }
            else if (AciIndex > AciColorNames.MaxIndexedColor)
            {
                AciIndex = AciColorNames.MaxIndexedColor;
            }

            Red = ClampByte(Red);
            Green = ClampByte(Green);
            Blue = ClampByte(Blue);
        }

        /// <summary>
        /// 索引色号是否落在可用的 1-255 区间内。
        /// </summary>
        public bool HasValidAciIndex()
        {
            return AciIndex >= AciColorNames.MinIndexedColor
                && AciIndex <= AciColorNames.MaxIndexedColor;
        }

        /// <summary>
        /// 目标颜色的中文描述，用于界面提示与完成提示。
        /// </summary>
        public string Describe()
        {
            switch (Mode)
            {
                case TextColorMode.Aci:
                    return "索引色 " + AciIndex.ToString(CultureInfo.InvariantCulture)
                        + "（" + AciColorNames.Name(AciIndex) + "）";
                case TextColorMode.TrueColor:
                    return "真彩色 RGB(" + Red.ToString(CultureInfo.InvariantCulture)
                        + "," + Green.ToString(CultureInfo.InvariantCulture)
                        + "," + Blue.ToString(CultureInfo.InvariantCulture) + ")";
                case TextColorMode.ByLayer:
                    return "随层 ByLayer";
                default:
                    return "随块 ByBlock";
            }
        }

        private static int ClampByte(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            return value > 255 ? 255 : value;
        }
    }
}
