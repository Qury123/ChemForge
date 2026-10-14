using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using ChemForge.Core.Block.Numbering;
using ChemForge.Models.Block.Numbering;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 计划服务：把「读属性 → 排序 → 编号」串起来。
    /// 预览和正式写入都走这里，保证预览看到的就是最终会写入的结果。
    /// </summary>
    public static class PlanBuilder
    {
        /// <summary>
        /// 完整构建：重新读取属性（图形可能已被改动）+ 排序 + 编号。
        /// 只读事务，不改动图形。
        /// </summary>
        /// <param name="wcsToSort">WCS → 排序坐标系的变换；WCS 模式传 Matrix3d.Identity。</param>
        public static NumberingPlan Build(
            Database db,
            List<BlockItem> candidates,
            NumberOptions options,
            Matrix3d wcsToSort)
        {
            if (candidates == null)
            {
                candidates = new List<BlockItem>();
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    ReadAttribute(db, tr, candidates[i], options);
                }
                // 只读事务，commit 让 AutoCAD 释放资源，不会产生数据库改动
                tr.Commit();
            }

            return SortAndNumber(candidates, options, wcsToSort);
        }

        /// <summary>
        /// 只重排 + 重新编号，不碰数据库。用于对话框里改排序参数时的即时预览。
        /// </summary>
        public static NumberingPlan SortAndNumber(
            List<BlockItem> candidates,
            NumberOptions options,
            Matrix3d wcsToSort)
        {
            NumberingPlan plan = new NumberingPlan();
            plan.Options = options;

            if (candidates == null)
            {
                return plan;
            }
            plan.Items = candidates;

            // ---- 准备排序点 ----
            int n = candidates.Count;
            List<SortPoint> points = new List<SortPoint>(n);
            List<double> heights = new List<double>(n);

            for (int i = 0; i < n; i++)
            {
                BlockItem item = candidates[i];
                Point3d p = PickSortPoint(item, options.Basis);
                if (options.Coord == CoordinateMode.CurrentUcs)
                {
                    p = p.TransformBy(wcsToSort);
                }
                item.SortX = p.X;
                item.SortY = p.Y;
                points.Add(new SortPoint(p.X, p.Y));
                if (item.Height > 0.0)
                {
                    heights.Add(item.Height);
                }
            }

            // ---- 行容差 ----
            bool autoTolerance = options.RowTolerance <= 0.0;
            double tolerance = autoTolerance ? ToleranceEstimator.Estimate(heights) : options.RowTolerance;
            if (autoTolerance && tolerance <= 0.0)
            {
                plan.Notes.Add("无法从块高度自动估算行容差（包围盒高度为 0），本次按“主轴值完全相等”分行。");
            }
            plan.ToleranceUsed = tolerance;
            plan.ToleranceAuto = autoTolerance;

            // ---- 排序 ----
            SortResult sorted = SortingService.Sort(
                points, tolerance, options.Axis, options.DescendingPrimary, options.DescendingInner);
            plan.RowCount = sorted.RowCount;

            // ---- 按最终顺序编号：跳过项不占用编号 ----
            int ordinal = 0;
            List<string> names = new List<string>();
            HashSet<string> nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int k = 0; k < sorted.Order.Length; k++)
            {
                int index = sorted.Order[k];
                BlockItem item = candidates[index];
                item.Row = sorted.RowIndex[index];

                if (item.CanWrite)
                {
                    ordinal++;
                    item.Ordinal = ordinal;
                    item.NewValue = NumberingService.FormatOrdinal(ordinal - 1, options);
                    plan.Writable.Add(item);
                }
                else
                {
                    item.Ordinal = 0;
                    item.NewValue = string.Empty;
                }

                if (nameSet.Add(item.EffectiveBlockName))
                {
                    names.Add(item.EffectiveBlockName);
                }
            }

            plan.WritableCount = ordinal;
            plan.SkippedCount = candidates.Count - ordinal;
            plan.BlockName = string.Join(" / ", names.ToArray());

            // ---- 把 Items 重排成最终阅读顺序 ----
            // 此前 Items 一直保持收集顺序（约等于图元创建顺序），只有 Ordinal/NewValue 是按
            // 排序结果赋值的，结果预览表格里序号列看起来乱序、不从小到大 —— 编号本身其实
            // 是对的，纯属显示顺序问题。这里按排序输出重排，跳过项保持在它们的几何位置。
            List<BlockItem> ordered = new List<BlockItem>(n);
            for (int k = 0; k < sorted.Order.Length; k++)
            {
                ordered.Add(candidates[sorted.Order[k]]);
            }
            plan.Items = ordered;

            return plan;
        }

        private static Point3d PickSortPoint(BlockItem item, SortBasis basis)
        {
            if (basis == SortBasis.BoundsCenter && item.HasBounds)
            {
                return item.BoundsCenter;
            }
            if (basis == SortBasis.AttributePosition && item.HasAttributePoint)
            {
                return item.AttributePoint;
            }
            return item.InsertPoint;
        }

        /// <summary>读取目标属性并判断是否可写。会被预览和写入各调用一次，保证结果与图形同步。</summary>
        private static void ReadAttribute(Database db, Transaction tr, BlockItem item, NumberOptions options)
        {
            item.Skip = SkipReason.None;
            item.AttributeId = ObjectId.Null;
            item.OldValue = string.Empty;
            item.HasAttributePoint = false;

            BlockReference reference = null;
            try
            {
                reference = tr.GetObject(item.Id, OpenMode.ForRead, false) as BlockReference;
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }

            if (reference == null)
            {
                item.Skip = SkipReason.BlockUnavailable;
                return;
            }

            AttributeReference attref = AttributeService.FindAttribute(reference, tr, options.Tag);
            if (attref == null)
            {
                item.Skip = SkipReason.NoAttribute;
                return;
            }
            if (attref.IsConstant)
            {
                item.Skip = SkipReason.ConstantAttribute;
                return;
            }
            if (attref.IsErased)
            {
                item.Skip = SkipReason.AttributeErased;
                return;
            }
            if (AttributeService.IsLockedLayer(db, tr, attref.Layer))
            {
                item.Skip = SkipReason.LockedLayer;
                return;
            }

            string text = attref.TextString ?? string.Empty;
            if (options.FillOnlyEmpty && text.Length > 0)
            {
                item.Skip = SkipReason.ValueNotEmpty;
                return;
            }

            item.AttributeId = attref.ObjectId;
            item.OldValue = text;

            try
            {
                // 属性位置是块定义坐标系下的坐标，乘 BlockTransform 换到 WCS
                item.AttributePoint = attref.Position.TransformBy(reference.BlockTransform);
                item.HasAttributePoint = true;
            }
            catch (AcException)
            {
                item.HasAttributePoint = false;
            }
            catch (Exception)
            {
                item.HasAttributePoint = false;
            }
        }
    }
}
