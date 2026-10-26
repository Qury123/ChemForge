#if NET48 || NET80
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ChemForge.Utilities;

namespace ChemForge.Core.Pipe
{
    /// <summary>
    /// PIPEBREAK / INSTBREAK 的跨图纸记忆：上次的缺口/架桥总宽度、上次点选过的管线图层、
    /// 上次点选过的仪表线图层（INSTBREAK 专用）。宽度与管线图层由两条命令共用。
    ///
    /// 历史做法是把这两项塞进 AutoCAD 系统变量 USERR1 / USERS1。问题在于这两个变量属于
    /// <b>图纸</b>而不属于用户：值随 DWG 一起保存，换一张图记忆就变了；而且为了记住插件
    /// 自己的一个输入，往用户的每一张图纸里都写进了自定义数据——图纸被 DXF 交换、被同事
    /// 打开时一并带走，用户既不知情也无从清理。
    ///
    /// 改为写插件自己的配置文件，落在 ConfigManager 指向的运行时目录（开发期是仓库的
    /// runtime\，安装后是 %LocalAppData%\ChemForge）。读失败一律回退到调用方给的默认值、
    /// 写失败只记日志：设置丢了不能影响命令本身能不能跑。
    /// </summary>
    internal static class PipeBreakSettings
    {
        private const string FileName = "PipeBreak.settings";
        private const string WidthKey = "LastWidth";
        private const string SCurveWidthKey = "LastSCurveWidth";
        private const string ModeKey = "LastMode";
        private const string GapDirectionKey = "LastGapDirection";
        private const string BridgeSideKey = "LastBridgeSide";
        private const string LayerKey = "LastPipeLayer";
        private const string InstrumentLayerKey = "LastInstrumentLayer";

        /// <summary>读取上次的总宽度；没存过或读不出来时返回 <paramref name="fallback"/>。</summary>
        public static double GetLastWidth(double fallback)
        {
            string raw;
            if (!Load().TryGetValue(WidthKey, out raw))
            {
                return fallback;
            }

            double value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            // 沿用改前的判定：0 或负数视为「没记过」，退回默认值。
            return Math.Abs(value) > 1e-9 ? value : fallback;
        }

