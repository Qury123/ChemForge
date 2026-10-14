using System;
using System.IO;

namespace ChemForge.Utilities
{
    /// <summary>
    /// 日志记录器 - 支持文件输出
    /// </summary>
    public static class Logger
    {
        private static readonly object LockObject = new object();
        private static string _logFilePath;

        /// <summary>
        /// 初始化日志文件路径
        /// </summary>
        /// <param name="logFilePath">日志文件完整路径</param>
        public static void Initialize(string logFilePath)
        {
            _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateIfUnusable(_logFilePath);
        }

        /// <summary>日志文件超过该大小即轮转，避免无限增长</summary>
        private const long MaxLogBytes = 4L * 1024 * 1024;

        /// <summary>运行期大小检查间隔（条数）：每写这么多条日志查一次文件大小</summary>
        private const int SizeCheckInterval = 200;

        /// <summary>已写入条数（用于触发运行期大小检查）</summary>
        private static int _writeCount;

        /// <summary>
        /// 日志文件不可用时轮转：超过 4MB，或内容不是文本时，
        /// 改名成 .bak 后重新开始记录。
        /// 起因：2026-10-07 本机 runtime\ChemForgePlugin.log 变成 5.28MB 高熵二进制垃圾
        /// （不含任何 [INFO]/日期/插件名，成因未查清），
        /// 导致"插件到底有没有加载"完全无法判断——日志必须始终可读。
        /// </summary>
        private static void RotateIfUnusable(string logFilePath)
        {
            try
            {
                if (!File.Exists(logFilePath))
                {
                    return;
                }

                var info = new FileInfo(logFilePath);
                if (info.Length > 0 && info.Length <= MaxLogBytes && LooksLikeText(logFilePath))
                {
                    return;
                }

                var backup = logFilePath + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Move(logFilePath, backup);
            }
            catch
            {
                // 轮转失败（文件被占用等）不应影响插件启动
            }
        }

        /// <summary>抽样判断文件是否像文本日志（非文本字节占比 ≤10% 视为文本）</summary>
        private static bool LooksLikeText(string logFilePath)
        {
            try
            {
                using (var stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var buffer = new byte[512];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        return true;
                    }

                    var suspicious = 0;
                    for (var i = 0; i < read; i++)
                    {
                        var value = buffer[i];
                        if (value == 9 || value == 10 || value == 13)
                        {
                            continue;
                        }

                        if (value < 32 || value == 127)
                        {
                            suspicious++;
                        }
                    }

                    return suspicious * 10 <= read;
                }
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 记录信息日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        /// <summary>
        /// 记录警告日志
        /// </summary>
        /// <param name="message">日志消息</param>
        public static void Warning(string message)
        {
            WriteLog("WARN", message);
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="exception">异常对象</param>
        public static void Error(string message, Exception exception = null)
        {
            var fullMessage = exception != null
                ? $"{message}{Environment.NewLine}{exception}"
                : message;
            WriteLog("ERROR", fullMessage);
        }

        private static void WriteLog(string level, string message)
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

            lock (LockObject)
            {
                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    try
                    {
                        File.AppendAllText(_logFilePath, entry + Environment.NewLine);

                        // 运行期也要轮转：早期只在 Initialize（启动）时检查一次，
                        // 长时间运行的会话仍能把文件写到任意大小（本机出过 5.28MB）。
                        // 每 SizeCheckInterval 条查一次文件大小，避免每条日志都做 IO 查询
                        if (++_writeCount % SizeCheckInterval == 0)
                        {
                            var info = new FileInfo(_logFilePath);
                            if (info.Exists && info.Length > MaxLogBytes)
                            {
                                RotateIfUnusable(_logFilePath);
                            }
                        }
                    }
                    catch
                    {
                        // 日志写入失败时不应抛出异常
                    }
                }
            }
        }
    }
}
