using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using ChemForge.Models.Text.TextColor;

namespace ChemForge.Core.Text.TextColor
{
    /// <summary>
    /// 批量改色的编排层：把一批对象交给 <see cref="TextColorApplier"/>，
    /// 需要时再深入块定义内部走一遍。
    ///
    /// 这里刻意不做刷新（REGEN）。刷新策略要由命令层按规模决定：
    /// 大图上无条件整图重生成要十几秒，而这个时间与真正改了几个对象无关。
    /// </summary>
    public static class TextColorEngine
    {
        /// <summary>
        /// 处理一批对象。
        /// </summary>
        /// <param name="database">当前图形数据库</param>
        /// <param name="ids">待处理的对象</param>
        /// <param name="options">处理选项</param>
        /// <param name="progress">进度回调（已处理数, 总数），可为 null</param>
        public static TextColorResult Run(
            Database database,
            IEnumerable<ObjectId> ids,
            TextColorOptions options,
            Action<int, int> progress = null)
        {
            if (database == null)
            {
                throw new ArgumentNullException("database");
            }

            if (ids == null)
            {
                throw new ArgumentNullException("ids");
            }

            TextColorApplier applier = new TextColorApplier(options);

            List<ObjectId> targets = new List<ObjectId>();
            foreach (ObjectId id in ids)
            {
                if (!id.IsNull && id.IsValid)
                {
                    targets.Add(id);
                }
            }

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                int done = 0;
                foreach (ObjectId id in targets)
                {
                    applier.Apply(transaction, id);

                    done++;
                    if (progress != null && (done % 500 == 0 || done == targets.Count))
                    {
                        progress(done, targets.Count);
                    }
                }

                if (applier.Result.ChangedCount > 0 || options.IncludeBlockDefinitions)
                {
                    transaction.Commit();
                }
            }

            if (options.IncludeBlockDefinitions)
            {
                ProcessBlocks(database, applier);
            }

            TextColorResult result = applier.Result;
            result.TouchedIds = applier.TouchedIds;

            // 报告不在这里拼。「给用户看什么、看多少」是命令层的事：
            // 同一份结果，命令行要完整报告、提示框只要四行结论，
            // 引擎把 Report 一起算好，命令层再按需要重拼，就会把同一份报告算两遍，
            // 还容易两边不一致（TCOLOR 以前就是这样）。
            return result;
        }

        /// <summary>
        /// 深入块定义内部处理。
        ///
        /// 跳过三类块：外部参照与其覆盖、布局块（模型/图纸空间）、
        /// 以及名字里带竖线的外部依赖块 —— 它们的内容不属于本图，
        /// 改了既改不动也容易出问题。
        /// </summary>
        private static void ProcessBlocks(Database database, TextColorApplier applier)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable table = transaction.GetObject(
                    database.BlockTableId, OpenMode.ForRead, false) as BlockTable;

                if (table == null)
                {
                    return;
                }

                foreach (ObjectId recordId in table)
                {
                    BlockTableRecord record = transaction.GetObject(
                        recordId, OpenMode.ForRead, false) as BlockTableRecord;

                    if (record == null || ShouldSkipBlock(record))
                    {
                        continue;
                    }

                    applier.Result.ScannedBlockCount++;
                    bool touched = false;

                    foreach (ObjectId entityId in record)
                    {
                        if (applier.Apply(transaction, entityId))
                        {
                            touched = true;
                        }
                    }

                    if (touched)
                    {
                        applier.Result.ProcessedBlockCount++;
                    }
                }

                transaction.Commit();
            }
        }

        private static bool ShouldSkipBlock(BlockTableRecord record)
        {
            if (record.IsLayout)
            {
                return true;
            }

            if (record.IsFromExternalReference || record.IsFromOverlayReference)
            {
                return true;
            }

            string name = record.Name;
            return !string.IsNullOrEmpty(name) && name.IndexOf('|') >= 0;
        }
    }
}
