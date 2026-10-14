using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ChemForge.Core.Block.Numbering;

namespace ChemForge.Models.Block.Numbering
{
    /// <summary>
    /// 预览表格里的一行。
    ///
    /// 候选块多的时候逐块平铺会撑出成百上千行，看不出重点。所以默认按"行(列)号"聚合：
    /// 一个 Group 代表图纸上的一整行(列)，表格里只占一行，摘要写"第 3 行 · 12 个 · 007~018"；
    /// 鼠标悬停时展开该行的完整清单并标出被跳过的块。
    /// 关掉聚合（<see cref="NumberOptions.PreviewGroupByRow"/> = false）时，每个 Group 只装一个块，
    /// 等价于原来的逐块平铺。
    ///
    /// 这纯粹是显示层的东西：不参与排序、编号、写入和命令行报告。
    /// </summary>
    public sealed class PreviewGroup
    {
        /// <summary>行(列)号，取 <see cref="BlockItem.Row"/> 的 1 基值；-1 表示未知。</summary>
        public int RowNumber = -1;

        /// <summary>该行(列)里的全部候选块，保持计划里的最终顺序。</summary>
        public readonly List<BlockItem> Items = new List<BlockItem>();

        /// <summary>其中的可编号块（保持顺序）。</summary>
        public readonly List<BlockItem> Writable = new List<BlockItem>();

        public int SkippedCount
        {
            get { return Items.Count - Writable.Count; }
        }

        public bool CanWrite
        {
            get { return Writable.Count > 0; }
        }

        /// <summary>该行(列)可编号块的序号范围描述：单块 "007"，多块 "007~018"，没有则 "-"。</summary>
        public string DescribeOrdinalRange()
        {
            if (Writable.Count == 0)
            {
                return "-";
            }

            string first = Writable[0].Ordinal.ToString("D3", CultureInfo.InvariantCulture);
            if (Writable.Count == 1)
            {
                return first;
            }

            // 序号是按排序顺序连续分配的，所以最后一块就是最大值，不必再遍历比较
            string last = Writable[Writable.Count - 1].Ordinal.ToString("D3", CultureInfo.InvariantCulture);
            return first + "~" + last;
        }

        /// <summary>表格"状态"列的文字。</summary>
        public string DescribeState()
        {
            int n = Items.Count;
            if (Writable.Count == n)
            {
                return string.Format(CultureInfo.InvariantCulture, "可编号 {0} 个", n);
            }
            if (Writable.Count == 0)
            {
                // 整行都跳过时列出原因归类，比笼统的"跳过 n 个"有用
                return string.Format(CultureInfo.InvariantCulture, "全部跳过：{0}", DescribeSkipReasons());
            }
            return string.Format(CultureInfo.InvariantCulture,
                "可编号 {0} 个，跳过 {1} 个（{2}）", Writable.Count, SkippedCount, DescribeSkipReasons());
        }

        /// <summary>表格"明细"列的单行摘要：本行(列)的块清单，压缩成一行。</summary>
        public string DescribeInline(int maxBlocks)
        {
            StringBuilder sb = new StringBuilder();

            int limit = maxBlocks <= 0 ? Items.Count : (Items.Count < maxBlocks ? Items.Count : maxBlocks);
            for (int i = 0; i < limit; i++)
            {
                if (i > 0)
                {
                    sb.Append("　");
                }

                BlockItem item = Items[i];
                if (item.CanWrite)
                {
                    sb.Append(item.Ordinal.ToString("D3", CultureInfo.InvariantCulture))
                      .Append(' ')
                      .Append(item.OldValue)
                      .Append("→")
                      .Append(item.NewValue);
                }
                else
                {
                    // 被跳过的块没有序号和新值，只标出旧值与跳过原因
                    sb.Append('✗').Append(' ').Append(item.OldValue)
                      .Append('（').Append(SkipReasonText.Describe(item.Skip)).Append('）');
                }
            }

            if (Items.Count > limit)
            {
                sb.Append("　…其余 ").Append(Items.Count - limit).Append(" 个略");
            }

            return sb.ToString();
        }

