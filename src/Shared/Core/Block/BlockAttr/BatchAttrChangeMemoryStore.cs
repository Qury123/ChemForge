using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using ChemForge.Models.Block.BlockAttr;
using ChemForge.Utilities;

namespace ChemForge.Core.Block.BlockAttr
{
    /// <summary>
    /// 「上次确定时填写的值」的持久化。
    ///
    /// 存放位置：<c>ConfigManager</c> 提供的配置目录（开发环境是仓库根的
    /// <c>runtime\</c>，正式安装是 <c>%LocalAppData%\ChemForge</c>），文件名
    /// <c>BatchAttrChange.xml</c>。
    ///
    /// **旧名迁移**：命令更名之前该文件叫 <c>AttributeReplace.xml</c>、
    /// 根节点也叫 <c>AttributeReplace</c>。2026-10-12 随命令一起改名（用户要求
    /// "所有的内容都修改，包括文件名"），为不让用户已经记住的那一套值凭空消失，
    /// <see cref="Load"/> 在新文件不存在而旧文件存在时会读取旧文件，随后把它写成新
    /// 文件名、并把旧文件改名为 <c>AttributeReplace.xml.migrated</c>（保留备份，不删）。
    ///
    /// 记忆写进插件自己的配置文件，而不是 AutoCAD 的用户配置（注册表）
    /// 或环境变量：既能跨图纸跨启动，又不占用
    /// AutoCAD 的环境变量命名空间，也不会和别的插件撞名。
    ///
    /// 读写全部 fail-soft：配置目录未初始化、文件损坏、磁盘只读……
    /// 任何一步失败都只是「这次没有记忆」，绝不能因此让命令报错。
    /// </summary>
    public static class BatchAttrChangeMemoryStore
    {
        /// <summary>配置文件名</summary>
        public const string FileName = "BatchAttrChange.xml";

        /// <summary>根节点名</summary>
        private const string RootName = "BatchAttrChange";

        /// <summary>改名前的配置文件名（命令更名前用的）</summary>
        private const string LegacyFileName = "AttributeReplace.xml";

        /// <summary>改名前的根节点名</summary>
        private const string LegacyRootName = "AttributeReplace";

        /// <summary>
        /// 读取记忆；任何异常都返回一个空记忆（不抛）。
        /// 新文件不存在而旧文件存在时，自动从旧文件迁移（见类型注释）。
        /// </summary>
        /// <returns>记忆体，永不为 null</returns>
        public static BatchAttrChangeMemory Load()
        {
            BatchAttrChangeMemory memory = new BatchAttrChangeMemory();

            try
            {
                string path = ConfigManager.GetConfigFilePath(FileName);
                string legacyPath = ConfigManager.GetConfigFilePath(LegacyFileName);
                bool migrating = false;
                if (!File.Exists(path))
                {
                    if (!File.Exists(legacyPath))
                    {
                        return memory;
                    }

                    path = legacyPath;
                    migrating = true;
                }

                XDocument doc = XDocument.Load(path);
                XElement root = doc.Root;
                if (root == null
                    || (root.Name.LocalName != RootName && root.Name.LocalName != LegacyRootName))
                {
                    return memory;
                }

                XElement tags = root.Element("Tags");
                if (tags != null)
                {
                    List<string> list = new List<string>();
                    foreach (XElement tag in tags.Elements("Tag"))
                    {
                        string value = (tag.Value ?? string.Empty).Trim();
                        if (value.Length > 0)
                        {
                            list.Add(value);
                        }
                    }

                    memory.Tags = list;
                }

                memory.Content = ReadString(root, "Content", string.Empty);
                memory.Height = ReadString(root, "Height", string.Empty);
                memory.WidthFactor = ReadString(root, "WidthFactor", string.Empty);
                memory.Rotation = ReadString(root, "Rotation", string.Empty);
                memory.Oblique = ReadString(root, "Oblique", string.Empty);
                memory.ColorNumber = ReadString(root, "ColorNumber", string.Empty);
                memory.StyleName = ReadString(root, "StyleName", string.Empty);
                memory.LayerName = ReadString(root, "LayerName", string.Empty);
                memory.LinetypeName = ReadString(root, "LinetypeName", string.Empty);

                memory.JustifyIndex = ReadInt(root, "JustifyIndex");
                memory.BackwardsIndex = ReadInt(root, "BackwardsIndex");
                memory.UpsideDownIndex = ReadInt(root, "UpsideDownIndex");
                memory.ColorIndex = ReadInt(root, "ColorIndex");
                memory.LineWeightIndex = ReadInt(root, "LineWeightIndex");

                if (migrating)
                {
                    MigrateLegacy(legacyPath, memory);
                }
            }
            catch (Exception ex)
            {
                Logger.Info("读取 BatchAttrChange.xml 失败，本次不启用记忆：" + ex.Message);
            }

            return memory;
        }

