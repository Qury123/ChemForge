using System;
using System.Text;

namespace ChemForge.Core.Text.TextColor
{
    /// <summary>
    /// 清除多行文字内容里内嵌的颜色覆盖码。
    ///
    /// 文字一旦在自己的内容里带了颜色码（形如 <c>\C1;</c> 的索引色覆盖、
    /// <c>\c16711680;</c> 的真彩色覆盖），显示时就以它为准 ——
    /// 这时改对象颜色在屏幕上看不到任何变化，这就是"改了颜色没反应"的根因。
    /// 本类只摘颜色码，其它格式码一律原样保留：
    /// <c>\P</c> 换行、<c>\H</c> 字高、<c>\W</c> 宽度因子、<c>\f</c> 字体、
    /// <c>\A</c> 对齐、<c>\S</c> 堆叠、<c>\Q</c> 倾斜角、<c>\L</c> 下划线、
    /// <c>\O</c> 上划线、<c>\K</c> 大小写、<c>\X</c> 换行符、<c>\T</c> 字距，
    /// 以及 <c>\\</c> 表示的字面反斜杠。
    /// </summary>
    public static class TextColorCodeStripper
    {
        private const string EmptyBracePair = "{}";

        /// <summary>
        /// 内容里是否含颜色覆盖码。
        ///
        /// 绝大多数文字并不含颜色码，先用这个方法判一次，
        /// 不含就整串原样返回，省掉一次完整解析。
        /// </summary>
        public static bool HasColorCode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return text.IndexOf("\\C", StringComparison.Ordinal) >= 0
                || text.IndexOf("\\c", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 去掉颜色覆盖码，并清掉因此空置的 <c>{}</c> 对。
        /// 不含颜色码时原样返回。
        /// </summary>
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string cleaned = HasColorCode(text) ? StripColorRuns(text) : text;
            return RemoveEmptyBracePairs(cleaned);
        }

        /// <summary>
        /// 按反斜杠分段拷贝，逐段丢弃颜色码。
        ///
        /// 之所以不是"逐字符判断 + 拼接"：那种写法在长文本上要反复重建整串，
        /// 一条两千字的多行文字会退化成平方级开销，大批量处理时卡顿非常明显。
        /// 这里只在反斜杠处切分，普通字符整段搬，复杂度是线性。
        /// </summary>
        private static string StripColorRuns(string text)
        {
            int length = text.Length;
            StringBuilder output = new StringBuilder(length);
            int cursor = 0;

            while (cursor < length)
            {
                int backslash = text.IndexOf('\\', cursor);
                if (backslash < 0)
                {
                    // 后面再没有反斜杠，剩下的一整段都是普通文字
                    output.Append(text, cursor, length - cursor);
                    break;
                }

                if (backslash > cursor)
                {
                    output.Append(text, cursor, backslash - cursor);
                }

                cursor = backslash;
                char next = cursor + 1 < length ? text[cursor + 1] : '\0';

                if (next == '\\')
                {
                    // 字面反斜杠：两个一起原样保留
                    output.Append("\\\\");
                    cursor += 2;
                }
                else if (next == 'C' || next == 'c')
                {
                    // 从反斜杠后第二个字符开始找分号，一直删到分号（含）
                    int semicolon = text.IndexOf(';', cursor + 2);
                    if (semicolon < 0)
                    {
                        // 没有分号说明这个颜色码没写完，后面全丢掉
                        cursor = length;
                    }
                    else
                    {
                        cursor = semicolon + 1;
                    }
                }
                else
                {
                    // 其它格式码：保留反斜杠，后续字符交给下一轮
                    output.Append('\\');
                    cursor++;
                }
            }

            return output.ToString();
        }

        /// <summary>
        /// 反复删除相邻的 <c>{}</c> 直到不再出现。
        ///
        /// 只删"空对"而不是删掉所有大括号：大括号里还包着别的格式码时
        /// 它仍有作用，删了会改变文字的排版含义。
        /// 之所以要循环，是因为删掉中间一对之后，两侧的大括号可能又凑成一对。
        /// </summary>
        private static string RemoveEmptyBracePairs(string text)
        {
            if (text.IndexOf(EmptyBracePair, StringComparison.Ordinal) < 0)
            {
                return text;
            }

            string current = text;
            while (true)
            {
                string next = current.Replace(EmptyBracePair, string.Empty);
                if (next.Length == current.Length)
                {
                    return next;
                }

                current = next;
            }
        }
    }
}
