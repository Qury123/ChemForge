#if NET80
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using ChemForge.Utilities;

namespace ChemForge.Application
{
    /// <summary>
    /// 功能区构建器 - 在 AutoCAD 功能区创建 ChemForge 选项卡。
    /// 与 CUIx 菜单栏互为备份：功能区由插件在运行时直接构建，
    /// 不依赖工作空间定义与菜单栏布局，随插件加载即时可见
    /// </summary>
    public static class RibbonBuilder
    {
        /// <summary>
        /// ChemForge 功能区选项卡标识
        /// </summary>
        public const string TabId = "CF_RIBBON_TAB_CHEMFORGE";

        /// <summary>
        /// 命令处理程序（所有按钮共用）
        /// </summary>
        private static readonly ICommand SharedCommandHandler = new RibbonCommandHandler();

        /// <summary>
        /// 命令名 → 图标基础名映射（嵌入资源）。
        /// 每个命令对应 16×16（Image）与 32×32（LargeImage）两枚：
        /// 大按钮（RibbonItemSize.Large）显示 LargeImage，小按钮显示 Image。
        /// </summary>
        private static readonly Dictionary<string, string> IconMap = new Dictionary<string, string>
        {
            { CommandNames.TextColor, "textcolor" },
            { CommandNames.BatchAttrChange, "batattchange" },
            { CommandNames.PipeBreak, "breakpipe" },
            // 仪表线自动打断（INSTBREAK）：breakinst_*.png 是 breakpipe_*.png 沿对角线转置的
            // 产物（十字本身对称，转置只是把红色缺口箭头从横线搬到竖线），一眼能看出
            // 「被打断的是另一条线」，又与管线打断同属一套图标语言。
            { CommandNames.InstrumentBreak, "breakinst" },
            // NumberInst（TAGNO）2026-10-14 随菜单项一并摘掉：图标资源 numberinst_*.png
            // 仍留在 UI\Resources\Icons 下，等业务实现后把这一行加回来即可。
            { CommandNames.ChemForge, "about" }
        };

        /// <summary>
        /// 图标缓存（按资源名），避免每个按钮重复从资源流解码
        /// </summary>
        private static readonly Dictionary<string, ImageSource> IconCache = new Dictionary<string, ImageSource>();

        /// <summary>
        /// 本会话是否由插件自己在运行时创建过功能区选项卡（退出时只拆自己建的那个）
        /// </summary>
        private static bool _runtimeTabCreated;

        /// <summary>
        /// "声明式选项卡已加载但当前工作空间未引用它"的警告只记一次，避免探测循环刷屏
        /// </summary>
        private static bool _hiddenTabWarned;

