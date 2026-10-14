using System.Collections.Generic;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 行(列)容差自动估算。
    ///
    /// 容差太大 → 本属两行的块被并成一行；太小 → 同一行的块被拆开。
    /// 用“块高度中位数 × 系数”作估计：工程图中同一行的块通常上下对齐，
    /// 行间距一般不小于一个块高，取 0.5 倍中位高度能覆盖绝大多数图面。
    /// </summary>
    public static class ToleranceEstimator
    {
        public const double DefaultFactor = 0.5;
        public const double MinimumFactor = 0.3;
        public const double MaximumFactor = 0.5;

        /// <summary>按块高度中位数估算容差；没有有效高度时返回 0（表示不做行合并）。</summary>
        public static double Estimate(IList<double> heights)
        {
            return Estimate(heights, DefaultFactor);
        }

        public static double Estimate(IList<double> heights, double factor)
        {
            if (heights == null || heights.Count == 0)
            {
                return 0.0;
            }
            if (factor < MinimumFactor)
            {
                factor = MinimumFactor;
            }
            if (factor > MaximumFactor)
            {
                factor = MaximumFactor;
            }

            List<double> valid = new List<double>(heights.Count);
            for (int i = 0; i < heights.Count; i++)
            {
                double h = heights[i];
                if (h > 1e-9 && !double.IsNaN(h) && !double.IsInfinity(h))
                {
                    valid.Add(h);
                }
            }

            if (valid.Count == 0)
            {
                return 0.0;
            }

            valid.Sort();
            double median;
            int mid = valid.Count / 2;
            if (valid.Count % 2 == 1)
            {
                median = valid[mid];
            }
            else
            {
                median = 0.5 * (valid[mid - 1] + valid[mid]);
            }

            return median * factor;
        }
    }
}
