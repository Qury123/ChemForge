using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using ChemForge.Models.Block.Numbering;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>
    /// 写入服务：在一个事务里批量写入属性值。
    ///
    /// 撤销行为：整个操作由一个命令 + 一个事务完成，AutoCAD 会把命令执行期间的改动
    /// 归并成一次撤销记录，用户在命令行输入一次 U 即可整体撤销。
    /// </summary>
    public static class AttributeWriter
    {
        public static WriteResult Write(Database db, List<BlockItem> items)
        {
            WriteResult result = new WriteResult();
            if (items == null || items.Count == 0)
            {
                return result;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < items.Count; i++)
                {
                    BlockItem item = items[i];

                    if (!item.CanWrite)
                    {
                        result.CountSkip(item.Skip);
                        continue;
                    }
                    if (item.AttributeId.IsNull)
                    {
                        result.CountSkip(SkipReason.NoAttribute);
                        continue;
                    }

                    AttributeReference attref;
                    try
                    {
                        attref = tr.GetObject(item.AttributeId, OpenMode.ForWrite, false) as AttributeReference;
                    }
                    catch (AcException ex)
                    {
                        result.Failed++;
                        result.Messages.Add(string.Format("块 {0}：打开属性失败 ({1})", item.Handle, ex.ErrorStatus));
                        continue;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Messages.Add(string.Format("块 {0}：打开属性失败 ({1})", item.Handle, ex.Message));
                        continue;
                    }

                    if (attref == null)
                    {
                        result.Failed++;
                        result.Messages.Add(string.Format("块 {0}：属性对象不存在", item.Handle));
                        continue;
                    }
                    if (attref.IsErased)
                    {
                        result.CountSkip(SkipReason.AttributeErased);
                        continue;
                    }

                    try
                    {
                        attref.TextString = item.NewValue;
                        result.Written++;
                        result.WrittenBlockIds.Add(item.Id);
                    }
                    catch (AcException ex)
                    {
                        result.Failed++;
                        result.Messages.Add(string.Format("块 {0}：写入失败 ({1})", item.Handle, ex.ErrorStatus));
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        result.Messages.Add(string.Format("块 {0}：写入失败 ({1})", item.Handle, ex.Message));
                    }
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>
        /// 把一组块参照设为**当前选择**（`SetImpliedSelection`）。
        ///
        /// AutoCAD 里"被选中"就等于"高亮显示"，所以这就是用户要的"写入后让这些块保持选中"：
        /// 命令结束后夹点还在、状态栏显示选中数量，用户可以直接接着做别的操作。
        ///
        /// 注意：这是**会话级预选集**，不会自己消失。本项目在写入后设置它作为本次命令的
        /// 收尾状态（下次执行 BLKNUM 时 `ReadImpliedSelection` 会把它读走并清除，
        /// 所以不会被误当成"用户预先选好的块"）。
        /// 传 null / 空集合表示清空选择。
        /// </summary>
        public static void Select(Editor editor, ObjectIdCollection ids)
        {
            if (editor == null)
            {
                return;
            }

            if (ids == null || ids.Count == 0)
            {
                editor.SetImpliedSelection(new ObjectId[0]);
                return;
            }

            ObjectId[] array = new ObjectId[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                array[i] = ids[i];
            }
            editor.SetImpliedSelection(array);
        }

        public static void Select(Editor editor, List<BlockItem> items)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ids.Add(items[i].Id);
                }
            }
            Select(editor, ids);
        }
    }
}
