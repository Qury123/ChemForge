using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Core.Block.Numbering;
using ChemForge.Commands.Common;
using ChemForge.Models.Block.Numbering;
using ChemForge.UI.Forms.Block.Numbering;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace ChemForge.Commands.Block.Numbering
{
    /// <summary>插件自我诊断：把"到底加载了什么、被什么挡住"讲清楚。</summary>
    internal static class BlockNumberingDiagnostics
    {
        internal static string PluginVersion
        {
            get
            {
                try
                {
                    var asm = typeof(BlockNumberingDiagnostics).Assembly;
                    Version v = asm.GetName().Version;
                    return v == null ? "?" : v.ToString();
                }
                catch (System.Exception)
                {
                    return "?";
                }
            }
        }

        internal static string PluginLocation
        {
            get
            {
                try
                {
                    return typeof(BlockNumberingDiagnostics).Assembly.Location;
                }
                catch (System.Exception)
                {
                    return "(未知)";
                }
            }
        }

        /// <summary>启动阶段异常必须让用户看见，否则就是"静默不注册命令"。</summary>
        internal static void ReportStartupError(string stage, System.Exception ex)
        {
            try
            {
                Document doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage(
                        "\n[BlockNumbering] 启动阶段(" + stage + ")出错，插件可能未正常工作：");
                    CommandHost.WriteException(doc.Editor, ex);
                    return;
                }

                if (CommandHost.HasUserInterface(out string _))
                {
                    CommandHost.ShowAlertSafely("BlockNumbering 启动阶段(" + stage + ")出错：\n" + ex);
                }
            }
            catch (System.Exception)
            {
            }
        }

        internal static string SysVar(string name)
        {
            try
            {
                object value = AcadApp.GetSystemVariable(name);
                return value == null ? "(null)" : value.ToString();
            }
            catch (System.Exception ex)
            {
                return "(读取失败: " + ex.Message + ")";
            }
        }
    }
}