        /// <summary>
        /// 从嵌入资源加载命令图标（失败返回 null，按钮退化为纯文字）
        /// </summary>
        private static ImageSource LoadIcon(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            lock (IconCache)
            {
                ImageSource cached;
                if (IconCache.TryGetValue(resourceName, out cached)) return cached;
            }

            try
            {
                var asm = typeof(RibbonBuilder).Assembly;
                using (Stream stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    lock (IconCache)
                    {
                        IconCache[resourceName] = bitmap;
                    }
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 根据命令宏（如 "TCOLOR " / "PIPEBREAK "）取对应图标资源全名
        /// </summary>
        private static string GetIconResource(string macro, int size)
        {
            if (string.IsNullOrEmpty(macro)) return null;
            string command = macro.Trim();
            string baseName;
            if (!IconMap.TryGetValue(command, out baseName)) return null;
            return "ChemForge.UI.Resources.Icons." + baseName + "_" + size + ".png";
        }

        /// <summary>
        /// 根据命令宏取 CUIx 同目录散图的文件名（如 chemforge_textcolor_16.png）；
        /// 无对应图标时返回 null。注意不能用 Path.GetFileName(资源名)：嵌入资源名
        /// 里的点号不是目录分隔符，那样会得到 chemforge_ChemForge.UI.Resources.Icons.textcolor_16.png
        /// </summary>
        private static string GetIconFileName(string macro, int size)
        {
            if (string.IsNullOrEmpty(macro)) return null;
            string command = macro.Trim();
            string baseName;
            if (!IconMap.TryGetValue(command, out baseName)) return null;
            return "chemforge_" + baseName + "_" + size + ".png";
        }

        /// <summary>
        /// 把命令图标的嵌入资源释放成 CUIx 同目录下的散图文件，返回**文件名**（失败返回 null）。
        ///
        /// 用途：声明式功能区（CUIx 内的 RibbonTabSource/RibbonCommandButton）只能通过
        /// AcCui 的 CreateMenuMacro(..., smallImage, largeImage, ...) 引用图片，而该参数
        /// 接受的是"图片资源 ID 或**文件名**"。这里把嵌入 PNG 落到菜单文件旁边，
        /// 由 AcCui 在保存 CUIx 时收进包；即便未被收进包，AutoCAD 也能按
        /// 支持目录里的散图解析到同名文件。
        /// </summary>
        public static string EnsureIconFile(string macro, int size, string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var resourceName = GetIconResource(macro, size);
            var fileName = GetIconFileName(macro, size);
            if (resourceName == null || fileName == null)
            {
                return null;
            }

            var targetPath = Path.Combine(directory, fileName);

            try
            {
                var asm = typeof(RibbonBuilder).Assembly;
                if (File.Exists(targetPath) &&
                    File.GetLastWriteTimeUtc(targetPath) >= File.GetLastWriteTimeUtc(asm.Location))
                {
                    return fileName;
                }

                using (Stream source = asm.GetManifestResourceStream(resourceName))
                {
                    if (source == null)
                    {
                        return null;
                    }

                    using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        source.CopyTo(target);
                    }
                }

                Logger.Info($"命令图标已释放到菜单目录：{fileName}");
                return fileName;
            }
            catch (Exception ex)
            {
                Logger.Warning($"释放命令图标失败（该按钮退化为纯文字）：{fileName} - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ChemForge 功能区选项卡当前是否已存在（**不判可见性**）。
        /// 存在即视为无需再建：声明式选项卡与运行时兜底选项卡用的是同一个 Id/标题，
        /// 再建一个就会出现两个 ChemForge 选项卡（2026-10-07 用户明确要求不能重复）。
        /// 可见性只用于记一条诊断日志：存在但不可见 = 当前工作空间没有引用它。
        /// </summary>
        public static bool IsRibbonPresent()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null)
                {
                    return false;
                }

                var tab = FindTab(ribbon);
                if (tab != null && !tab.IsVisible && !_runtimeTabCreated && !_hiddenTabWarned)
                {
                    _hiddenTabWarned = true;
                    Logger.Warning("ChemForge 选项卡已加载但当前工作空间未引用它，可能不可见；"
                        + "请运行 deploy\\Dev\\Mount-ChemForgeMenu.ps1 把选项卡引用写进工作空间。");
                }

                return tab != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 确保 ChemForge 功能区选项卡已创建（幂等，可在文档激活或空闲时反复调用）。
        /// 返回 true 表示创建成功或已存在；false 表示功能区尚未就绪，调用方应稍后重试。
        /// </summary>
        public static bool EnsureRibbon()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null)
                {
                    Logger.Info("AutoCAD 功能区尚未就绪，稍后重试。");
                    return false;
                }

                if (FindTab(ribbon) != null)
                {
                    return true;
                }

                var tab = new RibbonTab
                {
                    Title = MenuDefinition.RootMenuTitle,
                    Id = TabId
                };

                foreach (var menuDefinition in MenuDefinition.Menus)
                {
                    // 面板标题用纯显示名（去掉 \t 命令名/提示段）
                    var panelSource = new RibbonPanelSource { Title = menuDefinition.DisplayTitle };

                    foreach (var commandDefinition in menuDefinition.Children)
                    {
                        // 分隔线只对下拉菜单有意义，功能区面板直接跳过
                        if (commandDefinition.IsSeparator)
                        {
                            continue;
                        }

                        var button = new RibbonButton
                        {
                            Text = commandDefinition.DisplayTitle,
                            ShowText = true,
                            Size = RibbonItemSize.Large,
                            CommandHandler = SharedCommandHandler,
                            CommandParameter = commandDefinition.Macro
                        };

                        // 按钮图标：大按钮显示 LargeImage(32×32)，小图 Image(16×16) 同时设置。
                        // 加载失败则退化为纯文字按钮。
                        var smallIcon = LoadIcon(GetIconResource(commandDefinition.Macro, 16));
                        var largeIcon = LoadIcon(GetIconResource(commandDefinition.Macro, 32));
                        if (largeIcon != null || smallIcon != null)
                        {
                            if (largeIcon != null) button.LargeImage = largeIcon;
                            if (smallIcon != null) button.Image = smallIcon;
                            button.ShowImage = true;
                        }
                        else
                        {
                            button.ShowImage = false;
                        }

                        panelSource.Items.Add(button);
                    }

                    tab.Panels.Add(new RibbonPanel { Source = panelSource });
                }

                ribbon.Tabs.Add(tab);
                _runtimeTabCreated = true;
                Logger.Info("ChemForge 功能区选项卡已创建。");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("创建 ChemForge 功能区选项卡失败。", ex);
                return false;
            }
        }

        /// <summary>
        /// 移除 ChemForge 功能区选项卡（插件终止时调用）。
        ///
        /// 只移除**本会话由插件自己创建**的那个选项卡：CUIx 里声明式定义的选项卡
        /// 归 AutoCAD 所有（它的 Id 也可能等于 TabId），插件不该在退出时把它拆掉。
        /// </summary>
        public static void RemoveRibbon()
        {
            try
            {
                if (!_runtimeTabCreated)
                {
                    return;
                }

                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null)
                {
                    return;
                }

                var tab = FindRuntimeTab(ribbon);
                if (tab != null)
                {
                    ribbon.Tabs.Remove(tab);
                    _runtimeTabCreated = false;
                    Logger.Info("ChemForge 功能区选项卡已移除。");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"移除 ChemForge 功能区选项卡失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 按运行时 Id 精确查找插件自建的选项卡（不做标题兜底匹配）
        /// </summary>
        private static RibbonTab FindRuntimeTab(RibbonControl ribbon)
        {
            if (ribbon.Tabs == null)
            {
                return null;
            }

            foreach (var tab in ribbon.Tabs)
            {
                if (tab != null && string.Equals(tab.Id, TabId, StringComparison.Ordinal))
                {
                    return tab;
                }
            }

            return null;
        }

        /// <summary>
        /// 在功能区中查找**已显示**的 ChemForge 选项卡
        ///
        /// 按 Id 或标题认选项卡，**不看可见性**：CUIx 里声明式定义的选项卡若没有被任何工作空间
        /// 引用，AutoCAD 会把它读进功能区集合却保持隐藏（本机 FeaturedApps.cuix 等实测如此）。
        /// 判可见性会导致"隐藏的声明式选项卡 + 运行时兜底"各建一个 ChemForge 选项卡；
        /// 正解是让声明式选项卡进工作空间（deploy\Dev\Mount-ChemForgeMenu.ps1 写 9 个工作空间引用），
        /// 而不是在运行时补建第二个。
        /// </summary>
        private static RibbonTab FindTab(RibbonControl ribbon)
        {
            if (ribbon.Tabs == null)
            {
                return null;
            }

            foreach (var tab in ribbon.Tabs)
            {
                if (tab == null)
                {
                    continue;
                }

                // 两种来源都要认出来，避免"声明式 + 运行时兜底"各建一个 ChemForge 选项卡：
                //  1) 运行时兜底自建的选项卡：Id 就是 TabId；
                //  2) CUIx 里声明式定义的选项卡：ElementID 用的是同一个 TabId，
                //     但运行时 Id 未必等于 UID，故再按标题兜一层。
                if (string.Equals(tab.Id, TabId, StringComparison.Ordinal) ||
                    string.Equals(tab.Title, MenuDefinition.RootMenuTitle, StringComparison.Ordinal))
                {
                    return tab;
                }
            }

            return null;
        }

        /// <summary>
        /// 把菜单宏（如 ^C^C_TCOLOR）转换为可直接送入命令行的命令名
        /// </summary>
        internal static string ToCommandLine(string macro)
        {
            if (string.IsNullOrEmpty(macro))
            {
                return string.Empty;
            }

            var text = macro.Trim();

            // 去掉取消前缀 ^C（可重复出现）
            while (text.StartsWith("^C", StringComparison.Ordinal))
            {
                text = text.Substring(2).TrimStart();
            }

            // 只去掉命令名开头的语言无关前缀，保留命令名内部的下划线
            if (text.StartsWith("_", StringComparison.Ordinal))
            {
                text = text.Substring(1).TrimStart();
            }

            return text.Trim();
        }

        /// <summary>
        /// 功能区按钮命令处理程序：把按钮挂接的宏转成命令行输入执行
        /// </summary>
        private sealed class RibbonCommandHandler : ICommand
        {
            /// <inheritdoc />
            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            /// <inheritdoc />
            public bool CanExecute(object parameter)
            {
                return true;
            }

            /// <inheritdoc />
            public void Execute(object parameter)
            {
                try
                {
                    // 入口就记一条。这个运行时处理器从项目存在以来一次都没被触发过
                    // （用户看到的 ChemForge 功能区一直是 CUIx 声明式那一份），
                    // 有了这条日志才能一眼看出"点击到底有没有走到这里"。
                    Logger.Info("ChemForge 功能区按钮被点击（运行时处理器）。");

                    var button = parameter as RibbonButton;
                    var macro = button == null ? null : button.CommandParameter as string;
                    var command = ToCommandLine(macro);
                    if (string.IsNullOrEmpty(command))
                    {
                        Logger.Warning(
                            $"功能区按钮没有可执行的命令宏（CommandParameter={macro ?? "<null>"}），已忽略。");
                        return;
                    }

                    var document = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument;
                    if (document == null)
                    {
                        Logger.Warning("当前没有打开的图纸，功能区命令已忽略。");
                        return;
                    }

                    document.SendStringToExecute(command + " ", true, false, false);
                    Logger.Info($"功能区按钮已触发命令：{command}");
                }
                catch (Exception ex)
                {
                    Logger.Error("ChemForge 功能区按钮执行失败。", ex);
                }
            }
        }
    }
}
#endif
