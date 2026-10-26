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
    /// 经典菜单栏显示 ChemForge 的**真实机制**（2026-10-23 实测定案，缺一不可）：
    ///  1. ChemForge.cuix 由 MenuDefinition 生成，落在 AutoCAD 用户支持目录
    ///     （...\chs\Support\，与 acetmain.cuix 等局部菜单同路径）；
    ///  2. 根菜单带两个别名：ID_ChemForgeMenu（工作空间 WSPop 按 pUID 引用）与
    ///     **POP16**（官方规则：别名 POP1–POP16 的菜单在加载 CUIx 时自动上菜单栏；
    ///     本机实测 POP0–POP13 已被 PnP/acad/Express 占用，故取 POP16）。
    ///  3. **每个工作空间的 WSPop 列表都必须引用根菜单**（部署脚本写入，与 Express
    ///     的做法逐字一致）。POP16 的"加载即上栏"只在 CUIx 加载那一刻生效：菜单栏
    ///     在**切换工作空间时是按当前工作空间的 WSPop 列表重建的**，列表里没有
    ///     ChemForge 就会掉，且切回来也不会自己回来（2026-10-23 反复实测）。
    ///  4. 根菜单必须是**扁平结构**（直接含命令项的 PopMenuItem）。AutoCAD 2025 实测：
    ///     子项全是 <c>PopMenuRef</c>（级联引用子菜单）的弹出菜单**永远不画到菜单栏**
    ///     —— COM 模型里 OnMenuBar=True 都收得到，画面就是不渲染；而含真实菜单项
    ///     的弹出菜单（Express 根菜单、5 个历史子菜单）都能正常画。Express 根菜单
    ///     能画还因为它除了 PopMenuRef 还带着两条直挂的 PopMenuItem。
    ///  5. 部署脚本仍会把 ChemForge.cuix 登记为 PnP.cuix 的局部菜单
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
    /// 与官方 appmanager.cuix 同源），并由宿主 PnP.cuix 的每个工作空间
    /// WSRibbonTabSourceReference 引用；AutoCAD 随 CUIx 加载与工作空间切换自己重建。
    /// 2026-10-24 起运行时不再建兜底选项卡（曾经：启动 20 秒内每 0.5 秒探测、之后 5 秒，
    /// 缺失就用 RibbonBuilder 现造一套 WPF 对象）——它把「部署没做对」掩盖成「看起来正常」，
    /// 并且每会话多出一次 WPF 构建与大量主线程打断。现在只在启动时自检一次，缺失就报日志。
    /// </summary>
    public sealed class MenuServiceNet80 : IMenuService
    {
        private static bool _hooked;

        // 菜单栏项的补挂复查：需要走几次 COM 调用（MenuGroups → Menus → OnMenuBar），
        // 比 GetSystemVariable 重，单独节流；2 秒足够快（用户看不到缺口），也足够轻
        private static readonly TimeSpan MenuBarItemCheckInterval = TimeSpan.FromSeconds(2);
        private static DateTime _lastMenuBarItemCheck = DateTime.MinValue;

        // 已补挂过就只在「栏上又没有了」时再打日志，避免每次文档激活都刷一条
        private static bool _menuBarItemLogged;

        // 菜单栏补挂的单次诊断：每次启动只打一条，说明到底停在哪一步（缺组/缺根菜单/已在栏上）
        private static bool _menuBarDiagLogged;

        /// <inheritdoc />
        public void EnsureMenuLoaded(string cuixPath)
        {
            // 说明：这里曾经先调用 EnsureMenuInBar()（COM 运行时把菜单插进菜单栏）。
            // 菜单栏显示已改由**声明式挂载**负责（ChemForge.cuix 局部菜单 + 根菜单
            // POP16 别名 + 宿主 PnP.cuix 工作空间 WSPop 引用，见类头注释与本仓库
            // deploy\Dev\Register-ChemForgeMenu.ps1），运行时 COM 挂载既不是显示的必要条件，
            // 又是在进程内触发 .NET 运行时崩溃的高风险动作（动态 COM + Application.
            // ReloadAllMenus()），故不再在启动路径调用。

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
                // 菜单文件缺失、或内容与当前 MenuDefinition 不一致时重新生成（下次启动生效）；
                // 注册信息（PartialMenuFile / 工作空间 WSPop）由部署脚本维护，这里不触碰。
                // 判据是内容而非时间戳 —— 原因见 MenuFileNeedsRebuild 的注释。
                if (MenuFileNeedsRebuild(targetPath))
                {
                    CreateMenuFile(targetPath);
                    Logger.Info($"ChemForge CUIx 菜单文件已生成：{targetPath}");
                }
                else
                {
                    Logger.Info($"ChemForge CUIx 菜单文件已存在且与当前菜单定义一致：{targetPath}");
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"ChemForge CUIx 生成失败（可能被 AutoCAD 占用）：{ex.Message}");
            }

            HookRibbonEvents();

            // 启动早期先试一次菜单栏项补挂：CUIx 常常还没被 CAD 加载完，失败也无妨
            // （文档激活钩子会再试，且内部有 2 秒节流）。
            EnsureMenuBarItem();

            // 声明式功能区选项卡只自检一次，**不创建**（创建等于掩盖部署缺失）
            CheckDeclarativeRibbonTab();
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
        /// 判断 CUIx 是否需要重建。
        ///
        /// 2026-10-16 修正（v1.1.0 加 INSTBREAK 时实际踩到）：这里原来只比时间戳
        /// （CUIx 早于插件程序集才重建），而部署脚本 Mount-ChemForgeMenu.ps1 用
        /// ZipFile 'Update' 重写工作空间时会把 CUIx 的 LastWriteTimeUtc 刷到挂载时刻 ——
        /// 挂载之后 CUIx 恒比 DLL 新，判据恒为"不用重建"，**改了 MenuDefinition 也传不进
        /// CUIx**：装完新版本，菜单栏与功能区里还是旧菜单项（本次表现为找不到
        /// "仪表线自动打断"）。改用**内容判据**：CUIx 里 MenuMacro 的 UID 集合必须与
        /// MenuDefinition 的命令项别名集合完全一致，多一个、少一个、读不出来都重建。
        /// </summary>
        private static bool MenuFileNeedsRebuild(string cuixPath)
        {
            if (!File.Exists(cuixPath)) return true;

            try
            {
                var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectCommandAliases(MenuDefinition.Menus, expected);

                var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var zip = System.IO.Compression.ZipFile.OpenRead(cuixPath))
                {
                    var entry = zip.GetEntry("MenuGroup.cui");
                    if (entry == null) return true;

                    string xml;
                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                    {
                        xml = reader.ReadToEnd();
                    }

                    // 只认菜单宏声明（<MenuMacro UID="ID_BreakInstrument">）；
                    // <Name>/<SmallImage> 等元素上的同名属性不会被误当成菜单项。
                    const string marker = "<MenuMacro UID=\"";
                    int at = xml.IndexOf(marker, StringComparison.Ordinal);
                    while (at >= 0)
                    {
                        int start = at + marker.Length;
                        int end = xml.IndexOf('"', start);
                        if (end > start) actual.Add(xml.Substring(start, end - start));
                        at = xml.IndexOf(marker, end > start ? end : start, StringComparison.Ordinal);
                    }

                    if (!actual.SetEquals(expected)) return true;

                    // 文本判据（2026-10-26）：只比别名时，改标题不改别名（例如把
                    // "管线自动打断（缺口 / 半圆架桥）" 简化成 "管线自动打断"）会被判成
                    // "不用重建"，界面永远显示旧名字。这里把 MenuMacro 的 <Name> 文本
                    // 与 DisplayTitle 集合再比一遍。
                    var expectedTitles = new HashSet<string>(StringComparer.Ordinal);
                    CollectCommandTitles(MenuDefinition.Menus, expectedTitles);
                    var actualTitles = new HashSet<string>(StringComparer.Ordinal);
                    foreach (System.Text.RegularExpressions.Match nameMatch in
                             System.Text.RegularExpressions.Regex.Matches(xml, "<Name[^>]*>([^<]*)</Name>"))
                    {
                        // CUIx 里标题是 XML 转义过的（&amp; 等），先还原再比，
                        // 否则标题里带 & 的项会永远判成"不一致"、每次启动都重建
                        actualTitles.Add(System.Net.WebUtility.HtmlDecode(nameMatch.Groups[1].Value));
                    }

                    if (!actualTitles.SetEquals(expectedTitles))
                    {
                        Logger.Info("检测到菜单项标题与 MenuDefinition 不一致，重新生成菜单文件。");
                        return true;
                    }

                    // 图标判据（2026-10-26）：只给"关于 ChemForge"加了下拉菜单小图标，
                    // 旧文件的 SmallImage 是空的；只比别名与标题都不会触发重建，图标
                    // 就永远不出现。这里单独确认 About 宏的 SmallImage 已写上文件名。
                    if (!HasAboutSmallImage(xml))
                    {
                        Logger.Info("检测到「关于 ChemForge」缺少下拉菜单图标，重新生成菜单文件。");
                        return true;
                    }

                        // 结构判据（2026-10-23 扁平化改造）：旧版文件根菜单用 <PopMenuRef>
                        // 级联引用 5 个子菜单，AutoCAD 2025 实测这种弹出菜单**永远不画到
                        // 菜单栏**（COM 模型 OnMenuBar=True、画面不渲染）；新版文件根菜单
                        // 直接挂 PopMenuItem，且不再生成级联子菜单。两者同时查：只要文件
                        // 还是旧结构就重建一次。注意 PopMenu 元素带 hasDiesel 属性，不能
                        // 按 "<PopMenu UID=" 字面匹配，用 UID 属性值本身判断。
                        var popRoot = zip.GetEntry("PopMenuRoot.cui");
                        if (popRoot != null)
                        {
                            string popXml;
                            using (var stream = popRoot.Open())
                            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                            {
                                popXml = reader.ReadToEnd();
                            }

                            if (popXml.Contains("<PopMenuRef", StringComparison.Ordinal) ||
                                popXml.Contains("UID=\"ID_TextMenu\"", StringComparison.Ordinal))
                            {
                                Logger.Info("检测到旧版级联菜单结构（PopMenuRef 根菜单 / 级联子菜单），重新生成扁平版菜单文件。");
                                return true;
                            }
                        }

                    return false;
                }
            }
            catch (System.Exception ex)
            {
                // 读不出来（文件损坏 / 被占用）就重建：绝不因为读取失败而沿用一份可疑的菜单
                Logger.Warning($"读取 CUIx 菜单文件失败，将重新生成：{ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// 递归收集 MenuDefinition 里所有命令项的别名（子菜单节点与分隔线不算）。
        /// </summary>
        private static void CollectCommandAliases(IEnumerable<MenuItemDefinition> items, HashSet<string> into)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (item.IsCommand) into.Add(item.Alias);
                CollectCommandAliases(item.Children, into);
            }
        }

        /// <summary>
        /// 递归收集 MenuDefinition 里所有命令项的显示名（即写进 CUIx &lt;Name&gt; 的文本，
        /// 子菜单节点与分隔线不算），用于判断标题改动后是否需要重建菜单文件。
        /// </summary>
        private static void CollectCommandTitles(IEnumerable<MenuItemDefinition> items, HashSet<string> into)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (item.IsCommand) into.Add(item.DisplayTitle);
                CollectCommandTitles(item.Children, into);
            }
        }

        /// <summary>
        /// 检查 CUIx 里「关于 ChemForge」的菜单宏是否带了下拉菜单小图标。
        /// 只读 XML 文本，不加载 CUI：判据是 <c>&lt;MenuMacro UID="ID_About"&gt;</c>
        /// 之后第一处 <c>&lt;SmallImage Name="..." /&gt;</c> 的 Name 等于宿主内置资源名
        /// <c>RCDATA_16_ABOUT</c>（2026-10-26 实测：写成 png 文件名时下拉菜单里不画，
        /// 因此这里只认资源名；旧版本写下的文件名会被判成"没图标"并触发重建）。
        /// </summary>
        private static bool HasAboutSmallImage(string xml)
        {
            const string aboutMarker = "<MenuMacro UID=\"ID_About\"";
            int at = xml.IndexOf(aboutMarker, StringComparison.Ordinal);
            if (at < 0) return false;

            const string imageMarker = "<SmallImage Name=\"";
            int imageAt = xml.IndexOf(imageMarker, at, StringComparison.Ordinal);
            if (imageAt < 0) return false;

            // 不能误读到下一条菜单宏的 SmallImage：About 的宏里就该有，且位置在下一个
            // <MenuMacro 之前
            int nextMacro = xml.IndexOf("<MenuMacro ", at + aboutMarker.Length, StringComparison.Ordinal);
            if (nextMacro >= 0 && imageAt > nextMacro) return false;

            int valueStart = imageAt + imageMarker.Length;
            int valueEnd = xml.IndexOf('"', valueStart);
            if (valueEnd <= valueStart) return false;

            string value = xml.Substring(valueStart, valueEnd - valueStart).Trim();
            return string.Equals(value, "RCDATA_16_ABOUT", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 依据 MenuDefinition 生成 CUIx 菜单文件。
        /// 注意：CUIx 标识符（菜单/宏名）与显示标签一律用纯显示名 DisplayTitle
        /// （2026-10-26 起标题里不再有 \t 留白，菜单宽度由 AutoCAD 按最长标签自动撑开）
        ///
        /// 生成内容（2026-10-23 扁平化定案）：
        ///  1. 菜单栏根菜单带**两个**别名：ID_ChemForgeMenu（工作空间 WSPop 按 pUID 引用）
        ///     与 POP16（官方"POP1–POP16 加载即上菜单栏"规则），且**直接平铺全部命令项**
        ///     （扁平 PopMenuItem）——级联 PopMenuRef 结构在 AutoCAD 2025 画不上菜单栏；
        ///  2. 声明式功能区：在局部 CUIx 里定义 RibbonTabSource + 每个一级菜单一个
        ///     RibbonPanelSource（含 RibbonRow / RibbonCommandButton），由 AutoCAD 自己
        ///     随 CUIx 加载与工作空间切换重建，插件不再在运行时创建 WPF 功能区对象。
        ///     注意：工作空间 WSPop 引用（宿主 PnP.cuix）由部署脚本写入，见
        ///     deploy\Dev\Register-ChemForgeMenu.ps1 —— 缺少它切工作空间会丢菜单。
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
                // 2026-10-23 扁平化：不再为每个一级分组生成级联子菜单（子项全是
                // PopMenuRef 的弹出菜单 AutoCAD 2025 实测永远不画到菜单栏），一级
                // 分组只用于功能区面板的划分；经典菜单栏根菜单直接平铺全部命令项，
                // 分组边界由菜单项顺序（原设计里的分隔线位置）隐含表达。
                // AcCui 的 PopMenuItem.IsSeparator 只读且无公开工厂，分隔线无法经
                // CUI API 生成，故根菜单无分隔线。
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
                    // 从嵌入资源释放到 CUIx 同目录，失败则为 null（按钮退化为纯文字）。
                    // 小图标（smallImage）只作用于经典下拉菜单项前面的图案：2026-10-23
                    // 用户明确不要满屏小图案，所以默认不传。2026-10-26 例外放行
                    // "关于 ChemForge" 一项——参照 AutoCAD 帮助菜单里"关于(A)"前面那枚
                    // 图标，下拉菜单里给它一个 16×16 的 about_16.png。
                    // 功能区大按钮用的是 largeImage，不受这里影响。
                    // 经典下拉菜单项前面的图案**只认宿主内置的资源 ID**：2026-10-26
                    // 实测同名 png 文件引用（chemforge_about_16.png）会被写进 CUIx、
                    // 功能区也能用，但下拉菜单里不画；而 acad.cuix 里这些项全是
                    // "RCDATA_16_ABOUT" 这种资源名（整份 CUIx 里没有一个 png）。
                    // 因此这里直接借用帮助菜单「关于(A)」那一枚内置图标。
                    var isAbout = string.Equals(
                        commandDefinition.Macro == null ? null : commandDefinition.Macro.Trim(),
                        CommandNames.ChemForge,
                        StringComparison.OrdinalIgnoreCase);
                    var smallIcon = withIcons && isAbout ? "RCDATA_16_ABOUT" : null;
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

                    new PopMenuItem(macro, commandDefinition.Title, rootMenu, rootMenu.PopMenuItems.Count);

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

                // 2026-10-23 扁平化：不再 new PopMenuRef(subMenu, rootMenu, ...)。
                // 根菜单直接挂命令项（见上方循环），级联引用结构画不上菜单栏。
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

            // AcCui 没有分隔线的公开 API（PopMenuItem.IsSeparator 只读且无工厂），
            // 而经典菜单要按一级分组画分隔线（用户明确要求，2026-10-23）——SaveAs 之后
            // 直接回写 zip 里的 PopMenuRoot.cui，在分组边界的 PopMenuItem 后插入分隔线。
            InsertMenuSeparators(cuixPath);
        }

        /// <summary>
        /// 在生成好的 CUIx 里给经典菜单根菜单按一级分组边界插入分隔线。
        /// 幂等：已带本函数 UID（PMIU_250_SEPnn）的分隔线不会重复插入；
        /// 失败只记日志——分隔线是锦上添花，绝不影响菜单文件本身。
        /// </summary>
        private static void InsertMenuSeparators(string cuixPath)
        {
            try
            {
                // 边界 = 每个一级分组（除最后一组）的最后一条命令宏别名
                var boundaryAliases = new List<string>();
                var menus = MenuDefinition.Menus;
                for (int gi = 0; gi < menus.Count - 1; gi++)
                {
                    string last = null;
                    foreach (var child in menus[gi].Children)
                    {
                        if (child.IsCommand) last = child.Alias;
                    }
                    if (last != null) boundaryAliases.Add(last);
                }
                if (boundaryAliases.Count == 0) return;

                using (var zip = System.IO.Compression.ZipFile.Open(cuixPath, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var entry = zip.GetEntry("PopMenuRoot.cui");
                    if (entry == null) return;
                    string xml;
                    using (var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8))
                    {
                        xml = reader.ReadToEnd();
                    }

                    bool changed = false;
                    int sep = 0;
                    foreach (var alias in boundaryAliases)
                    {
                        string uid = "PMIU_250_SEP" + (++sep).ToString("00");
                        if (xml.Contains(uid, StringComparison.Ordinal)) continue;   // 幂等
                        // 找到该宏所在的 <PopMenuItem>…</PopMenuItem> 整块，插在其后
                        string pattern =
                            "(<PopMenuItem\\b[^>]*>(?:(?!</PopMenuItem>).)*?MenuMacroID=\"" + alias +
                            "\"(?:(?!</PopMenuItem>).)*?</PopMenuItem>)";
                        var m = System.Text.RegularExpressions.Regex.Match(
                            xml, pattern, System.Text.RegularExpressions.RegexOptions.Singleline);
                        if (!m.Success) continue;
                        string sepXml =
                            "\r\n    <PopMenuItem IsSeparator=\"true\" hasDiesel=\"false\" UID=\"" + uid + "\">\r\n" +
                            "      <ModifiedRev MajorVersion=\"25\" MinorVersion=\"0\" UserVersion=\"1\" />\r\n" +
                            "    </PopMenuItem>";
                        xml = xml.Substring(0, m.Index + m.Length) + sepXml + xml.Substring(m.Index + m.Length);
                        changed = true;
                    }

                    if (!changed) return;
                    using (var outStream = entry.Open())
                    {
                        outStream.SetLength(0);
                        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
                        outStream.Write(bytes, 0, bytes.Length);
                    }
                }
                Logger.Info("ChemForge 菜单分隔线已写入 CUIx（按一级分组边界）。");
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"写入菜单分隔线失败（菜单仍可用，仅无分隔线）：{ex.Message}");
            }
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

        /// <summary>
        /// 确保 ChemForge 根菜单挂在经典菜单栏上（运行时补挂，幂等、自愈）。
        ///
        /// 为什么需要它（2026-10 本机实测结论）：
        ///  工作空间里登记的 <c>WSPop MenuGroup="CHEMFORGE"</c> 与 AutoCAD 自己写的跨组引用
        ///  （<c>MenuGroup="PNP"</c>）逐字节同构，但 CAD 2025 启动构建菜单栏时**不采纳**局部菜单
        ///  组的这条引用：CHEMFORGE 组已加载、根菜单对象存在（5 个子菜单齐全），但
        ///  <c>OnMenuBar=False</c>，栏里只有 12 个出厂菜单 + Express。切换工作空间也不会把它补上
        ///  （用户报的「切到别的工作空间就消失、切回来也没有」正是此现象）。
        ///  实测在运行中的 CAD 里执行一次 <c>根菜单.InsertInMenuBar(栏尾)</c> 即立刻显示，
        ///  且连续切换「三维基础 → 三维建模 → 草图与注释」后始终留在栏上。
        ///
        /// 与历史做法（已删除的 MountAsPnpMenu）的区别：
        ///  - 不建菜单组、不建菜单项、不写注册表、不调 <c>Application.ReloadAllMenus()</c>
        ///    （那条路径才是进程内崩溃的来源）；只对**已经由 CUIx 加载好**的根菜单补一次插入；
        ///  - 幂等：<c>OnMenuBar</c> 已为 true 时什么都不做，所以每 2 秒探测也不会产生副作用；
        ///  - 失败只记日志（例如 CUIx 还没被 CAD 加载完），下个节拍自然重试。
        /// </summary>
        /// <summary>
        /// 菜单栏补挂的单次诊断日志：每次启动只输出一条，便于现场判断停在哪一步。
        /// </summary>
        private static void LogMenuBarDiag(string message)
        {
            if (_menuBarDiagLogged)
            {
                return;
            }

            _menuBarDiagLogged = true;
            Logger.Info("菜单栏补挂诊断：" + message);
        }

        private static bool EnsureMenuBarItem()
        {
            if (DateTime.UtcNow - _lastMenuBarItemCheck < MenuBarItemCheckInterval)
            {
                return false;
            }

            _lastMenuBarItemCheck = DateTime.UtcNow;

            try
            {
                var acad = GetAcadInterop();
                if (acad == null)
                {
                    return false; // CAD 尚未就绪（启动早期/关闭中）
                }

                Autodesk.AutoCAD.Interop.AcadMenuGroup group;
                try
                {
                    // 按组名取局部菜单组；CUIx 还没加载完时这里会抛 COMException，直接放弃等下次
                    group = acad.MenuGroups.Item(MenuDefinition.MenuGroupName);
                }
                catch
                {
                    LogMenuBarDiag("CHEMFORGE 菜单组尚未由宿主加载，稍后重试。");
                    return false;
                }

                if (group == null)
                {
                    LogMenuBarDiag("CHEMFORGE 菜单组取到空对象，稍后重试。");
                    return false;
                }

                Autodesk.AutoCAD.Interop.AcadPopupMenu root = null;
                for (int i = 0; i < group.Menus.Count; i++)
                {
                    var candidate = group.Menus.Item(i);
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(
                            candidate.TagString,
                            MenuDefinition.RootMenuAlias,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        root = candidate;
                        break;
                    }
                }

                if (root == null)
                {
                    LogMenuBarDiag("菜单组里找不到别名为 " + MenuDefinition.RootMenuAlias + " 的根菜单，菜单栏补挂跳过。");
                    return false; // 根菜单还没读进来
                }

                if (root.OnMenuBar)
                {
                    _menuBarItemLogged = false; // 已在栏上，日志标志复位
                    LogMenuBarDiag("根菜单已在菜单栏上（OnMenuBar=True），无需补挂。");
                    return true;
                }

                int barCount = acad.MenuBar.Count;
                root.InsertInMenuBar(barCount);

                if (!_menuBarItemLogged)
                {
                    _menuBarItemLogged = true;
                    Logger.Info($"ChemForge 菜单未被工作空间挂上菜单栏，已在运行时补挂到第 {barCount + 1} 位。");
                }

                return true;
            }
            catch (System.Exception ex)
            {
                // 典型的"下一次就好了"：CUIx 正在加载、CAD 正在切换工作空间重建菜单栏
                Logger.Warning($"补挂 ChemForge 菜单栏项失败（可忽略，下个节拍重试）：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 取 AutoCAD 的 ActiveX 应用对象（进程内，通过 ROT）。
        /// 2025 的托管 <c>Application.AcadApplication</c> 属性已不存在，但 ROT 里仍有注册，
        /// 用 <c>oleaut32!GetActiveObject</c> 取出来即可（不会启动新实例）。
        /// </summary>
        private static Autodesk.AutoCAD.Interop.AcadApplication GetAcadInterop()
        {
            foreach (var progId in AcadProgIds)
            {
                Type coclass;
                try
                {
                    coclass = Type.GetTypeFromProgID(progId, false);
                }
                catch
                {
                    continue;
                }

                if (coclass == null)
                {
                    continue;
                }

                var clsid = coclass.GUID;
                try
                {
                    object comObject;
                    GetActiveObject(ref clsid, IntPtr.Zero, out comObject);
                    var acad = comObject as Autodesk.AutoCAD.Interop.AcadApplication;
                    if (acad != null)
                    {
                        return acad;
                    }
                }
                catch
                {
                    // ROT 里没有（比如 CAD 还没完成启动），试下一个 ProgID / 等下次节拍
                }
            }

            // 2026-10-24 实测：按 ProgID 走 GetActiveObject 在本机一律失败（ROT 里的条目
            // 不是 ProgID 形式的显示名），但对象确实在 ROT 里 —— 直接枚举 ROT 兜底。
            return GetAcadInteropFromRot();
        }

        /// <summary>
        /// 直接枚举运行对象表（ROT）找 AutoCAD 的 ActiveX 应用对象。
        /// 只对显示名像 acad 的条目取对象，避免对无关的文件 moniker 做绑定。
        /// </summary>
        private static Autodesk.AutoCAD.Interop.AcadApplication GetAcadInteropFromRot()
        {
            object rotObject = null;
            try
            {
                if (GetRunningObjectTable(0, out rotObject) != 0 || rotObject == null)
                {
                    return null;
                }

                var rot = (System.Runtime.InteropServices.ComTypes.IRunningObjectTable)rotObject;
                System.Runtime.InteropServices.ComTypes.IEnumMoniker enumMoniker;
                rot.EnumRunning(out enumMoniker);
                if (enumMoniker == null)
                {
                    return null;
                }

                var monikers = new System.Runtime.InteropServices.ComTypes.IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker == null)
                    {
                        continue;
                    }

                    string displayName = null;
                    try
                    {
                        moniker.GetDisplayName(null, null, out displayName);
                    }
                    catch
                    {
                        displayName = null;
                    }

                    if (string.IsNullOrEmpty(displayName)
                        || displayName.IndexOf("acad", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    object comObject;
                    if (rot.GetObject(moniker, out comObject) != 0 || comObject == null)
                    {
                        continue;
                    }

                    var acad = comObject as Autodesk.AutoCAD.Interop.AcadApplication;
                    if (acad != null)
                    {
                        return acad;
                    }
                }
            }
            catch
            {
                // 枚举失败就当取不到，等下次节拍
            }
            finally
            {
                if (rotObject != null)
                {
                    try
                    {
                        System.Runtime.InteropServices.Marshal.ReleaseComObject(rotObject);
                    }
                    catch
                    {
                        // 忽略
                    }
                }
            }

            return null;
        }

        [System.Runtime.InteropServices.DllImport("ole32.dll")]
        private static extern int GetRunningObjectTable(int reserved, out object pprot);

        /// <summary>
        /// 列出 COM 模型里已加载的菜单组（局部菜单是否真的被宿主载入），仅供诊断日志。
        /// </summary>
        private static string DescribeMenuGroups()
        {
            try
            {
                var acad = GetAcadInterop();
                if (acad == null)
                {
                    return "ROT 中取不到 AutoCAD 实例";
                }

                var parts = new List<string>();
                int count = acad.MenuGroups.Count;
                for (int i = 0; i < count && i < 30; i++)
                {
                    var g = acad.MenuGroups.Item(i);
                    string file = string.Empty;
                    try { file = g.MenuFileName; } catch { }
                    parts.Add(string.IsNullOrEmpty(file) ? g.Name : $"{g.Name}@{file}");
                }

                return $"共{count}个：{string.Join(", ", parts)}";
            }
            catch (System.Exception ex)
            {
                return "读取菜单组失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 记录当前工作空间（WSCURRENT）。功能区选项卡引用写在工作空间里，
        /// 只有「当前工作空间」引用了它才会被画出来，故验收时必须知道是哪一个。
        /// </summary>
        private static string DescribeCurrentWorkspace()
        {
            try
            {
                var acad = GetAcadInterop();
                if (acad == null)
                {
                    return "ROT 中取不到 AutoCAD 实例";
                }

                try
                {
                    var value = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("WSCURRENT");
                    return "WSCURRENT=" + (value == null ? "<null>" : value.ToString());
                }
                catch (System.Exception ex)
                {
                    return "读取 WSCURRENT 失败：" + ex.Message;
                }
            }
            catch (System.Exception ex)
            {
                return "读取工作空间失败：" + ex.Message;
            }
        }

        // 版本化 ProgID 优先，最后退回与版本无关的那个（ROT 里通常只有一个实例）
        private static readonly string[] AcadProgIds =
        {
            "AutoCAD.Application.25",
            "AutoCAD.Application.24",
            "AutoCAD.Application",
        };

        [System.Runtime.InteropServices.DllImport("oleaut32.dll", PreserveSig = false)]
        private static extern void GetActiveObject(
            ref Guid rclsid,
            IntPtr pvReserved,
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.IUnknown)] out object ppunk);


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
        /// 挂钩文档激活事件，只维持**随图纸变化**的两项状态：
        /// 1) MENUBAR 随图纸保存（换图/新建后可能又是 0）——它决定经典菜单栏是否可见；
        /// 2) 主窗口偶发残留「总在最前」标志。
        ///
        /// 2026-10-24 删除了 Idle 钩子。它此前做了三件事，全部是主线程上的周期性宿主调用：
        ///   * 启动 20 秒内每 0.5 秒探测功能区（之后每 5 秒），缺失就用 RibbonBuilder 现造；
        ///   * 每 2 秒走 4 次 COM 补挂菜单栏项（GetActiveObject → MenuGroups.Item → 遍历
        ///     Menus → MenuBar.Count/OnMenuBar）；
        ///   * 每 5 秒复查 MENUBAR 与置顶标志。
        /// AutoCAD 的 Idle 由消息泵驱动、鼠标一动就触发，所以这三项在"空闲"时反而是持续的
        /// 主线程打断（2026-10 删除 WSCURRENT 轮询是对同一个病根的处理，当时以 2 秒节流
        /// 又请了回来）。现在菜单栏项与功能区选项卡都改为**声明式挂载**（宿主 PnP.cuix 每个
        /// 工作空间都有 CHEMFORGE 的 WSPop 与 WSRibbonTabSourceReference，见
        /// deploy\Dev\Register-ChemForgeMenu.ps1），运行时无需补挂、无需重建，
        /// 故此处的原则是：**不在 Idle 里轮询宿主状态**。
        /// </summary>
        private static void HookRibbonEvents()
        {
            if (_hooked)
                return;

            Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.DocumentActivated +=
                (s, e) =>
                {
                    EnsureClassicMenuBarVisible();
                    EnsureMenuBarItem();
                    EnsureMainWindowNotTopmost();
                };

            _hooked = true;
            Logger.Info("已注册文档激活钩子（仅随图纸变化的状态；不在 Idle 里轮询宿主状态）。");

            // 切换工作空间会重建经典菜单栏，运行时插入的那一项会被一并清掉；
            // 用系统变量变更事件（而不是轮询）在重建后补回去，保证"换了工作空间也不掉"。
            Autodesk.AutoCAD.ApplicationServices.Core.Application.SystemVariableChanged +=
                (s, e) =>
                {
                    try
                    {
                        if (!string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        _lastMenuBarItemCheck = DateTime.MinValue;
                        StartMenuBarWatch();
                    }
                    catch
                    {
                        // 事件里出错不能影响宿主
                    }
                };
        }

        /// <summary>
        /// 声明式功能区选项卡的启动自检：**只判断、不创建**。
        ///
        /// 选项卡由宿主 PnP.cuix 各工作空间的 WSRibbonTabSourceReference 引用，
        /// AutoCAD 随 CUIx 加载与工作空间切换自己重建（与 Express Tools 同构）。
        /// 这里若发现缺失，说明部署没做对；日志直接指出补救命令，而不是悄悄现造一套
        /// WPF 对象把问题掩盖过去。
        ///
        /// 功能区是应用级 UI，开始页（未打开任何图纸）下同样存在，故不做"必须有文档"判断。
        /// </summary>
        private static void CheckDeclarativeRibbonTab()
        {
            try
            {
                if (RibbonBuilder.IsRibbonPresent())
                {
                    Logger.Info($"声明式功能区选项卡已加载：{RibbonBuilder.TabId}");
                    LogRibbonTabContent();
                    return;
                }

                // 插件在 UI 初始化早期就被加载，此时 ComponentManager.Ribbon 往往还是 null，
                // 立刻判定会把"功能区还没建好"误报成"CUIx 没挂载"。改为一次性等待（最多 10 秒），
                // 功能区出现后再判，并打印一次选项卡/菜单组清单，便于定位到底缺哪一环。
                StartRibbonDiagnosticsWatch();
                StartMenuBarWatch();
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"功能区选项卡自检失败（可忽略）：{ex.Message}");
            }
        }

        /// <summary>
        /// 把 ChemForge 选项卡的内容（几个面板、每个面板几个按钮）打一行日志。
        /// 只读不写：用来确认 CUIx 里的面板定义真的被宿主解析了（不是只挂了个空壳选项卡）。
        /// </summary>
        private static void LogRibbonTabContent()
        {
            try
            {
                var ribbon = Autodesk.Windows.ComponentManager.Ribbon;
                if (ribbon == null)
                {
                    return;
                }

                Autodesk.Windows.RibbonTab target = null;
                foreach (var tab in ribbon.Tabs)
                {
                    if (tab == null)
                    {
                        continue;
                    }

                    if (string.Equals(tab.Id, RibbonBuilder.TabId, StringComparison.Ordinal) ||
                        (tab.Title != null && tab.Title.Contains(MenuDefinition.RootMenuTitle)))
                    {
                        target = tab;
                        break;
                    }
                }

                if (target == null)
                {
                    Logger.Info($"ChemForge 选项卡内容：功能区里没找到该选项卡（共 {ribbon.Tabs?.Count ?? 0} 个）。");
                    return;
                }

                var panels = target.Panels;
                var items = 0;
                if (panels != null)
                {
                    foreach (var p in panels)
                    {
                        try
                        {
                            if (p != null && p.Source != null && p.Source.Items != null)
                            {
                                items += p.Source.Items.Count;
                            }
                        }
                        catch
                        {
                            // 单个面板读不到不影响整体
                        }
                    }
                }

                if (panels == null || panels.Count == 0)
                {
                    Logger.Info("ChemForge 选项卡内容：面板尚未实例化（选项卡从未被激活；AdWindows 在激活时才创建面板，属正常行为）。");
                    return;
                }

                Logger.Info($"ChemForge 选项卡内容：面板 {panels.Count} 个、按钮合计 {items} 个。");
            }
            catch (System.Exception ex)
            {
                try { Logger.Warning($"功能区内容诊断失败（可忽略）：{ex.GetType().Name} {ex.Message}"); } catch { }
            }
        }

        private static System.Windows.Threading.DispatcherTimer _ribbonWatch;
        private static int _ribbonWatchTicks;
        private static System.Windows.Threading.DispatcherTimer _menuBarWatch;
        private static int _menuBarWatchTicks;

        /// <summary>
        /// 菜单栏补挂（运行时 InsertInMenuBar）的启动自检。
        ///
        /// 背景：经典菜单栏靠工作空间里的 WSPop 引用决定哪些菜单上栏，但这台 AutoCAD 在
        /// 启动时只会把**已经在 CUIx 组装阶段就登记好**的菜单画上栏，后加进工作空间的引用
        /// 一律静默丢弃（2026-10-24 实测：把 ChemForge 根菜单复制进宿主菜单组、连同菜单宏
        /// 一起补齐、并在 9+3 个工作空间里写引用，冷启动画面上依旧没有它；同一份文件里的
        /// 功能区选项卡引用却是生效的）。
        ///
        /// 用户 2026-10-24 决定走运行时插入：加载后一次 InsertInMenuBar，卸载时移除；
        /// 不切工作空间、不重建功能区、不写宿主文件。这里只重试有限次（菜单组/CUIx 可能
        /// 比插件晚就绪），成功即停，不在 Idle 里长期轮询。
        /// </summary>
        private static void StartMenuBarWatch()
        {
            if (_menuBarWatch != null)
            {
                return;
            }

            _menuBarWatchTicks = 0;
            _menuBarWatch = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            _menuBarWatch.Tick += (sender, args) =>
            {
                _menuBarWatchTicks++;
                try
                {
                    if (EnsureMenuBarItem())
                    {
                        Logger.Info($"菜单栏就绪（第 {_menuBarWatchTicks} 次检查）");
                        StopMenuBarWatch();
                        return;
                    }

                    if (_menuBarWatchTicks >= 8)
                    {
                        StopMenuBarWatch();
                        Logger.Warning("菜单栏补挂 8 次检查仍未成功，本次会话不加菜单项。");
                    }
                }
                catch (System.Exception ex)
                {
                    StopMenuBarWatch();
                    Logger.Warning($"菜单栏补挂失败（可忽略）：{ex.Message}");
                }
            };
            _menuBarWatch.Start();
        }

        private static void StopMenuBarWatch()
        {
            if (_menuBarWatch == null)
            {
                return;
            }

            _menuBarWatch.Stop();
            _menuBarWatch = null;
            _menuBarWatchTicks = 0;
        }

        /// <summary>
        /// 启动后等功能区建好再做一次自检（一次性，不作周期性轮询）。
        /// </summary>
        private static void StartRibbonDiagnosticsWatch()
        {
            if (_ribbonWatch != null)
            {
                return;
            }

            _ribbonWatchTicks = 0;
            _ribbonWatch = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _ribbonWatch.Tick += (sender, args) =>
            {
                _ribbonWatchTicks++;
                try
                {
                    if (RibbonBuilder.IsRibbonPresent())
                    {
                        StopRibbonWatch();
                        Logger.Info($"声明式功能区选项卡已加载：{RibbonBuilder.TabId}"
                            + $"（启动后约 {_ribbonWatchTicks} 秒）");
                        LogRibbonTabContent();
                        return;
                    }

                    if (_ribbonWatchTicks == 2 || _ribbonWatchTicks == 10)
                    {
                        Logger.Info($"功能区诊断（启动后约 {_ribbonWatchTicks} 秒）："
                            + RibbonBuilder.DescribeTabs());
                        Logger.Info("菜单组诊断：" + DescribeMenuGroups());
                        Logger.Info("工作空间诊断：" + DescribeCurrentWorkspace());
                    }

                    // 声明与引用都是对的，但 AutoCAD 建启动功能区时 CHEMFORGE 组还没注册进内存，
                    // 引用被静默丢掉。切一次工作空间（CUI 对话框"应用"的同一个动作）就会按引用
                    // 重建，选项卡随之出现；只做一次，不在 Idle 里轮询。
                    if (_ribbonWatchTicks == 3)
                    {
                        TryReapplyWorkspaceOnce();
                    }

                    if (_ribbonWatchTicks >= 10)
                    {
                        StopRibbonWatch();
                        Logger.Warning(
                            $"功能区选项卡 {RibbonBuilder.TabId} 在启动 10 秒后仍未被宿主加载：" +
                            "宿主 CUIx 的工作空间缺少 WSRibbonTabSourceReference，" +
                            "或局部菜单 ChemForge.cuix 尚未随 CUIx 载入。" +
                            "请在 AutoCAD 完全退出后运行 deploy\\Dev\\Register-ChemForgeMenu.ps1，再启动 AutoCAD。");
                    }
                }
                catch (System.Exception ex)
                {
                    StopRibbonWatch();
                    Logger.Warning($"功能区自检（延迟）失败（可忽略）：{ex.Message}");
                }
            };
            _ribbonWatch.Start();
        }

        private static void StopRibbonWatch()
        {
            if (_ribbonWatch == null)
            {
                return;
            }

            _ribbonWatch.Stop();
            _ribbonWatch = null;
        }

        private static bool _workspaceReapplied;

        /// <summary>
        /// 启动后一次性"重应用工作空间"（等价于 CUI 对话框里点一次"应用"）。
        ///
        /// 2026-10-24 实测：CUIx 里的 RibbonTabSource 与宿主各工作空间的
        /// WSRibbonTabSourceReference 全部正确（与 Express Tools 逐字段同构），
        /// 但 CAD 建启动功能区那一刻 CHEMFORGE 组尚未注册进内存，引用被静默丢掉；
        /// 切一次工作空间后 AutoCAD 按引用重建，选项卡正常出现。
        /// 这里只切一下再切回来，不建 WPF 对象、不轮询、只做一次。
        /// </summary>
        private static void TryReapplyWorkspaceOnce()
        {
            if (_workspaceReapplied)
            {
                return;
            }

            _workspaceReapplied = true;

            // 2026-10-25：改成"每个版本只跑一次"。
            // 背景：CUIx 里写好的选项卡引用，宿主只在"应用工作空间"那一刻才会采纳；
            // 新装机器首次启动不应用，选项卡就不出现（实测新装后连续两次启动都无）。
            // 但每次启动都跑又会在用户开机后被宿主重建功能区，冲掉手工开/关的状态。
            // 折中：装完/升级后的第一次启动静默应用一次，之后写标记文件永不重复；
            // 应用前记下功能区开合状态，应用后还原（见 CaptureRibbonState）。
            // 排查时可设 CHEMFORGE_WORKSPACE_REAPPLY=0 强制关闭，=1 强制重跑一次。
            string switchValue = System.Environment.GetEnvironmentVariable("CHEMFORGE_WORKSPACE_REAPPLY");
            if (string.Equals(switchValue, "0", StringComparison.Ordinal))
            {
                Logger.Info("工作空间重应用：已被环境变量 CHEMFORGE_WORKSPACE_REAPPLY=0 关闭。");
                return;
            }

            bool forced = string.Equals(switchValue, "1", StringComparison.Ordinal);
            string markerPath = GetWorkspaceReapplyMarkerPath();
            if (!forced && markerPath != null && System.IO.File.Exists(markerPath))
            {
                Logger.Info("工作空间重应用：本版本已应用过，跳过（仅新装/升级后首次启动需要）。");
                return;
            }

            MarkWorkspaceReapplyDone();

            try
            {
                object rawCurrent = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("WSCURRENT");
                string current = rawCurrent == null ? string.Empty : rawCurrent.ToString();

                // 应用工作空间会用 CUIx 里的定义覆盖"功能区是否显示"，
                // 用户手动开/关的状态会被冲掉，所以先记下来、之后还原。
                int ribbonStateBefore = CaptureRibbonState();

                // 启动时 WSCURRENT 可能为空（没有"当前工作空间"就不会按引用重建功能区）。
                // 这种情形直接把一个工作空间应用上去，等价于用户在 CUI 里点一次"确定"。
                if (string.IsNullOrWhiteSpace(current))
                {
                    string preferred = FindPreferredWorkspace();
                    if (preferred == null)
                    {
                        Logger.Warning("工作空间重应用：宿主 CUIx 里没有可用工作空间，跳过。");
                        return;
                    }

                    Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("WSCURRENT", preferred);
                    RestoreRibbonState(ribbonStateBefore, "应用工作空间后");
                    ScheduleRibbonStateRestore(ribbonStateBefore);
                    Logger.Info($"工作空间重应用（启动时没有当前工作空间）：已应用 {preferred}；"
                        + "ChemForge 选项卡" + (RibbonBuilder.IsRibbonPresent() ? "已出现。" : "仍未出现。"));
                    return;
                }

                string alternative = FindAlternativeWorkspace(current);
                if (alternative == null)
                {
                    Logger.Warning("工作空间重应用：宿主 CUIx 里找不到可用于切换的其它工作空间，跳过。");
                    return;
                }

                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("WSCURRENT", alternative);
                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("WSCURRENT", current);
                RestoreRibbonState(ribbonStateBefore, "切换工作空间后");
                ScheduleRibbonStateRestore(ribbonStateBefore);
                _workspaceReapplyTarget = current;
                _workspaceRibbonState = ribbonStateBefore;
                StartWorkspaceRestoreCheck();

                Logger.Info($"工作空间重应用：{current} → {alternative} → {current}；"
                    + "ChemForge 选项卡" + (RibbonBuilder.IsRibbonPresent() ? "已出现。" : "仍未出现。"));
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"工作空间重应用失败（可忽略）：{ex.Message}");
            }
        }

        /// <summary>记录"该版本已做过一次性工作空间应用"的标记文件（不写进宿主的任何配置）。</summary>
        private static string GetWorkspaceReapplyMarkerPath()
        {
            try
            {
                string dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    "ChemForge");
                System.IO.Directory.CreateDirectory(dir);

                string version = typeof(MenuServiceNet80).Assembly.GetName().Version.ToString();
                return System.IO.Path.Combine(dir, $"workspace-applied-{version}.flag");
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"工作空间重应用：无法定位标记文件目录（{ex.Message}），本次不做应用。");
                return null;
            }
        }

        private static void MarkWorkspaceReapplyDone()
        {
            try
            {
                string path = GetWorkspaceReapplyMarkerPath();
                if (path != null)
                {
                    System.IO.File.WriteAllText(path, DateTime.Now.ToString("o"));
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"工作空间重应用：写标记文件失败（{ex.Message}）。");
            }
        }

        /// <summary>
        /// 工作空间的应用是异步的，宿主可能在我们还原之后又把功能区状态写一遍；
        /// 这里再排一次延迟还原（幂等，已经是原值就什么都不做）。
        /// </summary>
        private static void ScheduleRibbonStateRestore(int before)
        {
            if (before < 0)
            {
                return;
            }

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2.5),
            };

            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                RestoreRibbonState(before, "延迟还原");
            };
            timer.Start();
        }

        private static string _workspaceReapplyTarget;
        private static int _workspaceRibbonState = -1;
        private static System.Windows.Threading.DispatcherTimer _workspaceRestoreCheck;

        /// <summary>读功能区显示状态（0 = 不显示，1 = 显示）；读不到返回 -1。</summary>
        private static int CaptureRibbonState()
        {
            try
            {
                object raw = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("RIBBONSTATE");
                if (raw != null)
                {
                    return System.Convert.ToInt32(raw);
                }
            }
            catch (System.Exception)
            {
            }

            // 读不到 RIBBONSTATE 时用"功能区对象在不在"判断：AdWindows 只有在功能区
            // 显示时才会创建 Ribbon 对象，存在即视为显示。
            try
            {
                return RibbonBuilder.IsRibbonPresent() ? 1 : 0;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// 应用/切换工作空间会按 CUIx 里存的定义改写"功能区是否显示"，
        /// 用户手动开着的功能区会被收起。这里把用户原来的显示状态还回去。
        /// </summary>
        private static void RestoreRibbonState(int before, string context)
        {
            if (before < 0)
            {
                return;
            }

            try
            {
                object raw = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("RIBBONSTATE");
                int now = raw == null ? -1 : System.Convert.ToInt32(raw);
                if (now == before)
                {
                    return;
                }

                Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("RIBBONSTATE", before);
                Logger.Info($"功能区显示状态已还原（{context}）：{now} → {before}");
                return;
            }
            catch (System.Exception)
            {
            }

            // 有些版本读不到 RIBBONSTATE（初始化为空时也读不到），退回用命令开/关功能区。
            try
            {
                bool present = RibbonBuilder.IsRibbonPresent();
                if (before == 1 && !present)
                {
                    SendRibbonCommand("_.RIBBON ");
                    Logger.Info($"功能区显示状态已还原（{context}）：按命令重新显示功能区。");
                }
                else if (before == 0 && present)
                {
                    SendRibbonCommand("_.RIBBONCLOSE ");
                    Logger.Info($"功能区显示状态已还原（{context}）：按命令收起功能区。");
                }
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>在文档上下文里异步执行一条功能区开/关命令（没有文档时直接跳过）。</summary>
        private static void SendRibbonCommand(string command)
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.SendStringToExecute(command + "\n", true, false, true);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"发送功能区命令失败（可忽略）：{ex.Message}");
            }
        }

        /// <summary>
        /// 重应用是"切走再切回"两次 SetSystemVariable，宿主异步处理，偶尔会停在中间那个
        /// 工作空间上。这里只在几秒后校验一次：不是用户原来那个就再设回去。
        /// </summary>
        private static void StartWorkspaceRestoreCheck()
        {
            CancelWorkspaceRestoreCheck();

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4),
            };

            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                try
                {
                    string target = _workspaceReapplyTarget;
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        return;
                    }

                    object raw = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("WSCURRENT");
                    string now = raw == null ? string.Empty : raw.ToString();
                    if (!string.Equals(now, target, StringComparison.Ordinal))
                    {
                        Autodesk.AutoCAD.ApplicationServices.Application.SetSystemVariable("WSCURRENT", target);
                        Logger.Info($"工作空间复位：{now} → {target}");
                    }

                    // 复位本身也是一次"应用工作空间"，同样会把功能区显示状态改掉。
                    RestoreRibbonState(_workspaceRibbonState, "工作空间复位后");
                }
                catch (System.Exception)
                {
                }
            };

            _workspaceRestoreCheck = timer;
            timer.Start();
        }

        private static void CancelWorkspaceRestoreCheck()
        {
            if (_workspaceRestoreCheck == null)
            {
                return;
            }

            _workspaceRestoreCheck.Stop();
            _workspaceRestoreCheck = null;
        }

        /// <summary>启动时用于"应用一个工作空间"的首选名字：优先经典二维工作空间。</summary>
        private static string FindPreferredWorkspace()
        {
            try
            {
                string supportDir = GetSupportDirectory();
                if (string.IsNullOrEmpty(supportDir))
                {
                    return null;
                }

                foreach (string fileName in new[] { "PnP.cuix", "acad.CUIX", "acad.cuix" })
                {
                    string path = Path.Combine(supportDir, fileName);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var names = ReadWorkspaceNames(path);
                    if (names.Count == 0)
                    {
                        continue;
                    }

                    foreach (string name in names)
                    {
                        if (string.Equals(name, "草图与注释", StringComparison.OrdinalIgnoreCase))
                        {
                            return name;
                        }
                    }

                    foreach (string name in names)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
            }

            return null;
        }

        /// <summary>从宿主 CUIx 的工作空间列表里挑一个与当前不同的名字。</summary>
        private static string FindAlternativeWorkspace(string current)
        {
            try
            {
                string supportDir = GetSupportDirectory();
                if (string.IsNullOrEmpty(supportDir))
                {
                    return null;
                }

                foreach (string fileName in new[] { "PnP.cuix", "acad.CUIX", "acad.cuix" })
                {
                    string path = Path.Combine(supportDir, fileName);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    foreach (string name in ReadWorkspaceNames(path))
                    {
                        if (!string.IsNullOrWhiteSpace(name)
                            && !string.Equals(name, current, StringComparison.OrdinalIgnoreCase))
                        {
                            return name;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // 读不到就当作没有备选，交给调用方跳过。
            }

            return null;
        }

        /// <summary>只读解析 CUIx 里的工作空间名字（WorkspaceRoot.cui / Name）。</summary>
        private static System.Collections.Generic.List<string> ReadWorkspaceNames(string cuixPath)
        {
            var names = new System.Collections.Generic.List<string>();
            using (var zip = System.IO.Compression.ZipFile.OpenRead(cuixPath))
            {
                var entry = zip.GetEntry("WorkspaceRoot.cui");
                if (entry == null)
                {
                    return names;
                }

                using (var stream = entry.Open())
                using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                {
                    var document = new System.Xml.XmlDocument();
                    document.LoadXml(reader.ReadToEnd());
                    var root = document.DocumentElement;
                    if (root == null)
                    {
                        return names;
                    }

                    foreach (System.Xml.XmlNode node in root.ChildNodes)
                    {
                        if (node.NodeType != System.Xml.XmlNodeType.Element)
                        {
                            continue;
                        }

                        var nameNode = node.SelectSingleNode("Name");
                        if (nameNode != null && !string.IsNullOrWhiteSpace(nameNode.InnerText))
                        {
                            names.Add(nameNode.InnerText);
                        }
                    }
                }
            }

            return names;
        }

        /// <inheritdoc />
        public void UnloadMenu()
        {
            // 功能区选项卡是**声明式挂载**（宿主 CUIx 的工作空间引用 + 局部菜单登记），
            // 归 AutoCAD 所有，插件退出时不拆；经典菜单栏项是本会话运行时插入的，
            // 退出前摘掉，保证"卸载后菜单栏回到原样"（与 Register -Uninstall 对称）。
            RemoveMenuBarItem();

            // 只停掉自己起的定时器（不碰宿主状态）。
            CancelWorkspaceRestoreCheck();
            StopRibbonWatch();
            StopMenuBarWatch();
            Logger.Info("ChemForge 插件退出：已摘除运行时插入的菜单栏项。");
        }

        /// <summary>
        /// 摘掉运行时插入的菜单栏项（对称清理，失败只记日志）。
        /// </summary>
        private static void RemoveMenuBarItem()
        {
            try
            {
                var acad = GetAcadInterop();
                if (acad == null)
                {
                    return;
                }

                var group = acad.MenuGroups.Item(MenuDefinition.MenuGroupName);
                if (group == null)
                {
                    return;
                }

                for (int i = 0; i < group.Menus.Count; i++)
                {
                    var candidate = group.Menus.Item(i);
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(
                            candidate.TagString,
                            MenuDefinition.RootMenuAlias,
                            StringComparison.OrdinalIgnoreCase)
                        && candidate.OnMenuBar)
                    {
                        candidate.RemoveFromMenuBar();
                        Logger.Info("已从菜单栏摘除 ChemForge 菜单项。");
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Warning($"摘除菜单栏项失败（可忽略）：{ex.Message}");
            }
        }
    }
}
#endif