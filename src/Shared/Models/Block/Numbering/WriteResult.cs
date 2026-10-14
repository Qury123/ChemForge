using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ChemForge.Models.Block.Numbering
{
    /// <summary>写入结果。</summary>
    public sealed class WriteResult
    {
        public int Written;
        public int Failed;

        /// <summary>真正被修改的块参照 ObjectId，用于写入后把它们保持为选中状态。</summary>
        public readonly ObjectIdCollection WrittenBlockIds = new ObjectIdCollection();

        /// <summary>逐条异常/说明信息。</summary>
        public readonly List<string> Messages = new List<string>();

        /// <summary>按跳过原因归类统计。</summary>
        public readonly Dictionary<SkipReason, int> SkipCounts = new Dictionary<SkipReason, int>();

        public void CountSkip(SkipReason reason)
        {
            if (reason == SkipReason.None)
            {
                return;
            }
            int n;
            SkipCounts.TryGetValue(reason, out n);
            SkipCounts[reason] = n + 1;
        }
    }
}
