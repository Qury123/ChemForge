using System.Collections.Generic;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 与 AutoCAD 无关的二维排序点。把排序算法的输入从 Point3d 解耦出来，
    /// 目的只有一个：排序逻辑可以脱离 AutoCAD 单独测试。
    /// </summary>
    public struct SortPoint
    {
        public readonly double X;
        public readonly double Y;

        public SortPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.###}, {1:0.###})", X, Y);
        }
    }

    /// <summary>
    /// 排序结果。
    /// Order[i] = 第 i 个位置对应的原始下标；
    /// RowIndex[原始下标] = 所属行(列)号，从 0 开始；
    /// RowCount = 行(列)总数。
    /// </summary>
    public sealed class SortResult
    {
        public int[] Order { get; private set; }
        public int[] RowIndex { get; private set; }
        public int RowCount { get; private set; }

        public SortResult(int[] order, int[] rowIndex, int rowCount)
        {
            Order = order;
            RowIndex = rowIndex;
            RowCount = rowCount;
        }

        public static SortResult Empty()
        {
            return new SortResult(new int[0], new int[0], 0);
        }
    }
}
