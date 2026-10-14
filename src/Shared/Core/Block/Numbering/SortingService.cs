using System;
using System.Collections.Generic;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 核心排序算法：稳定的“先上后下、同一行从左到右”。
    ///
    /// 注意：不要用简单的 “Y 降序 + X 升序”。同一个视觉行里往往存在几个单位的 Y 抖动，
    /// 直接按 Y 排会把一行打散，导致编号在图纸上跳来跳去。正确做法是：
    ///   1) 先按主轴（Y）降序预排序；
    ///   2) 以“当前行第一个点的主轴值”为基准，若 |值 - 基准| &lt;= 容差 则归为同一行；
    ///   3) 行内再按内轴（X）升序；
    ///   4) 行与行之间按主轴输出。
    ///
    /// 该类型不引用任何 AutoCAD 类型，可独立单元测试。
    /// </summary>
    public static class SortingService
    {
        /// <param name="points">待排序点，返回的下标即指向本集合。</param>
        /// <param name="tolerance">行(列)容差；&lt;=0 表示不做行合并（仅同值才算一行）。</param>
        /// <param name="axis">主排序轴。</param>
        /// <param name="descendingPrimary">主轴方向：true = 自上而下 / 自左而右。</param>
        /// <param name="descendingInner">内轴方向：true = 自右而左 / 自下而上。</param>
        public static SortResult Sort(
            IList<SortPoint> points,
            double tolerance,
            PrimaryAxis axis,
            bool descendingPrimary,
            bool descendingInner)
        {
            if (points == null || points.Count == 0)
            {
                return SortResult.Empty();
            }

            int n = points.Count;
            if (tolerance < 0.0 || double.IsNaN(tolerance))
            {
                tolerance = 0.0;
            }

            // ---- 1) 整体预排序：主轴优先，内轴次之，最后用原始下标保证结果可复现 ----
            int[] seq = new int[n];
            for (int i = 0; i < n; i++)
            {
                seq[i] = i;
            }

            Array.Sort(seq, delegate(int a, int b)
            {
                int c = CompareValue(PrimaryValue(points[a], axis), PrimaryValue(points[b], axis), descendingPrimary);
                if (c != 0)
                {
                    return c;
                }
                c = CompareValue(InnerValue(points[a], axis), InnerValue(points[b], axis), descendingInner);
                if (c != 0)
                {
                    return c;
                }
                return a.CompareTo(b);
            });

            // ---- 2) 行分组：与“当前行首个点”的主轴值比较，落在容差内即同一行 ----
            List<List<int>> groups = new List<List<int>>();
            List<int> current = new List<int>();
            double anchor = 0.0;

            for (int k = 0; k < n; k++)
            {
                int idx = seq[k];
                double v = PrimaryValue(points[idx], axis);

                if (current.Count == 0)
                {
                    anchor = v;
                    current.Add(idx);
                    continue;
                }

                if (Math.Abs(v - anchor) <= tolerance)
                {
                    current.Add(idx);
                }
                else
                {
                    groups.Add(current);
                    current = new List<int>();
                    anchor = v;
                    current.Add(idx);
                }
            }

            if (current.Count > 0)
            {
                groups.Add(current);
            }

            // ---- 3) 行内排序 + 4) 展平输出 ----
            int[] order = new int[n];
            int[] rowIndex = new int[n];
            int pos = 0;

            for (int g = 0; g < groups.Count; g++)
            {
                List<int> grp = groups[g];
                grp.Sort(delegate(int a, int b)
                {
                    int c = CompareValue(InnerValue(points[a], axis), InnerValue(points[b], axis), descendingInner);
                    if (c != 0)
                    {
                        return c;
                    }
                    return a.CompareTo(b);
                });

                for (int t = 0; t < grp.Count; t++)
                {
                    int idx = grp[t];
                    rowIndex[idx] = g;
                    order[pos] = idx;
                    pos++;
                }
            }

            return new SortResult(order, rowIndex, groups.Count);
        }

        private static double PrimaryValue(SortPoint p, PrimaryAxis axis)
        {
            return axis == PrimaryAxis.Row ? p.Y : p.X;
        }

        private static double InnerValue(SortPoint p, PrimaryAxis axis)
        {
            return axis == PrimaryAxis.Row ? p.X : p.Y;
        }

        private static int CompareValue(double a, double b, bool descending)
        {
            if (a > b)
            {
                return descending ? -1 : 1;
            }
            if (a < b)
            {
                return descending ? 1 : -1;
            }
            return 0;
        }
    }
}
