using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Interop.Common;
using ChemForge.Core.Common;
using ChemForge.Models.Block.BlockAttr;

namespace ChemForge.Core.Block.BlockAttr
{
    /// <summary>
    /// 写入阶段：把 <see cref="BatchAttrChangeOptions"/> 应用到一批块引用的增强属性上。
    ///
    /// 属性写入与批量应用两个阶段，
    /// 其中三条**必须保留**的原始设计（都是踩过坑才写下来的）：
    /// <list type="number">
    /// <item><b>颜色走 <c>ColorIndex</c>（DXF 组码 62），不走 ActiveX 的 Color。</b>
    /// 块内属性(ATTRIB)只有组码 62 能 100% 表达逻辑色：256=随层、0=随块、1-255=ACI。
    /// 把 256/0 传给 ActiveX 的 Color 时，AutoCAD 对块内属性常常拒绝或忽略，
    /// 颜色会实际停留在「随块」，于是在 0 层时跟随块所在层、显示成背景色。
    /// 托管 API 的 <c>AttributeReference.ColorIndex</c> 正是直接写组码 62，
    /// 所以这里用它。</item>
    /// <item><b>文字对齐只在明确给了非 0 值时才写。</b>
    /// 组码 72 的 0 就是「左对齐/默认」，这里刻意不写它，
    /// 避免把本来用其他方式对齐的属性强行拉回左对齐。</item>
    /// <item><b>目标图层恰为 "0" 且用户没指定颜色时，颜色按随层(256)写。</b>
    /// 否则「随块」的属性会跟随块引用所在层（常是 000 背景层），
    /// 造成用户报过的「换成 0 层却还是 000」。</item>
    /// </list>
    ///
    /// 唯一无法用托管 API 完成的是<b>反向 / 倒置</b>（DXF 组码 71 的 bit2 / bit4）：
    /// 2020 与 2025 两代 SDK 的 <c>AttributeReference</c> 都没有
    /// IsMirrored / IsUpsideDown（实测缺失），只能回到 ActiveX 的
    /// <c>AcadAttribute.Backward</c> / <c>UpsideDown</c> 补写。
    /// </summary>
    public static class BatchAttrChangeWriter
    {
        /// <summary>一次写入的统计结果</summary>
        public sealed class ApplyOutcome
        {
            /// <summary>实际发生写入的属性处数</summary>
            public int ChangedCount { get; set; }

            /// <summary>检查过的块引用个数</summary>
            public int ProcessedBlockCount { get; set; }

            /// <summary>目标图层为 "0" 的属性处数（回读校验用）</summary>
            public int TargetLayerZeroCount { get; set; }

            /// <summary>回读确认图层确为 "0" 的属性处数（回读校验用）</summary>
            public int HitLayerZeroCount { get; set; }

            /// <summary>回读校验的示例标签</summary>
            public string SampleTag { get; set; }

            /// <summary>ActiveX 补写「反向/倒置」失败时的提示（成功时为 null）</summary>
            public string ComMirrorWarning { get; set; }

            /// <summary>
            /// 位于锁定图层、被整体跳过的属性处数。
            ///
            /// 这些属性一个字都没改：锁定图层上连 <c>OpenMode.ForWrite</c> 都打不开。
            /// 单独计数是因为整批共用同一个事务 —— 不拦下来，
            /// 第一个锁定图层属性就会让前面改好的全部回滚。
            /// </summary>
            public int LockedSkippedCount { get; set; }

            /// <summary>提升为可写失败而跳过的属性处数（极少见，异常不得冒出去拖垮整批）</summary>
            public int FailedCount { get; set; }
        }

