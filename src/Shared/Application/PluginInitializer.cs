using System;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Utilities;

// 显式声明本程序集就是 AutoCAD 的扩展应用（IExtensionApplication）实现。
// AutoCAD 在程序集里没有任何声明时会自己扫描、找到唯一实现后调用它 ——
// 但那属于「未声明行为」：程序集里一旦出现第二个实现、或宿主版本的扫描规则不同，
// 初始化就会被静默跳过，表现为「插件加载了但菜单/日志一概没有」。
// 2026-10-16 为 AutoCAD 2019-2024（.NET Framework 那一支）补上显式声明，
// 让 Initialize()/Terminate() 的调用不依赖扫描结果。
[assembly: ExtensionApplication(typeof(ChemForge.Application.PluginInitializer))]

namespace ChemForge.Application
{
    /// <summary>
    /// 插件初始化器 - 实现 IExtensionApplication 接口
    /// </summary>
    public sealed class PluginInitializer : IExtensionApplication
    {
        /// <inheritdoc />
        public void Initialize()
        {
            try
            {
                Logger.Initialize(GetLogFilePath());
                ConfigManager.Initialize(GetConfigDirectory());

                // 明确记下运行时目录：日志/配置实际落在哪里，排查时不必再猜
                Logger.Info($"运行时目录：{GetConfigDirectory()}");

                // 无界面环境（AutoCAD Core Console）没有主窗口：
                // 访问 COM 菜单接口会让 AutoCAD 内部去取主窗口而触发
                // AccessViolation（该异常不可捕获），故只初始化日志与配置
                if (IsHeadlessEnvironment())
                {
                    Logger.Info("检测到无界面 CAD 环境（Core Console），已跳过菜单与功能区加载。");
                    return;
                }

                MenuBuilder.LoadMenu();
                Logger.Info("ChemForge 插件初始化完成。");
            }
            catch (System.Exception ex)
            {
                Logger.Error("ChemForge 插件初始化失败。", ex);
            }
        }

        /// <inheritdoc />
        public void Terminate()
        {
            try
            {
                Logger.Info("ChemForge 插件正在终止。");

                // 无界面环境下没有挂载过菜单，跳过卸载（同样避免访问 UI 接口）
                if (IsHeadlessEnvironment())
                {
                    return;
                }

                MenuBuilder.UnloadMenu();
            }
            catch (System.Exception ex)
            {
                Logger.Error("ChemForge 插件终止清理失败。", ex);
            }
        }

