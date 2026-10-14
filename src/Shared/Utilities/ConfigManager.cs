using System;
using System.IO;

namespace ChemForge.Utilities
{
    /// <summary>
    /// 配置目录管理器 - 负责初始化配置目录并提供配置文件路径
    /// </summary>
    public static class ConfigManager
    {
        private static string _configDirectory;

        /// <summary>
        /// 初始化配置管理器
        /// </summary>
        /// <param name="configDirectory">配置文件所在目录</param>
        public static void Initialize(string configDirectory)
        {
            _configDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            if (!Directory.Exists(_configDirectory))
            {
                Directory.CreateDirectory(_configDirectory);
            }
        }

        /// <summary>
        /// 获取配置文件的完整路径
        /// </summary>
        /// <param name="fileName">配置文件名</param>
        /// <returns>配置文件完整路径</returns>
        public static string GetConfigFilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(_configDirectory))
                throw new InvalidOperationException("配置管理器尚未初始化。");

            return Path.Combine(_configDirectory, fileName);
        }
    }
}
