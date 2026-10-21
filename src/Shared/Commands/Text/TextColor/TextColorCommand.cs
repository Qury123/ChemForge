using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Application;
using ChemForge.Commands.Common;
using ChemForge.Core.Text.TextColor;
using ChemForge.Models.Text.TextColor;
using ChemForge.UI.Forms.Text.TextColor;
using ChemForge.Utilities;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;
using Editor = Autodesk.AutoCAD.EditorInput.Editor;

namespace ChemForge.Commands.Text.TextColor
{
    /// <summary>
    /// 批量修改多行文字颜色（命令 <c>TCOLOR</c>）。
    ///
    /// 这个命令一次做两件事，缺一不可：
    /// <list type="number">
    ///   <item>把对象颜色改成用户选的目标色；</item>
    ///   <item>清掉文字内容里内嵌的颜色覆盖代码。</item>
    /// </list>
    /// 只做第一件事的话，遇到内容里带 <c>\C1;</c>、<c>\c16711680;</c> 这类
    /// 覆盖码的多行文字，用户会看到"改了颜色完全没反应" ——
    /// 内嵌颜色码的优先级高于对象颜色，这正是这个命令存在的理由。
    /// </summary>
    public sealed class TextColorCommand
    {
        /// <summary>命令行输出前缀，与菜单/文档里出现的名字保持一致。</summary>
        private const string CommandLabel = "TCOLOR";

        /// <summary>
        /// 改动超过这个数量就整图重生成。
        ///
        /// 逐个重画在少量改动时几乎无感，但几百个之后累积起来反而比整图重生成慢；
        /// 这里取一个折中值。整图重生成本身与改动数量无关，
        /// 所以绝不能无脑每次都做 —— 那正是"只改 10 个文字也要等十几秒"的来源。
        /// </summary>
        private const int FullRegenThreshold = 800;

        /// <summary>文字类对象的选择过滤条件。</summary>
        private const string TextTypeFilter = "MTEXT,TEXT,MULTILEADER";

