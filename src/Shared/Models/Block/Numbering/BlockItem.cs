using System.Collections.Generic;

namespace ChemForge.Models.Block.Numbering
{
    /// <summary>跳过原因。None 表示可以正常编号。</summary>
    public enum SkipReason
    {
        None = 0,

        /// <summary>该块（当前可见性状态）没有目标 Tag 的属性。</summary>
        NoAttribute = 1,

        /// <summary>常量属性，定义时已固定，无法修改。</summary>
        ConstantAttribute = 2,

        /// <summary>属性所在图层被锁定。</summary>
        LockedLayer = 3,

        /// <summary>属性对象已被删除（例如被可见性状态隐藏）。</summary>
        AttributeErased = 4,

        /// <summary>块定义来自外部参照(xref)。</summary>
        XrefBlock = 5,

        /// <summary>属性已有内容，且启用了“只填空白”。</summary>
        ValueNotEmpty = 6,

        /// <summary>块参照无法打开（例如已被删除）。</summary>
        BlockUnavailable = 7
    }

    public static class SkipReasonText
    {
        public static string Describe(SkipReason reason)
        {
            switch (reason)
            {
                case SkipReason.NoAttribute: return "缺少目标属性";
                case SkipReason.ConstantAttribute: return "常量属性不可修改";
                case SkipReason.LockedLayer: return "属性所在图层被锁定";
                case SkipReason.AttributeErased: return "属性已被删除/隐藏";
                case SkipReason.XrefBlock: return "块定义来自外部参照";
                case SkipReason.ValueNotEmpty: return "已有内容(只填空白)";
                case SkipReason.BlockUnavailable: return "块参照无法打开";
                default: return "-";
            }
        }
    }

    /// <summary>
    /// 一个候选块的全部信息。这是一个“数据载体”：几何信息在选择阶段缓存一次，
    /// 属性信息在每次预览/写入前重新读取（图形可能被用户改动）。
    ///
    /// 带 <c>#if BLOCKNUMBERING_ACAD</c> 的成员是 AutoCAD 类型（ObjectId / Point3d），
    /// 自测工程不引用 AutoCAD 程序集，靠这个宏把它们排除掉，
    /// 让排序 / 编号 / 预览分组这些纯逻辑可以直接跑单元测试。
    /// </summary>
    public sealed class BlockItem
    {
        // ---------------- 标识 ----------------

#if BLOCKNUMBERING_ACAD
        public Autodesk.AutoCAD.DatabaseServices.ObjectId Id;
#endif

        public string Handle = string.Empty;

        /// <summary>块参照实际指向的定义名；动态块会是以 * 开头的匿名名。</summary>
        public string RawBlockName = string.Empty;

        /// <summary>有效块名；动态块解析为原始块定义名，与 AutoCAD 的 EffectiveName 等价。</summary>
        public string EffectiveBlockName = string.Empty;

        public string LayerName = string.Empty;

        // ---------------- 几何（WCS） ----------------

#if BLOCKNUMBERING_ACAD
        public Autodesk.AutoCAD.Geometry.Point3d InsertPoint;
        public Autodesk.AutoCAD.Geometry.Point3d BoundsCenter;
#endif

        public bool HasBounds;

#if BLOCKNUMBERING_ACAD
        public Autodesk.AutoCAD.Geometry.Point3d AttributePoint;
#endif

        public bool HasAttributePoint;

        /// <summary>包围盒高度，用于自动估算行容差；0 表示未知。</summary>
        public double Height;

        // ---------------- 属性 ----------------

#if BLOCKNUMBERING_ACAD
        public Autodesk.AutoCAD.DatabaseServices.ObjectId AttributeId;
#endif

        public string OldValue = string.Empty;
        public string NewValue = string.Empty;

        // ---------------- 排序与编号结果 ----------------
        public int Row = -1;

        /// <summary>从 1 开始的序号；0 表示不参与编号（被跳过）。</summary>
        public int Ordinal;

        /// <summary>实际参与排序的坐标（已按所选坐标系/基准换算）。</summary>
        public double SortX;
        public double SortY;

        public SkipReason Skip = SkipReason.None;

        public bool CanWrite
        {
            get { return Skip == SkipReason.None; }
        }
    }

    /// <summary>候选块收集阶段的统计，用于向用户说明“为什么数量比选中的少”。</summary>
    public sealed class CollectionStats
    {
        public int Received;
        public int NonBlock;
        public int Anonymous;
        public int Xref;
        public int Kept;

        public readonly HashSet<string> Names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public string Describe()
        {
            List<string> parts = new List<string>();
            parts.Add("收到 " + Received.ToString());
            if (NonBlock > 0) parts.Add("非块参照 " + NonBlock.ToString());
            if (Anonymous > 0) parts.Add("匿名块 " + Anonymous.ToString());
            if (Xref > 0) parts.Add("外部参照块 " + Xref.ToString());
            parts.Add("有效 " + Kept.ToString());
            return string.Join("，", parts.ToArray());
        }
    }
}
