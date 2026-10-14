using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using ChemForge.Models.Block.BlockAttr;

namespace ChemForge.Core.Block.BlockAttr
{
    /// <summary>
    /// 采集阶段：把「本次选中的块引用」整理成对话框需要的一切
    /// —— 有效块名、属性标签全集、全图同名块集合、动态下拉候选。
    ///
    /// 属性收集与选择集扩展两个阶段，
    /// 其中有两处刻意的改进：
    /// <list type="number">
    /// <item>「全图同名块」不再全图扫 INSERT 逐个比名字，而是遍历块表、
    /// 对有效块名命中的块表记录调 <c>GetBlockReferenceIds</c> 反查引用。
    /// 匿名动态块引用同样覆盖（有效块名已在
    /// <see cref="BlockEffectiveNameResolver"/> 里还原），且大图下快得多。</item>
    /// <item>候选列表（文字样式 / 图层 / 线型）在**同一个事务里**顺手取走，
    /// 事务随后立即关闭 —— 弹对话框时绝不能再持有事务。</item>
    /// </list>
    /// </summary>
    public static class BatchAttrChangeCollector
    {
        /// <summary>
        /// 采集一次批量属性替换所需的全部上下文。
        /// </summary>
        /// <param name="database">当前数据库</param>
        /// <param name="selectedBlockIds">用户本次选中的块引用 ObjectId</param>
        /// <returns>目标集合；database 为 null 时返回空集合</returns>
        public static BatchAttrChangeTargets Collect(Database database, IEnumerable<ObjectId> selectedBlockIds)
        {
            BatchAttrChangeTargets targets = new BatchAttrChangeTargets();
            if (database == null || selectedBlockIds == null)
            {
                return targets;
            }

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = transaction.GetObject(database.BlockTableId, OpenMode.ForRead, false) as BlockTable;
                if (blockTable == null)
                {
                    return targets;
                }

                BlockEffectiveNameResolver resolver = new BlockEffectiveNameResolver(blockTable, transaction);

                // ---- 1) 本次选中的块：去重后的有效块名 ----
                List<string> names = new List<string>();
                HashSet<string> nameSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (ObjectId blockId in selectedBlockIds)
                {
                    BlockReference blockReference =
                        transaction.GetObject(blockId, OpenMode.ForRead, false) as BlockReference;
                    if (blockReference == null)
                    {
                        continue;
                    }

                    targets.SelectedBlockIds.Add(blockId);

                    string name = resolver.GetEffectiveName(blockReference, transaction);
                    if (name.Length > 0 && nameSet.Add(name))
                    {
                        names.Add(name);
                    }
                }

                names.Sort(StringComparer.Ordinal);
                targets.BlockNames = names;

                // ---- 2) 属性标签全集（跨所有选中块去重 + 排序）----
                targets.Tags = CollectTags(transaction, targets.SelectedBlockIds);

                // ---- 3) 全图同名块 ----
                targets.SameNameBlockIds = ExpandToSameName(blockTable, transaction, resolver, nameSet);

                // ---- 4) 动态下拉候选 ----
                targets.StyleNames = CollectTableNames(transaction, database.TextStyleTableId, true, false);
                targets.LayerNames = CollectTableNames(transaction, database.LayerTableId, false, true);
                targets.LinetypeNames = CollectTableNames(transaction, database.LinetypeTableId, false, false);

                // 默认范围 = 仅本次选中的图块；对话框切换范围时再改这一份
                targets.BlockIds = targets.SelectedBlockIds;

                transaction.Commit();
            }

            return targets;
        }

