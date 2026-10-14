#if NET80
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using Autodesk.AutoCAD.Customization;
using ChemForge.Application;
using ChemForge.CAD.Abstraction;
using ChemForge.Utilities;

namespace ChemForge.CAD.Impl
{
    /// <summary>
    /// .NET 8.0 环境下的菜单服务实现（兼容 AutoCAD 2025-2026）。
    ///
    /// 经典菜单栏显示 ChemForge 的**真实机制**（2026-10 定案）：
    ///  1. ChemForge.cuix 由 MenuDefinition 生成，落在 AutoCAD 用户支持目录
    ///     （...\chs\Support\，与 acetmain.cuix 等局部菜单同路径）；
    ///  2. 根菜单带两个别名：ID_ChemForgeMenu（工作空间 WSPop 按 pUID 引用）与
    ///     **POP16**（官方规则：别名 POP1–POP16 的菜单在加载 CUIx 时自动上菜单栏，
    ///     POP17–POP499 必须写进工作空间才显示）。本机实测 POP0–POP13 已被
    ///     PnP/acad/Express 占用，故取 POP16 —— 菜单栏从此由 AutoCAD 自己挂，
    ///     不需要插件改宿主 PnP.cuix 写 WSPop，也不需要清理配置文件布局条目；
    ///  3. 部署脚本仍会把 ChemForge.cuix 登记为 PnP.cuix 的局部菜单
    ///     （PartialMenuFile），这是 CUIx 被加载的前提。
    ///
    /// 已删除的历史做法（每一条都对应一次现场故障）：
    ///  - 运行时 COM 挂载（MountAsPnpMenu/EnsureMenuInBar：在 PNP 组建菜单 +
    ///    InsertInMenuBar + Application.ReloadAllMenus）——既不是显示的必经路径，
    ///    又是在进程内触发 CAD 崩溃的高风险动作；
    ///  - Idle 里读取 WSCURRENT 跟踪工作空间切换——Idle 触发极密集（鼠标移动即触发），
    ///    每次 Idle 一次 COM 系统变量调用，是"切换工作空间卡顿"的主要来源；
    ///  - 启动时清理配置文件布局（RemoveChemForgeLayoutEntry）——POP16 之后 AutoCAD
    ///    会合法地写下自己的布局条目，插件再删就是跟它抢同一批键值；遗留条目改由本机
    ///    部署脚本在 CAD 关闭时清理（不在插件运行时处理）。
    ///
    /// 功能区：改为在局部 CUIx 里**声明式**定义（RibbonTabSource/RibbonPanelSource，
    /// 与官方 appmanager.cuix 同源），AutoCAD 随 CUIx 加载与工作空间切换自己重建；
    /// 运行时 RibbonBuilder 只作为兜底保留（探测到选项卡缺失才创建：启动 20 秒内
    /// 每 0.5 秒重试，之后 5 秒；连续重建时放大到 60 秒）。
    /// </summary>
    public sealed class MenuServiceNet80 : IMenuService
    {
        private static bool _hooked;
        private static DateTime _lastRibbonProbe = DateTime.MinValue;
        private static readonly TimeSpan RibbonProbeInterval = TimeSpan.FromSeconds(5);

        // 菜单栏 / MENUBAR / 主窗口置顶标志的复查节流：三项都很轻（一次 GetSystemVariable
        // + 一次 GetWindowLong），但 Idle 触发极密集，故按 5 秒复查一次
        private static DateTime _lastMenuBarCheck = DateTime.MinValue;

        // 功能区探测退避：稳定存在时按 RibbonProbeInterval 探测；若连续重建（说明有
        // 外部因素反复清掉选项卡），间隔放大到 RibbonProbeMaxInterval，避免
        // "缺失 → 重建 → 再被清掉"的循环每 5 秒创建一套 WPF 对象（堆压力来源之一）
        private const int RibbonRecreateWarnThreshold = 3;
        private static readonly TimeSpan RibbonProbeMaxInterval = TimeSpan.FromSeconds(60);
        private static int _ribbonRecreateCount;

