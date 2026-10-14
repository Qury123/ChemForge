using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using ChemForge.Application;
using ChemForge.Commands.Common;
using ChemForge.Core.Block.BlockAttr;
using ChemForge.Models.Block.BlockAttr;
using ChemForge.UI.Forms.Block.BlockAttr;
using ChemForge.Utilities;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;
using Editor = Autodesk.AutoCAD.EditorInput.Editor;

namespace ChemForge.Commands.Block.BlockAttr
{
    /// <summary>
    /// 批量修改块增强属性（命令 <c>BLKATTCHANGE</c>）。
    ///
    /// 交互流程如下：
    /// <code>
    ///   选择图块 → 汇总增强属性标签 → 弹出面板（内容 / 文字选项 / 特性）
    ///   → 校验 → 写入（含撤销支持）→ REGEN → 命令行报告 + 结果弹窗
    /// </code>
    ///
    /// 与其它命令一样，命令体只做一件事：把异常兜住并打出来。
    /// 否则 AutoCAD 只会静静地什么都不做，用户完全无从判断。
    /// </summary>
    public sealed class BatchAttrChangeCommand
    {
        /// <summary>命令行输出前缀，与菜单/文档里出现的名字保持一致。</summary>
        private const string CommandLabel = "BLKATTCHANGE";

