using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ChemForge.Models.Block.Numbering;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 选择服务：从选择集或参考块收集候选块，做匿名块/外部参照过滤，并缓存几何信息。
    /// </summary>
    public static class BlockSelector
    {
        /// <summary>匿名块名以 * 开头（例如 *U12、*D3、*P1）。</summary>
        public static bool IsAnonymousName(string name)
        {
            return !string.IsNullOrEmpty(name) && name[0] == '*';
        }

        /// <summary>
        /// 取块参照的“有效名”，等价于 ActiveX 的 EffectiveName。
        ///
        /// 注意：AutoCAD .NET API 的 BlockReference 上 **并没有** EffectiveName 属性
        /// （已对 acdbmgd.dll R25 验证），所以这里必须自己算：
        ///   1) 普通块：Name 就是块定义名；
        ///   2) 动态块：Name 往往是匿名名 *U##，需要用 DynamicBlockTableRecord
        ///      拿回原始块定义名；
        ///   3) 兜底：从匿名块定义的 XData(AcDbBlockRepBTag) 里读出原始定义的句柄再反查。
        /// </summary>
        public static string GetEffectiveName(BlockReference reference, Transaction tr)
        {
            if (reference == null)
            {
                return string.Empty;
            }

            string raw = reference.Name ?? string.Empty;
            if (!IsAnonymousName(raw))
            {
                return raw;
            }

            if (reference.IsDynamicBlock)
            {
                // 首选：动态块定义（原始块定义）
                try
                {
                    ObjectId id = reference.DynamicBlockTableRecord;
                    if (!id.IsNull)
                    {
                        BlockTableRecord btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                        if (btr != null && !string.IsNullOrEmpty(btr.Name) && !IsAnonymousName(btr.Name))
                        {
                            return btr.Name;
                        }
                    }
                }
                catch (AcException)
                {
                }
                catch (Exception)
                {
                }

                // 兜底：XData 反查原始定义句柄
                string viaXData = TryResolveOriginalNameByXData(reference, tr);
                if (!string.IsNullOrEmpty(viaXData))
                {
                    return viaXData;
                }
            }

            return raw;
        }

        /// <summary>
        /// 从选择集结果收集候选块。
        /// </summary>
        /// <param name="filterEffectiveName">只保留这个有效块名；null 表示不过滤。</param>
        public static List<BlockItem> Collect(
            Database db,
            Transaction tr,
            IEnumerable<ObjectId> ids,
            bool skipXref,
            string filterEffectiveName,
            CollectionStats stats)
        {
            List<BlockItem> items = new List<BlockItem>();
            if (stats == null)
            {
                stats = new CollectionStats();
            }

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId id in ids)
            {
                stats.Received++;

                BlockReference reference = null;
                try
                {
                    reference = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                }
                catch (AcException)
                {
                }
                catch (Exception)
                {
                }

                if (reference == null)
                {
                    stats.NonBlock++;
                    continue;
                }

                string effectiveName = GetEffectiveName(reference, tr);

                // 仍然是匿名名的，属于 *U/*D/*P 之类的内部块，排除
                if (IsAnonymousName(effectiveName))
                {
                    stats.Anonymous++;
                    continue;
                }

                BlockTableRecord definition = null;
                try
                {
                    if (bt.Has(reference.Name))
                    {
                        definition = tr.GetObject(bt[reference.Name], OpenMode.ForRead) as BlockTableRecord;
                    }
                }
                catch (AcException)
                {
                }

                if (skipXref && definition != null
                    && (definition.IsFromExternalReference || definition.IsFromOverlayReference))
                {
                    stats.Xref++;
                    continue;
                }

                if (!string.IsNullOrEmpty(filterEffectiveName)
                    && !string.Equals(effectiveName, filterEffectiveName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BlockItem item = new BlockItem();
                item.Id = reference.ObjectId;
                item.Handle = reference.Handle.ToString();
                item.RawBlockName = reference.Name;
                item.EffectiveBlockName = effectiveName;
                item.LayerName = reference.Layer;
                item.InsertPoint = reference.Position;
                item.AttributeId = ObjectId.Null;

                Point3d center;
                double height;
                if (TryGetExtents(reference, out center, out height))
                {
                    item.BoundsCenter = center;
                    item.Height = height;
                    item.HasBounds = true;
                }
                else
                {
                    item.BoundsCenter = reference.Position;
                    item.Height = 0.0;
                    item.HasBounds = false;
                }

                items.Add(item);
                stats.Kept++;
                stats.Names.Add(effectiveName);
            }

            return items;
        }

        /// <summary>
        /// 按“同名块”查找：遍历当前空间或整张图纸（模型空间 + 所有布局）的顶层块参照。
        /// 只处理顶层块参照，不递归进块定义内部。
        /// </summary>
        public static List<ObjectId> FindSameNameIds(
            Database db,
            Transaction tr,
            bool wholeDrawing)
        {
            List<ObjectId> ids = new List<ObjectId>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            if (wholeDrawing)
            {
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = null;
                    try
                    {
                        btr = tr.GetObject(btrId, OpenMode.ForRead) as BlockTableRecord;
                    }
                    catch (AcException)
                    {
                    }
                    if (btr == null || !btr.IsLayout)
                    {
                        continue;
                    }
                    AppendInsertIds(btr, ids);
                }
            }
            else
            {
                BlockTableRecord current = null;
                try
                {
                    current = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                }
                catch (AcException)
                {
                }
                if (current != null)
                {
                    AppendInsertIds(current, ids);
                }
            }

            return ids;
        }

        /// <summary>取块参照的包围盒中心与高度（WCS）。块没有有效图形时会抛异常，这里吞掉并返回 false。</summary>
        public static bool TryGetExtents(BlockReference reference, out Point3d center, out double height)
        {
            center = reference != null ? reference.Position : Point3d.Origin;
            height = 0.0;
            if (reference == null)
            {
                return false;
            }

            try
            {
                Extents3d extents = reference.GeometricExtents;
                center = new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
                height = Math.Abs(extents.MaxPoint.Y - extents.MinPoint.Y);
                return true;
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }

            return false;
        }

        private static void AppendInsertIds(BlockTableRecord btr, List<ObjectId> target)
        {
            foreach (ObjectId id in btr)
            {
                try
                {
                    // ObjectId.ObjectClass 不需要打开对象，可先做一次廉价过滤
                    Autodesk.AutoCAD.Runtime.RXClass cls = id.ObjectClass;
                    if (cls != null && string.Equals(cls.DxfName, "INSERT", StringComparison.OrdinalIgnoreCase))
                    {
                        target.Add(id);
                    }
                }
                catch (AcException)
                {
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool TryGetDefiningRecord(BlockReference reference, Transaction tr, out BlockTableRecord btr)
        {
            btr = null;
            try
            {
                BlockTable bt = (BlockTable)tr.GetObject(reference.Database.BlockTableId, OpenMode.ForRead);
                string name = reference.Name;
                if (string.IsNullOrEmpty(name) || !bt.Has(name))
                {
                    return false;
                }
                btr = tr.GetObject(bt[name], OpenMode.ForRead) as BlockTableRecord;
                return btr != null;
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// 兜底方案：匿名动态块定义的 XData 里有 AcDbBlockRepBTag 段，
        /// 其中记录着原始块定义的句柄（DXF 1005）。
        /// </summary>
        private static string TryResolveOriginalNameByXData(BlockReference reference, Transaction tr)
        {
            try
            {
                BlockTableRecord defining;
                if (!TryGetDefiningRecord(reference, tr, out defining))
                {
                    return null;
                }

                ResultBuffer xdata = defining.XData;
                if (xdata == null)
                {
                    return null;
                }

                TypedValue[] values = xdata.AsArray();
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i].TypeCode != (int)DxfCode.ExtendedDataRegAppName)
                    {
                        continue;
                    }
                    if (!string.Equals(values[i].Value as string, "AcDbBlockRepBTag", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    for (int j = i + 1; j < values.Length; j++)
                    {
                        if (values[j].TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                        {
                            break;
                        }
                        if (values[j].TypeCode != (int)DxfCode.ExtendedDataHandle)
                        {
                            continue;
                        }

                        string handleText = values[j].Value as string;
                        if (string.IsNullOrEmpty(handleText))
                        {
                            continue;
                        }

                        long raw = Convert.ToInt64(handleText, 16);
                        ObjectId id = reference.Database.GetObjectId(false, new Handle(raw), 0);
                        if (id.IsNull)
                        {
                            continue;
                        }

                        BlockTableRecord original = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                        if (original != null && !string.IsNullOrEmpty(original.Name) && !IsAnonymousName(original.Name))
                        {
                            return original.Name;
                        }
                    }
                }
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }

            return null;
        }
    }
}
