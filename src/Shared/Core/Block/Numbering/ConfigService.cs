using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ChemForge.Core.Block.Numbering;
using ChemForge.Utilities;

namespace ChemForge.Core.Block.Numbering
{
    /// <summary>块名 → 上次使用的属性 Tag，下次自动带出来。</summary>
    [Serializable]
    public class TagMemory
    {
        public string BlockName { get; set; }
        public string Tag { get; set; }

        public TagMemory()
        {
            BlockName = string.Empty;
            Tag = string.Empty;
        }
    }

    /// <summary>插件配置。</summary>
    [Serializable]
    public class AppConfig
    {
        public NumberOptions Options { get; set; }

        public List<TagMemory> TagByBlock { get; set; }

        public AppConfig()
        {
            Options = new NumberOptions();
            TagByBlock = new List<TagMemory>();
        }

        public string FindTag(string blockName)
        {
            if (string.IsNullOrEmpty(blockName) || TagByBlock == null)
            {
                return string.Empty;
            }
            for (int i = 0; i < TagByBlock.Count; i++)
            {
                if (string.Equals(TagByBlock[i].BlockName, blockName, StringComparison.OrdinalIgnoreCase))
                {
                    return TagByBlock[i].Tag ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public void RememberTag(string blockName, string tag)
        {
            if (string.IsNullOrEmpty(blockName) || string.IsNullOrEmpty(tag))
            {
                return;
            }
            if (TagByBlock == null)
            {
                TagByBlock = new List<TagMemory>();
            }
            for (int i = 0; i < TagByBlock.Count; i++)
            {
                if (string.Equals(TagByBlock[i].BlockName, blockName, StringComparison.OrdinalIgnoreCase))
                {
                    TagByBlock[i].Tag = tag;
                    return;
                }
            }
            TagMemory memory = new TagMemory();
            memory.BlockName = blockName;
            memory.Tag = tag;
            TagByBlock.Add(memory);
        }
    }

    /// <summary>
    /// 配置服务：把参数存到插件统一的运行时目录（ConfigManager 指向的位置 —— 开发期是仓库的
    /// runtime\，安装后是 %LocalAppData%\ChemForge）。
    /// 读写失败一律静默降级到默认值，不能让配置问题影响主流程。
    /// </summary>
    public static class ConfigService
    {
        private const string FileName = "BlockNumbering.xml";

        private static readonly object Sync = new object();

        public static string ConfigPath
        {
            get { return ConfigManager.GetConfigFilePath(FileName); }
        }

        public static string ConfigDirectory
        {
            get { return Path.GetDirectoryName(ConfigPath); }
        }

        /// <summary>
        /// 历史路径 %APPDATA%\BlockNumbering\BlockNumbering.xml：旧版本把配置写死在漫游
        /// AppData 下一个与插件无关的目录里。现在只读它一次用于迁移，不再写入。
        /// </summary>
        private static string LegacyConfigPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(appData, "BlockNumbering"), FileName);
            }
        }

        public static AppConfig Load()
        {
            lock (Sync)
            {
                try
                {
                    string path = ConfigPath;
                    bool fromLegacyPath = false;

                    if (!File.Exists(path))
                    {
                        string legacy = LegacyConfigPath;
                        if (!File.Exists(legacy))
                        {
                            return new AppConfig();
                        }

                        // 老用户升级：先读老路径那份，下面顺手写进新路径完成迁移。
                        path = legacy;
                        fromLegacyPath = true;
                    }

                    AppConfig config = ReadFrom(path);
                    if (config == null)
                    {
                        return new AppConfig();
                    }

                    if (fromLegacyPath)
                    {
                        Save(config);
                    }

                    return config;
                }
                catch (Exception)
                {
                    return new AppConfig();
                }
            }
        }

        /// <summary>从指定文件反序列化；内容不可解析时返回 null，由调用方决定怎么回退。</summary>
        private static AppConfig ReadFrom(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                AppConfig config = serializer.Deserialize(stream) as AppConfig;
                if (config == null)
                {
                    return null;
                }
                if (config.Options == null)
                {
                    config.Options = new NumberOptions();
                }
                if (config.TagByBlock == null)
                {
                    config.TagByBlock = new List<TagMemory>();
                }
                return config;
            }
        }

        public static void Save(AppConfig config)
        {
            if (config == null)
            {
                return;
            }

            lock (Sync)
            {
                try
                {
                    string directory = ConfigDirectory;
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    using (FileStream stream = File.Create(ConfigPath))
                    {
                        XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                        serializer.Serialize(stream, config);
                    }
                }
                catch (Exception)
                {
                    // 配置写不进去不影响本次操作
                }
            }
        }
    }
}
