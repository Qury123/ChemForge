using System;
using System.Globalization;
using System.Text;

namespace ChemForge.Models.Text.TextColor
{
    /// <summary>
    /// 把执行结果拼成给用户看的完成提示。
    ///
    /// 提示要能回答三个问题：改了多少、目标是什么、有没有被跳过的。
    /// 少了最后一项，用户会以为“明明选了 100 个却只改了 80 个”是丢了数据。
    ///
    /// 两个口径：<see cref="Build"/> 是命令行用的完整报告，
    /// <see cref="BuildBrief"/> 是结果窗口用的短版（只留结论）。
    /// </summary>
    public static class TextColorReport
    {
        /// <summary>完整报告：命令行输出用，含样例、核对信息与刷新提示。</summary>
        public static string Build(TextColorResult result, TextColorOptions options)
        {
            return BuildCore(result, options, true);
        }

        /// <summary>
        /// 短版报告：结果窗口用。
        ///
        /// 与 <see cref="Build"/> 同一口径，但去掉样例、核对信息与刷新提示 ——
        /// 那些是留在命令行里查问题用的，摆进窗口只会把“到底改了几个”淹掉。
        /// </summary>
        public static string BuildBrief(TextColorResult result, TextColorOptions options)
        {
            return BuildCore(result, options, false);
        }

        /// <summary>
        /// 两种口径的共同部分：结论 → 目标 → 跳过统计 → 撤销提示，顺序固定；
        /// 完整报告只是在中间多插样例、逐条消息与核对信息。
        ///
        /// 合在一起写而不是各写一遍：两边的开头三行必须逐字一致（测试也是这么断言的），
        /// 各写一遍就等于给“以后改了一边忘了另一边”留了个口子。
        /// </summary>
        private static string BuildCore(TextColorResult result, TextColorOptions options, bool detailed)
        {
            if (result == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();

            AppendHeadline(builder, result);
            AppendTarget(builder, result, options);
            AppendSkipNotes(builder, result);

            if (detailed)
            {
                AppendSample(builder, "[样例·清理前] ", result.BeforeSample);
                AppendSample(builder, "[样例·清理后] ", result.AfterSample);
                AppendSample(builder, "[核对] ", result.ColorProbe);

                if (result.Messages != null)
                {
                    foreach (string message in result.Messages)
                    {
                        if (!string.IsNullOrEmpty(message))
                        {
                            AppendLine(builder, message);
                        }
                    }
                }
            }

            AppendUndoNote(builder);

            return builder.ToString();
        }

        /// <summary>首行结论：“完成！共修改 N 个对象……”。</summary>
        private static void AppendHeadline(StringBuilder builder, TextColorResult result)
        {
            builder.Append("完成！共修改 ")
                .Append(Number(result.ChangedCount))
                .Append(" 个对象（改颜色 ")
                .Append(Number(result.ColorChangedCount))
                .Append(" 处、清除内部颜色码 ")
                .Append(Number(result.CodeStrippedCount))
                .Append(" 处）");
        }

        /// <summary>目标颜色与作用范围。</summary>
        private static void AppendTarget(StringBuilder builder, TextColorResult result, TextColorOptions options)
        {
            if (options == null)
            {
                return;
            }

            AppendLine(builder, "目标颜色：" + options.Describe());

            string range = "范围：所选对象";
            if (options.IncludeBlockDefinitions)
            {
                range += " + 块定义内部（扫描 "
                    + Number(result.ScannedBlockCount)
                    + " 个块定义，其中 "
                    + Number(result.ProcessedBlockCount)
                    + " 个被改动）";
            }

            AppendLine(builder, range);
        }

        /// <summary>被跳过的三类统计，有才写。</summary>
        private static void AppendSkipNotes(StringBuilder builder, TextColorResult result)
        {
            if (result.FieldSkippedCount > 0)
            {
                AppendLine(
                    builder,
                    "另有 " + Number(result.FieldSkippedCount)
                    + " 个含字段的文字已跳过内容清理（仅改了对象颜色）。");
            }

            if (result.LockedSkippedCount > 0)
            {
                AppendLine(
                    builder,
                    "另有 " + Number(result.LockedSkippedCount)
                    + " 个文字位于锁定图层，已整体跳过（锁定图层上无法修改对象，连对象颜色也写不进去）。");
            }

            if (result.FailedCount > 0)
            {
                AppendLine(
                    builder,
                    "有 " + Number(result.FailedCount)
                    + " 个对象的内容读取失败，已只改对象颜色。");
            }
        }

        private static void AppendUndoNote(StringBuilder builder)
        {
            AppendLine(builder, "如需撤销：Ctrl+Z 一次即可全部还原。");
        }

        private static void AppendSample(StringBuilder builder, string label, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            AppendLine(builder, label + value);
        }

        /// <summary>换行后追加一行 —— 报告里每一条都是独立一行，写成方法省得漏。</summary>
        private static void AppendLine(StringBuilder builder, string text)
        {
            builder.Append(Environment.NewLine).Append(text);
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
