using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ChemForge.Models.Text.TextColor
{
    /// <summary>
    /// 一次批量改色的执行结果。
    /// 计数分得比较细，是因为完成提示要告诉用户
    /// 「改了几处颜色、清了几处内部色码、跳过了几处」——
    /// 只报一个总数的话，用户看到"修改 0 个对象"时会以为命令没生效，
    /// 而实际上可能只是所有文字都已经是对的。
    /// </summary>
    public sealed class TextColorResult
    {
        /// <summary>扫描到的文字对象数。</summary>
        public int ScannedCount { get; set; }

        /// <summary>确实改动过的文字对象数。</summary>
        public int ChangedCount { get; set; }

        /// <summary>改过对象颜色的对象数。</summary>
        public int ColorChangedCount { get; set; }

        /// <summary>清掉过内部颜色覆盖码的对象数。</summary>
        public int CodeStrippedCount { get; set; }

        /// <summary>
        /// 含字段（FIELD）而只改了颜色、没动内容的文字数。
        /// 这些是刻意跳过的，不是失败。
        /// </summary>
        public int FieldSkippedCount { get; set; }

        /// <summary>处理失败的对象数。</summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 位于锁定图层、被整体跳过的文字对象数。
        ///
        /// 这些对象**一个字都没改**：锁定图层上连对象颜色都写不进去。
        /// 单独计数是因为整批共用同一个事务 —— 不拦下来，第一个锁定图层对象
        /// 就会让前面改好的全部回滚，用户看到的是"什么都没发生"。
        /// </summary>
        public int LockedSkippedCount { get; set; }

        /// <summary>扫描到的块定义数。</summary>
        public int ScannedBlockCount { get; set; }

        /// <summary>内部确实有文字被改动的块定义数。</summary>
        public int ProcessedBlockCount { get; set; }

        /// <summary>清理前的内容样例（截断后）。</summary>
        public string BeforeSample { get; set; }

        /// <summary>清理后的内容样例（截断后）。</summary>
        public string AfterSample { get; set; }

        /// <summary>首个处理对象改后颜色组的核对信息。</summary>
        public string ColorProbe { get; set; }

        /// <summary>执行过程中的非致命提示（供命令层按需打印）。</summary>
        public IList<string> Messages { get; } = new List<string>();

        /// <summary>
        /// 改动过的对象，供命令层选择刷新方式：
        /// 少量改动只需重画这些对象，改得多了再整图重生成。
        /// </summary>
        public IList<ObjectId> TouchedIds { get; set; }

        /// <summary>给用户看的完成提示全文。</summary>
        public string Report { get; set; }
    }
}
