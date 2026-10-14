using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace ChemForge.Core.Block.BlockAttr
{
    /// <summary>
    /// 「有效块名」解析器。
    ///
    /// ---- 为什么不能直接用块名 ----
    /// 动态块只要被改过参数，AutoCAD 就会为它生成一个**匿名块定义**（形如 <c>*U12</c>），
    /// 并让 INSERT 转而引用那个匿名定义。此时 <c>BlockTableRecord.Name</c> 与
    /// <c>BlockReference.Name</c> 都返回 <c>*U12</c>，而用户在「特性」面板、
    /// 增强属性编辑器里看到的是原始块名。所以这里优先取 ActiveX 的
    /// <c>EffectiveName</c>，失败才回退 DXF 组码 2。
    ///
    /// ---- C# 侧怎么算 ----
    /// AutoCAD 托管 API 的 <c>BlockReference</c> **没有** EffectiveName 属性
    /// （2020 与 2025 两代 SDK 实测均缺失），所以按下面的顺序自己算，
    /// 任一步拿到「非匿名」的名字就返回：
    ///   1) <c>BlockReference.DynamicBlockTableRecord</c> —— 动态块引用指向的原始块定义，
    ///      最直接也最快的一步；
    ///   2) 匿名块定义自身的 XData <c>AcDbBlockRepBTag</c> 段里记着原始块定义的句柄
    ///      （DXF 1005），按句柄反查；
    ///   3) 构造期遍历块表建立的「匿名块定义 → 动态块定义名」映射；
    ///   4) 最后才走 ActiveX 的 <c>EffectiveName</c>。
    ///
    /// 第 2 步与 <c>ChemForge.Core.Block.Numbering.BlockSelector.GetEffectiveName</c> 同源
    /// —— 那一份是 BLKNUM 实机验证过的实现。这里没有直接复用它，是因为两者服务对象不同
    /// （那边服务于批量编号的选块，本文件服务于属性批量替换）；两处算法若日后分叉，以本文件为准。
    /// </summary>
    public sealed class BlockEffectiveNameResolver
    {
        /// <summary>匿名动态块定义的 XData 段名，其中记录原始块定义的句柄。</summary>
        private const string BlockRepresentationTag = "AcDbBlockRepBTag";

        private readonly Dictionary<ObjectId, string> _anonymousToDefinition = new Dictionary<ObjectId, string>();

        /// <summary>块名以 <c>*</c> 开头即为匿名块（<c>*U12</c> 动态块、<c>*D3</c> 标注、<c>*X5</c> 填充…）。</summary>
        /// <param name="name">块名</param>
        /// <returns>是匿名块名时为 true</returns>
        public static bool IsAnonymousName(string name)
        {
            return !string.IsNullOrEmpty(name) && name[0] == '*';
        }

        /// <summary>
        /// 基于当前块表建立「匿名块定义 → 动态块定义名」映射。
        /// 一次采集建一次即可，别在循环里反复建。
        /// </summary>
        /// <param name="blockTable">当前数据库的块表</param>
        /// <param name="transaction">块表所在的事务</param>
        public BlockEffectiveNameResolver(BlockTable blockTable, Transaction transaction)
        {
            if (blockTable == null || transaction == null)
            {
                return;
            }

            foreach (ObjectId recordId in blockTable)
            {
                BlockTableRecord record = transaction.GetObject(recordId, OpenMode.ForRead, false) as BlockTableRecord;
                if (record == null)
                {
                    continue;
                }

                // 只关心「动态块的定义本身」：非匿名 + IsDynamicBlock
                if (!record.IsDynamicBlock || record.IsAnonymous)
                {
                    continue;
                }

                ObjectIdCollection anonymousIds = null;
                try
                {
                    anonymousIds = record.GetAnonymousBlockIds();
                }
                catch (System.Exception)
                {
                    // 个别记录（例如来自外部参照的块表记录）可能取不到，跳过即可
                    continue;
                }

                if (anonymousIds == null)
                {
                    continue;
                }

                foreach (ObjectId anonymousId in anonymousIds)
                {
                    if (!_anonymousToDefinition.ContainsKey(anonymousId))
                    {
                        _anonymousToDefinition.Add(anonymousId, record.Name);
                    }
                }
            }
        }

        /// <summary>
        /// 取块表记录的有效块名（用户视角的块名）。
        /// </summary>
        /// <param name="blockRecord">块表记录（可以是匿名块定义）</param>
        /// <param name="transaction">块表记录所在的事务；为 null 时跳过需要开对象的两步</param>
        /// <returns>有效块名</returns>
        public string GetEffectiveName(BlockTableRecord blockRecord, Transaction transaction)
        {
            if (blockRecord == null)
            {
                return string.Empty;
            }

            string raw = blockRecord.Name ?? string.Empty;
            if (!IsAnonymousName(raw))
            {
                return raw;
            }

            // ① 匿名块定义自身的 XData 里记着原始块定义的句柄
            string viaXData = TryResolveOriginalNameByXData(blockRecord, transaction);
            if (!string.IsNullOrEmpty(viaXData))
            {
                return viaXData;
            }

            // ② 构造期建立的映射表。
            //    这里刻意不再要求 blockRecord.IsDynamicBlock —— 匿名动态块定义上这个属性
            //    并不可靠，之前正是这道多余的判断让 *U 名字直接漏给了用户。
            string definitionName;
            if (_anonymousToDefinition.TryGetValue(blockRecord.ObjectId, out definitionName)
                && !string.IsNullOrEmpty(definitionName))
            {
                return definitionName;
            }

            return raw;
        }

        /// <summary>
        /// 取块引用的有效块名（用户视角的块名）。
        /// </summary>
        /// <param name="blockReference">块引用</param>
        /// <param name="transaction">事务</param>
        /// <returns>有效块名；全部手段都失败时回退 <c>BlockReference.Name</c></returns>
        public string GetEffectiveName(BlockReference blockReference, Transaction transaction)
        {
            if (blockReference == null)
            {
                return string.Empty;
            }

            string raw = blockReference.Name ?? string.Empty;
            if (!IsAnonymousName(raw))
            {
                return raw;
            }

            // ① 动态块引用 → 原始块定义（托管 API 里最直接的一步）
            try
            {
                if (blockReference.IsDynamicBlock)
                {
                    ObjectId definitionId = blockReference.DynamicBlockTableRecord;
                    if (!definitionId.IsNull && transaction != null)
                    {
                        BlockTableRecord definition =
                            transaction.GetObject(definitionId, OpenMode.ForRead, false) as BlockTableRecord;
                        if (definition != null
                            && !string.IsNullOrEmpty(definition.Name)
                            && !IsAnonymousName(definition.Name))
                        {
                            return definition.Name;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
            }

            // ② 经它指向的块表记录继续解析（XData 反查 + 映射表）
            try
            {
                if (transaction != null)
                {
                    BlockTableRecord record =
                        transaction.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead, false) as BlockTableRecord;
                    if (record != null)
                    {
                        string name = GetEffectiveName(record, transaction);
                        if (!string.IsNullOrEmpty(name) && !IsAnonymousName(name))
                        {
                            return name;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
            }

            // ③ 兜底：ActiveX 的 EffectiveName
            try
            {
                Autodesk.AutoCAD.Interop.Common.AcadBlockReference comReference =
                    blockReference.AcadObject as Autodesk.AutoCAD.Interop.Common.AcadBlockReference;
                if (comReference != null)
                {
                    string effective = comReference.EffectiveName;
                    if (!string.IsNullOrEmpty(effective) && !IsAnonymousName(effective))
                    {
                        return effective;
                    }
                }
            }
            catch (System.Exception)
            {
            }

            return raw;
        }

        /// <summary>
        /// 匿名动态块定义的 XData 里有 <c>AcDbBlockRepBTag</c> 段，
        /// 其中记录着原始块定义的句柄（DXF 1005）。
        /// </summary>
        /// <param name="blockRecord">匿名块定义</param>
        /// <param name="transaction">事务</param>
        /// <returns>原始块名；取不到时返回 null</returns>
        private static string TryResolveOriginalNameByXData(BlockTableRecord blockRecord, Transaction transaction)
        {
            if (transaction == null)
            {
                return null;
            }

            try
            {
                ResultBuffer xdata = blockRecord.XData;
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
                    if (!string.Equals(values[i].Value as string, BlockRepresentationTag, StringComparison.Ordinal))
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

                        long rawHandle = Convert.ToInt64(handleText, 16);
                        ObjectId id = blockRecord.Database.GetObjectId(false, new Handle(rawHandle), 0);
                        if (id.IsNull)
                        {
                            continue;
                        }

                        BlockTableRecord original = transaction.GetObject(id, OpenMode.ForRead, false) as BlockTableRecord;
                        if (original != null
                            && !string.IsNullOrEmpty(original.Name)
                            && !IsAnonymousName(original.Name))
                        {
                            return original.Name;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
            }

            return null;
        }
    }
}
