namespace ChemForge.CAD.Abstraction
{
    /// <summary>
    /// 菜单服务抽象 - 负责 ChemForge 菜单的可用性，
    /// 具体实现按 CAD 版本隔离在 CAD/Impl 中。
    ///
    /// 2026-10 起菜单栏显示走声明式路线：CUIx 由部署脚本生成并登记为宿主 CUIx 的
    /// 局部菜单，根菜单带官方别名 POP1–POP16，AutoCAD 加载 CUIx 时自己把它挂上菜单栏；
    /// 运行时实现只保证菜单栏处于显示状态（MENUBAR=1），不再用 COM 建菜单/插菜单栏。
    /// </summary>
    public interface IMenuService
    {
        /// <summary>
        /// 确保 ChemForge 菜单可用（生成/校验 CUIx 提示路径、保证经典菜单栏处于显示状态）
        /// </summary>
        /// <param name="cuixPath">
        /// ChemForge.cuix 的**提示路径**：Net48 实现用它做校验与日志；Net80 实现会自行
        /// 定位 AutoCAD 用户支持目录（可能与传入值不同），故调用方无需保证该路径已存在
        /// </param>
        void EnsureMenuLoaded(string cuixPath);

        /// <summary>
        /// 释放菜单资源（插件终止时调用）。
        /// 声明式挂载下菜单由 AutoCAD 随 CUIx 自行加载/卸载，实现通常只记录日志；
        /// 清除安装痕迹请用 deploy\Dev\Mount-ChemForgeMenu.ps1 -Uninstall
        /// </summary>
        void UnloadMenu();
    }
}
