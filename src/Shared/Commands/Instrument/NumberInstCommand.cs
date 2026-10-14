using System;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Application;
using ChemForge.Utilities;

namespace ChemForge.Commands.Instrument
{
    /// <summary>
    /// 仪表块自动编号命令
    /// </summary>
    public sealed class NumberInstCommand : ICommand
    {
        /// <summary>
        /// 命令入口（仪表位号 TAG NO.）
        /// </summary>
        [CommandMethod(CommandNames.NumberInst)]
        public static void CommandEntry()
        {
            new NumberInstCommand().Execute();
        }

        /// <inheritdoc />
        public void Execute()
        {
            Logger.Info("命令 TAGNO 开始执行。");

            try
            {
                // 业务尚未实现：编号引擎（Core.Numbering.NumberingEngine）本身可用，缺的是
                // 「选仪表块 → 读位号属性 → 写回」这一段。
                //
                // 改前的写法是调 CADServiceFactory.Instance.CreateService()，而那一层
                // （ChemForge.CAD.Abstraction 的 ICADService 与三个 CADServiceNet* 实现）
                // 从未真正实现过，每个方法都直接抛 NotImplementedException；异常被下面的
                // catch 吞掉，用户在命令行看到的是「敲了没反应」。
                // 2026-10-14：该抽象层因零消费者已整体删除，这里改为直接给出明确回执。
                //
                // 2026-10-14：菜单与功能区入口已摘掉（见 MenuDefinition.cs / RibbonBuilder.cs），
                // 命令名仍登记在 PackageContents.xml，命令行敲 TAGNO 会走到这里。
                WriteMessage(
                    "\n仪表块自动编号（TAGNO）尚未实现，本次未修改图纸。"
                    + "\n编号引擎已就绪，但「选仪表块 → 读位号 → 写回」尚未接入；"
                    + "\n该功能未在菜单与功能区提供入口。");
            }
            catch (System.Exception ex)
            {
                Logger.Error("TAGNO 执行失败。", ex);
            }
        }

        /// <summary>
        /// 把消息写到命令行。有活动文档时走 Editor（用户可见）；没有文档时退化为日志
        /// （accoreconsole 等无界面宿主下 Console 用户看不到）。
        /// </summary>
        private static void WriteMessage(string message)
        {
            var document = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (document != null)
            {
                document.Editor.WriteMessage(message + "\n");
                return;
            }

            Logger.Info(message);
        }
    }
}
