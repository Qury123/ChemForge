namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 批量属性替换的执行结果。
    /// 汇总了三部分：批量应用的计数、
    /// 拼出来的报告文本，以及「回读校验」的诊断文本。
    /// </summary>
    public sealed class BatchAttrChangeResult
    {
        /// <summary>实际被改动的属性数量</summary>
        public int ChangedCount { get; set; }

        /// <summary>处理过的块引用数量（本次范围内）</summary>
        public int ProcessedBlockCount { get; set; }

        /// <summary>
        /// 位于锁定图层、被整体跳过的属性处数。
        /// 这些属性一个字都没改，必须在报告里说清楚，
        /// 否则用户会以为"选了 100 处只改了 80 处"是丢了数据。
        /// </summary>
        public int LockedSkippedCount { get; set; }

        /// <summary>提升为可写失败而跳过的属性处数（极少见）</summary>
        public int FailedCount { get; set; }

        /// <summary>命令行 / 弹窗用的多行报告文本</summary>
        public string Report { get; set; } = string.Empty;

        /// <summary>
        /// 「回读校验」诊断文本；只在目标图层恰为 "0" 时才有内容，其余情况为 null。
        /// 由写入阶段的回读校验产生。
        /// </summary>
        public string Diagnostics { get; set; }

        /// <summary>
        /// COM（ActiveX）补写「反向 / 倒置」时出现的错误说明；正常为 null。
        ///
        /// AutoCAD 托管 API 没有 IsMirrored / IsUpsideDown，只能借 ActiveX 补写，
        /// 万一宿主禁用 ActiveX，这里会带上原因，报告里会提示用户。
        /// </summary>
        public string ComMirrorWarning { get; set; }
    }
}
