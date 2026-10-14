using System;
using System.Globalization;
using System.Xml.Serialization;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>主排序轴：按行(Y)优先，还是按列(X)优先。</summary>
    public enum PrimaryAxis
    {
        /// <summary>行优先：先上后下，同一行内再从左到右（默认）。</summary>
        Row = 0,

        /// <summary>列优先：先左后右，同一列内再从上到下。</summary>
        Column = 1
    }

    /// <summary>排序基准点。</summary>
    public enum SortBasis
    {
        /// <summary>块插入点（默认，最稳定）。</summary>
        InsertPoint = 0,

        /// <summary>块包围盒中心。</summary>
        BoundsCenter = 1,

        /// <summary>目标属性的文字位置。</summary>
        AttributePosition = 2
    }

    /// <summary>排序所用坐标系。</summary>
    public enum CoordinateMode
    {
        /// <summary>世界坐标系（默认，与视图旋转无关）。</summary>
        Wcs = 0,

        /// <summary>当前 UCS（UCS 旋转时按视觉方向排序）。</summary>
        CurrentUcs = 1
    }

    /// <summary>编号与排序的全部参数。可被 XmlSerializer 直接序列化到配置文件。</summary>
    [Serializable]
    public class NumberOptions
    {
        // ---------------- 目标属性 ----------------

        /// <summary>要写入的增强属性 Tag（大小写不敏感）。</summary>
        public string Tag { get; set; }

        // ---------------- 编号规则 ----------------

        /// <summary>起始值。</summary>
        public int Start { get; set; }

        /// <summary>步长。</summary>
        public int Step { get; set; }

        /// <summary>补零位数，0 表示不补零。</summary>
        public int Pad { get; set; }

        /// <summary>前缀。</summary>
        public string Prefix { get; set; }

        /// <summary>后缀。</summary>
        public string Suffix { get; set; }

        // ---------------- 排序规则 ----------------

        /// <summary>主排序轴。</summary>
        public PrimaryAxis Axis { get; set; }

        /// <summary>排序基准点。</summary>
        public SortBasis Basis { get; set; }

        /// <summary>排序坐标系。</summary>
        public CoordinateMode Coord { get; set; }

        /// <summary>行(列)容差；&lt;= 0 表示按块高度自动估算。</summary>
        public double RowTolerance { get; set; }

        /// <summary>行(列)间方向：true = 自上而下 / 自左而右（默认）。</summary>
        public bool DescendingPrimary { get; set; }

        /// <summary>行(列)内方向：true = 自右而左 / 自下而上。</summary>
        public bool DescendingInner { get; set; }

        // ---------------- 写入行为 ----------------

        /// <summary>只填空白：属性已有内容时不覆盖。</summary>
        public bool FillOnlyEmpty { get; set; }

        /// <summary>跳过块定义来自外部参照(xref)的块。</summary>
        public bool SkipXrefBlocks { get; set; }

        /// <summary>
        /// 预览表格按行(列)聚合：同一行(列)只显示一行摘要，鼠标悬停可看该行全部块。
        /// 候选块多的时候用它把表格压下来；关掉就是逐块平铺。
        /// 只影响对话框的显示，不参与编号、写入与报告。
        /// </summary>
        public bool PreviewGroupByRow { get; set; }

        public NumberOptions()
        {
            Tag = string.Empty;
            Start = 1;
            Step = 1;
            Pad = 3;
            Prefix = string.Empty;
            Suffix = string.Empty;
            Axis = PrimaryAxis.Row;
            Basis = SortBasis.InsertPoint;
            Coord = CoordinateMode.Wcs;
            RowTolerance = 0.0;
            DescendingPrimary = true;
            DescendingInner = false;
            FillOnlyEmpty = false;
            SkipXrefBlocks = true;
            PreviewGroupByRow = true;
        }

        public NumberOptions Clone()
        {
            NumberOptions c = new NumberOptions();
            c.Tag = Tag;
            c.Start = Start;
            c.Step = Step;
            c.Pad = Pad;
            c.Prefix = Prefix;
            c.Suffix = Suffix;
            c.Axis = Axis;
            c.Basis = Basis;
            c.Coord = Coord;
            c.RowTolerance = RowTolerance;
            c.DescendingPrimary = DescendingPrimary;
            c.DescendingInner = DescendingInner;
            c.FillOnlyEmpty = FillOnlyEmpty;
            c.SkipXrefBlocks = SkipXrefBlocks;
            c.PreviewGroupByRow = PreviewGroupByRow;
            return c;
        }

        /// <summary>排序方式的中文描述，用于报告。</summary>
        public string DescribeSort()
        {
            string axisText;
            if (Axis == PrimaryAxis.Row)
            {
                axisText = DescendingPrimary ? "行优先(先上后下)" : "行优先(先下后上)";
            }
            else
            {
                // 列优先时主轴是 X：降序 = 自右而左
                axisText = DescendingPrimary ? "列优先(先右后左)" : "列优先(先左后右)";
            }

            string innerText;
            if (Axis == PrimaryAxis.Row)
            {
                innerText = DescendingInner ? "行内从右到左" : "行内从左到右";
            }
            else
            {
                innerText = DescendingInner ? "列内从上到下" : "列内从下到上";
            }

            string basisText = "插入点";
            if (Basis == SortBasis.BoundsCenter) basisText = "包围盒中心";
            if (Basis == SortBasis.AttributePosition) basisText = "属性文字位置";

            string coordText = Coord == CoordinateMode.CurrentUcs ? "当前UCS" : "WCS";

            return string.Format(CultureInfo.InvariantCulture,
                "{0} / {1} / 基准 {2} / 坐标系 {3}", axisText, innerText, basisText, coordText);
        }
    }
}