        /// <summary>
        /// 把设置应用到指定块集合里、标签命中的全部增强属性。
        /// </summary>
        /// <param name="database">当前数据库</param>
        /// <param name="blockIds">要处理的块引用 ObjectId</param>
        /// <param name="tags">只处理这些标签的属性</param>
        /// <param name="options">用户设置</param>
        /// <returns>统计结果</returns>
        public static ApplyOutcome Apply(
            Database database,
            IEnumerable<ObjectId> blockIds,
            ICollection<string> tags,
            BatchAttrChangeOptions options)
        {
            ApplyOutcome outcome = new ApplyOutcome();
            if (database == null || blockIds == null || tags == null || options == null)
            {
                return outcome;
            }

            // 大小写不敏感：标签比较不区分大小写，
            // 即 "PARTNO" 与 "partno" 视为同一个标签。这里必须照做，
            // 否则「全图所有同名块」范围内、或记忆里带回的标签只要大小写与原定义不同，
            // 就会静默匹配不上，最后只报一句「共更新 0 处属性」而没有任何提示。
            HashSet<string> tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
            if (tagSet.Count == 0)
            {
                return outcome;
            }

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                Resolved resolved = Resolve(transaction, database, options);

                foreach (ObjectId blockId in blockIds)
                {
                    BlockReference blockReference =
                        transaction.GetObject(blockId, OpenMode.ForRead, false) as BlockReference;
                    if (blockReference == null)
                    {
                        continue;
                    }

                    outcome.ProcessedBlockCount++;
                    bool blockChanged = false;

                    foreach (ObjectId attributeId in blockReference.AttributeCollection)
                    {
                        // 先按只读打开：这一步只为拿 Tag 与所在图层做筛选。
                        // 原来这里直接 OpenMode.ForWrite，会把不匹配的属性也一并打开，
                        // 而落在锁定图层上的属性当场抛 OnLockedLayer ——
                        // 整批共用一个事务，于是全部改动一起回滚。
                        AttributeReference attribute =
                            transaction.GetObject(attributeId, OpenMode.ForRead, false) as AttributeReference;
                        if (attribute == null)
                        {
                            continue;
                        }

                        if (!tagSet.Contains(attribute.Tag))
                        {
                            continue;
                        }

                        if (LayerLockGuard.IsOnLockedLayer(transaction, attribute))
                        {
                            outcome.LockedSkippedCount++;
                            continue;
                        }

                        try
                        {
                            attribute.UpgradeOpen();
                        }
                        catch (System.Exception)
                        {
                            // 提升失败就跳过这一处。绝不能让异常冒出去：
                            // 整批共用一个事务，冒出去就是"什么都没发生"。
                            outcome.FailedCount++;
                            continue;
                        }

                        if (EditAttribute(attribute, options, resolved, outcome))
                        {
                            blockChanged = true;
                        }
                    }

                    if (blockChanged)
                    {
                        // 块引用本身也刷一次，保证属性改动在屏幕上立即体现
                        try
                        {
                            blockReference.UpgradeOpen();
                            blockReference.RecordGraphicsModified(true);
                        }
                        catch (System.Exception)
                        {
                        }
                    }
                }

                transaction.Commit();
            }

            return outcome;
        }

        /// <summary>把设置里「按名字给的东西」预解析成一次性的句柄，避免逐属性查表</summary>
        private sealed class Resolved
        {
            public ObjectId TextStyleId = ObjectId.Null;
            public bool HasTextStyle;
            public string LayerName;
            public string LinetypeName;
            public int? ColorIndex;
        }

        private static Resolved Resolve(Transaction transaction, Database database, BatchAttrChangeOptions options)
        {
            Resolved resolved = new Resolved();

            if (options.ModifyFormat && !string.IsNullOrEmpty(options.TextStyleName))
            {
                TextStyleTable styleTable =
                    transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead, false) as TextStyleTable;
                if (styleTable != null && styleTable.Has(options.TextStyleName))
                {
                    resolved.TextStyleId = styleTable[options.TextStyleName];
                    resolved.HasTextStyle = true;
                }
            }

            if (options.ModifyProperties)
            {
                if (!string.IsNullOrEmpty(options.LayerName))
                {
                    resolved.LayerName = options.LayerName;
                }

                if (!string.IsNullOrEmpty(options.LinetypeName))
                {
                    resolved.LinetypeName = options.LinetypeName;
                }

                resolved.ColorIndex = options.ColorIndex;

                // 特例：目标图层恰为 "0" 而用户没点颜色 → 按随层(256)写
                if (!resolved.ColorIndex.HasValue
                    && string.Equals(resolved.LayerName, "0", StringComparison.Ordinal))
                {
                    resolved.ColorIndex = 256;
                }
            }

