using System;
using System.Globalization;

namespace ChemForge.Core.Text.TextColor
{
    /// <summary>
    /// 文字内容的小工具：判断内容里有没有需要解析的东西、
    /// 以及把内容截成适合放在命令行里的样例。
    /// </summary>
    public static class TextColorContent
    {
        /// <summary>多行文字的字段标记。含字段的文字内容由字段求值生成，不能被直接改写。</summary>
        private const string FieldMarker = "%<";

        /// <summary>样例在命令行里保留的最大字符数。</summary>
        private const int SampleLength = 60;

        /// <summary>
        /// 内容里是否出现反斜杠。
        ///
        /// 完全没有反斜杠的文字不可能带格式码，也就没必要去读、写它的内容 ——
        /// 对多行文字来说，读一次内容是一次跨进程的属性访问，
        /// 大批量处理时这类访问正是耗时的主要来源。
        /// </summary>
        public static bool HasBackslash(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf('\\') >= 0;
        }

        /// <summary>
        /// 内容里是否含字段（FIELD）。
        /// 字段的显示文本是算出来的，清掉颜色码会连字段定义一起破坏，
        /// 所以这类文字只改对象颜色、不动内容。
        /// </summary>
        public static bool ContainsField(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(FieldMarker, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 取内容的前若干字符做样例，超长时补省略号。
        /// </summary>
        public static string Head(string text, int limit = SampleLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= limit)
            {
                return text;
            }

            return text.Substring(0, limit) + "...";
        }

        /// <summary>
        /// 把内容里的换行、制表等不可见字符换成可见写法，
        /// 免得命令行样例被撑成好几行。
        /// </summary>
        public static string ToSingleLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string flattened = text
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n")
                .Replace("\t", " ");

            return Head(flattened);
        }

        /// <summary>
        /// 内容是否为空或只有空白。
        /// </summary>
        public static bool IsBlank(string text)
        {
            return string.IsNullOrEmpty(text) || text.Trim().Length == 0;
        }

        /// <summary>
        /// 判断一行界面输入是否是纯数字。
        /// 编辑框里可以敲进任何东西，只有纯数字才值得当成色号采纳 ——
        /// 否则"12a"会被解析成 12，用户看到的和实际生效的就对不上了。
        /// </summary>
        public static bool IsDigitsOnly(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 把界面输入按 0-255 限幅解析；非纯数字返回 <paramref name="fallback"/>。
        /// </summary>
        public static int ParseByte(string text, int fallback)
        {
            if (!IsDigitsOnly(text))
            {
                return fallback;
            }

            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            if (value < 0)
            {
                return 0;
            }

            return value > 255 ? 255 : value;
        }
    }
}
