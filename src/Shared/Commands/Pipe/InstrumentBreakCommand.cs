#if NET48 || NET80
using System;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Application;
using ChemForge.Core.Pipe;
using ChemForge.Utilities;

namespace ChemForge.Commands.Pipe
{
    /// <summary>
    /// 仪表线自动打断命令（仪表线 × 管线交叉缺口打断 / 半圆架桥，固定打断仪表线）
    ///
    /// 菜单位置：ChemForge → 管线与仪表 → 仪表线自动打断（菜单/功能区宏 INSTBREAK）
    ///
    /// 与 PipeBreakCommand 的差异：图层要点选两次（仪表线 + 管线）、不询问缺口方向
    /// （避让侧固定为仪表线），详见 PipeBreakEngine.ExecuteInstrument 的说明。
    ///
    /// 注意：本项目的命令统一用静态入口。CommandMethod 其实实例方法也认（官方参考原文是
    /// 「marks an instance or static function as a command」，实例方法要求类有公开无参构造函数），
    /// 统一静态只是省掉实例生命周期这层考虑、风格一致；真正只认静态方法的是 LispFunction。
    /// </summary>
    public sealed class InstrumentBreakCommand : ICommand
    {
        /// <summary>
        /// 命令唯一入口
        /// </summary>
        [CommandMethod(CommandNames.InstrumentBreak)]
        public static void CommandEntry()
        {
            ExecuteCore();
        }

        /// <summary>
        /// 执行仪表线打断主流程
        /// </summary>
        public static void ExecuteCore()
        {
            Logger.Info("命令 INSTBREAK 开始执行。");
            try
            {
                PipeBreakEngine.ExecuteInstrument();
            }
            catch (System.Exception ex)
            {
                Logger.Error("INSTBREAK 执行失败。", ex);
            }
        }

        /// <inheritdoc />
        public void Execute()
        {
            ExecuteCore();
        }
    }
}
#endif