        /// <summary>
        /// 命令入口。
        ///
        /// <c>UsePickSet</c>：允许"先选文字、再敲命令"，这是用户已经习惯的用法。
        /// </summary>
        [CommandMethod(CommandNames.TextColor, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void ChangeTextColor()
        {
            try
            {
                ChangeCore();
            }
            catch (System.Exception ex)
            {
                CommandHost.ReportFailure(CommandLabel, ex);
            }
        }

        // ==================================================================
        //  主流程
        // ==================================================================

        private void ChangeCore()
        {
            // 入口日志放在所有早退之前：否则"点了没反应"时连命令有没有被调用都判断不了。
            Logger.Info("命令 TCOLOR 被调用。");

            // 界面判断必须最靠前：无界面宿主里弹对话框会直接原生崩溃，托管代码 catch 不到。
            Document probe = AcadApp.DocumentManager.MdiActiveDocument;
            if (!CommandHost.RequireUserInterface(
                    probe != null ? probe.Editor : null,
                    CommandLabel,
                    CommandLabel + " 需要弹出对话框，请在带界面的 AutoCAD 里执行。"))
            {
                Logger.Warning("命令 TCOLOR 在无界面宿主中被忽略（accoreconsole 等）。");
                return;
            }

            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Logger.Warning("命令 TCOLOR 被调用时没有打开的图形（MdiActiveDocument 为 null），已忽略。");
                return;
            }

            Editor editor = doc.Editor;
            Database database = doc.Database;
            if (database == null)
            {
                CommandHost.WriteMessageNoDynPrompt(editor, "\n当前文档没有可用的图形数据库，无法修改文字颜色。");
                return;
            }

            CommandHost.WriteMessageNoDynPrompt(editor, "\n>>> 批量修改多行文字颜色 <<<");
            CommandHost.WriteMessageNoDynPrompt(
                editor,
                "\n说明：本命令会同时改对象颜色 + 清除文字内部的颜色覆盖代码。");

            // ---- ① 待处理对象 ----
            // 先选好文字再敲命令的，直接用预选集；没选就等面板上那个按钮。
            // 与 BLKATTCHANGE 一致：命令先弹面板，选择由用户在面板上发起，
            // 不再用命令行提示拦一道（2026-10-21 用户要求：「先弹窗口，然后窗口上面加个按钮」）。
            List<ObjectId> targets = FilterTextObjects(ReadImpliedSelection(editor));
            if (targets.Count > 0)
            {
                LogSelection(targets);
            }

            // ---- ② 面板回合循环 ----
            TextColorOptions chosen = BuildInitialOptions();

            while (true)
            {
                TextTypeCounts counts = CountByType(targets);

                TextColorFormContext context = new TextColorFormContext
                {
                    MTextCount = counts.MText,
                    TextCount = counts.Text,
                    MultiLeaderCount = counts.MultiLeader,
                    Options = chosen
                };

                TextColorDialogAction action;
                using (TextColorForm form = new TextColorForm(context))
                {
                    CommandHost.ShowDialogSafely(form);
                    action = form.Action;

                    // 「确定」与「① 在图纸中选择…」都会把面板上的当前选项带回来，
                    // 去调色板/图上吸取这类动作不动选项，是 null。
                    if (form.Options != null)
                    {
                        chosen = form.Options;
                    }
                }

                if (action == TextColorDialogAction.Cancel)
                {
                    CommandHost.WriteMessageNoDynPrompt(editor, "\n已取消，未做任何修改。");
                    return;
                }

                if (action == TextColorDialogAction.PickObjects)
                {
                    // 框选取消或空选时保留上一轮的选择，不清空。
                    List<ObjectId> picked = PromptSelection(editor);
                    if (picked.Count > 0)
                    {
                        targets = picked;
                        LogSelection(targets);
                    }
                    else if (targets.Count == 0)
                    {
                        CommandHost.WriteMessageNoDynPrompt(editor, "\n没有框选到文字对象。");
                    }

                    continue;
                }

                if (action == TextColorDialogAction.PickColor)
                {
                    if (!TryPickSystemColor(chosen))
                    {
                        CommandHost.WriteMessageNoDynPrompt(editor, "\n未选择颜色。");
                    }

                    continue;
                }

                if (action == TextColorDialogAction.Eyedropper)
                {
                    if (!TryPickFromDrawing(editor, chosen))
                    {
                        CommandHost.WriteMessageNoDynPrompt(editor, "\n未选取对象。");
                    }

                    continue;
                }

                // 什么都没选就点了确定：面板再弹回来，别拿个空集合去跑引擎。
                if (targets.Count == 0)
                {
                    CommandHost.WriteMessageNoDynPrompt(
                        editor,
                        "\n还没有选择文字：请点面板上的「① 在图纸中选择要改色的文字」。");
                    continue;
                }

                break;
            }

            // ---- ③ 执行 ----
            Logger.Info("命令 TCOLOR 开始执行，目标：" + chosen.Describe());

            TextColorResult result = TextColorEngine.Run(
                database,
                targets,
                chosen,
                delegate (int done, int total)
                {
                    CommandHost.WriteMessageNoDynPrompt(
                        editor,
                        "\r  已处理 " + done.ToString(CultureInfo.InvariantCulture)
                        + " / " + total.ToString(CultureInfo.InvariantCulture) + " ...");
                });

            // ---- ④ 刷新 ----
            bool fullRegen = result.ProcessedBlockCount > 0 || result.ChangedCount > FullRegenThreshold;
            if (fullRegen)
            {
                CommandHost.RegenerateQuietly(editor);
            }
            else
            {
                RecordGraphicsModifiedQuietly(database, result.TouchedIds);
                result.Messages.Add(
                    "（本次只重画了改动对象，未全图重生成；若显示未刷新请执行 REGEN）");
            }

            // ---- ⑤ 报告 ----
            result.Report = TextColorReport.Build(result, chosen);
            string report = result.Report;

            CommandHost.WriteMessageNoDynPrompt(editor, "\n" + report);

            // 命令行那份该说的都说了，但它字号小、刷得快，用户常常一眼没看见。
            // 再弹一个 AutoCAD 原生提示框（与 BLKATTCHANGE 收尾那个同款），
            // 内容只留结论：完整报告里那些样例、核对信息是留在命令行查问题用的，
            // 全塞进提示框只会把"到底改了几个"淹掉。
            string brief = TextColorReport.BuildBrief(result, chosen);
            if (!CommandHost.ShowAlertSafely(brief))
            {
                // 原生提示框抛异常时（日志里会有 "ShowAlertDialog 失败"）兜底：
                // 宁可换个样子弹，也别让用户什么都看不到 —— 这正是当初
                // "点了确定，除了命令行什么都没有"的那次投诉。
                using (TextColorResultForm resultForm = new TextColorResultForm(brief))
                {
                    CommandHost.ShowDialogSafely(resultForm);
                }
            }

            Logger.Info("命令 TCOLOR 完成：" + result.ChangedCount + " 个对象被改动。");
        }

        // ==================================================================
        //  选择
        // ==================================================================

        private static List<ObjectId> ReadImpliedSelection(Editor editor)
        {
            List<ObjectId> ids = new List<ObjectId>();

            try
            {
                PromptSelectionResult implied = editor.SelectImplied();
                if (implied.Status == PromptStatus.OK && implied.Value != null)
                {
                    ids.AddRange(implied.Value.GetObjectIds());

                    // 用完就清掉预选集，免得它一直挂着影响后续命令
                    editor.SetImpliedSelection(new ObjectId[0]);
                }
            }
            catch (System.Exception)
            {
            }

            return FilterTextObjects(ids);
        }

        private static List<ObjectId> PromptSelection(Editor editor)
        {
            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = "\n请选择对象（可框选；自动只保留多行文字/单行文字/多重引线，其它对象忽略）："
            };

            SelectionFilter filter = new SelectionFilter(
                new[] { new TypedValue((int)DxfCode.Start, TextTypeFilter) });

            PromptSelectionResult result = editor.GetSelection(options, filter);
            if (result.Status != PromptStatus.OK || result.Value == null)
            {
                return new List<ObjectId>();
            }

            List<ObjectId> all = new List<ObjectId>(result.Value.GetObjectIds());
            List<ObjectId> kept = FilterTextObjects(all);

            int ignored = all.Count - kept.Count;
            if (ignored > 0)
            {
                CommandHost.WriteMessageNoDynPrompt(
                    editor,
                    "\n已自动忽略 " + ignored.ToString(CultureInfo.InvariantCulture) + " 个非文字对象。");
            }

            return kept;
        }