        /// <summary>悬停提示：本行(列)的完整明细。</summary>
        public string DescribeTooltip(int maxBlocks)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("第 ").Append(RowNumber).Append(" 行(列)　共 ").Append(Items.Count).Append(" 个块");

            if (SkippedCount > 0)
            {
                sb.Append("　其中跳过 ").Append(SkippedCount).Append(" 个");
            }
            sb.AppendLine();
            sb.AppendLine("--------------------------------");

            int limit = maxBlocks <= 0 ? Items.Count : (Items.Count < maxBlocks ? Items.Count : maxBlocks);
            for (int i = 0; i < limit; i++)
            {
                BlockItem item = Items[i];
                sb.Append(item.CanWrite ? "  " : "✗ ");
                if (item.CanWrite)
                {
                    sb.Append(item.Ordinal.ToString("D3", CultureInfo.InvariantCulture)).Append("  ");
                }
                else
                {
                    sb.Append("     ");
                }
                sb.Append(item.OldValue);
                if (item.CanWrite)
                {
                    sb.Append(" → ").Append(item.NewValue);
                }
                sb.Append("　[").Append(item.LayerName).Append('/').Append(item.Handle).Append(']');
                if (!item.CanWrite)
                {
                    sb.Append("　").Append(SkipReasonText.Describe(item.Skip));
                }
                if (i < limit - 1)
                {
                    sb.AppendLine();
                }
            }

            if (Items.Count > limit)
            {
                sb.AppendLine();
                sb.Append("… 其余 ").Append(Items.Count - limit).Append(" 个略（可关掉“按行聚合”逐块查看）");
            }

            return sb.ToString();
        }

        private string DescribeSkipReasons()
        {
            // 原因种类很少（最多 7 种），直接按 SkipReasonText 的规定顺序数一遍即可
            SkipReason[] all =
            {
                SkipReason.NoAttribute, SkipReason.ConstantAttribute, SkipReason.LockedLayer,
                SkipReason.AttributeErased, SkipReason.XrefBlock, SkipReason.ValueNotEmpty,
                SkipReason.BlockUnavailable
            };

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < all.Length; i++)
            {
                int count = 0;
                for (int j = 0; j < Items.Count; j++)
                {
                    if (Items[j].Skip == all[i])
                    {
                        count++;
                    }
                }
                if (count == 0)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append('、');
                }
                sb.Append(SkipReasonText.Describe(all[i]));
                if (count > 1)
                {
                    sb.Append('×').Append(count.ToString(CultureInfo.InvariantCulture));
                }
            }

            return sb.Length > 0 ? sb.ToString() : "-";
        }

        // =====================================================================
        // 分组
        // =====================================================================

        /// <summary>悬停提示最多列出多少个块（太多会撑出屏幕）。</summary>
        public const int MaxTooltipBlocks = 30;

        /// <summary>"明细"列单行摘要最多列出多少个块（再多就折行/撑列，意义也不大）。</summary>
        public const int MaxInlineBlocks = 8;

        /// <summary>
        /// 把计划里的块按行(列)聚合成表格行。
        /// groupByRow 为 false 时每个块各占一行（逐块平铺）。
        /// </summary>
        public static List<PreviewGroup> Build(NumberingPlan plan, bool groupByRow)
        {
            List<PreviewGroup> groups = new List<PreviewGroup>();
            if (plan == null)
            {
                return groups;
            }

            List<BlockItem> items = plan.Items;
            if (!groupByRow)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    PreviewGroup single = new PreviewGroup();
                    single.RowNumber = items[i].Row + 1;
                    single.Items.Add(items[i]);
                    if (items[i].CanWrite)
                    {
                        single.Writable.Add(items[i]);
                    }
                    groups.Add(single);
                }
                return groups;
            }

            PreviewGroup current = null;
            for (int i = 0; i < items.Count; i++)
            {
                BlockItem item = items[i];
                int row = item.Row + 1;

                // 排序结果里同一行(列)的块本来就是相邻的，所以只要跟上一行比即可
                if (current == null || current.RowNumber != row)
                {
                    current = new PreviewGroup();
                    current.RowNumber = row;
                    groups.Add(current);
                }

                current.Items.Add(item);
                if (item.CanWrite)
                {
                    current.Writable.Add(item);
                }
            }

            return groups;
        }
    }
}