        /// <summary>
        /// 命令入口。
        ///
        /// <c>UsePickSet</c>：允许"先选块、再敲命令"，这是用户已经习惯的用法
        /// （BLKNUM 也是这么做的）。预选集里不是块参照的对象会被过滤掉。
        /// </summary>
        [CommandMethod(CommandNames.BatchAttrChange, CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void ReplaceAttributes()
        {
            try
            {
                ReplaceCore();
            }
            catch (System.Exception ex)
            {
                CommandHost.ReportFailure(CommandLabel, ex);
            }
        }

        // ==================================================================
        //  主流程
        // ==================================================================

        private void ReplaceCore()
        {
            // 入口日志必须放在所有早退之前。
            // 2026-10-12 排查"点了功能区按钮毫无反应、什么都不输出"时，下面几处早退都是静默的，
            // 结果连"命令到底有没有被调用"都无法从日志判断，只能靠猜。这条日志就是为了堵住这个盲区。
            Logger.Info("命令 BLKATTCHANGE 被调用。");

            // 界面判断必须放在最前面：无界面宿主（accoreconsole 等）里访问 Document.Database、
            // 让 AutoCAD 预处理 UsePickSet 拾取集、或者弹对话框，都可能直接原生崩溃（AV），
            // 那种崩溃在托管代码里 catch 不到，只能提前拦下来。
            Document probe = AcadApp.DocumentManager.MdiActiveDocument;
            if (!CommandHost.RequireUserInterface(
                    probe != null ? probe.Editor : null,
                    CommandLabel,
                    CommandLabel + " 需要弹出对话框，请在带界面的 AutoCAD 里执行。"))
            {
                Logger.Warning("命令 BLKATTCHANGE 在无界面宿主中被忽略（accoreconsole 等）。");
                return;
            }

            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                // 没有打开的图形时（停在"开始"选项卡），AutoCAD 可能仍然允许点击功能区按钮，
                // 但文档上下文命令没有可用的 Editor，什么也输出不了。必须留下日志痕迹，
                // 否则现象就是"点了没反应"，无从排查。
                Logger.Warning("命令 BLKATTCHANGE 被调用时没有打开的图形（MdiActiveDocument 为 null），已忽略。");
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            if (db == null)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n当前文档没有可用的图形数据库，无法修改块属性。");
                return;
            }

            CommandHost.WriteMessageNoDynPrompt(ed, "\n>>> 批量修改块属性：文字内容 / 文字选项 / 特性 <<<");
            Logger.Info("命令 BLKATTCHANGE 开始执行。");

            // ---- ① 初始样板块 ----
            // 命令行不再先框选：BLKATTCHANGE 直接弹面板，选块/框范围全在面板里做
            // （用户 2026-10-12 裁定，照 图块编号的流程）。
            // 唯一例外是 UsePickSet 预选集 —— 用户已经"先选块再敲命令"的习惯要保住。
            List<ObjectId> pickedIds = FilterBlocks(db, ReadImpliedSelection(ed));
            BatchAttrChangeRangeSource rangeSource = pickedIds.Count > 0
                ? BatchAttrChangeRangeSource.PickedBlocks
                : BatchAttrChangeRangeSource.None;

            if (pickedIds.Count == 0)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n请点面板里的「① 在图纸中选择要修改的图块」。");
            }

            BatchAttrChangeTargets targets = null;
            BatchAttrChangeSelection selection = null;
            BatchAttrChangeMemory memory = null;
            BatchAttrChangeSessionState session = null;

            // 面板是可反复弹出的：①②两个按钮会先把它关掉、回图纸拾取，拾取完再弹一个新的。
            // 做法照搬 图块编号命令的回合循环 —— 它那边注释写得很清楚：
            // 「每轮都新建窗体（旧的已在回图纸拾取前挪到屏幕外并关闭）。
            //   参数经 NumberOptions 原样带回，所以界面状态不会丢。」
            // 这里对应的就是 session + rangeSource 两样东西。
            while (true)
            {
                // ---- ② 汇总标签与候选（采集完立即关事务，绝不让事务跨模态对话框）----
                targets = BatchAttrChangeCollector.Collect(db, pickedIds);

                // 选是选了、但一个带属性的块都没有：面板上会显示成"尚未选择图块"，
                // 光看面板看不出是"没选"还是"选了但没属性"，所以命令行必须补一句。
                if (pickedIds.Count > 0 && (targets.Tags == null || targets.Tags.Count == 0))
                {
                    CommandHost.WriteMessageNoDynPrompt(ed, "\n所选图块均不含增强属性，请重新选择。");
                }

                // ---- ③ 弹面板 ----
                BatchAttrChangeContext context = new BatchAttrChangeContext
                {
                    BlockLabel = targets.BlockNames == null
                        ? string.Empty
                        : string.Join("、", targets.BlockNames),
                    SelectedBlockCount = targets.SelectedBlockIds.Count,
                    SameNameBlockCount = targets.SameNameBlockIds.Count,
                    Tags = targets.Tags,
                    StyleNames = targets.StyleNames,
                    LayerNames = targets.LayerNames,
                    LinetypeNames = targets.LinetypeNames,
                    RememberedMemory = BatchAttrChangeMemoryStore.Load(),
                    RangeSource = rangeSource,
                    Session = session
                };

                BatchAttrChangeAction action;
                using (BatchAttrChangeForm form = new BatchAttrChangeForm(context))
                {
                    CommandHost.ShowDialogSafely(form);

                    action = form.Action;

                    if (form.Session != null)
                    {
                        session = form.Session;
                        memory = form.Session.Memory;
                    }

                    if (action == BatchAttrChangeAction.Apply)
                    {
                        selection = form.Selection;
                    }
                }

                if (action == BatchAttrChangeAction.Cancel)
                {
                    CommandHost.WriteMessageNoDynPrompt(ed, "\n已取消，未做任何修改。");
                    return;
                }

                if (action == BatchAttrChangeAction.Apply)
                {
                    break;
                }

                if (action == BatchAttrChangeAction.PickBlocks)
                {
                    // 用户按 Esc 或直接回车（什么都没选）时**保留上一轮**，
                    // 免得辛苦勾好的标签被一次误操作清空。
                    List<ObjectId> repicked = PromptBlocks(ed, db, out bool blocksCancelled);
                    if (!blocksCancelled && repicked != null && repicked.Count > 0)
                    {
                        pickedIds = repicked;
                        rangeSource = BatchAttrChangeRangeSource.PickedBlocks;
                    }

                    continue;
                }

                if (action == BatchAttrChangeAction.PickRange)
                {
                    if (targets.BlockNames == null || targets.BlockNames.Count == 0)
                    {
                        continue;
                    }

                    List<ObjectId> region = PromptRange(ed, out bool rangeCancelled);
                    if (rangeCancelled)
                    {
                        // 用户自己按的 Esc：面板里那行说明还留着上一轮的数字，不用命令行复述
                        continue;
                    }

                    if (region == null || region.Count == 0)
                    {
                        // 「不选任何对象直接回车 = 整个图形」——照 图块编号的约定
                        pickedIds = new List<ObjectId>(targets.SameNameBlockIds);
                        rangeSource = BatchAttrChangeRangeSource.WholeDrawing;
                        continue;
                    }

                    List<ObjectId> sameName = FilterSameNameBlocks(db, region, targets.BlockNames);
                    if (sameName.Count == 0)
                    {
                        // 框到的对象里一个同名块都没有：范围**保持不变**，只回一句话
                        // （照 图块编号的做法，它遇到 found.Count == 0 也是直接 continue）
                        CommandHost.WriteMessageNoDynPrompt(ed, "\n所选范围内没有与①同名的图块，范围保持不变。");
                        continue;
                    }

                    pickedIds = sameName;
                    rangeSource = BatchAttrChangeRangeSource.PickedRange;
                }
            }

            if (selection == null || selection.Options == null)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n未获取到设置，程序结束。");
                return;
            }