        // 启动初期（功能区尚未就绪的那几秒）按 0.5 秒密集重试，让兜底选项卡尽快出现，
        // 而不是等第一次 5 秒节流到期；20 秒后回到常规节流
        private static readonly DateTime StartupUtc = DateTime.UtcNow;
        private static readonly TimeSpan RibbonProbeStartupInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan RibbonProbeStartupWindow = TimeSpan.FromSeconds(20);

        /// <inheritdoc />
        public void EnsureMenuLoaded(string cuixPath)
        {
            // 说明：这里曾经先调用 EnsureMenuInBar()（COM 运行时把菜单插进菜单栏）。
            // 菜单栏显示已改由**静态挂载**负责（出厂 PnP.cuix 内的扁平菜单 + 工作空间
            // WSPop(MenuGroup="PNP") + 配置文件布局条目，见 deploy\Dev\Mount-ChemForgeMenu.ps1），
            // 运行时 COM 挂载既不是显示的必要条件，又是在进程内触发 .NET 运行时崩溃的高风险动作
            // （动态 COM + Application.ReloadAllMenus()），故不再在启动路径调用。

            var supportDir = GetSupportDirectory();
            if (supportDir == null)
            {
                Logger.Warning("未能定位 AutoCAD 用户支持目录，跳过 ChemForge 菜单文件维护。");
                return;
            }

            // 2026-10 改造：这里曾经每次启动都清理配置文件布局里指向 CHEMFORGE 的
            // Pop<n> 条目（RemoveChemForgeLayoutEntry）。现在根菜单带 POP16 别名，
            // AutoCAD 自己就会把它挂上菜单栏并**合法地**写下对应布局条目，
            // 插件再删就是跟 AutoCAD 抢同一批键值 —— 正是"连续切换后布局错乱"的推手之一。
            // 历史遗留条目（旧版插件用 COM 补挂留下的）改由本机部署脚本在 CAD 关闭时
            // 清理，运行时不再触碰注册表。

            // 经典菜单栏在工作空间「草图与注释」/新建图纸下默认是关闭的（MENUBAR=0）。
            // ChemForge 项是 AutoCAD 在**构建菜单栏那一刻**从工作空间 WSPop 追加进去的，
            // 之后再打开「显示菜单栏」只会按配置文件的布局建栏，ChemForge 不会被补进来
            // （实测：启动时 MENUBAR=0 → 手动打开菜单栏后栏里只有 12 个出厂菜单 + Express）。
            // 用户的目标就是让 ChemForge 出现在菜单栏，故每次启动统一置 MENUBAR=1。
            EnsureClassicMenuBarVisible();

            // 主窗口若残留「总在最前」标志（AutoCAD 偶发），启动时清一次，
            // 避免 CAD 盖在其它软件上面（详见 EnsureMainWindowNotTopmost）
            EnsureMainWindowNotTopmost();

            // 说明：这里曾经调用 EnsureFileDialogsEnabled() 并挂钩 DocumentCreated 事件，
            // 每次启动与每张图打开都把 FILEDIA 强写为 1。该做法已删除 —— 它兜的是 AutoCAD 自身的
            // NETLOAD/打开/保存，而 ChemForge 自己一个文件对话框都不用；起因只是开发机上的一次
            // 环境故障（原注释："本机存在 FILEDIA 被置 0 的残留"），不该固化成产品行为，更不该
            // 悄悄覆盖那些有意关闭文件对话框的用户（脚本化作业常这么设）。要恢复，用户自己敲 FILEDIA。
            var targetPath = Path.Combine(supportDir, "ChemForge.cuix");
            try
            {
                // 菜单文件缺失或早于插件程序集时重新生成（下次启动生效）；
                // 注册信息（PartialMenuFile / 工作空间 WSPop）由部署脚本维护，这里不触碰
                if (!File.Exists(targetPath) || IsOlderThanPlugin(targetPath))
                {
                    CreateMenuFile(targetPath);
                    Logger.Info($"ChemForge CUIx 菜单文件已生成：{targetPath}");
                }
                else
                {
                    Logger.Info($"ChemForge CUIx 菜单文件已存在：{targetPath}");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"ChemForge CUIx 生成失败（可能被 AutoCAD 占用）：{ex.Message}");
            }

            HookRibbonEvents();
        }

