#if NET48
using System;
using ChemForge.CAD.Abstraction;
using ChemForge.Utilities;

namespace ChemForge.CAD.Impl
{
    /// <summary>
    /// .NET Framework 4.8 环境下的菜单服务实现（兼容 AutoCAD 2019-2024）。
    ///
    /// 菜单栏显示走与 Net80 相同的**声明式挂载**路线（2026-10-23 实测定案，缺一不可）：
    ///  1. ChemForge.cuix 由部署脚本生成/安装到 AutoCAD 用户支持目录，并登记为宿主
    ///     CUIx（PnP.cuix）的局部菜单（PartialMenuFile），见
    ///     deploy\Dev\Register-ChemForgeMenu.ps1；
    ///  2. 根菜单带官方菜单栏别名（POP1–POP16 之一）；
    ///  3. **宿主 CUIx 的每个工作空间都写入了指向根菜单的 WSPop 引用**（与 Express
    ///     逐字同构）。POPn 别名的"加载即上栏"只在加载那一刻生效，切工作空间重建
    ///     菜单栏时按 WSPop 列表来，缺引用就掉且切回来不回来（Net80 侧反复实测）；
    ///  4. 根菜单必须是**扁平结构**（直接命令项）：子项全是 PopMenuRef 的弹出菜单
    ///     AutoCAD 2025 画不上菜单栏（模型在、画面不渲染）；
    ///  5. 插件运行时只做两件小事：把 MENUBAR 置 1、换图后复查一次。
    ///
    /// 已删除的历史做法（每一条都对应一次现场故障）：
    ///  - 通过 COM（AcadApplication.MenuGroups / MenuBar / InsertInMenuBar）在运行时
    ///    建菜单组、建菜单并插入菜单栏，外加 Idle 重试：既不是显示的必经路径，又是在
    ///    AutoCAD 进程内动态调用 COM 的高风险动作；
    ///  - 逐项 AddMenuItem(0, ...) 的插入位置在低版本 COM 接口上不可控（菜单项倒序、
    ///    分隔线错位）——改用 CUIx 声明式定义后这类顺序问题在文件里就是确定的。
    ///
    /// 低版本（2019-2024）没有 AcCui 托管程序集，故这里不生成 CUIx 文件；
    /// CUIx 与局部菜单登记由部署脚本在 CAD 关闭时写入。
    /// </summary>
    public sealed class MenuServiceNet48 : IMenuService
    {
        private static bool _hooked;

        /// <inheritdoc />
        public void EnsureMenuLoaded(string cuixPath)
        {
            if (string.IsNullOrWhiteSpace(cuixPath))
                throw new ArgumentException("CUIx 菜单文件路径不能为空。", nameof(cuixPath));

            // 说明：这里曾经调用 CreateMenusInMemory()（COM 建组 + InsertInMenuBar）
            // 并注册 Idle 重试。菜单栏显示已由 CUIx 的 POP1–POP16 别名负责，
            // 运行时 COM 建菜单不再参与，故整段删除。
            Logger.Info($"ChemForge 菜单为声明式挂载（CUIx 提示路径：{cuixPath}），运行时不再用 COM 建菜单。");

            EnsureClassicMenuBarVisible();
            HookDocumentActivation();
        }

        /// <summary>
        /// 经典菜单栏开关（MENUBAR）在工作空间「草图与注释」以及新建图纸下默认是 0（关闭）。
        /// ChemForge 的菜单栏项是 AutoCAD 在**构建菜单栏那一刻**从 CUIx 定义里追加进栏尾的；
        /// 构建完成后再把 MENUBAR 从 0 打开，只会按配置文件布局重建菜单栏，ChemForge
        /// 不会被补进来（Net80 侧实测：启动时 MENUBAR=0，手动打开"显示菜单栏"后栏里
        /// 只有 12 个出厂菜单 + Express）。用户的目标正是让 ChemForge 出现在菜单栏，
        /// 故每次启动统一置 MENUBAR=1，让 AutoCAD 在构建菜单栏时就带上它。
        /// 已在 1 时不做任何事（幂等）。
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
        /// MENUBAR 是随图纸保存的系统变量：换图/新建图纸后可能又变回 0，
        /// 故在文档激活时复查一次。这里只做一次系统变量读写，
        /// 不触碰 COM 菜单接口（历史崩溃风险正来自后者）。
        /// </summary>
        private static void HookDocumentActivation()
        {
            if (_hooked)
                return;

            Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentActivated +=
                (s, e) => EnsureClassicMenuBarVisible();

            _hooked = true;
            Logger.Info("已注册文档激活时的菜单栏复查钩子。");
        }

        /// <inheritdoc />
        public void UnloadMenu()
        {
            // 声明式挂载：菜单随 CUIx 由 AutoCAD 自己加载与卸载，插件不再在运行时建组，
            // 因此这里没有需要卸载的 COM 菜单组。
            // 若要清除安装痕迹（局部菜单登记 / 工作空间 WSPop 引用 / 菜单文件），
            // 请关闭 CAD 后执行 deploy\Dev\Register-ChemForgeMenu.ps1 -Uninstall [-RemoveFile]。
            Logger.Info("ChemForge 菜单为声明式挂载，UnloadMenu 无需运行时卸载动作。");
        }
    }
}
#endif