        /// <summary>
        /// 只留下文字类对象。
        ///
        /// 用 <c>ObjectClass.DxfName</c> 判断而不是逐个打开对象：
        /// 后者在大选择集上要把每个对象都读进内存，纯属浪费 ——
        /// 真正要不要处理，执行阶段还会再判一次。
        /// </summary>
        private static List<ObjectId> FilterTextObjects(IEnumerable<ObjectId> ids)
        {
            List<ObjectId> kept = new List<ObjectId>();

            if (ids == null)
            {
                return kept;
            }

            foreach (ObjectId id in ids)
            {
                if (id.IsNull || !id.IsValid)
                {
                    continue;
                }

                try
                {
                    string dxfName = id.ObjectClass.DxfName;
                    if (string.Equals(dxfName, "MTEXT", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dxfName, "TEXT", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dxfName, "MULTILEADER", StringComparison.OrdinalIgnoreCase))
                    {
                        kept.Add(id);
                    }
                }
                catch (System.Exception)
                {
                }
            }

            return kept;
        }

        private sealed class TextTypeCounts
        {
            public int MText;
            public int Text;
            public int MultiLeader;
        }

        private static void LogSelection(List<ObjectId> targets)
        {
            TextTypeCounts counts = CountByType(targets);
            Logger.Info(
                "命令 TCOLOR 选中文字：多行文字 " + counts.MText
                + " / 单行文字 " + counts.Text
                + " / 多重引线 " + counts.MultiLeader + "。");
        }

