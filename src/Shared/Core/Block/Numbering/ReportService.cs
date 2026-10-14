using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.AutoCAD.EditorInput;
using ChemForge.Core.Block.Numbering;
using ChemForge.Models.Block.Numbering;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>结果报告：命令行摘要 + 跳过明细。</summary>
    public static class ReportService
    {
        /// <summary>
        /// 命令行一行摘要最多列几个跳过明细；超过就折叠成"其余 N 个略"。
        /// 用户需要看全的时候，可视化页面的表格里本来就有完整的"明细"列。
        /// </summary>
        private const int MaxInlineSkips = 8;

        /// <summary>
        /// 打印结果报告。
        ///
        /// <paramref name="writeLine"/> 由命令层注入（通常是"不触发 F12 动态输入浮动框"
        /// 的那个写入器），本服务不直接依赖 `Editor.WriteMessage`，这样输出渠道可以替换。
        /// </summary>
        public static void Print(Editor editor, NumberingPlan plan, WriteResult write, Action<string> writeLine)
        {
            if (editor == null || plan == null)
            {
                return;
            }

            if (writeLine == null)
            {
                writeLine = delegate(string s) { editor.WriteMessage(s); };
            }

            writeLine(BuildSummaryLine(plan, write));
        }

        /// <summary>
        /// 把"这次到底编了多少、跳过多少"压成一行。
        /// 返回的字符串以 <c>"\n"</c> 开头，保持 AutoCAD 命令行提示的换行惯例。
        /// </summary>
        private static string BuildSummaryLine(NumberingPlan plan, WriteResult write)
        {
            NumberOptions options = plan.Options;
            StringBuilder sb = new StringBuilder();

            sb.Append("\n编号完成: ");
            sb.Append(plan.BlockName);
            sb.Append(" / ");
            sb.Append(options.Tag);

            if (write != null)
            {
                sb.Append("  成功 ");
                sb.Append(write.Written.ToString(CultureInfo.InvariantCulture));
                if (write.Failed > 0)
                {
                    sb.Append("  失败 ");
                    sb.Append(write.Failed.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                sb.Append("  候选 ");
                sb.Append(plan.Items.Count.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append("  跳过 ");
            sb.Append(plan.SkippedCount.ToString(CultureInfo.InvariantCulture));

            int rowCount = plan.RowCount;
            sb.Append("  行(列)数 ");
            sb.Append(rowCount.ToString(CultureInfo.InvariantCulture));

            string breakdown = BuildSkipBreakdown(plan);
            if (breakdown.Length > 0)
            {
                sb.Append("  [").Append(breakdown).Append("]");
            }

            for (int i = 0; i < plan.Notes.Count; i++)
            {
                sb.Append("  提示: ").Append(plan.Notes[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 跳过原因的一行摘要，例如 <c>"跳过 3 (重叠 2、无属性 1)"</c>。
        /// 超出 <see cref="MaxInlineSkips"/> 个时只列前几个，后面接"其余 N 个略"。
        /// </summary>
        private static string BuildSkipBreakdown(NumberingPlan plan)
        {
            Dictionary<SkipReason, int> counts = new Dictionary<SkipReason, int>();

            for (int i = 0; i < plan.Items.Count; i++)
            {
                SkipReason reason = plan.Items[i].Skip;
                if (reason == SkipReason.None)
                {
                    continue;
                }
                int n;
                counts.TryGetValue(reason, out n);
                counts[reason] = n + 1;
            }

            if (counts.Count == 0)
            {
                return string.Empty;
            }

            List<SkipReason> keys = new List<SkipReason>(counts.Keys);
            keys.Sort(delegate(SkipReason a, SkipReason b) { return ((int)a).CompareTo((int)b); });

            StringBuilder sb = new StringBuilder();
            int limit = Math.Min(keys.Count, MaxInlineSkips);
            for (int i = 0; i < limit; i++)
            {
                if (i > 0)
                {
                    sb.Append("、");
                }
                sb.Append(SkipReasonText.Describe(keys[i]));
                sb.Append(" ");
                sb.Append(counts[keys[i]].ToString(CultureInfo.InvariantCulture));
            }
            if (keys.Count > limit)
            {
                sb.Append("、其余 ");
                sb.Append((keys.Count - limit).ToString(CultureInfo.InvariantCulture));
                sb.Append(" 项略");
            }

            return sb.ToString();
        }
    }
}