        public static void SetLastWidth(double value)
        {
            Save(WidthKey, value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 读取用户上次**手动输入**的 S 型符号宽度；没存过（一直用默认值）时返回 <paramref name="fallback"/>。
        /// 只记用户自己给的数，跟缺口宽度相同的默认值不入库，所以这里读不到就说明用户从没改过。
        /// </summary>
        public static double GetLastSCurveWidth(double fallback)
        {
            string raw;
            if (!Load().TryGetValue(SCurveWidthKey, out raw))
            {
                return fallback;
            }

            double value;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return fallback;
            }

            return value > 1e-9 ? value : fallback;
        }

        public static void SetLastSCurveWidth(double value)
        {
            Save(SCurveWidthKey, value.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>清掉记忆的 S 型符号宽度（用户改回「跟缺口宽度一致」时调用）。</summary>
        public static void ClearLastSCurveWidth()
        {
            Save(SCurveWidthKey, string.Empty, removeKey: true);
        }

        /// <summary>
        /// 读取上次选过的打断模式关键字（D = 缺口 / H = 架桥 / S = S 型）；
        /// 没存过或存了不认识的值时返回 <paramref name="fallback"/>。
        /// </summary>
        public static string GetLastMode(string fallback)
        {
            return ReadKeyword(ModeKey, fallback, "D", "H", "S");
        }

        public static void SetLastMode(string value)
        {
            string normalized = NormalizeKeyword(value, "D", "H", "S");
            if (normalized != null) Save(ModeKey, normalized);
        }

        /// <summary>读取上次选过的缺口方向关键字（V = 垂直 / H = 水平）；没存过时返回 <paramref name="fallback"/>。</summary>
        public static string GetLastGapDirection(string fallback)
        {
            return ReadKeyword(GapDirectionKey, fallback, "V", "H");
        }

        public static void SetLastGapDirection(string value)
        {
            string normalized = NormalizeKeyword(value, "V", "H");
            if (normalized != null) Save(GapDirectionKey, normalized);
        }

        /// <summary>读取上次选过的架桥半圆凸起方向（U / D / L / R）；没存过时返回 <paramref name="fallback"/>。</summary>
        public static string GetLastBridgeSide(string fallback)
        {
            return ReadKeyword(BridgeSideKey, fallback, "U", "D", "L", "R");
        }

        public static void SetLastBridgeSide(string value)
        {
            string normalized = NormalizeKeyword(value, "U", "D", "L", "R");
            if (normalized != null) Save(BridgeSideKey, normalized);
        }

        /// <summary>读一个只允许若干大写关键字的值；不合法一律退回默认值。</summary>
        private static string ReadKeyword(string key, string fallback, params string[] allowed)
        {
            string raw;
            if (!Load().TryGetValue(key, out raw))
            {
                return fallback;
            }

            string value = NormalizeKeyword(raw, allowed);
            return value == null ? fallback : value;
        }

        /// <summary>把用户选项规整成大写关键字；不认识时返回 null。</summary>
        private static string NormalizeKeyword(string value, params string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string upper = value.Trim().ToUpperInvariant();
            return Array.IndexOf(allowed, upper) >= 0 ? upper : null;
        }

        /// <summary>读取上次的管线图层名；没存过时返回 null（调用方据此改为提示用户点选）。</summary>
        public static string GetLastLayer()
        {
            string raw;
            if (!Load().TryGetValue(LayerKey, out raw))
            {
                return null;
            }

            return string.IsNullOrEmpty(raw) ? null : raw;
        }

        public static void SetLastLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return;
            }

            Save(LayerKey, layerName);
        }

        /// <summary>读取上次的仪表线图层名（INSTBREAK）；没存过时返回 null（调用方据此改为提示用户点选）。</summary>
        public static string GetLastInstrumentLayer()
        {
            string raw;
            if (!Load().TryGetValue(InstrumentLayerKey, out raw))
            {
                return null;
            }

            return string.IsNullOrEmpty(raw) ? null : raw;
        }

        public static void SetLastInstrumentLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return;
            }

            Save(InstrumentLayerKey, layerName);
        }

        /// <summary>
        /// 解析「键=值」文本。文件不存在、被占用、内容损坏等任何异常都退化成空表，
        /// 让调用方走默认值。
        /// </summary>
        private static Dictionary<string, string> Load()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string path = ConfigManager.GetConfigFilePath(FileName);
                if (!File.Exists(path))
                {
                    return map;
                }

                foreach (string line in File.ReadAllLines(path))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text[0] == '#')
                    {
                        continue;
                    }

                    // 只按第一个 '=' 切分：图层名里允许出现 '='。
                    int split = text.IndexOf('=');
                    if (split <= 0)
                    {
                        continue;
                    }

                    map[text.Substring(0, split).Trim()] = text.Substring(split + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                Logger.Info("读取 PIPEBREAK 记忆设置失败，本次改用默认值：" + ex.Message);
            }

            return map;
        }

        /// <summary>写入一项并保留文件里其它项（两个设置共用一个文件）。</summary>
        private static void Save(string key, string value)
        {
            Save(key, value, removeKey: false);
        }

        /// <summary>写入（或删除）一项并保留文件里其它项。</summary>
        private static void Save(string key, string value, bool removeKey)
        {
            try
            {
                var map = Load();
                if (removeKey)
                {
                    map.Remove(key);
                }
                else
                {
                    map[key] = value;
                }

                string path = ConfigManager.GetConfigFilePath(FileName);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var lines = new List<string> { "# ChemForge PIPEBREAK / INSTBREAK 命令的跨图纸记忆，可安全删除。" };
                foreach (KeyValuePair<string, string> pair in map)
                {
                    lines.Add(pair.Key + "=" + pair.Value);
                }

                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Info("保存 PIPEBREAK 记忆设置失败，本次不记忆：" + ex.Message);
            }
        }
    }
}
#endif
