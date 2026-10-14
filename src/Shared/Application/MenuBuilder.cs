using System;
using ChemForge.CAD.Abstraction;
using ChemForge.CAD.Impl;
using ChemForge.Utilities;

namespace ChemForge.Application
{
    /// <summary>
    /// 菜单构建器 - 负责加载 ChemForge CUIx 菜单，
    /// 首次启动时依据 MenuDefinition 自动生成菜单文件并挂载到菜单栏
    /// </summary>
    public static class MenuBuilder
    {
        /// <summary>
        /// CUIx 菜单文件名
        /// </summary>
        private const string MenuFileName = "ChemForge.cuix";

        /// <summary>
        /// 加载 ChemForge 下拉菜单
        /// </summary>
        public static void LoadMenu()
        {
            Logger.Info("MenuBuilder.LoadMenu 开始加载 CUIx 菜单。");

            try
            {
                var cuixPath = ConfigManager.GetConfigFilePath(MenuFileName);
                var service = CreateMenuService();
                if (service == null)
                {
                    Logger.Warning("当前 CAD 目标框架暂不支持菜单加载，已跳过。");
                    return;
                }

                service.EnsureMenuLoaded(cuixPath);
                Logger.Info("ChemForge 菜单加载完成。");
            }
            catch (System.Exception ex)
            {
                Logger.Error("ChemForge 菜单加载失败。", ex);
            }
        }

        /// <summary>
        /// 卸载 ChemForge 菜单组（插件终止时调用），
        /// 防止菜单布局被写入 CAD 配置文件导致下次启动恢复失败
        /// </summary>
        public static void UnloadMenu()
        {
            Logger.Info("MenuBuilder.UnloadMenu 开始卸载 CUIx 菜单。");

            try
            {
                var service = CreateMenuService();
                if (service == null)
                {
                    Logger.Warning("当前 CAD 目标框架暂不支持菜单卸载，已跳过。");
                    return;
                }

                service.UnloadMenu();
                Logger.Info("ChemForge 菜单卸载完成。");
            }
            catch (System.Exception ex)
            {
                Logger.Error("ChemForge 菜单卸载失败。", ex);
            }
        }

        /// <summary>
        /// 根据当前编译目标创建对应的菜单服务实现
        /// </summary>
        private static IMenuService CreateMenuService()
        {
#if NET80
            return new MenuServiceNet80();
#elif NET48
            return new MenuServiceNet48();
#else
            return null;
#endif
        }
    }
}