        /// <summary>
        /// 是否为无界面 CAD 环境（AutoCAD Core Console，即 accoreconsole.exe）。
        /// 该环境没有主窗口，任何 UI / COM 菜单调用都会导致进程级崩溃，
        /// 且 AccessViolation 无法用 try/catch 捕获，必须提前识别并跳过
        /// </summary>
        private static bool IsHeadlessEnvironment()
        {
            try
            {
                var processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                return processName.IndexOf("accoreconsole", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static string GetLogFilePath()
        {
            // 日志文件名与 CUIx 基名不同，避免与 AutoCAD 为已加载 CUIx
            // 生成的伴生文件（如 .mnr / .log）冲突
            return System.IO.Path.Combine(GetConfigDirectory(), "ChemForgePlugin.log");
        }

        /// <summary>
        /// 运行时文件目录（配置、日志）：默认位于仓库根 runtime 子目录
        /// （向上查找 ChemForge.sln 定位仓库），开发期全部文件都在仓库内；
        /// 非仓库环境（正式安装）回退到 %LocalAppData%\ChemForge ——
        /// 不写已安装的插件目录，卸载后不留残渣，也避免只读的 Program Files
        /// </summary>
        private static string GetConfigDirectory()
        {
            try
            {
                var assemblyLocation = typeof(PluginInitializer).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    var realLocation = ResolveRealPath(assemblyLocation);
                    var dir = System.IO.Path.GetDirectoryName(realLocation);
                    for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                    {
                        if (System.IO.File.Exists(System.IO.Path.Combine(dir, "ChemForge.sln")))
                        {
                            var repoRuntimeDirectory = System.IO.Path.Combine(dir, "runtime");
                            System.IO.Directory.CreateDirectory(repoRuntimeDirectory);
                            return repoRuntimeDirectory;
                        }

                        dir = System.IO.Path.GetDirectoryName(dir);
                    }
                }

                // 非仓库环境（正式安装）：用用户可写目录，不写插件安装目录
                var localAppDataDirectory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChemForge");
                System.IO.Directory.CreateDirectory(localAppDataDirectory);
                return localAppDataDirectory;
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"运行时目录创建失败，回退到 %LocalAppData%：{ex.Message}");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(localAppData, "ChemForge");
        }

        /// <summary>
        /// 解析 NTFS Junction / 符号链接，得到文件的真实路径
        /// （开发部署用 Junction 把用户级 Autoloader 目录链接回仓库）
        /// </summary>
        private static string ResolveRealPath(string path)
        {
            // 首选：解析所在目录的真实路径。目录句柄不与已加载的程序集冲突，
            // 且能穿透链路上任意一层 Junction —— 而 Directory.ResolveLinkTarget
            // 只对“本身是链接”的路径生效，对链接目录“里面”的子目录返回 null，
            // 所以 Junction 指向的 net8.0 目录必须用句柄方式解析。
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                var realDirectory = TryResolveDirectoryHandle(directory);
                if (!string.IsNullOrEmpty(realDirectory))
                {
                    return System.IO.Path.Combine(realDirectory, System.IO.Path.GetFileName(path));
                }
            }

            // 退化路径：直接解析文件句柄
            var byHandle = TryResolveFileHandle(path);
            if (!string.IsNullOrEmpty(byHandle))
            {
                return byHandle;
            }

            return path;
        }

        /// <summary>
        /// 通过目录句柄解析真实路径（穿透祖先目录上的 NTFS Junction / 符号链接），失败返回 null。
        /// 目录以 FILE_FLAG_BACKUP_SEMANTICS 打开且只请求 FILE_READ_ATTRIBUTES，
        /// 不需要读写权限，也不触碰已加载的程序集文件本身。
        /// </summary>
        private static string TryResolveDirectoryHandle(string directoryPath)
        {
            Microsoft.Win32.SafeHandles.SafeFileHandle handle = null;
            try
            {
                handle = CreateFile(
                    directoryPath,
                    FileReadAttributes,
                    FileShareAll,
                    System.IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics,
                    System.IntPtr.Zero);
                if (handle == null || handle.IsInvalid)
                {
                    return null;
                }

                var buffer = new System.Text.StringBuilder(1024);
                var length = GetFinalPathNameByHandle(handle.DangerousGetHandle(), buffer, 1024, 0);
                if (length > 0 && length < buffer.Length)
                {
                    return StripLongPathPrefix(buffer.ToString(0, (int)length));
                }
            }
            catch (System.Exception)
            {
                // 解析失败时返回 null，由调用方决定回退策略
            }
            finally
            {
                if (handle != null)
                {
                    handle.Dispose();
                }
            }

            return null;
        }

        /// <summary>
        /// 通过文件句柄解析真实路径（穿透 NTFS Junction / 符号链接），失败返回 null
        /// </summary>
        private static string TryResolveFileHandle(string path)
        {
            try
            {
                using (var stream = new System.IO.FileStream(
                    path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                {
                    var buffer = new System.Text.StringBuilder(1024);
                    var length = GetFinalPathNameByHandle(
                        stream.SafeFileHandle.DangerousGetHandle(), buffer, 1024, 0);
                    if (length > 0 && length < buffer.Length)
                    {
                        return StripLongPathPrefix(buffer.ToString(0, (int)length));
                    }
                }
            }
            catch (System.Exception)
            {
                // 解析失败时返回 null，由调用方决定回退策略
            }

            return null;
        }

        /// <summary>
        /// 通用剥掉 Win32 长路径前缀，不假定盘符：
        ///   \\?\C:\dir\file        → C:\dir\file
        ///   \\?\UNC\server\share\f → \\server\share\f
        /// </summary>
        private static string StripLongPathPrefix(string finalPath)
        {
            const string UncPrefix = @"\\?\UNC\";
            const string LongPathPrefix = @"\\?\";
            if (finalPath.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + finalPath.Substring(UncPrefix.Length);
            }

            if (finalPath.StartsWith(LongPathPrefix, StringComparison.Ordinal))
            {
                return finalPath.Substring(LongPathPrefix.Length);
            }

            return finalPath;
        }

        // CreateFile 参数常量（只读属性访问目录，不请求读写权限）
        private const uint FileReadAttributes = 0x0080;
        private const uint FileShareAll = 0x00000007;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "CreateFileW")]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            System.IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            System.IntPtr templateFile);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandle(System.IntPtr fileHandle, System.Text.StringBuilder filePath, uint filePathLength, uint flags);
    }
}