            return resolved;
        }

        /// <summary>
        /// 处理单个属性；返回是否发生了写入。
        /// </summary>
        private static bool EditAttribute(
            AttributeReference attribute,
            BatchAttrChangeOptions options,
            Resolved resolved,
            ApplyOutcome outcome)
        {
            bool changed = false;

            // ---- ① 文字内容 ----
            if (options.ModifyContent)
            {
                attribute.TextString = options.NewContent ?? string.Empty;
                changed = true;
            }

            // ---- ② 文字选项 ----
            if (options.ModifyFormat)
            {
                if (resolved.HasTextStyle)
                {
                    attribute.TextStyleId = resolved.TextStyleId;
                    changed = true;
                }

                if (options.Justify.HasValue && options.Justify.Value != 0)
                {
                    attribute.HorizontalMode = (TextHorizontalMode)options.Justify.Value;
                    changed = true;
                }

                if (options.Height.HasValue)
                {
                    attribute.Height = options.Height.Value;
                    changed = true;
                }

                if (options.WidthFactor.HasValue)
                {
                    attribute.WidthFactor = options.WidthFactor.Value;
                    changed = true;
                }

                if (options.Rotation.HasValue)
                {
                    attribute.Rotation = options.Rotation.Value;
                    changed = true;
                }

                if (options.Oblique.HasValue)
                {
                    attribute.Oblique = options.Oblique.Value;
                    changed = true;
                }
            }

            // ---- ③ 特性 ----
            if (options.ModifyProperties)
            {
                if (resolved.LayerName != null)
                {
                    attribute.Layer = resolved.LayerName;
                    changed = true;

                    // 只有目标图层恰为 "0" 时才做回读校验：
                    // 「改成 0 层却显示成 000」是用户报过的现象，需要能一眼分辨
                    // 是「没写进数据」还是「写进去了但显示/颜色不对」
                    if (string.Equals(resolved.LayerName, "0", StringComparison.Ordinal))
                    {
                        outcome.TargetLayerZeroCount++;
                        if (string.Equals(attribute.Layer, "0", StringComparison.Ordinal))
                        {
                            outcome.HitLayerZeroCount++;
                            if (outcome.SampleTag == null)
                            {
                                outcome.SampleTag = attribute.Tag;
                            }
                        }
                    }
                }

                if (resolved.ColorIndex.HasValue)
                {
                    attribute.ColorIndex = resolved.ColorIndex.Value;
                    changed = true;
                }

                if (resolved.LinetypeName != null)
                {
                    attribute.Linetype = resolved.LinetypeName;
                    changed = true;
                }

                if (options.LineWeight.HasValue)
                {
                    // -1 = 随层。托管 API 直接写 ByLayer 即可
                    attribute.LineWeight = (LineWeight)options.LineWeight.Value;
                    changed = true;
                }
            }

            // ---- ④ 反向 / 倒置：托管 API 没有入口，回到 ActiveX 补写 ----
            if (options.ModifyFormat && (options.Backwards.HasValue || options.UpsideDown.HasValue))
            {
                ApplyMirrorViaActiveX(attribute, options, outcome);
            }

            if (changed)
            {
                outcome.ChangedCount++;
                try
                {
                    // 让改动立即可见，最终整体刷新由 REGEN 兜底
                    attribute.RecordGraphicsModified(true);
                }
                catch (System.Exception)
                {
                }
            }

            return changed;
        }

        /// <summary>
        /// 用 ActiveX 写「反向 / 倒置」（DXF 组码 71 的 bit2 / bit4）。
        ///
        /// 托管 API 在 2020 与 2025 两代都没有对应属性，只能走这条老路；
        /// 失败不抛出，只记一条提示 —— 其余修改项已经写进去了，不该因为
        /// 这一项把整次操作变成失败。
        /// </summary>
        private static void ApplyMirrorViaActiveX(
            AttributeReference attribute,
            BatchAttrChangeOptions options,
            ApplyOutcome outcome)
        {
            try
            {
                AcadAttribute comAttribute = attribute.AcadObject as AcadAttribute;
                if (comAttribute == null)
                {
                    if (outcome.ComMirrorWarning == null)
                    {
                        outcome.ComMirrorWarning = "未能取得属性的 ActiveX 接口，「反向/倒置」没有写入。";
                    }

                    return;
                }

                if (options.Backwards.HasValue)
                {
                    comAttribute.Backward = options.Backwards.Value;
                }

                if (options.UpsideDown.HasValue)
                {
                    comAttribute.UpsideDown = options.UpsideDown.Value;
                }

                comAttribute.Update();
            }
            catch (System.Exception ex)
            {
                if (outcome.ComMirrorWarning == null)
                {
                    outcome.ComMirrorWarning = "写入「反向/倒置」失败：" + ex.Message;
                }
            }
        }
    }
}
