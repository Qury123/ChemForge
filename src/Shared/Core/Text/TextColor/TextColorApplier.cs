using System;
using Autodesk.AutoCAD.DatabaseServices;
using ChemForge.Core.Common;
using ChemForge.Models.Text.TextColor;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace ChemForge.Core.Text.TextColor
{
    /// <summary>
    /// 逐个对象地落实一次改色。
    ///
    /// 处理顺序对多行文字是有讲究的：必须先清内容里的颜色码，再写对象颜色。
    /// 反过来的话，写颜色时若触发了实体的整体回写，
    /// 刚清掉的颜色码会被早先读到的那份内容带回来。
    /// </summary>
    public sealed class TextColorApplier
    {
        private readonly TextColorOptions _options;

        /// <summary>改动过的对象，供调用方决定最后重画哪些位置。</summary>
        private readonly System.Collections.Generic.List<ObjectId> _touched =
            new System.Collections.Generic.List<ObjectId>();

        /// <summary>是否已经记录过颜色核对信息（只记第一个对象）。</summary>
        private bool _probeTaken;

        /// <summary>是否已经记录过内容样例（只记第一个真正被清理的对象）。</summary>
        private bool _sampleTaken;

        public TextColorApplier(TextColorOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            _options = options;
            _options.Normalize();
            Result = new TextColorResult();
        }

        /// <summary>累计结果。</summary>
        public TextColorResult Result { get; private set; }

        /// <summary>改动过的对象集合。</summary>
        public System.Collections.Generic.IList<ObjectId> TouchedIds
        {
            get { return _touched; }
        }

        /// <summary>
        /// 处理一个对象。不认识的对象类型直接忽略，不计入扫描数。
        /// </summary>
        /// <returns>该对象是否被改动过</returns>
        public bool Apply(Transaction transaction, ObjectId id)
        {
            if (transaction == null || id.IsNull || !id.IsValid)
            {
                return false;
            }

            Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
            if (entity == null)
            {
                return false;
            }

            MText mtext = entity as MText;
            if (mtext != null)
            {
                Result.ScannedCount++;
                if (!CanWrite(transaction, entity))
                {
                    return false;
                }

                return ApplyMText(transaction, mtext, id);
            }

            DBText singleLine = entity as DBText;
            if (singleLine != null)
            {
                if (!_options.IncludeSingleLineText)
                {
                    return false;
                }

                Result.ScannedCount++;
                if (!CanWrite(transaction, entity))
                {
                    return false;
                }

                // 单行文字的内容里不会有格式码，只改对象颜色
                WriteColor(singleLine);
                RecordColorProbe(singleLine);
                Result.ColorChangedCount++;
                Result.ChangedCount++;
                _touched.Add(id);
                return true;
            }

            MLeader leader = entity as MLeader;
            if (leader != null)
            {
                if (!_options.IncludeMultiLeader)
                {
                    return false;
                }

                Result.ScannedCount++;
                if (!CanWrite(transaction, entity))
                {
                    return false;
                }

                return ApplyMultiLeader(transaction, leader, id);
            }

            return false;
        }

        private bool ApplyMText(Transaction transaction, MText mtext, ObjectId id)
        {
            if (_options.StripColorCodes)
            {
                // 先读一次内容看有没有反斜杠；没有就完全不碰内容属性。
                // 这一步是性能关键：多行文字的内容访问比对象颜色贵得多。
                string original = ReadText(mtext);
                if (TextColorContent.HasBackslash(original))
                {
                    StripContent(
                        original,
                        delegate (string cleaned) { WriteText(transaction, mtext, cleaned); });
                }
            }

            WriteColor(mtext);
            RecordColorProbe(mtext);
            Result.ColorChangedCount++;
            Result.ChangedCount++;
            _touched.Add(id);
            return true;
        }

        private bool ApplyMultiLeader(Transaction transaction, MLeader leader, ObjectId id)
        {
            // 引线的文字是"内容块"，只有内容类型为多行文字时才有可改的文本。
            // 这里同样不做 DXF 预判，直接尝试清理，取不到就只改颜色。
            if (_options.StripColorCodes)
            {
                try
                {
                    MText content = leader.MText;
                    if (content != null)
                    {
                        string original = content.Contents ?? string.Empty;
                        if (TextColorContent.HasBackslash(original))
                        {
                            StripContent(
                                original,
                                delegate (string cleaned)
                                {
                                    content.UpgradeOpen();
                                    content.Contents = cleaned;
                                });
                        }
                    }
                }
                catch (System.Exception)
                {
                    // 引线内容不一定是可写的，拿不到就只改颜色，不影响整体结果
                    Result.FailedCount++;
                }
            }

            WriteColor(leader);
            RecordColorProbe(leader);
            Result.ColorChangedCount++;
            Result.ChangedCount++;
            _touched.Add(id);
            return true;
        }

        /// <summary>
        /// 共用的内容清理：含字段的文字只计数、不改写。
        /// </summary>
        private bool StripContent(string original, Action<string> write)
        {
            if (TextColorContent.ContainsField(original))
            {
                Result.FieldSkippedCount++;
                return false;
            }

            string cleaned = TextColorCodeStripper.Strip(original);
            if (string.Equals(cleaned, original, StringComparison.Ordinal))
            {
                return false;
            }

            write(cleaned);
            Result.CodeStrippedCount++;

            if (!_sampleTaken)
            {
                _sampleTaken = true;
                Result.BeforeSample = TextColorContent.ToSingleLine(original);
                Result.AfterSample = TextColorContent.ToSingleLine(cleaned);
            }

            return true;
        }

        /// <summary>
        /// 取多行文字的原始内容。
        ///
        /// 必须是 <c>Contents</c> 而不是 <c>Text</c>：<c>Text</c> 返回的是剥掉格式码之后的
        /// 纯文本（只读），颜色覆盖码在它里面根本看不到 —— 用它判断"有没有颜色码"
        /// 会永远得到"没有"，清理逻辑等于没写。
        /// </summary>
        private static string ReadText(MText mtext)
        {
            try
            {
                return mtext.Contents ?? string.Empty;
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }

        private static void WriteText(Transaction transaction, MText mtext, string text)
        {
            mtext.UpgradeOpen();
            mtext.Contents = text;
        }

        /// <summary>
        /// 先查后写：锁定图层上的对象一个属性都写不进去（<c>UpgradeOpen()</c> 抛 OnLockedLayer），
        /// 而 <see cref="TextColorEngine.Run"/> 把整批对象放在同一个事务里 ——
        /// 不拦下来，第一个锁定图层对象就会让前面改好的全部回滚，
        /// 用户看到的是"选了 100 个、什么都没发生"。
        ///
        /// 拦下来之后单独计数，由完成提示告诉用户跳过了多少个。
        /// 必须在写内容之前判断：多行文字的色码清理同样写不进锁定图层。
        /// </summary>
        private bool CanWrite(Transaction transaction, Entity entity)
        {
            if (!LayerLockGuard.IsOnLockedLayer(transaction, entity))
            {
                return true;
            }

            Result.LockedSkippedCount++;
            return false;
        }

        private void WriteColor(Entity entity)
        {
            entity.UpgradeOpen();

            switch (_options.Mode)
            {
                case TextColorMode.Aci:
                    entity.ColorIndex = _options.AciIndex;
                    break;
                case TextColorMode.TrueColor:
                    entity.Color = AcColor.FromRgb(
                        (byte)_options.Red,
                        (byte)_options.Green,
                        (byte)_options.Blue);
                    break;
                case TextColorMode.ByLayer:
                    entity.ColorIndex = AciColorNames.ByLayerValue;
                    break;
                default:
                    entity.ColorIndex = AciColorNames.ByBlockValue;
                    break;
            }
        }

        private void RecordColorProbe(Entity entity)
        {
            if (_probeTaken)
            {
                return;
            }

            try
            {
                string probe = "颜色组码 62=" + entity.ColorIndex;
                AcColor color = entity.Color;
                if (color != null && color.ColorMethod == AcColorMethod.ByColor)
                {
                    System.Drawing.Color rgb = color.ColorValue;
                    int packed = (rgb.R << 16) | (rgb.G << 8) | rgb.B;
                    probe += " / 420(真彩色)=" + packed;
                }
                else
                {
                    probe += " / 420(真彩色)=无(随层或索引色)";
                }

                Result.ColorProbe = probe;
                _probeTaken = true;
            }
            catch (System.Exception)
            {
                // 核对信息只是给用户看的辅助，取不到就算了
            }
        }
    }
}
