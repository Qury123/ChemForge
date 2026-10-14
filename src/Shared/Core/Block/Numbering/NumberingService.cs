using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 编号生成：起始值 + 步长 + 补零 + 前后缀。
    /// 与 AutoCAD 无关，可独立单元测试。
    /// </summary>
    public static class NumberingService
    {
        /// <summary>第 ordinal 个（0 起）应该得到的数值。</summary>
        public static int ValueAt(int ordinal, NumberOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            return options.Start + ordinal * options.Step;
        }

        /// <summary>把一个数值按补零/前后缀规则格式化成字符串。</summary>
        public static string Format(int value, NumberOptions options)
        {
            if (options == null)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            string body;
            if (options.Pad > 0)
            {
                body = value.ToString("D" + options.Pad.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
            else
            {
                body = value.ToString(CultureInfo.InvariantCulture);
            }

            return (options.Prefix ?? string.Empty) + body + (options.Suffix ?? string.Empty);
        }

        /// <summary>第 ordinal 个（0 起）的最终字符串。</summary>
        public static string FormatOrdinal(int ordinal, NumberOptions options)
        {
            return Format(ValueAt(ordinal, options), options);
        }

        /// <summary>形如 “001 ~ 128” 的范围描述；count 为 0 时返回 “-”。</summary>
        public static string DescribeRange(int count, NumberOptions options)
        {
            if (count <= 0)
            {
                return "-";
            }
            string first = FormatOrdinal(0, options);
            string last = FormatOrdinal(count - 1, options);
            if (count == 1)
            {
                return first;
            }
            return first + " ~ " + last;
        }

        /// <summary>形如 “001, 002, 003, ... 128” 的样例描述。</summary>
        public static string DescribeSample(int count, NumberOptions options, int maxItems)
        {
            if (count <= 0)
            {
                return "-";
            }
            if (maxItems <= 0)
            {
                maxItems = 4;
            }

            List<string> parts = new List<string>();
            int head = Math.Min(count, maxItems);
            for (int i = 0; i < head; i++)
            {
                parts.Add(FormatOrdinal(i, options));
            }
            if (count > head)
            {
                parts.Add("...");
                parts.Add(FormatOrdinal(count - 1, options));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
