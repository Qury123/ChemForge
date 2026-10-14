using System;
using Autodesk.AutoCAD.DatabaseServices;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace ChemForge.Core.Common
{
    /// <summary>
    /// 图层锁定判断 —— 批量命令的「先查后写」闸门。
    ///
    /// 为什么必须有它：AutoCAD 不允许修改锁定图层上的对象。对这类对象调用
    /// <c>UpgradeOpen()</c>（或以 <c>OpenMode.ForWrite</c> 打开）会抛
    /// <c>Autodesk.AutoCAD.Runtime.Exception</c>，其 ErrorStatus 为 OnLockedLayer。
    /// 而批量命令把整批对象放在**同一个事务**里，一个锁定图层对象就能让整批改动
    /// 一起回滚 —— 用户看到的是「选了 100 个，结果什么都没发生」，且没有任何提示。
    ///
    /// 所以批量流程一律先查后写：命中锁定图层就跳过该对象并单独计数，
    /// 在完成提示里明确写出「跳过 N 处（锁定图层）」，而不是整批静默失败。
    ///
    /// 使用者：Core\Block\Numbering\AttributeService（编号计划）、
    /// Core\Text\TextColor\TextColorApplier（TCOLOR）、
    /// Core\Block\BlockAttr\BatchAttrChangeWriter（BLKATTCHANGE）。
    /// </summary>
    public static class LayerLockGuard
    {
        /// <summary>
        /// 指定名字的图层是否被锁定。
        ///
        /// 图层不存在、或读图层表失败时返回 false —— 判断不出来就当作没锁，
        /// 让真正的写入去暴露问题。反过来（查不到就跳过）会变成更难查的静默丢数据。
        /// </summary>
        public static bool IsLockedLayer(Database database, Transaction transaction, string layerName)
        {
            if (database == null || transaction == null || string.IsNullOrEmpty(layerName))
            {
                return false;
            }

            try
            {
                LayerTable table = transaction.GetObject(database.LayerTableId, OpenMode.ForRead) as LayerTable;
                if (table == null || !table.Has(layerName))
                {
                    return false;
                }

                LayerTableRecord record = transaction.GetObject(table[layerName], OpenMode.ForRead) as LayerTableRecord;
                return record != null && record.IsLocked;
            }
            catch (AcException)
            {
            }
            catch (Exception)
            {
            }

            return false;
        }

        /// <summary>实体所在图层是否被锁定。</summary>
        public static bool IsOnLockedLayer(Transaction transaction, Entity entity)
        {
            if (transaction == null || entity == null)
            {
                return false;
            }

            try
            {
                return IsLockedLayer(entity.Database, transaction, entity.Layer);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
