using System.Globalization;

namespace ChemForge.Models.Text.TextColor
{
    /// <summary>
    /// AutoCAD 索引色（ACI）的名称表与颜色列表换算。
    ///
    /// 名称按 AutoCAD 原生「选择颜色」对话框的习惯组织：1-9 是九种基本色，
    /// 250-255 是灰度尾段，10-249 每十个一组对应一个色相（24 组 × 10 个），
    /// 组内再带一个序号 —— 因为相邻号段在屏幕上几乎分不出来，
    /// 光列号数用户没法挑，必须给出可读的色相 + 序号。
    /// </summary>
    public static class AciColorNames
    {
        /// <summary>索引色号的合法上界。</summary>
        public const int MaxIndexedColor = 255;

        /// <summary>索引色号的合法下界。</summary>
        public const int MinIndexedColor = 1;

        /// <summary>随层的索引色号。</summary>
        public const int ByLayerValue = 256;

        /// <summary>随块的索引色号。</summary>
        public const int ByBlockValue = 0;

        /// <summary>颜色列表的总项数：随层、随块，再加 1..255。</summary>
        public const int ListItemCount = 257;

        /// <summary>
        /// 24 个色相名。10-249 每十个一组，按顺序对应到这里的第 0-23 项。
        /// </summary>
        private static readonly string[] Hues =
        {
            "红", "橙", "橙黄", "黄", "黄绿", "绿", "青绿", "青",
            "蓝青", "蓝", "蓝紫", "紫", "紫红", "洋红", "粉红", "浅粉",
            "肉色", "灰蓝", "灰", "灰", "灰", "灰", "深灰", "深灰"
        };

        /// <summary>
        /// 索引色号转中文颜色名。超出已知范围的号返回空串。
        /// </summary>
        /// <param name="aci">索引色号，含 0（随块）与 256（随层）</param>
        public static string Name(int aci)
        {
            switch (aci)
            {
                case ByLayerValue: return "随层 ByLayer";
                case ByBlockValue: return "随块 ByBlock";
                case 1: return "红";
                case 2: return "黄";
                case 3: return "绿";
                case 4: return "青";
                case 5: return "蓝";
                case 6: return "洋红";
                case 7: return "白/黑";
                case 8: return "深灰";
                case 9: return "浅灰";
                case 250: return "黑";
                case 251: return "暗灰";
                case 252: return "中灰";
                case 253: return "浅灰";
                case 254: return "灰白";
                case 255: return "白";
            }

            if (aci < 10 || aci > 249)
            {
                return string.Empty;
            }

            int group = (aci / 10) - 1;      // 落在哪个色相组（0-23）
            int offsetInGroup = aci % 10;    // 组内序号（0-9）
            return Hues[group] + "-" + (offsetInGroup + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 颜色列表项的显示文本，形如 <c>  1  红</c>。
        /// 号数左补空格到三位，让整列数字对齐。
        /// </summary>
        public static string ListItem(int aci)
        {
            string number = aci.ToString(CultureInfo.InvariantCulture);
            if (number.Length < 3)
            {
                number = number.PadLeft(3, ' ');
            }

            return number + "  " + Name(aci);
        }

        /// <summary>
        /// 颜色列表的序号转索引色号。
        /// 列表顺序是 256（随层）、0（随块），其后 1..255。
        /// </summary>
        public static int ListIndexToAci(int index)
        {
            if (index == 0)
            {
                return ByLayerValue;
            }

            if (index == 1)
            {
                return ByBlockValue;
            }

            return index - 1;
        }

        /// <summary>
        /// 索引色号转颜色列表的序号（<see cref="ListIndexToAci"/> 的逆运算）。
        /// </summary>
        public static int AciToListIndex(int aci)
        {
            if (aci == ByLayerValue)
            {
                return 0;
            }

            if (aci == ByBlockValue)
            {
                return 1;
            }

            return aci + 1;
        }
    }
}
