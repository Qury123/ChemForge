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
    /// <summary>
    /// 命令层：只负责流程编排与交互，具体工作交给各个服务。
    /// 每个命令都包一层 try/catch —— 运行时异常一定要打到命令行，
    /// 否则用户看到的就是"命令没反应"。
    /// </summary>
    public sealed class BlockNumberingCommands
    {

        /// <summary>
        /// 属性块批量编号主命令。
        /// 交互流程：选择块 → 选属性/参数（含预览）→ 事务写入 → 报告。
        /// </summary>
        [CommandMethod("BLKNUM", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void BatchNumber()
        {
            try
            {
                BatchNumberCore();
            }
            catch (System.Exception ex)
            {
                CommandHost.ReportFailure("BLKNUM", ex);
            }
        }

        private void BatchNumberCore()
        {
            // 界面判断必须放在最前面：无界面宿主（accoreconsole 等）里，访问 Document.Database
            // 或让 AutoCAD 预处理 UsePickSet 拾取集都可能直接原生崩溃（AV 0xC0000005），
            // 那种崩溃在托管代码里 catch 不到，所以只能提前拦。
            Document probe = AcadApp.DocumentManager.MdiActiveDocument;
            Editor probeEditor = probe != null ? probe.Editor : null;
            if (!CommandHost.RequireUserInterface(
                    probeEditor,
                    "BLKNUM",
                    "BLKNUM 需要弹出对话框；无界面宿主请用 BLKNUMINFO 确认插件状态。"))
            {
                return;
            }

            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;
            if (db == null)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n当前文档没有可用的图形数据库，无法编号。");
                return;
            }

            AppConfig config = ConfigService.Load();
            NumberOptions options = config.Options;
            if (options == null)
            {
                options = new NumberOptions();
            }

            // ============ 流程：先弹对话框，再在对话框里两步选块 ============
            //   ① 在图纸中点选一个块作为"样板" —— 决定按哪种块编号；
            //   ② 点"选择范围"框选一个区域 —— 区域内与样板同名的块自动被收集。
            // 顺序反过来（先弹窗、再选块）是为了避免"先框选、选错块又得重来"。
            //
            // 对话框本身不能执行编辑器提示（模态期间无法拾取），所以采用
            // "对话框 → 选块 → 再开对话框"的循环：按钮只返回一个动作，
            // 真正的拾取在命令上下文里做。
            //
            // 命令执行前已经选好块的情况（UsePickSet 预选集）：
            // 把第一个块当样板，其余按同名过滤（与"两步选块"的模型一致）。
            List<ObjectId> pendingIds = ReadImpliedSelection(ed);
            ObjectId sampleId = ObjectId.Null;   // 样板块（用于取属性定义、取块名）

            CollectionStats stats = new CollectionStats();
            List<BlockItem> candidates = null;
            List<AttributeDefInfo> definitions = null;
            string blockLabel = string.Empty;

            NumberingPlan plan = null;
            NumberingAction action = NumberingAction.Cancel;

            while (true)
            {
                // ---- ① 刚选完样板块：只更新"按哪种块编号"，先不收集对象 ----
                if (!ObjectId.Null.Equals(sampleId))
                {
                    ObjectId id = sampleId;
                    sampleId = ObjectId.Null;

                    candidates = null;
                    definitions = null;
                    blockLabel = string.Empty;

                    if (TryReadSample(db, id, out blockLabel, out definitions))
                    {
                        // 这里**刻意不往命令行打"样板块：xxx。再点②…"**：
                        // ① 对话框状态栏（那行黄字）已经原样显示了同样的信息，命令行再打
                        //    一遍纯属冗余；
                        // ② 开着 F12 时命令行输出会被"动态输入提示"抓到跟着光标浮动的灰框
                        //    里，而且不自己消失，用户回到对话框后会看到它挡着界面。
                        // 插件对自己的输出负责 —— 冗余的过程提示就不打了。
                    }
                    else
                    {
                        definitions = null;
                        blockLabel = string.Empty;
                        CommandHost.WriteMessageNoDynPrompt(ed,
                            "\n所选对象不是（或不是一个带属性的）块参照，请重新选择样板块。");
                    }
                }

                // ---- ② 启动时带了预选集：第一个块作样板，整批按同名收集 ----
                if (pendingIds != null)
                {
                    List<ObjectId> ids = pendingIds;
                    pendingIds = null;

                    candidates = null;
                    definitions = null;
                    blockLabel = string.Empty;

                    if (TryReadSample(db, ids[0], out blockLabel, out definitions))
                    {
                        stats = new CollectionStats();
                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            candidates = BlockSelector.Collect(
                                db, tr, ids, options.SkipXrefBlocks, blockLabel, stats);
                            tr.Commit();
                        }

                        // 这里同样**刻意不打命令行**：收集到多少个、跳过了多少个，
                        // 对话框状态栏那行已经写得很清楚（"样板：xxx —— 可编号 N 个…"）。
                        // 少一条命令行输出 = 少一次被 F12 动态输入浮动框抓走并挂屏的机会。
                    }
                    else
                    {
                        definitions = null;
                        blockLabel = string.Empty;
                        // 这种情况对话框里没别的途径告诉用户（没有样板 → 状态栏只会写
                        // "请先点①"），所以必须保留一条命令行提示。
                        CommandHost.WriteMessageNoDynPrompt(ed, "\n预选的第一个对象不是带属性的块，已忽略预选集。");
                    }
                }

                // 记住上次这个块用过的 Tag
                if (candidates != null && !string.IsNullOrEmpty(blockLabel))
                {
                    string remembered = config.FindTag(blockLabel);
                    if (!string.IsNullOrEmpty(remembered) && ContainsTag(definitions, remembered))
                    {
                        options.Tag = remembered;
                    }
                }

                // 闭包捕获当前这批候选（每次循环都会重建）
                List<BlockItem> snapshot = candidates;
                Func<NumberOptions, NumberingPlan> rebuild = delegate(NumberOptions current)
                {
                    // 排序固定按世界坐标系(WCS)，所以变换矩阵是单位阵
                    return PlanBuilder.Build(db, snapshot, current, Matrix3d.Identity);
                };

                // 每轮都新建窗体（旧的已在回图纸拾取前挪到屏幕外并关闭）。
                // 参数经 NumberOptions 原样带回，所以界面状态不会丢。
                using (NumberingOptionsForm form = new NumberingOptionsForm(
                    definitions, candidates, blockLabel, options, rebuild, Matrix3d.Identity))
                {
                    CommandHost.ShowDialogSafely(form);
                    action = form.Action;
                    options = form.Options;
                    plan = form.Plan;
                }

                if (action == NumberingAction.Cancel)
                {
                    return;
                }
                if (action == NumberingAction.Write)
                {
                    break;
                }

                if (action == NumberingAction.PickSample)
                {
                    ObjectId picked;
                    if (TryPromptSampleBlock(ed, out picked))
                    {
                        sampleId = picked;
                    }
                    continue;
                }

                if (action == NumberingAction.PickRange)
                {
                    if (string.IsNullOrEmpty(blockLabel))
                    {
                        // 没样板就去点②，对话框状态栏那行黄字本来就写着"请先点①"，
                        // 命令行不再重复（见上面"过程提示不打"的说明）。
                        continue;
                    }

                    bool rangeCancelled;
                    List<ObjectId> regionIds = PromptForRange(ed, db, out rangeCancelled);
                    if (rangeCancelled)
                    {
                        // 用户自己按的 Esc，他知道自己在干什么；
                        // 对话框状态栏会照常显示"再点【② 选择范围】…"，无需命令行复述。
                        continue;
                    }

                    CollectionStats rangeStats = new CollectionStats();
                    List<BlockItem> found;
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        found = BlockSelector.Collect(
                            db, tr, regionIds, options.SkipXrefBlocks, blockLabel, rangeStats);
                        tr.Commit();
                    }

                    if (found.Count == 0)
                    {
                        // 一条都不打：范围空的这件事，对话框状态栏已经用同一句话说明，
                        // 界面本身也没变化（候选还是空的），用户看得见"可编号 0 个"。
                        // 连续三条 WriteMessage 正是最容易在 F12 下挂出一串灰框的写法。
                        continue;
                    }

                    // 把"选了这么多 / 其中真正要编号的有多少"明确打出来。
                    // 上面那行 AutoCAD 原生的"找到 N 个"是**选中对象总数**，不是要编号的
                    // 块数，用户看那行会以为要编号 2000 多个，所以这里必须补一行真实数量。
                    //
                    // 走 WriteMessageNoDynPrompt：命令行照常看得到，但不会被 F12 动态输入
                    // 抓到浮动灰框里挂着不走（那正是用户回到可视化页面后看到的遮挡物）。
                    CommandHost.WriteMessageNoDynPrompt(ed,
                        "\n本范围内与样板“{0}”同名的可编号块 {1} 个（选中对象共 {2} 个，跳过 {3} 个）。",
                        blockLabel, found.Count, rangeStats.Received,
                        rangeStats.Received - found.Count);

                    candidates = found;
                    stats = rangeStats;
                    continue;
                }

                return;   // 未知动作，安全退出
            }

            if (plan == null || plan.WritableCount == 0)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n没有可编号的块，已结束。");
                return;
            }

            // ============ 3. 事务写入 ============
            // 命令上下文本身持有文档锁，因此这里不需要再 LockDocument；
            // 若改成从面板(模型对话框)调用，必须先 doc.LockDocument()。
            WriteResult write = AttributeWriter.Write(db, plan.Items);

            // ============ 4. 结果报告 ============
            // 报告走 WriteMessageNoDynPrompt：命令行能看全，但不会冒到 F12 动态输入
            // 浮动框里挡着（那个框不自己刷新，会一直挂在屏幕上）。
            ReportService.Print(ed, plan, write, delegate(string s) { CommandHost.WriteMessageNoDynPrompt(ed, s); });

            if (write.Written == 0)
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n提示：没有写入任何值，请检查目标属性 Tag 与表格中的“状态”列。");
            }
            else
            {
                CommandHost.WriteMessageNoDynPrompt(ed, "\n完成。输入 U 可一次性撤销本次全部修改。");
            }

            // ============ 5. 收尾：让被编号的块保持选中 ============
            // 写入后直接把刚改过的块设为"当前选择"—— AutoCAD 里被选中就是高亮的，
            // 而且夹点会保留在屏幕上，用户可以立刻核对/接着操作。
            //
            // 这里**刻意不再补打任何命令行消息**：
            //   ① 块已经被高亮选中，状态栏也会显示选中数量，再说一句"已保持选中"是冗余的
            //      （用户明确要求去掉那句提示）；
            //   ② 更要紧的是 —— WriteMessage 会结束上一次选择状态，而 Select 用的正是
            //      "预选集"机制。在 Select 之后再打消息，等于把刚设好的选中又冲掉。
            //      Select 必须是本命令的最后一步。
            if (write.Written > 0)
            {
                AttributeWriter.Select(ed, write.WrittenBlockIds);
            }

            SaveConfig(config, options, blockLabel);
        }

        /// <summary>使用说明。</summary>
        [CommandMethod("BLKNUMHELP", CommandFlags.Modal)]
        public void ShowHelp()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            doc.Editor.WriteMessage(ChemForge.UI.Forms.Block.Numbering.NumberingHelpText.CommandLineText);
        }

        /// <summary>
        /// 插件自我诊断。当"NETLOAD 之后没有命令"时，让用户先把这些信息打出来，
        /// 一眼就能区分：是插件根本没加载，还是被 SECURELOAD / TRUSTEDPATHS 挡了，
        /// 还是加载到了别的文件。
        /// </summary>
        [CommandMethod("BLKNUMINFO", CommandFlags.Modal)]
        public void ShowInfo()
        {
            try
            {
                Document doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    return;
                }

                Editor ed = doc.Editor;
                string location = BlockNumberingDiagnostics.PluginLocation;

                ed.WriteMessage(
                    "\n===== BlockNumbering 自检 =====" +
                    "\n程序集版本   : " + BlockNumberingDiagnostics.PluginVersion +
                    "\n程序集路径   : " + location +
                    "\nAutoCAD 版本 : " + BlockNumberingDiagnostics.SysVar("ACADVER") +
                    "\n产品         : " + BlockNumberingDiagnostics.SysVar("PRODUCT") +
                    "\nSECURELOAD   : " + BlockNumberingDiagnostics.SysVar("SECURELOAD") +
                    "\nTRUSTEDPATHS : " + BlockNumberingDiagnostics.SysVar("TRUSTEDPATHS") +
                    "\n已注册命令   : BLKNUM / BLKNUMHELP / BLKNUMINFO" +
                    "\n可用图形界面 : " + CommandHost.HasUserInterface(out string _) +
                    "\n宿主进程     : " + CommandHost.HostProcessName +
                    "\n动态输入开关 : " + CommandHost.DescribeDynInputHook() + " + 空白擦除(兜底)" +
                    "\n===============================");

                if (string.IsNullOrEmpty(location) || location.IndexOf("://", StringComparison.Ordinal) >= 0)
                {
                    ed.WriteMessage("\n提示：程序集没有磁盘路径（可能来自字节数组加载）。");
                }

                string secureLoad = BlockNumberingDiagnostics.SysVar("SECURELOAD");
                if (secureLoad == "1" || secureLoad == "2")
                {
                    ed.WriteMessage(
                        "\n提示：SECURELOAD=" + secureLoad
                        + "，AutoCAD 只允许从 TRUSTEDPATHS 里的目录加载可执行文件。"
                        + "\n      若插件放在其它目录，NETLOAD 会被拦下且可能不报警。");
                    ed.WriteMessage(
                        "\n      解决办法（二选一）："
                        + "\n        1) 把插件目录加入受信路径："
                        + "\n           (setvar \"TRUSTEDPATHS\" (strcat (getvar \"TRUSTEDPATHS\") \";" + GuessFolder(location) + "\"))"
                        + "\n        2) 或者临时放开限制： (setvar \"SECURELOAD\" 0)");
                }
            }
            catch (System.Exception ex)
            {
                CommandHost.ReportFailure("BLKNUMINFO", ex);
            }
        }

        private static string GuessFolder(string filePath)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath) && filePath.IndexOf("://", StringComparison.Ordinal) < 0)
                {
                    return System.IO.Path.GetDirectoryName(filePath);
                }
            }
            catch (System.Exception)
            {
            }
            return "(插件所在目录)";
        }

        /// <summary>
        /// 读取"先选块、再执行命令"的预选集（UsePickSet）。
        /// 读到之后顺手清空预选，避免影响后续拾取。没有预选返回 null。
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
        /// 读取一个块参照，取它的有效块名与属性定义。成功返回 true。
        /// 样板块（① 步）和"执行命令前的预选集"都会用到。
        /// </summary>
        private static bool TryReadSample(
            Database db, ObjectId id, out string effectiveName, out List<AttributeDefInfo> definitions)
        {
            effectiveName = string.Empty;
            definitions = null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockReference reference = null;
                try
                {
                    reference = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                }
                catch (AcException)
                {
                }
                catch (System.Exception)
                {
                }

                if (reference != null)
                {
                    effectiveName = BlockSelector.GetEffectiveName(reference, tr);
                    definitions = AttributeService.GetAttributeDefinitions(reference, tr);
                }
                tr.Commit();
            }

            return definitions != null && definitions.Count > 0;
        }

        /// <summary>
        /// 第 ① 步：回到图纸中点选一个块作为"样板"（决定按哪种块编号）。
        /// 取消或选到的不是块时返回 false。
        /// </summary>
        private static bool TryPromptSampleBlock(Editor ed, out ObjectId picked)
        {
            picked = ObjectId.Null;

            PromptEntityOptions entityOptions =
                new PromptEntityOptions("\n点选一个块作为样板（程序按与之同名的块编号）: ");
            entityOptions.SetRejectMessage("\n请选择一个块参照。");
            entityOptions.AddAllowedClass(typeof(BlockReference), true);

            PromptEntityResult result = ed.GetEntity(entityOptions);
            if (result.Status != PromptStatus.OK)
            {
                // 用户按了 Esc。对话框原来什么样、现在就什么样，不需要命令行复述。
                return false;
            }

            picked = result.ObjectId;
            return true;
        }

        /// <summary>
        /// 第 ② 步：回到图纸用 **AutoCAD 自带的框选** 选一批对象
        /// （就是"选择对象:"提示，可以拖窗口/交叉框，也可以逐点点选，回车结束；
        ///  选中过程中对象本身就是高亮的，不需要额外做亮显）。
        /// 返回选中的所有对象 Id，同名过滤交给随后的 BlockSelector.Collect 用样板名完成。
        ///
        /// 什么都不选直接回车 = 不限定范围，取整个图形里的同名块（保留这一步的兜底能力）。
        /// Esc 取消时 cancelled = true。
        /// </summary>
        private static List<ObjectId> PromptForRange(Editor ed, Database db, out bool cancelled)
        {
            cancelled = false;

            PromptSelectionOptions selectionOptions = new PromptSelectionOptions();
            selectionOptions.MessageForAdding =
                "\n选择要编号的块（框选或点选；只收集与样板同名的块，回车结束；"
                + "不选直接回车 = 整个图形）: ";
            selectionOptions.AllowDuplicates = false;

            // 原生框选每轮都会回显"找到 N 个，总计 M 个"，那是**选中对象总数**、
            // 不是要编号的块数。这里不去改 CMDECHO（那是用户的 CAD 设置），
            // 而是紧接着由插件自己补一行真正有用的统计（见调用方），把误解盖过去。
            PromptSelectionResult result = ed.GetSelection(selectionOptions);

            if (result.Status == PromptStatus.Cancel)
            {
                cancelled = true;
                return null;
            }

            // 选中了对象 → 就用这批；什么都没选直接回车 → 整个图形
            if (result.Status == PromptStatus.OK && result.Value != null && result.Value.Count > 0)
            {
                return new List<ObjectId>(result.Value.GetObjectIds());
            }

            // 不选直接回车 = 整个图形。这条**结果**由调用方补一行真实统计
            // （"本范围内与样板 xxx 同名的可编号块 N 个"），所以这里不另打提示——
            // 省一条命令行输出就少一次被 F12 动态输入抓走挂屏的机会。

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<ObjectId> all = BlockSelector.FindSameNameIds(db, tr, true);
                tr.Commit();
                return all;
            }
        }

        private static bool ContainsTag(List<AttributeDefInfo> definitions, string tag)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                if (string.Equals(definitions[i].Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void SaveConfig(AppConfig config, NumberOptions options, string blockName)
        {
            config.Options = options;
            config.RememberTag(blockName, options.Tag);
            ConfigService.Save(config);
        }
    }
}
