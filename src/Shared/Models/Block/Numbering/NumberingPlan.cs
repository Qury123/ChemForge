using System.Collections.Generic;
using ChemForge.Core.Block.Numbering;

namespace ChemForge.Models.Block.Numbering
{
    /// <summary>
    /// 一次编号的完整计划：排序结果 + 每个块应写入的值。
    /// 预览和写入用的是同一个对象，保证“所见即所写”。
    /// </summary>
    public sealed class NumberingPlan
    {
        /// <summary>全部候选块（含被跳过的），已按最终顺序填好 Row / Ordinal / NewValue。</summary>
        public List<BlockItem> Items = new List<BlockItem>();

        /// <summary>按编号顺序排列的可写入块，便于报告与高亮。</summary>
        public List<BlockItem> Writable = new List<BlockItem>();

        public int RowCount;
        public int WritableCount;
        public int SkippedCount;

        /// <summary>本次实际使用的行容差；ToleranceAuto 为 true 时表示是自动估算值。</summary>
        public double ToleranceUsed;
        public bool ToleranceAuto;

        /// <summary>候选块的块名（多种则用“/”连接）。</summary>
        public string BlockName = string.Empty;

        /// <summary>给用户的补充说明（例如容差无法自动估算）。</summary>
        public List<string> Notes = new List<string>();

        public NumberOptions Options = new NumberOptions();

        public string DescribeTolerance()
        {
            string text = ToleranceUsed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return ToleranceAuto ? (text + " (自动)") : text;
        }
    }
}
