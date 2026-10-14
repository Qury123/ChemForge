using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using ChemForge.Core.Common;
using ChemForge.Models.Block.Numbering;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>属性服务：列属性清单、按 Tag 查找属性引用、判断图层锁定。</summary>
    public static class AttributeService
    {
        /// <summary>
        /// 列出块定义里全部属性定义（含常量/不可见），并补上实际存在的属性引用。
        /// 动态块的属性定义位于匿名块定义里，因此这里用 reference.Name 对应的 BlockTableRecord。
        /// </summary>
        public static List<AttributeDefInfo> GetAttributeDefinitions(BlockReference reference, Transaction tr)
        {
            List<AttributeDefInfo> result = new List<AttributeDefInfo>();
            if (reference == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            BlockTableRecord definition = null;
            try
            {
                BlockTable bt = (BlockTable)tr.GetObject(reference.Database.BlockTableId, OpenMode.ForRead);
                if (bt.Has(reference.Name))
                {
                    definition = tr.GetObject(bt[reference.Name], OpenMode.ForRead) as BlockTableRecord;
                }
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }

            if (definition != null)
            {
                foreach (ObjectId id in definition)
                {
                    AttributeDefinition attdef = null;
                    try
                    {
                        attdef = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                    }
                    catch (AcException)
                    {
                    }
                    if (attdef == null)
                    {
                        continue;
                    }

                    string tag = attdef.Tag ?? string.Empty;
                    if (tag.Length == 0 || !seen.Add(tag))
                    {
                        continue;
                    }

                    AttributeDefInfo info = new AttributeDefInfo();
                    info.Tag = tag;
                    info.Prompt = attdef.Prompt ?? string.Empty;
                    info.Constant = attdef.Constant;
                    info.Invisible = attdef.Invisible;
                    info.Preset = attdef.Preset;
                    result.Add(info);
                }
            }

            // 有些块（或某些可见性状态）实际有属性引用但定义里没读到，这里补齐
            foreach (ObjectId id in reference.AttributeCollection)
            {
                AttributeReference attref = null;
                try
                {
                    attref = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                }
                catch (AcException)
                {
                }
                if (attref == null)
                {
                    continue;
                }

                string tag = attref.Tag ?? string.Empty;
                if (tag.Length == 0 || !seen.Add(tag))
                {
                    continue;
                }

                AttributeDefInfo info = new AttributeDefInfo();
                info.Tag = tag;
                info.Constant = attref.IsConstant;
                info.Invisible = attref.Invisible;
                result.Add(info);
            }

            result.Sort(delegate(AttributeDefInfo a, AttributeDefInfo b)
            {
                return string.Compare(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        /// <summary>按块参照 ObjectId 列出属性定义。</summary>
        public static List<AttributeDefInfo> GetAttributeDefinitions(Database db, Transaction tr, ObjectId blockReferenceId)
        {
            BlockReference reference = null;
            try
            {
                reference = tr.GetObject(blockReferenceId, OpenMode.ForRead, false) as BlockReference;
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }

            if (reference == null)
            {
                return new List<AttributeDefInfo>();
            }
            return GetAttributeDefinitions(reference, tr);
        }

        /// <summary>按 Tag（大小写不敏感）查找块参照上的属性引用；找不到返回 null。</summary>
        public static AttributeReference FindAttribute(BlockReference reference, Transaction tr, string tag)
        {
            if (reference == null || string.IsNullOrEmpty(tag))
            {
                return null;
            }

            foreach (ObjectId id in reference.AttributeCollection)
            {
                AttributeReference attref = null;
                try
                {
                    attref = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                }
                catch (AcException)
                {
                }
                if (attref == null)
                {
                    continue;
                }

                if (string.Equals(attref.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return attref;
                }
            }

            return null;
        }

        /// <summary>
        /// 图层是否被锁定（锁定的图层上无法修改对象）。
        ///
        /// 实现已收敛到 <see cref="LayerLockGuard.IsLockedLayer"/> —— 同一个判断原本在
        /// 编号计划、TCOLOR、BLKATTCHANGE 三处各写了一遍，三处都是「批量共用同一事务」，
        /// 漏一处就是一个锁定图层对象让整批回滚。这里保留方法名，调用方（PlanBuilder）不动。
        /// </summary>
        public static bool IsLockedLayer(Database db, Transaction tr, string layerName)
        {
            return LayerLockGuard.IsLockedLayer(db, tr, layerName);
        }
    }
}