        /// <summary>
        /// 汇总一批块引用上全部增强属性的标签，去重并按 ASCII 序排序。
        /// 这里用自写比较器避开 locale 差异，
        /// 不依赖运行环境的区域设置，这里用
        /// <see cref="StringComparer.Ordinal"/> 等价。
        /// </summary>
        /// <param name="transaction">事务</param>
        /// <param name="blockIds">块引用 ObjectId</param>
        /// <returns>标签列表</returns>
        public static IList<string> CollectTags(Transaction transaction, IEnumerable<ObjectId> blockIds)
        {
            // 去重用大小写不敏感：与写入端的 tagSet 保持一致，
            // 即 "PARTNO" 与 "partno" 视为同一个标签。
            // 同名块之间若把标签写成不同大小写，这里只保留最先遇到的那一种写法，
            // 写入时两种写法都能命中。
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (transaction == null || blockIds == null)
            {
                return new List<string>();
            }

            foreach (ObjectId blockId in blockIds)
            {
                BlockReference blockReference =
                    transaction.GetObject(blockId, OpenMode.ForRead, false) as BlockReference;
                if (blockReference == null)
                {
                    continue;
                }

                foreach (ObjectId attributeId in blockReference.AttributeCollection)
                {
                    AttributeReference attribute =
                        transaction.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                    if (attribute == null)
                    {
                        continue;
                    }

                    string tag = attribute.Tag;
                    if (!string.IsNullOrEmpty(tag))
                    {
                        set.Add(tag);
                    }
                }
            }

            List<string> list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// 「全图所有同名图块」：按有效块名反查块表中每个记录的块引用。
        ///
        /// 不采用「全库扫一遍 INSERT 再按有效块名过滤」的做法；
        /// 这里用
        /// <c>BlockTableRecord.GetBlockReferenceIds(true, true)</c> 直接拿引用，
        /// 覆盖模型空间、图纸空间与全部布局，语义与 <c>ssget "X"</c> 一致。
        /// </summary>
        /// <param name="blockTable">块表</param>
        /// <param name="transaction">事务</param>
        /// <param name="resolver">有效块名解析器</param>
        /// <param name="names">要匹配的有效块名集合</param>
        /// <returns>块引用 ObjectId 列表</returns>
        public static IList<ObjectId> ExpandToSameName(
            BlockTable blockTable,
            Transaction transaction,
            BlockEffectiveNameResolver resolver,
            ICollection<string> names)
        {
            List<ObjectId> result = new List<ObjectId>();
            if (blockTable == null || transaction == null || resolver == null || names == null || names.Count == 0)
            {
                return result;
            }

            foreach (ObjectId recordId in blockTable)
            {
                BlockTableRecord record = transaction.GetObject(recordId, OpenMode.ForRead, false) as BlockTableRecord;
                if (record == null)
                {
                    continue;
                }

                // 布局（*Model_Space / *Paper_Space）与其它系统匿名块的名字天然不在
                // 用户选中的集合里，下面这一句就把它们滤掉了，无需额外判断
                if (!names.Contains(resolver.GetEffectiveName(record, transaction)))
                {
                    continue;
                }

                ObjectIdCollection references;
                try
                {
                    references = record.GetBlockReferenceIds(true, true);
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (references == null)
                {
                    continue;
                }

                foreach (ObjectId referenceId in references)
                {
                    result.Add(referenceId);
                }
            }

            return result;
        }

        /// <summary>
        /// 取符号表里的名字（文字样式 / 图层 / 线型），去重排序。
        /// </summary>
        /// <param name="transaction">事务</param>
        /// <param name="tableId">符号表 ObjectId</param>
        /// <param name="excludeAsterisk">是否剔除名字里带 "*" 的项（文字样式用，即过滤形文件样式）</param>
        /// <param name="ensureZeroLayer">是否强制保证列表里有图层 "0"</param>
        /// <returns>名字列表</returns>
        public static IList<string> CollectTableNames(
            Transaction transaction,
            ObjectId tableId,
            bool excludeAsterisk,
            bool ensureZeroLayer)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            SymbolTable table = transaction.GetObject(tableId, OpenMode.ForRead, false) as SymbolTable;
            if (table == null)
            {
                return new List<string>();
            }

            foreach (ObjectId recordId in table)
            {
                SymbolTableRecord record = transaction.GetObject(recordId, OpenMode.ForRead, false) as SymbolTableRecord;
                if (record == null)
                {
                    continue;
                }

                string name = record.Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (excludeAsterisk && name.IndexOf('*') >= 0)
                {
                    continue;
                }

                set.Add(name);
            }

            if (ensureZeroLayer)
            {
                set.Add("0");
            }

            List<string> list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
