using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 一次批量属性替换的**目标集合**：范围内全部块引用 + 属性标签全集 +
    /// 当前图纸的动态下拉候选（文字样式 / 图层 / 线型）。
    ///
    /// 采集完成后事务即关闭，之后才弹对话框 —— 绝不能让事务跨越模态对话框。
    /// </summary>
    public sealed class BatchAttrChangeTargets
    {
        /// <summary>范围内全部块引用的 ObjectId（已按修改范围展开）</summary>
        public IList<ObjectId> BlockIds { get; set; } = new List<ObjectId>();

        /// <summary>本次直接选中的块引用 ObjectId（「仅本次选中的图块」用这一份）</summary>
        public IList<ObjectId> SelectedBlockIds { get; set; } = new List<ObjectId>();

        /// <summary>全图同名块展开后的块引用 ObjectId（「全图所有同名图块」用这一份）</summary>
        public IList<ObjectId> SameNameBlockIds { get; set; } = new List<ObjectId>();

        /// <summary>选中块的唯一有效块名（顿号连接后即对话框的「当前块名：」）</summary>
        public IList<string> BlockNames { get; set; } = new List<string>();

        /// <summary>汇总去重排序后的属性标签全集</summary>
        public IList<string> Tags { get; set; } = new List<string>();

        /// <summary>当前图纸的文字样式名（已排序）</summary>
        public IList<string> StyleNames { get; set; } = new List<string>();

        /// <summary>当前图纸的图层名（已排序，必定含 "0"）</summary>
        public IList<string> LayerNames { get; set; } = new List<string>();

        /// <summary>当前图纸的线型名（已排序）</summary>
        public IList<string> LinetypeNames { get; set; } = new List<string>();
    }
}
