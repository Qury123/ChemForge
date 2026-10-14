#if NET48 || NET80
using System;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Application;
using ChemForge.Core.Pipe;
using ChemForge.Utilities;

namespace ChemForge.Commands.Pipe
{
    /// <summary>
    /// 管线自动打断命令（管线交叉缺口打断 / 半圆架桥）
    ///
    /// 菜单位置：ChemForge → 管线与仪表 → 管线自动打断（菜单/功能区宏 PIPEBREAK）
    ///
    /// 注意：本项目的命令统一用静态入口。CommandMethod 其实实例方法也认（官方参考原文是
    /// 「marks an instance or static function as a command」，实例方法要求类有公开无参构造函数），
    /// 统一静态只是省掉实例生命周期这层考虑、风格一致；真正只认静态方法的是 LispFunction。
    /// </summary>
    public sealed class PipeBreakCommand : ICommand
    {
        /// <summary>
        /// 命令唯一入口（早期曾另有 CF_BREAKPIPE 别名，已随命令更名移除）
        /// </summary>
        [CommandMethod(CommandNames.PipeBreak)]
        public static void CommandEntry()
        {
            ExecuteCore();
        }

        /// <summary>
        /// 执行管线打断主流程
        /// </summary>
        public static void ExecuteCore()
        {
            Logger.Info("命令 PIPEBREAK 开始执行。");
            try
            {
                PipeBreakEngine.Execute();
            }
            catch (System.Exception ex)
            {
                Logger.Error("PIPEBREAK 执行失败。", ex);
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