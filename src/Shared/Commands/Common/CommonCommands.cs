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
                var message = "ChemForge · 工艺匠造工坊 v" + version + "\n"
                              + "锻造化工工艺，精绘设计蓝图\n"
                              + "Copyright © 2026 ChemForge Contributors. 保留所有权利。";

                // AutoCAD 没有控制台宿主，Console.WriteLine 用户永远看不到；
                // 有活动文档时写到命令行
                var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (document != null)
                {
                    document.Editor.WriteMessage("\n" + message + "\n");
                }

                // 命令行之外再弹窗（点「关于 ChemForge（版本信息）」要能看到窗口与版权信息）。
                // Net80 / Net48 用自绘对话框（有标题与排版）；其余目标框架没有 WinForms 宿主，
                // 退化为原生提示框。
#if NET80 || NET48
                // 界面判断必须在弹窗之前：ShowModalDialog 由 AcWindows.dll 实现，
                // 无界面宿主（accoreconsole 等）里这次加载会在方法体执行之前直接 AV 崩进程，
                // 那种崩溃在托管代码里 catch 不到。详见 CommandHost.ShowDialogSafely 的说明。
                if (!CommandHost.HasUserInterface(out string reason))
                {
                    if (document != null)
                    {
                        try
                        {
                            document.Editor.WriteMessage(
                                "\n无法显示 ChemForge 关于窗口：" + reason
                                + "\n版本信息已在上方命令行输出。");
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
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog(message);
#endif
            }
            catch (System.Exception ex)
            {
                Logger.Error("CHEMFORGE 执行失败。", ex);
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
