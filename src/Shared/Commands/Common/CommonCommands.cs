using System;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Application;
using ChemForge.Utilities;
#if NET80 || NET48
using ChemForge.UI.Forms.Common;
#endif

namespace ChemForge.Commands.Common
{
    /// <summary>
    /// 通用命令集合
    /// </summary>
    public sealed class CommonCommands : ICommand
    {
        /// <summary>
        /// 显示插件信息（键入 ChemForge 即版本信息；原生 ABOUT/VERSION 已被 AutoCAD 占用）
        /// </summary>
        [CommandMethod(CommandNames.ChemForge)]
        public static void About()
        {
            Logger.Info("命令 CHEMFORGE 开始执行。");
            try
            {
                var version = GetVersionText();

                // 2026-10-26 用户要求：点「关于 ChemForge」只弹窗，命令行不再输出版权信息
                // （命令行那几行和窗口内容是重复的，只留窗）。无界面宿主没有窗口可弹，
                // 才退化成命令行一行，否则那里什么也看不到。
#if NET80 || NET48
                // 界面判断必须在弹窗之前：ShowModalDialog 由 AcWindows.dll 实现，
                // 无界面宿主（accoreconsole 等）里这次加载会在方法体执行之前直接 AV 崩进程，
                // 那种崩溃在托管代码里 catch 不到。详见 CommandHost.ShowDialogSafely 的说明。
                var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (!CommandHost.HasUserInterface(out string reason))
                {
                    if (document != null)
                    {
                        try
                        {
                            document.Editor.WriteMessage(
                                "\nChemForge · 工艺匠造工坊 v" + version
                                + "\n（当前宿主没有用户界面，无法显示关于窗口：" + reason + "）\n");
                        }
                        catch (System.Exception)
                        {
                        }
                    }

                    return;
                }

                using (var dialog = new AboutDialog(version))
                {
                    CommandHost.ShowDialogSafely(dialog);
                }
#else
                var message = "ChemForge · 工艺匠造工坊 v" + version + "\n"
                              + "锻造化工工艺，精绘设计蓝图\n"
                              + "Copyright © 2026 ChemForge Contributors. 保留所有权利。";
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog(message);
#endif
            }
            catch (System.Exception ex)
            {
                Logger.Error("CHEMFORGE 执行失败。", ex);

                // 只写日志等于"敲了没反应"：日志落在 %LOCALAPPDATA%，用户看不到。
                // 与 TCOLOR / BLKATTCHANGE / BLKNUM 一致，把失败原因打到命令行。
                CommandHost.ReportFailure(CommandNames.ChemForge, ex);
            }
        }

        /// <summary>
        /// 取程序集版本号，显示成「主.次.修订」三段。
        /// </summary>
        private static string GetVersionText()
        {
            var version = typeof(CommonCommands).Assembly.GetName().Version;
            return version == null ? "1.0.0" : version.ToString(3);
        }

        /// <inheritdoc />
        public void Execute()
        {
            About();
        }
    }
}