        private static TextTypeCounts CountByType(IEnumerable<ObjectId> ids)
        {
            TextTypeCounts counts = new TextTypeCounts();

            foreach (ObjectId id in ids)
            {
                try
                {
                    string dxfName = id.ObjectClass.DxfName;
                    if (string.Equals(dxfName, "MTEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        counts.MText++;
                    }
                    else if (string.Equals(dxfName, "TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        counts.Text++;
                    }
                    else if (string.Equals(dxfName, "MULTILEADER", StringComparison.OrdinalIgnoreCase))
                    {
                        counts.MultiLeader++;
                    }
                }
                catch (System.Exception)
                {
                }
            }

            return counts;
        }

        // ==================================================================
        //  颜色的两种"从外部拿"
        // ==================================================================

        private static TextColorOptions BuildInitialOptions()
        {
            return new TextColorOptions();
        }

        /// <summary>
        /// 调系统的颜色选择框。取回来的可能是索引色也可能是真彩色，
        /// 按返回值的实际类型决定模式，而不是按用户之前点过什么。
        /// </summary>
        private static bool TryPickSystemColor(TextColorOptions options)
        {
            try
            {
                Autodesk.AutoCAD.Windows.ColorDialog dialog =
                    new Autodesk.AutoCAD.Windows.ColorDialog();
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return false;
                }

                AcColor color = dialog.Color;
                if (color == null)
                {
                    return false;
                }

                if (color.ColorMethod == AcColorMethod.ByAci)
                {
                    options.Mode = TextColorMode.Aci;
                    options.AciIndex = color.ColorIndex;
                }
                else
                {
                    System.Drawing.Color rgb = color.ColorValue;
                    options.Mode = TextColorMode.TrueColor;
                    options.Red = rgb.R;
                    options.Green = rgb.G;
                    options.Blue = rgb.B;
                }

                options.Normalize();
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 在图上点一个对象，把它当前的颜色取过来当目标色。
        /// </summary>
        private static bool TryPickFromDrawing(Editor editor, TextColorOptions options)
        {
            try
            {
                PromptEntityOptions prompt = new PromptEntityOptions(
                    "\n点选一个对象以吸取其颜色：");
                prompt.SetRejectMessage("\n请点选一个图形对象。");

                PromptEntityResult result = editor.GetEntity(prompt);
                if (result.Status != PromptStatus.OK)
                {
                    return false;
                }

                using (Transaction transaction =
                    editor.Document.Database.TransactionManager.StartTransaction())
                {
                    Entity entity = transaction.GetObject(
                        result.ObjectId, OpenMode.ForRead, false) as Entity;

                    if (entity == null)
                    {
                        return false;
                    }

                    AcColor color = entity.Color;
                    if (color == null)
                    {
                        return false;
                    }

                    if (color.ColorMethod == AcColorMethod.ByColor)
                    {
                        System.Drawing.Color rgb = color.ColorValue;
                        options.Mode = TextColorMode.TrueColor;
                        options.Red = rgb.R;
                        options.Green = rgb.G;
                        options.Blue = rgb.B;
                    }
                    else if (color.ColorIndex == AciColorNames.ByLayerValue)
                    {
                        options.Mode = TextColorMode.ByLayer;
                    }
                    else if (color.ColorIndex == AciColorNames.ByBlockValue)
                    {
                        options.Mode = TextColorMode.ByBlock;
                    }
                    else
                    {
                        options.Mode = TextColorMode.Aci;
                        options.AciIndex = color.ColorIndex;
                    }

                    options.Normalize();
                    return true;
                }
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // ==================================================================
        //  刷新
        // ==================================================================

        /// <summary>
        /// 只把改动过的对象标脏重画。
        ///
        /// 这一步对应的是"少量改动"的快速路径：整图重生成在大图上要十几秒，
        /// 而那个耗时可观到用户会以为命令卡死了。
        /// </summary>
        private static void RecordGraphicsModifiedQuietly(Database database, IList<ObjectId> ids)
        {
            if (database == null || ids == null || ids.Count == 0)
            {
                return;
            }

            try
            {
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        if (id.IsNull || !id.IsValid || id.IsErased)
                        {
                            continue;
                        }

                        Entity entity = transaction.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (entity == null)
                        {
                            continue;
                        }

                        // 逐个兜底：UpgradeOpen 在某些情况下会抛（最典型的就是锁定图层），
                        // 不能让一个对象把其余对象的重画一起带掉 ——
                        // 那会让"其实已经改好了"看起来像"没改"，而这里本来就是静默收尾的快速路径。
                        try
                        {
                            entity.UpgradeOpen();
                            entity.RecordGraphicsModified(true);
                        }
                        catch (System.Exception)
                        {
                        }
                    }

                    transaction.Commit();
                }
            }
            catch (System.Exception)
            {
            }
        }
    }
}