        /// <summary>
        /// COM 后期绑定取属性 / 调方法（替代 dynamic）。
        /// 目标是 IDispatch 的 __ComObject 时，Type.InvokeMember 由运行时自带的 COM 绑定层
        /// 处理，不经过 DLR 的运行期绑定器；普通属性与带参属性（Item(name)）走同一入口。
        /// </summary>
        private static object GetComMember(object target, string name, object[] args = null)
        {
            if (target == null)
            {
                return null;
            }

            return target.GetType().InvokeMember(
                name,
                System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.InvokeMethod,
                null,
                target,
                args);
        }

        /// <summary>
        /// 定位 AutoCAD 用户支持目录（...\R25.0\chs\support\）：
        /// 优先取当前主 CUIx（acad.CUIX）所在目录，回退到注册表配置文件 ACAD 支持路径第一段
        /// </summary>
        private static string GetSupportDirectory()
        {
            // 优先取主 CUIx（ACAD 菜单组）所在目录。
            // 这里用 Type.InvokeMember 做 COM 后期绑定，而不是 dynamic：dynamic 要经 DLR 的
            // 运行期绑定器（Microsoft.CSharp）才落到 COM 上，这条路径在 .NET Framework 与
            // .NET 8 上的支持并不一致；InvokeMember 走运行时自带的 COM 绑定层，net48 与
            // net80 两个目标行为完全相同，也不再依赖额外的绑定器程序集。
            try
            {
                object comApp = Autodesk.AutoCAD.ApplicationServices.Application.AcadApplication;
                object menuGroups = GetComMember(comApp, "MenuGroups");
                foreach (var groupName in new[] { "ACAD", "PNP" })
                {
                    object group = null;
                    try
                    {
                        group = GetComMember(menuGroups, "Item", new object[] { groupName });
                    }
                    catch (System.Exception)
                    {
                        group = null;
                    }

                    if (group != null)
                    {
                        var menuFile = GetComMember(group, "MenuFileName") as string;
                        if (!string.IsNullOrEmpty(menuFile))
                        {
                            var dir = Path.GetDirectoryName(menuFile);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            {
                                return dir;
                            }
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 继续尝试回退路径
            }

            try
            {
                using (var autocad = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Autodesk\AutoCAD"))
                {
                    if (autocad == null)
                    {
                        return null;
                    }

                    foreach (var versionName in autocad.GetSubKeyNames())
                    {
                        using (var version = autocad.OpenSubKey(versionName))
                        {
                            if (version == null)
                            {
                                continue;
                            }

                            foreach (var productName in version.GetSubKeyNames())
                            {
                                using (var profiles = version.OpenSubKey(productName + @"\Profiles"))
                                {
                                    if (profiles == null)
                                    {
                                        continue;
                                    }

                                    foreach (var profileName in profiles.GetSubKeyNames())
                                    {
                                        using (var general = profiles.OpenSubKey(profileName + @"\General"))
                                        {
                                            if (general == null)
                                            {
                                                continue;
                                            }

                                            var acadVar = general.GetValue("ACAD") as string;
                                            if (!string.IsNullOrEmpty(acadVar))
                                            {
                                                foreach (var candidate in acadVar.Split(';'))
                                                {
                                                    if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                                                    {
                                                        return candidate;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 回退失败，返回 null
            }

            return null;
        }

        /// <summary>
        /// CUIx 是否早于插件程序集（插件重新构建后需要刷新菜单定义）
        /// </summary>
        private static bool IsOlderThanPlugin(string cuixPath)
        {
            try
            {
                var pluginTime = File.GetLastWriteTimeUtc(typeof(MenuServiceNet80).Assembly.Location);
                var cuixTime = File.GetLastWriteTimeUtc(cuixPath);
                return cuixTime < pluginTime;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 依据 MenuDefinition 生成 CUIx 菜单文件。
        /// 注意：CUIx 标识符（菜单/宏名）一律用纯显示名 DisplayTitle，
        /// 显示标签才保留 \t 留白（\t 进入标识符会导致"不能加载菜单项"）
        ///
        /// 生成内容（2026-10 改造后）：
        ///  1. 菜单栏根菜单带**两个**别名：ID_ChemForgeMenu（工作空间 WSPop 按 pUID 引用）
        ///     与 POP16（官方"POP1–POP16 加载即上菜单栏"规则，不再依赖改宿主 PnP.cuix）；
        ///  2. 声明式功能区：在局部 CUIx 里定义 RibbonTabSource + 每个一级菜单一个
        ///     RibbonPanelSource（含 RibbonRow / RibbonCommandButton），由 AutoCAD 自己
        ///     随 CUIx 加载与工作空间切换重建，插件不再在运行时创建 WPF 功能区对象。
        ///
        /// 图标为 best-effort：带图标的生成若抛异常（AcCui 的散图路径需要宿主服务），
        /// 自动用纯文字按钮重来一次，保证菜单文件一定生成成功。
        /// </summary>
        private static void CreateMenuFile(string cuixPath)
        {
            try
            {
                BuildMenuFile(cuixPath, withIcons: true);
            }
            catch (System.Exception ex)
            {
                // 给 CreateMenuMacro 传图片文件名时，AcCui 会走内部的 addLooseImage(...)，
                // 该路径依赖 CustomizationSection.HostServices（"在 AutoCAD 之外加载 AcCui
                // 的应用需自行实现"）。没有宿主服务时它会 NRE（本机独立试跑实测）。
                // 图标只是锦上添花：整体重来一次（全新 CustomizationSection，不会留下
                // 重复宏），退化成纯文字按钮，绝不让菜单文件生成失败。
                Logger.Warning($"带图标的 CUIx 生成失败（{ex.GetType().Name}: {ex.Message}），改用纯文字按钮重试。");
                BuildMenuFile(cuixPath, withIcons: false);
            }
        }

        /// <summary>
        /// CreateMenuFile 的实现体。<paramref name="withIcons"/> 为 false 时不向
        /// CreateMenuMacro 传图片（按钮只有文字），用于图标路径失败后的重试。
        /// </summary>
        private static void BuildMenuFile(string cuixPath, bool withIcons)
        {
            var customization = new CustomizationSection();
            customization.MenuGroupName = MenuDefinition.MenuGroupName;
            customization.MenuGroup.Name = MenuDefinition.MenuGroupName;

            var macroGroup = new MacroGroup("ChemForge", customization.MenuGroup);

            var rootMenu = new PopMenu(
                MenuDefinition.RootMenuTitle,
                new StringCollection { MenuDefinition.RootMenuAlias, MenuDefinition.RootMenuBarAlias },
                MenuDefinition.RootMenuAlias,
                customization.MenuGroup)
            {
                // 显式指定 UID：工作空间（WSPop）按 pUID 引用本菜单，
                // 若放任 AcCui 自动生成随机 UID，菜单栏渲染层就找不到菜单项
                ElementID = MenuDefinition.RootMenuAlias
            };

            // 声明式功能区的面板先建好，选项卡最后建（引用需要面板的 ElementID）
            var ribbonRoot = customization.MenuGroup.RibbonRoot;
            var panelSources = new List<RibbonPanelSource>();
            var cuixDirectory = Path.GetDirectoryName(cuixPath);

            foreach (var subMenuDefinition in MenuDefinition.Menus)
            {
                var subMenu = new PopMenu(
                    subMenuDefinition.DisplayTitle,
                    new StringCollection { subMenuDefinition.Alias },
                    subMenuDefinition.Alias,
                    customization.MenuGroup)
                {
                    ElementID = subMenuDefinition.Alias
                };

                var panelSource = new RibbonPanelSource(ribbonRoot)
                {
                    Text = subMenuDefinition.DisplayTitle,
                    Name = subMenuDefinition.DisplayTitle,

                    // 显式指定 UID：出厂 PnP.cuix 的工作空间按 "<菜单组名>.<面板UID>" 引用面板
                    // （Express Tools 就是 EXPRESS.RBN_0001 这种写法）。若放任 AcCui 自动生成
                    // 随机 UID，每次重新生成菜单文件后工作空间引用就会失效（面板全部消失）。
                    // 用菜单别名去掉 ID_ 前缀，稳定且可读：ID_TextMenu -> CF_PANEL_TextMenu
                    ElementID = "CF_PANEL_" + (subMenuDefinition.Alias.StartsWith("ID_", StringComparison.Ordinal)
                        ? subMenuDefinition.Alias.Substring(3)
                        : subMenuDefinition.Alias)
                };
                // RibbonRow 只有这个无参构造（AcCui 标注为"仅供内部使用"，但官方 ADN 样例
                // 与 AutoCAD 自己生成的 appmanager.cuix 都是这么写的：行不自动挂到面板上，
                // 需要显式 panelSource.Items.Add(row)），故此处忽略该过时警告
#pragma warning disable CS0618
                var row = new RibbonRow();
#pragma warning restore CS0618
                panelSource.Items.Add(row);

                foreach (var commandDefinition in subMenuDefinition.Children)
                {
                    // 分隔线：AcCui 的 PopMenuItem.IsSeparator 为只读且无公开工厂，
                    // 生成文件里跳过（不影响 Express 式菜单结构）
                    if (commandDefinition.IsSeparator)
                    {
                        continue;
                    }

                    // 宏的图片只能经 CreateMenuMacro 的 smallImage/largeImage 传入
                    // （"图片资源 ID 或文件名"）；这里给文件名，图标由 RibbonBuilder
                    // 从嵌入资源释放到 CUIx 同目录，失败则为 null（按钮退化为纯文字）
                    var smallIcon = withIcons
                        ? RibbonBuilder.EnsureIconFile(commandDefinition.Macro, 16, cuixDirectory)
                        : null;
                    var largeIcon = withIcons
                        ? RibbonBuilder.EnsureIconFile(commandDefinition.Macro, 32, cuixDirectory)
                        : null;

                    var macro = macroGroup.CreateMenuMacro(
                        commandDefinition.DisplayTitle,
                        commandDefinition.Macro,
                        commandDefinition.Alias,
                        commandDefinition.DisplayTitle,
                        MacroType.Any,
                        smallIcon,
                        largeIcon,
                        null);

                    new PopMenuItem(macro, commandDefinition.Title, subMenu, subMenu.PopMenuItems.Count);

                    var button = new RibbonCommandButton(row)
                    {
                        Text = commandDefinition.DisplayTitle,
                        ButtonStyle = RibbonButtonStyle.LargeWithText,
                        MacroID = commandDefinition.Alias,
                        TooltipTitle = commandDefinition.DisplayTitle
                    };
                    row.Items.Add(button);
                }

                ribbonRoot.RibbonPanelSources.Add(panelSource);
                panelSources.Add(panelSource);

                new PopMenuRef(subMenu, rootMenu, rootMenu.PopMenuItems.Count);
            }

            // 选项卡：ElementID 用 RibbonBuilder.TabId，运行时兜底据此判断
            // "声明式选项卡已存在"，从而不再重复创建一套 WPF 对象。
            // WorkspaceBehavior=MergeOrAddTab + DefaultDisplay=AddToWorkspace：
            // 局部 CUIx 加载时由 AutoCAD 自己把选项卡并入（或加入）工作空间，
            // 与官方 appmanager.cuix 的做法同源。
            var tabSource = new RibbonTabSource(ribbonRoot)
            {
                Name = MenuDefinition.RootMenuTitle,
                Text = MenuDefinition.RootMenuTitle,
                ElementID = RibbonBuilder.TabId,
                WorkspaceBehavior = TabWorkspaceBehavior.MergeOrAddTab,
                DefaultDisplay = DefaultDisplay.AddToWorkspace
            };
            tabSource.Aliases.Add(MenuDefinition.RibbonTabAlias);

            foreach (var panelSource in panelSources)
            {
                var reference = new RibbonPanelSourceReference(tabSource)
                {
                    PanelId = panelSource.ElementID,
                    ElementID = panelSource.ElementID + "_REF"
                };
                tabSource.Items.Add(reference);
            }

            ribbonRoot.RibbonTabSources.Add(tabSource);

            customization.SaveAs(cuixPath);
        }

        /// <summary>
        /// 经典菜单栏开关（MENUBAR）在工作空间「草图与注释」以及新建图纸下默认是 0（关闭）。
        /// ChemForge 的菜单栏项由 AutoCAD 在**构建菜单栏的那一刻**从工作空间定义
        /// （WSPop 列表）里追加进栏尾，构建完成后再把 MENUBAR 从 0 打开，只会按配置文件
        /// 的布局重建菜单栏，ChemForge 不会被补进来（本机实测：启动时 MENUBAR=0，
        /// 手动打开"显示菜单栏"后栏里只有 12 个出厂菜单 + Express，没有 ChemForge）。
        /// 用户的目标正是让 ChemForge 出现在菜单栏，故每次启动统一置 MENUBAR=1，
        /// 让 AutoCAD 在构建菜单栏时就带上它。已在 1 时不做任何事（幂等）。
        /// </summary>
        private static void EnsureClassicMenuBarVisible()
        {
            try
            {
                if ((short)Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("MENUBAR") != 1)
                {
                    Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("MENUBAR", 1);
                    Logger.Info("已强制 MENUBAR=1（显示经典菜单栏，ChemForge 随之出现在菜单栏）。");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"强制 MENUBAR=1 失败（可忽略）：{ex.Message}");
            }
        }

        // ---- 主窗口「总在最前」残留标志的自愈（AutoCAD 偶发问题）----
        // AutoCAD 会话中若某个模态对话框异常关闭，主窗口会残留 WS_EX_TOPMOST，
        // 表现为"CAD 一直盖在别的软件上面、怎么点都盖不下去"。
        // 这里只做一件事：检测到该标志存在时用 SetWindowPos(HWND_NOTOPMOST) 清掉它；
        // 标志不存在时**什么都不做**（不改变窗口大小/位置/激活状态，不影响其它软件）。
        private const int GwlExStyle = -20;
        private const long WsExTopmost = 0x8L;
        private static readonly IntPtr HwndNotTopmost = new IntPtr(-2);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        private static long GetWindowExStyle(IntPtr hWnd)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, GwlExStyle).ToInt64()
                : GetWindowLong32(hWnd, GwlExStyle);
        }

        /// <summary>
        /// 清除 AutoCAD 主窗口残留的「总在最前」标志（幂等、无副作用）。
        /// 仅在 <c>WS_EX_TOPMOST</c> 确实存在时才调用 <c>SetWindowPos(HWND_NOTOPMOST)</c>，
        /// 且只带 SWP_NOSIZE|SWP_NOMOVE|SWP_NOACTIVATE，不会移动/缩放/激活/显示窗口。
        /// 刻意不带 SWP_SHOWWINDOW(0x0040)：它的语义是「显示窗口」，对最小化的主窗口有还原作用；
        /// 而本方法跑在 CAD 的 Idle 回调里、主窗口必然已可见，带上它只有副作用没有收益。
        /// 清除失败也不影响其它功能：该标志是窗口运行时状态，重启 AutoCAD 即消失。
        /// </summary>
        private static void EnsureMainWindowNotTopmost()
        {
            try
            {
                var hWnd = Autodesk.AutoCAD.ApplicationServices.Core.Application.MainWindow.Handle;
                if (hWnd == IntPtr.Zero)
                {
                    return;
                }

                if ((GetWindowExStyle(hWnd) & WsExTopmost) == 0)
                {
                    return; // 正常状态：不动任何东西
                }

                SetWindowPos(
                    hWnd, HwndNotTopmost, 0, 0, 0, 0,
                    SwpNoSize | SwpNoMove | SwpNoActivate);

                if ((GetWindowExStyle(hWnd) & WsExTopmost) == 0)
                {
                    Logger.Info("已清除 AutoCAD 主窗口残留的置顶标志（WS_EX_TOPMOST），恢复正常窗口层级。");
                }
                else
                {
                    Logger.Warning("清除 AutoCAD 主窗口置顶标志失败（重启 AutoCAD 即可恢复，不影响图纸与其它功能）。");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"检查/清除主窗口置顶标志失败（可忽略）：{ex.Message}");
            }
        }

        /// <summary>
        /// 挂钩文档激活与空闲事件，用于维持功能区选项卡与菜单栏状态：
        /// 1) 功能区是应用级 UI，但工作空间切换等场景可能把它清掉，
        ///    故 Idle 里定时探测（缺失即重建；连续重建时退避到 60 秒）；
        /// 2) MENUBAR 随图纸保存（换图/新建后可能又是 0），主窗口偶发残留置顶标志，
        ///    两者由同一节拍（5 秒）复查；
        /// 3) 文档激活时同样确保一次（打开新图/切换激活）。
        ///
        /// 注意：这里**不**做工作空间切换跟踪。菜单栏由 AutoCAD 按工作空间定义静态
        /// 重建（出厂 PnP.cuix 内的扁平菜单 + WSPop 引用），插件无需补挂；曾经在 Idle 里
        /// 读取 WSCURRENT 的跟踪逻辑已删除——它让每一次空闲都发一次 COM 系统变量调用
        /// （鼠标移动即触发 Idle），是切换工作空间卡顿的主要来源之一。
        /// </summary>
        private static void HookRibbonEvents()
        {
            if (_hooked)
                return;

            Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.DocumentActivated +=
                (s, e) =>
                {
                    EnsureRibbonOnce();
                    EnsureClassicMenuBarVisible();
                    EnsureMainWindowNotTopmost();
                };

            Autodesk.AutoCAD.ApplicationServices.Core.Application.Idle +=
                (s, e) =>
                {
                    // 功能区探测：缺失才重建；连续重建时按退避间隔探测
                    if (DateTime.UtcNow - _lastRibbonProbe >= CurrentRibbonProbeInterval())
                    {
                        _lastRibbonProbe = DateTime.UtcNow;
                        EnsureRibbonOnce();
                    }

                    // MENUBAR 是随图纸保存的系统变量：换图/新建图纸后可能又是 0，
                    // 定期复查保证经典菜单栏始终可见（ChemForge 才在菜单栏里）；
                    // 顺带复查主窗口是否又被置顶（AutoCAD 偶发残留），有则清掉
                    if (DateTime.UtcNow - _lastMenuBarCheck >= RibbonProbeInterval)
                    {
                        _lastMenuBarCheck = DateTime.UtcNow;
                        EnsureClassicMenuBarVisible();
                        EnsureMainWindowNotTopmost();
                    }
                };

            _hooked = true;
            Logger.Info("已注册功能区/菜单栏维持钩子（Idle 探测，按 5 秒节流，不做工作空间轮询）。");
        }

        /// <summary>
        /// 功能区探测间隔：连续重建超过阈值后放大到 60 秒，避免"缺失 → 重建 → 再被清掉"
        /// 的循环每 5 秒创建一套 WPF 对象；插件刚启动的 20 秒内则按 0.5 秒密集重试，
        /// 让兜底选项卡尽快出现（声明式选项卡在时这里根本不会触发）
        /// </summary>
        private static TimeSpan CurrentRibbonProbeInterval()
        {
            if (_ribbonRecreateCount > RibbonRecreateWarnThreshold)
            {
                return RibbonProbeMaxInterval;
            }

            return DateTime.UtcNow - StartupUtc < RibbonProbeStartupWindow
                ? RibbonProbeStartupInterval
                : RibbonProbeInterval;
        }

        /// <summary>
        /// 确保 ChemForge 功能区选项卡存在。
        ///
        /// 功能区是**应用级 UI**，开始页（未打开任何图纸）下同样存在——Express Tools 等
        /// 选项卡在开始页也显示，ChemForge 亦应如此，故这里**不**做"必须有文档"的前置判断
        /// （按钮在无文档时由 RibbonCommandHandler 记录日志并忽略，不会异常）。
        /// 仅有幂等判断：已存在则跳过并把重建计数归零，缺失则重建并累计重建次数。
        /// </summary>
        private static void EnsureRibbonOnce()
        {
            if (RibbonBuilder.IsRibbonPresent())
            {
                _ribbonRecreateCount = 0; // 选项卡稳定存在，退避计数归零
                return;
            }

            if (RibbonBuilder.EnsureRibbon())
            {
                _ribbonRecreateCount++;
                if (_ribbonRecreateCount == RibbonRecreateWarnThreshold + 1)
                {
                    Logger.Warning(
                        $"功能区选项卡已被反复清除并重建 {_ribbonRecreateCount} 次，探测间隔放大到 " +
                        $"{RibbonProbeMaxInterval.TotalSeconds:0} 秒（减少 WPF 对象创建）。");
                }
            }
        }

        /// <inheritdoc />
        public void UnloadMenu()
        {
            RibbonBuilder.RemoveRibbon();
        }
    }
}
#endif