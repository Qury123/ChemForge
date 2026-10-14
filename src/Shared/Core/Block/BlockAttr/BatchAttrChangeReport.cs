using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ChemForge.Models.Block.BlockAttr;

namespace ChemForge.Core.Block.BlockAttr
{
    /// <summary>
    /// 结果报告文本。
    ///
    /// 本类只负责拼文本，输出方式交给命令层决定（命令行 + 弹窗）。
    ///
    /// 数字一律两位小数，
    /// 且用 <see cref="CultureInfo.InvariantCulture"/>，避免中文系统下
    /// 小数点变成别的符号。
    /// </summary>
    public static class BatchAttrChangeReport
    {
        /// <summary>
        /// 拼出完整报告文本。
        /// </summary>
        /// <param name="options">本次实际生效的设置</param>
        /// <param name="tags">本次处理的属性标签</param>
        /// <param name="changedCount">更新的属性处数</param>
        /// <param name="lockedSkippedCount">位于锁定图层、被整体跳过的属性处数</param>
        /// <param name="failedCount">打开失败、被跳过的属性处数</param>
        /// <returns>多行报告文本</returns>
        public static string Build(
            BatchAttrChangeOptions options,
            IList<string> tags,
            int changedCount,
            int lockedSkippedCount = 0,
            int failedCount = 0)
        {
            if (options == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("处理完成，共更新 ").Append(changedCount).Append(" 处属性。");

            // 跳过项必须单独报出来：整批共用一个事务，这些对象是"被拦下来"的，
            // 不写清楚，用户会以为"选了 100 处只改了 80 处"是丢了数据。
            if (lockedSkippedCount > 0)
            {
                builder.Append("\n另有 ").Append(lockedSkippedCount)
                    .Append(" 处属性位于锁定图层，已整体跳过（锁定图层上无法修改对象）。");
            }

            if (failedCount > 0)
            {
                builder.Append("\n有 ").Append(failedCount).Append(" 处属性打开失败，已跳过。");
            }

            builder.Append("\n所选属性标签：");
            builder.Append(tags == null || tags.Count == 0 ? "（无）" : string.Join("、", ToArray(tags)));
            builder.Append("\n\n文字内容：").Append(BuildContentSection(options));
            builder.Append("\n\n文字选项：").Append(BuildFormatSection(options));
            builder.Append("\n\n特性：").Append(BuildPropertySection(options));
            return builder.ToString();
        }

        private static string BuildContentSection(BatchAttrChangeOptions options)
        {
            if (!options.ModifyContent)
            {
                return "未修改";
            }

            if (string.IsNullOrEmpty(options.NewContent))
            {
                return "已清空";
            }

            return "改为「" + options.NewContent + "」";
        }

        private static string BuildFormatSection(BatchAttrChangeOptions options)
        {
            List<string> parts = new List<string>();

            if (options.ModifyFormat)
            {
                if (!string.IsNullOrEmpty(options.TextStyleName))
                {
                    parts.Add("字体样式「" + options.TextStyleName + "」");
                }

                // 对齐值 0 就是「左对齐/默认」，等于没改，不进报告
                if (options.Justify.HasValue && options.Justify.Value != 0)
                {
                    parts.Add("文字对齐=" + BatchAttrChangeTables.JustifyLabel(options.Justify.Value));
                }

                if (options.Backwards.HasValue)
                {
                    parts.Add("反向=" + (options.Backwards.Value ? "是" : "否"));
                }

                if (options.UpsideDown.HasValue)
                {
                    parts.Add("倒置=" + (options.UpsideDown.Value ? "是" : "否"));
                }

                if (options.Height.HasValue)
                {
                    parts.Add("字高=" + FormatNumber(options.Height.Value));
                }

                if (options.WidthFactor.HasValue)
                {
                    parts.Add("宽度因子=" + FormatNumber(options.WidthFactor.Value));
                }

                if (options.Rotation.HasValue)
                {
                    parts.Add("旋转角=" + FormatNumber(options.Rotation.Value));
                }

                if (options.Oblique.HasValue)
                {
                    parts.Add("倾斜角=" + FormatNumber(options.Oblique.Value));
                }
            }

            return parts.Count == 0 ? "未修改" : string.Join("，", parts.ToArray());
        }

        private static string BuildPropertySection(BatchAttrChangeOptions options)
        {
            List<string> parts = new List<string>();

            if (options.ModifyProperties)
            {
                if (!string.IsNullOrEmpty(options.LayerName))
                {
                    parts.Add("图层「" + options.LayerName + "」");
                }

                if (options.ColorIndex.HasValue)
                {
                    parts.Add(DescribeColor(options.ColorIndex.Value));
                }
                else if (string.Equals(options.LayerName, "0", StringComparison.Ordinal))
                {
                    // 与写入层一致地说明这个「隐形」的自动补色，否则用户会以为颜色没被碰过
                    parts.Add("颜色=随层(ByLayer)（目标图层为 0，自动写入）");
                }

                if (!string.IsNullOrEmpty(options.LinetypeName))
                {
                    parts.Add("线型「" + options.LinetypeName + "」");
                }

                if (options.LineWeight.HasValue)
                {
                    parts.Add("线宽=" + BatchAttrChangeTables.LineWeightLabel(options.LineWeight.Value));
                }
            }

            return parts.Count == 0 ? "未修改" : string.Join("，", parts.ToArray());
        }

        /// <summary>颜色的中文描述：256 随层、0 随块、其余报 ACI 号</summary>
        private static string DescribeColor(int colorIndex)
        {
            if (colorIndex == 256)
            {
                return "颜色=随层(ByLayer)";
            }

            if (colorIndex == 0)
            {
                return "颜色=随块(ByBlock)";
            }

            return "颜色号=" + colorIndex.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 图层 "0" 的回读校验文本。
        ///
        /// 只在目标图层恰为 "0" 时才有意义：那个现象（改成 0 层画面还是 000）
        /// 必须能一眼区分「数据没写进去」和「写进去了但显示/颜色不对」。
        /// </summary>
        /// <param name="targetCount">目标改为图层 0 的属性处数</param>
        /// <param name="hitCount">回读确认已是 0 层的处数</param>
        /// <param name="sampleTag">示例标签</param>
        /// <returns>校验文本；无需校验时返回 null</returns>
        public static string BuildDiagnostics(int targetCount, int hitCount, string sampleTag)
        {
            if (targetCount <= 0)
            {
                return null;
            }

            string tag = string.IsNullOrEmpty(sampleTag) ? "—" : sampleTag;
            return "\n[回读校验] 目标改为图层 0 的属性共 " + targetCount.ToString(CultureInfo.InvariantCulture)
                + " 处，其中实际图层已是 0 的 " + hitCount.ToString(CultureInfo.InvariantCulture)
                + " 处（示例标签：" + tag + "）。"
                + "\n若 M = N → 已写入 0 层，画面仍是 000 则是颜色/显示问题；"
                + "\n若 M = 0 → 图层没写进 0 层，属数据写入问题。";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string[] ToArray(IList<string> list)
        {
            string[] array = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                array[i] = list[i];
            }

            return array;
        }
    }
}