        /// <summary>
        /// 把从旧文件读到的记忆写到新文件名下，并把旧文件改名为
        /// <c>AttributeReplace.xml.migrated</c>（保留备份，不删用户数据）。
        /// 任何一步失败都只记日志 —— 本次已经读到的记忆照样返回。
        /// </summary>
        /// <param name="legacyPath">旧文件路径</param>
        /// <param name="memory">刚从旧文件读出的记忆</param>
        private static void MigrateLegacy(string legacyPath, BatchAttrChangeMemory memory)
        {
            try
            {
                Save(memory);

                string backup = legacyPath + ".migrated";
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(legacyPath, backup);
                Logger.Info("旧的 AttributeReplace.xml 已迁移为 BatchAttrChange.xml（旧文件改名为 AttributeReplace.xml.migrated 留底）。");
            }
            catch (Exception ex)
            {
                Logger.Info("迁移旧的 AttributeReplace.xml 失败（不影响本次读到的记忆）：" + ex.Message);
            }
        }

        /// <summary>
        /// 保存记忆；任何异常都被吞掉（记忆是锦上添花，不该影响主流程）。
        /// </summary>
        /// <param name="memory">要保存的记忆体</param>
        public static void Save(BatchAttrChangeMemory memory)
        {
            if (memory == null)
            {
                return;
            }

            try
            {
                XElement root = new XElement(RootName);

                XElement tags = new XElement("Tags");
                if (memory.Tags != null)
                {
                    foreach (string tag in memory.Tags)
                    {
                        if (!string.IsNullOrEmpty(tag))
                        {
                            tags.Add(new XElement("Tag", tag));
                        }
                    }
                }

                root.Add(tags);
                root.Add(new XElement("Content", memory.Content ?? string.Empty));
                root.Add(new XElement("Height", memory.Height ?? string.Empty));
                root.Add(new XElement("WidthFactor", memory.WidthFactor ?? string.Empty));
                root.Add(new XElement("Rotation", memory.Rotation ?? string.Empty));
                root.Add(new XElement("Oblique", memory.Oblique ?? string.Empty));
                root.Add(new XElement("ColorNumber", memory.ColorNumber ?? string.Empty));
                root.Add(new XElement("StyleName", memory.StyleName ?? string.Empty));
                root.Add(new XElement("LayerName", memory.LayerName ?? string.Empty));
                root.Add(new XElement("LinetypeName", memory.LinetypeName ?? string.Empty));
                root.Add(new XElement("JustifyIndex", memory.JustifyIndex.ToString(CultureInfo.InvariantCulture)));
                root.Add(new XElement("BackwardsIndex", memory.BackwardsIndex.ToString(CultureInfo.InvariantCulture)));
                root.Add(new XElement("UpsideDownIndex", memory.UpsideDownIndex.ToString(CultureInfo.InvariantCulture)));
                root.Add(new XElement("ColorIndex", memory.ColorIndex.ToString(CultureInfo.InvariantCulture)));
                root.Add(new XElement("LineWeightIndex", memory.LineWeightIndex.ToString(CultureInfo.InvariantCulture)));

                string path = ConfigManager.GetConfigFilePath(FileName);
                new XDocument(root).Save(path);
            }
            catch (Exception ex)
            {
                Logger.Info("保存 BatchAttrChange.xml 失败，本次不记录记忆：" + ex.Message);
            }
        }

        private static string ReadString(XElement root, string name, string fallback)
        {
            XElement element = root.Element(name);
            return element == null ? fallback : (element.Value ?? fallback);
        }

        private static int ReadInt(XElement root, string name)
        {
            XElement element = root.Element(name);
            if (element == null)
            {
                return 0;
            }

            int value;
            return int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : 0;
        }
    }
}
