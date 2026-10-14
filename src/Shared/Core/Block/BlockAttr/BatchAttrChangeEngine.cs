using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using ChemForge.Models.Block.BlockAttr;

namespace ChemForge.Core.Block.BlockAttr
{
    /// <summary>
    /// 批量属性替换的编排层：校验 → 写入 → 生成报告。
    ///
    /// 负责「对话框返回之后」的那一段
    /// （参数校验的降级逻辑 + 批量应用 + 报告生成），
    /// 命令层只负责选块、弹面板、把结果说给用户听。
    /// </summary>
    public static class BatchAttrChangeEngine
    {
        /// <summary>
        /// 执行一次批量替换。
        /// </summary>
        /// <param name="database">当前数据库</param>
        /// <param name="blockIds">范围内全部块引用</param>
        /// <param name="options">用户设置（内部会先做一次降级归一）</param>
        /// <param name="selectedTags">本次要处理的属性标签</param>
        /// <returns>结果；调用前应先用 <see cref="BatchAttrChangeOptions.Normalize"/> 判空</returns>
        public static BatchAttrChangeResult Run(
            Database database,
            IEnumerable<ObjectId> blockIds,
            BatchAttrChangeOptions options,
            IList<string> selectedTags)
        {
            BatchAttrChangeResult result = new BatchAttrChangeResult();
            if (database == null || options == null || selectedTags == null || selectedTags.Count == 0)
            {
                return result;
            }

            // 降级归一在这里再兜一次底：即使命令层漏判，也不会出现
            // 「开关开着但一个字段都没填」导致的全量空写
            options.Normalize();

            BatchAttrChangeWriter.ApplyOutcome outcome =
                BatchAttrChangeWriter.Apply(database, blockIds, selectedTags, options);

            result.ChangedCount = outcome.ChangedCount;
            result.ProcessedBlockCount = outcome.ProcessedBlockCount;
            result.ComMirrorWarning = outcome.ComMirrorWarning;
            result.LockedSkippedCount = outcome.LockedSkippedCount;
            result.FailedCount = outcome.FailedCount;
            result.Report = BatchAttrChangeReport.Build(
                options,
                selectedTags,
                outcome.ChangedCount,
                outcome.LockedSkippedCount,
                outcome.FailedCount);

            // 只有「目标图层恰为 0」才值得回读校验（用户报过的 0 层显示问题）
            if (options.ModifyProperties
                && string.Equals(options.LayerName, "0", StringComparison.Ordinal))
            {
                result.Diagnostics = BatchAttrChangeReport.BuildDiagnostics(
                    outcome.TargetLayerZeroCount,
                    outcome.HitLayerZeroCount,
                    outcome.SampleTag);
            }

            return result;
        }
    }
}