            if (selection.SelectedTags == null || selection.SelectedTags.Count == 0)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n未选择任何属性，程序结束。");
                return;
            }

            // ---- ④ 记忆落盘（取消时不动记忆）----
            if (memory != null)
            {
                BatchAttrChangeMemoryStore.Save(memory);
            }

            // ---- ⑤ 定范围 ----
            BatchAttrChangeOptions options = selection.Options;
            IList<ObjectId> blockIds = targets.SelectedBlockIds;
            if (options.Scope == BatchAttrChangeScope.AllSameName)
            {
                blockIds = ExpandScope(db, targets);
                CommandHost.WriteMessageNoDynPrompt(
                    ed,
                    string.Format(CultureInfo.CurrentCulture, "\n修改范围：全图所有同名块，共 {0} 个。", blockIds.Count));
            }
            else
            {
                CommandHost.WriteMessageNoDynPrompt(
                    ed,
                    string.Format(CultureInfo.CurrentCulture, "\n修改范围：仅本次选中的 {0} 个块。", blockIds.Count));
            }

            // ---- ⑥ 写入 ----
            BatchAttrChangeResult result =
                BatchAttrChangeEngine.Run(db, blockIds, options, selection.SelectedTags);

            if (result.ChangedCount == 0)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n未做任何修改（选中的属性没有可改的内容）。");
                return;
            }

            // 显示刷新：整体重生成一次，让改动立即可见
            CommandHost.RegenerateQuietly(ed);

            Logger.Info(string.Format(
                CultureInfo.InvariantCulture,
                "命令 BLKATTCHANGE 完成：处理 {0} 个块，更新 {1} 处属性。",
                result.ProcessedBlockCount,
                result.ChangedCount));

            // ---- ⑦ 报告 ----
            string report = result.Report ?? string.Empty;
            CommandHost.WriteMessageNoDynPrompt(ed, "\n" + report);

            if (!string.IsNullOrEmpty(result.Diagnostics))
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n" + result.Diagnostics);
            }

            if (!string.IsNullOrEmpty(result.ComMirrorWarning))
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n" + result.ComMirrorWarning);
            }

            CommandHost.ShowAlertSafely(report);
        }

        // ==================================================================
        //  选择 / 范围
        // ==================================================================

        /// <summary>
        /// ① 回图纸选块（决定"样板"名）。由面板上的「① 在图纸中选择要修改的图块」按钮触发。
        /// 提示框选，只保留块参照，其余对象静默丢弃。
        /// 对应 图块编号命令的 <c>TryPromptSampleBlock</c>，区别是这里允许一次框选多个块
        /// —— "一次改多种块"这个能力要保住。
        /// </summary>
        /// <param name="ed">编辑器</param>
        /// <param name="db">图形数据库（用于过滤非块参照）</param>
        /// <param name="cancelled">用户是否按了 Esc</param>
        /// <returns>块参照 id 列表；Esc 时为 null</returns>
        private static List<ObjectId> PromptBlocks(Editor ed, Database db, out bool cancelled)
        {
            cancelled = false;

            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\n请选择要修改的图块（可框选；回车结束）: ",
                AllowDuplicates = false
            };

            SelectionFilter filter = new SelectionFilter(
                new[] { new TypedValue((int)DxfCode.Start, "INSERT") });

            PromptSelectionResult prompt = ed.GetSelection(selectionOptions, filter);
            if (prompt.Status == PromptStatus.Cancel)
            {
                cancelled = true;
                return null;
            }

            if (prompt.Status != PromptStatus.OK || prompt.Value == null || prompt.Value.Count == 0)
            {
                return new List<ObjectId>();
            }

            return FilterBlocks(db, new List<ObjectId>(prompt.Value.GetObjectIds()));
        }

        /// <summary>
        /// ② 回图纸框范围。由面板上的「② 重新框选范围」按钮触发。
        ///
        /// 与 <see cref="PromptBlocks"/> 的两点关键差异：
        /// ①**不加** INSERT 过滤器 —— 这里框的是"范围"，用户框的是图纸上一片区域，
        ///   加了过滤器反而框不中；选完再由 <see cref="FilterSameNameBlocks"/> 按有效块名过滤。
        /// ②**空选直接回车 = 整个图形**（返回空集合），这是照搬 图块编号的约定
        ///   （其 <c>PromptForRange</c>，见 `Commands.cs:982-1019`）。
        /// </summary>
        /// <param name="ed">编辑器</param>
        /// <param name="cancelled">用户是否按了 Esc</param>
        /// <returns>框到的对象 id 列表；Esc 时为 null，空选回车时为空列表</returns>
        private static List<ObjectId> PromptRange(Editor ed, out bool cancelled)
        {
            cancelled = false;

            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\n选择要处理的块（框选或点选；只收集与①同名的块，回车结束；不选直接回车 = 整个图形）: ",
                AllowDuplicates = false
            };

            PromptSelectionResult prompt = ed.GetSelection(selectionOptions);
            if (prompt.Status == PromptStatus.Cancel)
            {
                cancelled = true;
                return null;
            }

            if (prompt.Status != PromptStatus.OK || prompt.Value == null || prompt.Value.Count == 0)
            {
                return new List<ObjectId>();
            }

            return new List<ObjectId>(prompt.Value.GetObjectIds());
        }

        /// <summary>
        /// 从框选结果里只留下"有效块名与①同名"的块参照。
        ///
        /// 必须走 <see cref="BlockEffectiveNameResolver"/> 而不是直接比 DXF 组码 2：
        /// 动态块改过参数后引用名会变成匿名名（*U…），按原名比会整批漏掉
        /// （这正是 2026-10-12 用户反馈"显示 *U256 而不是真实块名"的同一个坑）。
        /// 单个对象出错吞掉不影响整批 —— 框选时常常连尺寸线、文字一起框进来。
        /// </summary>
        /// <param name="db">图形数据库</param>
        /// <param name="ids">待过滤的对象</param>
        /// <param name="sampleNames">①选中的那些块的有效名</param>
        /// <returns>同名块参照 id 列表</returns>
        private static List<ObjectId> FilterSameNameBlocks(
            Database db, IEnumerable<ObjectId> ids, IList<string> sampleNames)
        {
            List<ObjectId> result = new List<ObjectId>();
            if (db == null || ids == null || sampleNames == null || sampleNames.Count == 0)
            {
                return result;
            }

            HashSet<string> wanted = new HashSet<string>(sampleNames, StringComparer.OrdinalIgnoreCase);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable table = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
                if (table == null)
                {
                    return result;
                }

                BlockEffectiveNameResolver resolver = new BlockEffectiveNameResolver(table, tr);

                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased)
                    {
                        continue;
                    }

                    try
                    {
                        BlockReference reference = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                        if (reference == null)
                        {
                            continue;
                        }

                        if (wanted.Contains(resolver.GetEffectiveName(reference, tr)))
                        {
                            result.Add(id);
                        }
                    }
                    catch (AcException)
                    {
                    }
                    catch (System.Exception)
                    {
                    }
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>
        /// 读取"先选块、再执行命令"的预选集（UsePickSet）。读到后顺手清空预选，
        /// 避免影响后续拾取。没有预选返回 null。
        /// </summary>
        private static List<ObjectId> ReadImpliedSelection(Editor ed)
        {
            if (ed == null)
            {
                return null;
            }

            try
            {
                PromptSelectionResult implied = ed.SelectImplied();
                if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
                {
                    List<ObjectId> ids = new List<ObjectId>(implied.Value.GetObjectIds());
                    ed.SetImpliedSelection(new ObjectId[0]);
                    return ids;
                }
            }
            catch (AcException)
            {
            }
            catch (System.Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// 从一批 ObjectId 里挑出块参照。打不开或不是块参照的一律丢弃，
        /// 单个对象出错不影响整批（用户框选时常常连尺寸线一起框进来）。
        /// </summary>
        private static List<ObjectId> FilterBlocks(Database db, IEnumerable<ObjectId> ids)
        {
            List<ObjectId> result = new List<ObjectId>();
            if (db == null || ids == null)
            {
                return result;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || id.IsErased)
                    {
                        continue;
                    }

                    try
                    {
                        if (tr.GetObject(id, OpenMode.ForRead, false) is BlockReference)
                        {
                            result.Add(id);
                        }
                    }
                    catch (AcException)
                    {
                    }
                    catch (System.Exception)
                    {
                    }
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>
        /// 把"仅本次选中"扩成"全图同名块"。
        ///
        /// 刻意不用 <c>BlockTableRecord.GetBlockReferenceIds</c> 之外的任何按名字查表的方式：
        /// 动态块被改过参数后 AutoCAD 会生成匿名块定义（*U…），按名字查会漏掉这些引用。
        /// 有效名解析表由 <see cref="BlockEffectiveNameResolver"/> 提供。
        /// </summary>
        private static IList<ObjectId> ExpandScope(Database db, BatchAttrChangeTargets targets)
        {
            if (db == null || targets == null || targets.BlockNames == null || targets.BlockNames.Count == 0)
            {
                return new List<ObjectId>();
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable table = tr.GetObject(db.BlockTableId, OpenMode.ForRead, false) as BlockTable;
                if (table == null)
                {
                    return new List<ObjectId>();
                }

                BlockEffectiveNameResolver resolver = new BlockEffectiveNameResolver(table, tr);
                IList<ObjectId> expanded =
                    BatchAttrChangeCollector.ExpandToSameName(table, tr, resolver, targets.BlockNames);
                tr.Commit();

                // 同名块必然包含本次选中的那些；万一某个选中块不在结果里（极端情况：
                // 块定义在采集与展开之间被删除），补回去，免得"改了个更小的集合"。
                HashSet<ObjectId> seen = new HashSet<ObjectId>(expanded);
                List<ObjectId> merged = new List<ObjectId>(expanded);
                foreach (ObjectId id in targets.SelectedBlockIds)
                {
                    if (seen.Add(id))
                    {
                        merged.Add(id);
                    }
                }

                return merged;
            }
        }
    }
}
