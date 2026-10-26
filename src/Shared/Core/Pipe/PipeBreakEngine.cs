#if NET48 || NET80
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ChemForge.Utilities;
// ChemForge.Application 与 Autodesk.AutoCAD.ApplicationServices.Application 同名冲突，
// 用别名区分（管道命令主体使用 CAD 静态入口 Application 与 ApplicationServices.Core.Application）
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ChemForge.Core.Pipe
{
    /// <summary>
    /// 管线交叉缺口打断 / 半圆架桥引擎
    /// 命令: PIPEBREAK（管线 × 管线，避让侧由「缺口方向」决定）
    /// 命令: INSTBREAK（仪表线 × 管线，避让侧固定为仪表线，跨图层配对）
    ///
    /// 两条命令共用同一套几何处理：交互段各自提问，处理段统一走 <see cref="RunCore"/>。
    /// 单图层模式（PIPEBREAK）保持原有行为不变：同向管线不配对，避让侧按方向选取。
    /// 双图层模式（INSTBREAK）只做跨图层配对，避让侧恒为「仪表线图层」那一侧。
    ///
    /// 本引擎随 ChemForge 主插件一并编译进 Net48 / Net80 目标。
    /// 实现要点：CommandMethod 挂载为静态入口（实例方法不会被 AutoCAD 注册），
    /// 逻辑类整体为静态类。
    ///
    /// v2 性能重构要点（相对 v1）：
    ///   1. 均匀网格空间索引 + 方向分组：不再 O(n²) 两两求交，同向管线直接不配对；
    ///   2. 每条管线只 GetObject 一次，方向/包围盒/顶点/长度全部预缓存；
    ///   3. 取消循环内逐条 WriteMessage，改为一次性汇总输出；
    ///   4. 图层冻结/锁定、模型空间 Id 均做缓存，不再逐实体查表；
    ///   5. 分割点改用「弧长定位」(GetDistAtPoint / GetPointAtDist)，
    ///      替代每条交点新建一个临时 Circle 做 IntersectWith + 排序；
    ///   6. 只在缺口边界点分割，段取舍用二分查找判定，避免 O(k²)；
    ///   7. 实体属性用 LayerId / LinetypeId 直接赋值（免字符串查符号表），
    ///      且每条管线只读取一次源属性。
    /// </summary>
    public static class PipeBreakEngine
    {
        // ==================== 数据结构 ====================

        /// <summary>管线缓存：几何对象只打开一次，方向/包围盒/顶点/长度全部预计算</summary>
        private sealed class PipeInfo
        {
            public ObjectId Id;
            public Curve Curve;
            public bool IsVertical;
            /// <summary>双图层模式下的分组：0 = 被打断（避让）侧，1 = 另一侧；单图层模式不使用</summary>
            public int Group;
            public double MinX, MinY, MaxX, MaxY;
            public Point3d[] Nodes;            // Line:2 点；Polyline:全部顶点（缓存，避免逐次 P/Invoke）
            public bool ConstZ;                // Z 是否恒定（水平面内管线）
            public double Z;
            public double Length = double.NaN; // 弧长，惰性计算
        }

        /// <summary>待处理的打断记录（避让管线索引 + 分割点）</summary>
        private sealed class BreakRecord
        {
            public int AvoidIndex;
            public Point3d IntersectionPoint;
            public Point3d SplitA;
            public Point3d SplitB;
        }

        /// <summary>参数区间（用于合并删除段）</summary>
        private struct Interval
        {
            public double Start;
            public double End;
            public Interval(double start, double end)
            {
                Start = start;
                End = end;
            }
        }

        /// <summary>源实体属性快照：每条管线只读取一次，避免逐段重复读/查符号表</summary>
        private sealed class EntityProps
        {
            public ObjectId LayerId;
            public ObjectId LinetypeId;
            public double LinetypeScale;
            public LineWeight LineWeight;
            public int ColorIndex;
            public Autodesk.AutoCAD.Colors.Transparency Transparency;
            public string PlotStyleName;
            public bool ApplyPlotStyle;
        }

        // ==================== 命令主体（由 Commands/Pipe/*Command 调用） ====================

        /// <summary>缺口处理方式：只挖缺口 / 半圆拱桥 / S 型样条</summary>
        private enum BreakMode
        {
            /// <summary>只挖缺口，不补任何图形</summary>
            Gap,
            /// <summary>缺口处补半圆拱桥（圆弧段）</summary>
            Bridge,
            /// <summary>缺口处补 S 型样条曲线（5 个拟合点）</summary>
            SCurve
        }

        /// <summary>一次打断请求的全部参数：交互段填好，处理段不再提问</summary>
        private sealed class BreakRequest
        {
            public double TotalWidth;

            /// <summary>S 型符号宽度（默认 = <see cref="TotalWidth"/>，可单独改；符号内部比例固定为 W/8）</summary>
            public double SCurveWidth;

            /// <summary>缺口处理方式</summary>
            public BreakMode Mode;
            public Vector3d SideVector = new Vector3d(0, 1, 0);

            /// <summary>被打断（避让）侧所在图层；为 null 表示单图层模式</summary>
            public string AvoidLayer;

            /// <summary>另一侧图层；单图层模式下为 null</summary>
            public string OtherLayer;

            /// <summary>单图层模式：true = 打断垂直线（即原「缺口方向 垂直(V)」）</summary>
            public bool BreakVertical = true;

            /// <summary>日志用的命令名</summary>
            public string CommandName = "PIPEBREAK";

            /// <summary>被打断侧的称呼（管线 / 仪表线），仅用于提示文字</summary>
            public string AvoidLabel = "管线";

            /// <summary>双图层模式：只做跨图层配对，避让侧恒为 <see cref="AvoidLayer"/></summary>
            public bool TwoLayer;
        }

        /// <summary>把模式关键字（D / H / S）转成枚举，未知值按「缺口」处理</summary>
        private static BreakMode ParseMode(string keyword)
        {
            switch (keyword)
            {
                case "H": return BreakMode.Bridge;
                case "S": return BreakMode.SCurve;
                default: return BreakMode.Gap;
            }
        }

        /// <summary>日志/提示用的模式中文名</summary>
        private static string ModeLabel(BreakMode mode)
        {
            switch (mode)
            {
                case BreakMode.Bridge: return "架桥";
                case BreakMode.SCurve: return "S型";
                default: return "缺口";
            }
        }

        /// <summary>图层点选结果</summary>
        private enum LayerPick
        {
            /// <summary>已确定图层</summary>
            Ok,
            /// <summary>用户按 Esc 取消</summary>
            Cancelled,
            /// <summary>既没点选也没有上次记忆</summary>
            Failed
        }

        /// <summary>执行管线自动打断主流程（PIPEBREAK，单人单事务，Esc 可整体回滚）</summary>
        public static void Execute()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Logger.Warning("命令 PIPEBREAK 被调用时没有打开的图形（MdiActiveDocument 为 null），已忽略。");
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. 读取上次保存的总宽度（存插件自己的配置文件，不写图纸系统变量）
            double lastWidth = PipeBreakSettings.GetLastWidth(1000.0);

            // 2. 输入宽度
            var pdo = new PromptDoubleOptions("\n请输入缺口/架桥总宽度(mm): ");
            pdo.DefaultValue = lastWidth;
            pdo.AllowNone = false;
            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK) return;
            double totalWidth = pdr.Value;
            PipeBreakSettings.SetLastWidth(totalWidth);

            // 3. 选择模式：缺口 / 半圆架桥 / S 型（记忆上次选择，回车沿用）
            var pko = new PromptKeywordOptions("\n选择模式 [缺口(D)/架桥(H)/S型(S)]: ");
            pko.Keywords.Add("D");
            pko.Keywords.Add("H");
            pko.Keywords.Add("S");
            pko.Keywords.Default = PipeBreakSettings.GetLastMode("D");
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;
            BreakMode mode = ParseMode(pkr.StringResult);
            PipeBreakSettings.SetLastMode(pkr.StringResult);

            // 3.5 缺口方向（程序自动判断避让管线，无需逐点选择；记忆上次选择）
            var pkoDir = new PromptKeywordOptions("\n选择缺口方向 [垂直(V)/水平(H)]: ");
            pkoDir.Keywords.Add("V");
            pkoDir.Keywords.Add("H");
            pkoDir.Keywords.Default = PipeBreakSettings.GetLastGapDirection("V");
            PromptResult pkrDir = ed.GetKeywords(pkoDir);
            if (pkrDir.Status != PromptStatus.OK) return;
            bool breakVertical = pkrDir.StringResult != "H";
            PipeBreakSettings.SetLastGapDirection(breakVertical ? "V" : "H");

            // 3.6 S 型符号宽度：默认与缺口/架桥宽度一致，可单独改（符号内部比例固定为 W/8）
            double sWidth = mode == BreakMode.SCurve
                ? PromptSCurveWidth(ed, totalWidth)
                : totalWidth;
            if (sWidth <= 0) return;

            // 3.7 架桥：半圆凸起朝向（S 型不需要：S 左右对称，凸起朝向不改变形状）
            Vector3d sideVector = new Vector3d(0, 1, 0); // 默认上方 U
            if (mode == BreakMode.Bridge)
            {
                Vector3d? side = PromptBridgeSide(ed);
                if (side == null) return;
                sideVector = side.Value;
            }

            // 3.9 指定管线所在图层（记忆上次选择，与宽度记忆共用同一份插件配置文件）
            string pipeLayer;
            LayerPick pick = PromptLayer(ed, db,
                "\n请点选一条管线以确定管线图层（回车使用上次图层）: ",
                PipeBreakSettings.GetLastLayer(), PipeBreakSettings.SetLastLayer, out pipeLayer);
            if (pick == LayerPick.Cancelled) return;
            if (pick == LayerPick.Failed)
            {
                ed.WriteMessage("\n无法确定管线图层。");
                return;
            }
            ed.WriteMessage("\n管线图层: " + pipeLayer);

            // 4. 框选管线：用选择过滤器在拾取阶段就过滤类型和图层，避免把整张图带进来
            var filter = new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE,LWPOLYLINE"),
                new TypedValue(8, pipeLayer)
            });
            var pso = new PromptSelectionOptions();
            pso.MessageForAdding = "选择管线（Line/二维Polyline）";
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;

            // 5. 处理（单图层模式：避让侧仍由「缺口方向」决定，行为与改造前一致）
            RunCore(doc, sw, new BreakRequest
            {
                TotalWidth = totalWidth,
                SCurveWidth = sWidth,
                Mode = mode,
                SideVector = sideVector,
                BreakVertical = breakVertical,
                AvoidLayer = pipeLayer,
                OtherLayer = null,
                TwoLayer = false,
                CommandName = "PIPEBREAK",
                AvoidLabel = "管线"
            }, psr.Value.GetObjectIds());
        }

        /// <summary>
        /// 执行仪表线自动打断主流程（INSTBREAK，单人单事务，Esc 可整体回滚）
        ///
        /// 与 PIPEBREAK 的差异：
        ///   1. 图层点选两次：先仪表线图层、再管线图层（各自记忆上次选择）；
        ///   2. 不再询问「缺口方向」——避让侧固定为仪表线，即仪表线让管线；
        ///   3. 框选一次即可：过滤器同时接受两个图层，程序按图层分组配对；
        ///   4. 只处理跨图层交叉；同层交叉（仪表线×仪表线、管线×管线）不打断，
        ///      管线×管线仍交给 PIPEBREAK。
        /// </summary>
        public static void ExecuteInstrument()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Logger.Warning("命令 INSTBREAK 被调用时没有打开的图形（MdiActiveDocument 为 null），已忽略。");
                return;
            }

            Database db = doc.Database;
            Editor ed = doc.Editor;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. 读取上次保存的总宽度（与 PIPEBREAK 共用同一记忆值）
            double lastWidth = PipeBreakSettings.GetLastWidth(1000.0);

            // 2. 输入宽度
            var pdo = new PromptDoubleOptions("\n请输入缺口/架桥总宽度(mm): ");
            pdo.DefaultValue = lastWidth;
            pdo.AllowNone = false;
            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK) return;
            double totalWidth = pdr.Value;
            PipeBreakSettings.SetLastWidth(totalWidth);

            // 3. 选择模式：缺口 / 半圆架桥 / S 型（记忆上次选择，回车沿用）
            var pko = new PromptKeywordOptions("\n选择模式 [缺口(D)/架桥(H)/S型(S)]: ");
            pko.Keywords.Add("D");
            pko.Keywords.Add("H");
            pko.Keywords.Add("S");
            pko.Keywords.Default = PipeBreakSettings.GetLastMode("D");
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;
            BreakMode mode = ParseMode(pkr.StringResult);
            PipeBreakSettings.SetLastMode(pkr.StringResult);

            // 3.6 S 型符号宽度：默认与缺口/架桥宽度一致，可单独改（符号内部比例固定为 W/8）
            double sWidth = mode == BreakMode.SCurve
                ? PromptSCurveWidth(ed, totalWidth)
                : totalWidth;
            if (sWidth <= 0) return;

            // 4. 架桥：半圆凸起朝向（S 型不需要：S 左右对称，凸起朝向不改变形状）
            Vector3d sideVector = new Vector3d(0, 1, 0); // 默认上方 U
            if (mode == BreakMode.Bridge)
            {
                Vector3d? side = PromptBridgeSide(ed);
                if (side == null) return;
                sideVector = side.Value;
            }

            // 5. 指定仪表线图层（被打断的一侧）
            string instrLayer;
            LayerPick pickInstr = PromptLayer(ed, db,
                "\n请点选一条仪表线以确定仪表线图层（回车使用上次图层）: ",
                PipeBreakSettings.GetLastInstrumentLayer(), PipeBreakSettings.SetLastInstrumentLayer,
                out instrLayer);
            if (pickInstr == LayerPick.Cancelled) return;
            if (pickInstr == LayerPick.Failed)
            {
                ed.WriteMessage("\n无法确定仪表线图层。");
                return;
            }
            ed.WriteMessage("\n仪表线图层: " + instrLayer);

            // 6. 指定管线图层（不被打断的一侧）
            string pipeLayer;
            LayerPick pickPipe = PromptLayer(ed, db,
                "\n请点选一条管线以确定管线图层（回车使用上次图层）: ",
                PipeBreakSettings.GetLastLayer(), PipeBreakSettings.SetLastLayer, out pipeLayer);
            if (pickPipe == LayerPick.Cancelled) return;
            if (pickPipe == LayerPick.Failed)
            {
                ed.WriteMessage("\n无法确定管线图层。");
                return;
            }
            ed.WriteMessage("\n管线图层: " + pipeLayer);

            if (string.Equals(instrLayer, pipeLayer, StringComparison.OrdinalIgnoreCase))
            {
                ed.WriteMessage("\n仪表线图层与管线图层相同（" + instrLayer +
                                "），无法区分让位关系，命令已取消。");
                return;
            }

            // 7. 框选：一次框选两个图层（<OR> 过滤器同时接受仪表线与管线），避免把整张图带进来
            var filter = new SelectionFilter(new[]
            {
                new TypedValue(0, "LINE,LWPOLYLINE"),
                new TypedValue(-4, "<OR"),
                new TypedValue(8, instrLayer),
                new TypedValue(8, pipeLayer),
                new TypedValue(-4, "OR>")
            });
            var pso = new PromptSelectionOptions();
            pso.MessageForAdding = "选择仪表线与管线（Line/二维Polyline）";
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK) return;

            // 8. 处理（双图层模式：避让侧恒为仪表线）
            RunCore(doc, sw, new BreakRequest
            {
                TotalWidth = totalWidth,
                SCurveWidth = sWidth,
                Mode = mode,
                SideVector = sideVector,
                AvoidLayer = instrLayer,
                OtherLayer = pipeLayer,
                TwoLayer = true,
                CommandName = "INSTBREAK",
                AvoidLabel = "仪表线"
            }, psr.Value.GetObjectIds());
        }

        /// <summary>
        /// 询问架桥半圆凸起方向 U/D/L/R（记忆上次选择，回车沿用）；取消时返回 null。
        /// </summary>
        private static Vector3d? PromptBridgeSide(Editor ed)
        {
            var pko = new PromptKeywordOptions("\n架桥半圆凸起方向 [上方(U)/下方(D)/左(L)/右(R)]: ");
            pko.Keywords.Add("U");
            pko.Keywords.Add("D");
            pko.Keywords.Add("L");
            pko.Keywords.Add("R");
            pko.Keywords.Default = PipeBreakSettings.GetLastBridgeSide("U");
            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return null;

            PipeBreakSettings.SetLastBridgeSide(pkr.StringResult);
            switch (pkr.StringResult)
            {
                case "D": return new Vector3d(0, -1, 0);
                case "L": return new Vector3d(-1, 0, 0);
                case "R": return new Vector3d(1, 0, 0);
                default: return new Vector3d(0, 1, 0);
            }
        }

        /// <summary>
        /// 询问 S 型符号宽度：默认值优先用**用户上次手动输入过的值**（记忆在 PipeBreak.settings 的
        /// <c>LastSCurveWidth</c>），没记过时默认等于缺口/架桥宽度；直接回车即接受默认值。
        /// 只有用户真的输入了一个与缺口宽度不同的数才记忆；输回缺口宽度视为「恢复默认」，清掉记忆。
        /// 输入非正值视为取消。
        /// </summary>
        private static double PromptSCurveWidth(Editor ed, double totalWidth)
        {
            double remembered = PipeBreakSettings.GetLastSCurveWidth(0.0);
            bool hasMemory = remembered > 1e-9;
            double defaultValue = hasMemory ? remembered : totalWidth;

            string message = hasMemory
                ? "\n请输入 S 型符号宽度(mm) <回车沿用上次 " + remembered.ToString("0.###") + ">: "
                : "\n请输入 S 型符号宽度(mm) <回车与缺口宽度一致>: ";

            var pdo = new PromptDoubleOptions(message);
            pdo.DefaultValue = defaultValue;
            pdo.AllowNone = true;
            PromptDoubleResult pdr = ed.GetDouble(pdo);

            // 回车 = 接受默认值，记忆保持原样
            if (pdr.Status == PromptStatus.None) return defaultValue;
            if (pdr.Status != PromptStatus.OK) return -1.0;
            if (pdr.Value <= 0.0)
            {
                ed.WriteMessage("\nS 型符号宽度必须大于 0，已取消。");
                return -1.0;
            }

            if (Math.Abs(pdr.Value - totalWidth) <= 1e-9)
            {
                // 跟缺口宽度一致 = 默认值，不需要记忆
                if (hasMemory) PipeBreakSettings.ClearLastSCurveWidth();
            }
            else
            {
                PipeBreakSettings.SetLastSCurveWidth(pdr.Value);
            }

            return pdr.Value;
        }

        /// <summary>
        /// 点选一条线以确定图层；回车（AllowNone）沿用上次记忆。
        /// <paramref name="persist"/> 非空时把点选结果写进跨图纸记忆。
        /// </summary>
        private static LayerPick PromptLayer(Editor ed, Database db, string message,
                                             string lastLayer, Action<string> persist,
                                             out string layer)
        {
            layer = lastLayer;

            var peo = new PromptEntityOptions(message);
            peo.AllowNone = true;
            peo.SetRejectMessage("请选择 Line 或二维 Polyline。");
            peo.AddAllowedClass(typeof(Line), true);
            peo.AddAllowedClass(typeof(Polyline), true);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.Cancel) return LayerPick.Cancelled;
            if (per.Status == PromptStatus.OK)
            {
                using (Transaction trLayer = db.TransactionManager.StartTransaction())
                {
                    Entity e = trLayer.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                    layer = e != null ? e.Layer : null;
                    trLayer.Commit();
                }
                if (!string.IsNullOrEmpty(layer) && persist != null)
                    persist(layer);
            }

            return string.IsNullOrEmpty(layer) ? LayerPick.Failed : LayerPick.Ok;
        }

        /// <summary>处理段：框选结果 → 求交 → 分割/架桥（单人单事务，Esc 可整体回滚）</summary>
        private static void RunCore(Document doc, System.Diagnostics.Stopwatch sw,
                                    BreakRequest req, ObjectId[] selectedIds)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;
            bool twoLayer = req.TwoLayer;
            // 提示文字里的「线」的统称：单图层模式沿用改造前的措辞（管线），双图层模式泛指两侧曲线
            string lineWord = twoLayer ? "曲线" : "管线";
            Logger.Info("命令 " + req.CommandName + " 进入处理段：宽度=" + req.TotalWidth +
                        "，模式=" + ModeLabel(req.Mode) +
                        "，图层模式=" + (twoLayer ? "双图层（避让侧固定为 " + req.AvoidLabel + "）" : "单图层"));

            // 在事务内完成全部处理（可整体 Undo）
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 模型空间 Id 只查一次（v1 每个实体都开一次块表）
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId msId = bt[BlockTableRecord.ModelSpace];

                // 阶段0：一次性打开全部图元并缓存几何信息（双图层模式下同时打好分组标记）
                List<PipeInfo> pipes = BuildPipeInfos(tr, selectedIds, req.AvoidLayer, req.OtherLayer, msId,
                                                      out int skipType, out int skipLayer,
                                                      out int skipLocked, out int skipSpace);
                if (skipType + skipLayer + skipLocked + skipSpace > 0)
                    ed.WriteMessage("\n[过滤汇总] 类型不符 " + skipType + "，" +
                                    (twoLayer ? "非目标图层 " : "非管线图层 ") + skipLayer +
                                    "，图层冻结/锁定 " + skipLocked + "，非模型空间 " + skipSpace);

                if (pipes.Count < 2)
                {
                    ed.WriteMessage(twoLayer ? "\n有效图元数量不足。" : "\n有效管线数量不足。");
                    return;
                }

                if (twoLayer)
                {
                    int avoidCount = 0, otherCount = 0;
                    foreach (PipeInfo p in pipes)
                    {
                        if (p.Group == 0) avoidCount++; else otherCount++;
                    }
                    ed.WriteMessage("\n有效" + req.AvoidLabel + ": " + avoidCount +
                                    " 条，管线: " + otherCount + " 条");
                    if (avoidCount == 0 || otherCount == 0)
                    {
                        ed.WriteMessage("\n" + req.AvoidLabel + "与管线必须各选到至少一条，没有可处理的交叉。");
                        return;
                    }
                }
                else
                {
                    ed.WriteMessage("\n有效管线: " + pipes.Count + " 条");
                }

                // 阶段1：分组配对 + 均匀网格空间索引求交
                List<BreakRecord> records = new List<BreakRecord>();
                int validIntersectionCount = 0;
                SkipStats skip = new SkipStats();
                long testedPairs = 0;

                CollectIntersections(pipes, req, records,
                                     ref validIntersectionCount, ref skip, ref testedPairs);

                long bruteForce = (long)pipes.Count * (pipes.Count - 1) / 2;
                ed.WriteMessage("\n参与求交的候选组合: " + testedPairs + "（无索引时需 " + bruteForce + "）");
                ed.WriteMessage("\n检测到有效交点数: " + validIntersectionCount + "，跳过: " + skip.Total);

                if (skip.Total > 0)
                {
                    // 分类明细：让用户知道每一个跳过原因，而不是笼统的"跳过 N"
                    var reasons = new System.Collections.Generic.List<string>();
                    if (skip.ZMismatch > 0) reasons.Add("高程(Z)不一致 " + skip.ZMismatch);
                    if (skip.EndpointTouch > 0) reasons.Add("交点与" + lineWord + "端点重合 " + skip.EndpointTouch);
                    if (skip.EndpointNearMiss > 0) reasons.Add("交点贴近" + lineWord + "端点(没接准) " + skip.EndpointNearMiss);
                    if (skip.OffCurve > 0) reasons.Add("交点未投影到避让" + req.AvoidLabel + " " + skip.OffCurve);
                    if (skip.SplitFail > 0) reasons.Add("缺口间距不足/分割失败 " + skip.SplitFail);
                    if (skip.SplitNearNode > 0) reasons.Add("分割点贴近" + lineWord + "端点 " + skip.SplitNearNode);
                    ed.WriteMessage("\n[跳过明细] " + string.Join("，", reasons));

                    // 面向经验最丰富场景给出明确建议：间距太短
                    if (skip.SplitFail > 0)
                        ed.WriteMessage("\n提示：缺口总宽度为 " + req.TotalWidth + "，交叉点距" + lineWord +
                                        "端点需大于 " + (req.TotalWidth / 2.0).ToString("0.###") +
                                        "（半宽）。若交叉点贴近端点或" + lineWord +
                                        "过短，将无法打断——请增大宽度或调整" + lineWord + "位置。");

                    // 「端点没接准」单独点名报出：这属于画图问题，用户需要知道具体是哪两条线、差多少
                    if (skip.EndpointNearMiss > 0)
                    {
                        ed.WriteMessage("\n[端点没接准] " + skip.EndpointNearMiss + " 处交点落在" + lineWord +
                                        "的端点/顶点附近 —— 两条" + lineWord +
                                        "只是端接（T 型），并没有真正交叉，已按端接处理、未打断：");
                        foreach (string note in skip.NearMissDetail)
                            ed.WriteMessage("\n  · " + note);
                        if (skip.EndpointNearMiss > skip.NearMissDetail.Count)
                            ed.WriteMessage("\n  · ...（其余 " +
                                            (skip.EndpointNearMiss - skip.NearMissDetail.Count) + " 处从略）");
                        ed.WriteMessage("\n  提示：这属于画图问题 —— 端点应精确捕捉到另一条" + lineWord +
                                        "上；若确实要交叉，请把" + lineWord + "画过交叉点再打断。");
                    }
                }

                if (records.Count == 0)
                {
                    ed.WriteMessage("\n没有可处理的交叉。");
                    return;
                }

                // 阶段2：按避让管线分组，统一分割、删除中间段、生成拱桥
                BlockTableRecord modelSpace =
                    (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);

                int successCount = ProcessGroups(tr, pipes, records, modelSpace, ed, req);

                ed.WriteMessage("\n成功处理数量: " + successCount + "，耗时 " + sw.ElapsedMilliseconds + " ms");
                tr.Commit();
            }
        }

        // ==================== 阶段0：管线缓存 ====================

        private static List<PipeInfo> BuildPipeInfos(Transaction tr, ObjectId[] ids,
                                                     string avoidLayer, string otherLayer,
                                                     ObjectId msId,
                                                     out int skipType, out int skipLayer,
                                                     out int skipLocked, out int skipSpace)
        {
            skipType = skipLayer = skipLocked = skipSpace = 0;
            bool twoLayer = otherLayer != null;
            var pipes = new List<PipeInfo>(ids.Length);
            // 图层状态缓存：v1 对每个实体都开一次 LayerTableRecord
            var lockedLayers = new Dictionary<ObjectId, bool>();

            foreach (ObjectId id in ids)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) { skipType++; continue; }
                if (!(ent is Line) && !(ent is Polyline)) { skipType++; continue; }

                // 双图层模式：避让侧（仪表线）记 0，另一侧（管线）记 1；
                // 单图层模式：一律记 0，避让侧留到求交时按方向决定。
                int group;
                if (string.Equals(ent.Layer, avoidLayer, StringComparison.OrdinalIgnoreCase))
                {
                    group = 0;
                }
                else if (twoLayer && string.Equals(ent.Layer, otherLayer, StringComparison.OrdinalIgnoreCase))
                {
                    group = 1;
                }
                else
                {
                    skipLayer++;
                    continue;
                }

                bool bad;
                if (!lockedLayers.TryGetValue(ent.LayerId, out bad))
                {
                    bad = false;
                    LayerTableRecord ltr = tr.GetObject(ent.LayerId, OpenMode.ForRead) as LayerTableRecord;
                    if (ltr != null && (ltr.IsFrozen || ltr.IsLocked)) bad = true;
                    lockedLayers[ent.LayerId] = bad;
                }
                if (bad) { skipLocked++; continue; }

                if (ent.OwnerId != msId) { skipSpace++; continue; }

                Curve cv = ent as Curve;
                if (cv == null) { skipType++; continue; }

                var info = new PipeInfo { Id = id, Curve = cv, Group = group };
                try
                {
                    Extents3d ext = cv.GeometricExtents;
                    info.MinX = ext.MinPoint.X; info.MinY = ext.MinPoint.Y;
                    info.MaxX = ext.MaxPoint.X; info.MaxY = ext.MaxPoint.Y;
                }
                catch
                {
                    skipType++;
                    continue;
                }

                Point3d sp = cv.StartPoint, ep = cv.EndPoint;
                info.IsVertical = Math.Abs(ep.X - sp.X) < Math.Abs(ep.Y - sp.Y);
                info.Nodes = BuildNodes(cv);
                info.ConstZ = IsConstZ(cv, info.Nodes);
                info.Z = sp.Z;
                pipes.Add(info);
            }
            return pipes;
        }

        /// <summary>缓存顶点（Line 两点 / Polyline 全部顶点），供端点判定做纯托管比较，避免逐次 P/Invoke</summary>
        private static Point3d[] BuildNodes(Curve cv)
        {
            if (cv is Line ln)
                return new[] { ln.StartPoint, ln.EndPoint };

            if (cv is Polyline pl)
            {
                int n = pl.NumberOfVertices;
                var arr = new Point3d[n];
                for (int i = 0; i < n; i++) arr[i] = pl.GetPoint3dAt(i);
                return arr;
            }
            return null;
        }

        private static bool IsConstZ(Curve cv, Point3d[] nodes)
        {
            if (nodes == null) return false;
            double z0 = nodes[0].Z;
            for (int i = 1; i < nodes.Length; i++)
                if (Math.Abs(nodes[i].Z - z0) > 1e-9) return false;
            // 多段线含圆弧段时顶点 Z 相同即可认为是平面曲线
            return true;
        }

        /// <summary>「端点没接准」明细最多报几条（其余只报总数，避免刷屏）</summary>
        private const int MaxNearMissDetail = 5;

        /// <summary>跳过原因分类统计（用于给用户明确的诊断提示，而非笼统的"跳过 N"）</summary>
        private sealed class SkipStats
        {
            public int ZMismatch;        // 两管线高程（Z）不一致
            public int EndpointTouch;    // 交点与管线端点/顶点重合
            public int EndpointNearMiss; // 交点落在管线端点/顶点附近但没接准（画图问题，按端接处理）
            public int OffCurve;         // 交点投影不在避让管线上
            public int SplitFail;        // 分割点求取失败（间距不足/弧长接口异常等）
            public int SplitNearNode;    // 分割点贴近管线端点

            /// <summary>「端点没接准」的明细（只留前若干条，避免刷屏）</summary>
            public readonly List<string> NearMissDetail = new List<string>();

            public int Total
            {
                get
                {
                    return ZMismatch + EndpointTouch + EndpointNearMiss +
                           OffCurve + SplitFail + SplitNearNode;
                }
            }
        }

        // ==================== 阶段1：空间索引求交 ====================

        private static void CollectIntersections(List<PipeInfo> pipes, BreakRequest req,
                                                 List<BreakRecord> records,
                                                 ref int validCount, ref SkipStats skip,
                                                 ref long testedPairs)
        {
            if (skip == null) skip = new SkipStats();
            int n = pipes.Count;
            double half = req.TotalWidth / 2.0;
            // 端点「重合」容差：原实现是 Tolerance.Global.EqualPoint * 2 = 2e-10，
            // 那是 AutoCAD 判定"同一个点"的量级。大坐标图纸里双精度噪声本身就能超过它，
            // 于是"端点明明接在管线上"也判不出来 → 被当成交叉打断。
            // 改用 1e-6：仍远小于任何肉眼可见的误差，却比浮点噪声大好几个数量级。
            double tol = 1e-6;
            double tolSq = tol * tol;
            // 端点「没接准」容差：交点落在缺口范围内（半宽）时，另一条管线只是"接"在
            // 避让管线上（T 型/端接），并没有真正穿过去 —— 打断会把接好的连接挖出缺口。
            // 这种情况按端接处理（不打断），并单独报出明细，提示这是画图问题。
            double nodeTolSq = half * half;
            // 提示文字里的「线」的统称（与 RunCore 保持一致）
            string lineWord = req.TwoLayer ? "曲线" : "管线";

            // 配对分组：
            //   单图层模式（PIPEBREAK）—— 按方向分组，同向管线不产生缺口交叉，天然省掉一半以上的组合；
            //   双图层模式（INSTBREAK）—— 按图层分组，只做跨图层配对，
            //   同层交叉（仪表线×仪表线、管线×管线）不处理。
            // 约定：probes 为「查询侧」，双图层模式下恒为避让侧（仪表线）；
            //       indexed 为「建索引侧」，双图层模式下恒为另一侧（管线）。两组互斥。
            var probes = new List<int>();
            var indexed = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (req.TwoLayer)
                {
                    if (pipes[i].Group == 0) probes.Add(i); else indexed.Add(i);
                }
                else
                {
                    if (pipes[i].IsVertical) probes.Add(i); else indexed.Add(i);
                }
            }

            // 全局包围盒（用于确定网格尺寸）
            double gMinX = double.MaxValue, gMinY = double.MaxValue;
            double gMaxX = double.MinValue, gMaxY = double.MinValue;
            foreach (PipeInfo p in pipes)
            {
                if (p.MinX < gMinX) gMinX = p.MinX;
                if (p.MinY < gMinY) gMinY = p.MinY;
                if (p.MaxX > gMaxX) gMaxX = p.MaxX;
                if (p.MaxY > gMaxY) gMaxY = p.MaxY;
            }

            // 只对「另一侧」建网格索引，用「避让侧」去查询
            var cells = new Dictionary<int, List<int>>();
            var bigOnes = new List<int>();
            int nx, ny;
            double cellW, cellH;
            BuildGrid(pipes, indexed, gMinX, gMinY, gMaxX, gMaxY,
                      cells, bigOnes, out nx, out ny, out cellW, out cellH);

            var cand = new List<int>(64);
            var pts = new Point3dCollection();
            int[] stamp = new int[n];
            int tick = 0;
            long pairs = 0;

            for (int vi = 0; vi < probes.Count; vi++)
            {
                int viIdx = probes[vi];
                PipeInfo vp = pipes[viIdx];

                cand.Clear();
                tick++;
                CollectCandidates(vp, pipes, cand, gMinX, gMinY, nx, ny, cellW, cellH,
                                  cells, bigOnes, stamp, tick);

                for (int k = 0; k < cand.Count; k++)
                {
                    int hiIdx = cand[k];
                    PipeInfo hp = pipes[hiIdx];
                    pairs++;

                    pts.Clear();
                    try
                    {
                        vp.Curve.IntersectWith(hp.Curve, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch
                    {
                        continue;
                    }
                    if (pts.Count == 0) continue;

                    // 确定避让（被打断）管线：
                    //   双图层模式 —— 恒为查询侧的仪表线（仪表线让管线）；
                    //   单图层模式 —— 按「缺口方向」选择垂直或水平一侧。
                    int avoidIdx = (req.TwoLayer || req.BreakVertical) ? viIdx : hiIdx;
                    PipeInfo avoid = pipes[avoidIdx];
                    PipeInfo other = avoidIdx == viIdx ? hp : vp;

                    bool zChecked = avoid.ConstZ && other.ConstZ;
                    double zDiff = zChecked ? Math.Abs(avoid.Z - other.Z) : 0.0;
                    if (zChecked && zDiff > 0.001) { skip.ZMismatch += pts.Count; continue; }

                    for (int m = 0; m < pts.Count; m++)
                    {
                        Point3d pt = pts[m];

                        // 端点相交跳过（用缓存顶点做纯托管比较）
                        if (IsNearNode(vp, pt, tolSq) || IsNearNode(hp, pt, tolSq))
                        {
                            skip.EndpointTouch++;
                            continue;
                        }

                        Point3d onCurve;
                        try
                        {
                            onCurve = avoid.Curve.GetClosestPointTo(pt, false);
                        }
                        catch
                        {
                            skip.OffCurve++;
                            continue;
                        }
                        if (DistSq(onCurve, pt) > tolSq) { skip.OffCurve++; continue; }

                        // 非平面管线才需要额外投影一次做 Z 校验
                        if (!zChecked)
                        {
                            try
                            {
                                Point3d onOther = other.Curve.GetClosestPointTo(pt, false);
                                if (Math.Abs(onCurve.Z - onOther.Z) > 0.001) { skip.ZMismatch++; continue; }
                            }
                            catch
                            {
                                skip.ZMismatch++;
                                continue;
                            }
                        }

                        // 端接判定（第二档）：**另一条**管线的端点落在缺口范围内 —— 它只是"接"在
                        // 避让管线上（T 型端接），并没有真正穿过去。打断会在接点处挖出缺口、
                        // 把本来接好的连接断开，所以按端接处理并记下明细。
                        // 只查「另一条」：避让管线自己的端头贴近交点属于"端头太短"，
                        // 仍走下面的 TryGetSplitPoints 失败分支，提示语才是对的。
                        Point3d nearNode;
                        double nearDist;
                        PipeInfo nearPipe = FindNearNode(other, pt, nodeTolSq, out nearNode, out nearDist);
                        if (nearPipe != null)
                        {
                            skip.EndpointNearMiss++;
                            if (skip.NearMissDetail.Count < MaxNearMissDetail)
                            {
                                skip.NearMissDetail.Add("交点 (" + pt.X.ToString("0.###") + ", " +
                                    pt.Y.ToString("0.###") + ")：图层 " + nearPipe.Curve.Layer +
                                    " 的" + lineWord + "端点/顶点 (" + nearNode.X.ToString("0.###") + ", " +
                                    nearNode.Y.ToString("0.###") + ") 偏离 " + nearDist.ToString("0.###"));
                            }
                            continue;
                        }

                        Point3d splitA, splitB;
                        if (!TryGetSplitPoints(avoid, onCurve, half, out splitA, out splitB))
                        {
                            skip.SplitFail++;
                            continue;
                        }
                        if (IsNearNode(avoid, splitA, tolSq) || IsNearNode(avoid, splitB, tolSq))
                        {
                            skip.SplitNearNode++;
                            continue;
                        }

                        validCount++;
                        records.Add(new BreakRecord
                        {
                            AvoidIndex = avoidIdx,
                            IntersectionPoint = pt,
                            SplitA = splitA,
                            SplitB = splitB
                        });
                    }
                }
            }
            testedPairs = pairs;
        }

        // ---------- 均匀网格 ----------

        private static void BuildGrid(List<PipeInfo> pipes, List<int> members,
                                      double minX, double minY, double maxX, double maxY,
                                      Dictionary<int, List<int>> cells, List<int> bigOnes,
                                      out int nx, out int ny, out double cellW, out double cellH)
        {
            double w = maxX - minX, h = maxY - minY;
            if (w <= 0 || double.IsNaN(w)) w = 1.0;
            if (h <= 0 || double.IsNaN(h)) h = 1.0;

            int m = members.Count < 1 ? 1 : members.Count;
            // 目标：格子总数 ≈ 成员数，使平均每格 1~2 个对象
            double cell = Math.Sqrt(w * h / m);
            if (cell <= 0 || double.IsNaN(cell) || double.IsInfinity(cell)) cell = Math.Max(w, h);

            nx = (int)Math.Ceiling(w / cell);
            ny = (int)Math.Ceiling(h / cell);
            if (nx < 1) nx = 1;
            if (ny < 1) ny = 1;
            if (nx > 1024) nx = 1024;
            if (ny > 1024) ny = 1024;

            cellW = w / nx;
            cellH = h / ny;

            foreach (int idx in members)
            {
                PipeInfo p = pipes[idx];
                int ix0 = Clamp((int)Math.Floor((p.MinX - minX) / cellW), 0, nx - 1);
                int ix1 = Clamp((int)Math.Floor((p.MaxX - minX) / cellW), 0, nx - 1);
                int iy0 = Clamp((int)Math.Floor((p.MinY - minY) / cellH), 0, ny - 1);
                int iy1 = Clamp((int)Math.Floor((p.MaxY - minY) / cellH), 0, ny - 1);

                // 跨度特别大的对象不铺进格子，单独走「大对象」通道，避免内存爆炸
                if ((ix1 - ix0 + 1) * (iy1 - iy0 + 1) > 64)
                {
                    bigOnes.Add(idx);
                    continue;
                }

                for (int iy = iy0; iy <= iy1; iy++)
                {
                    int rowBase = iy * nx;
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        int key = rowBase + ix;
                        List<int> list;
                        if (!cells.TryGetValue(key, out list))
                        {
                            list = new List<int>();
                            cells[key] = list;
                        }
                        list.Add(idx);
                    }
                }
            }
        }

        private static void CollectCandidates(PipeInfo q, List<PipeInfo> pipes, List<int> cand,
                                              double minX, double minY, int nx, int ny,
                                              double cellW, double cellH,
                                              Dictionary<int, List<int>> cells, List<int> bigOnes,
                                              int[] stamp, int tick)
        {
            // 注意：网格里只放了「另一侧」（单图层模式为水平管线），查询方是「避让侧」
            // （单图层模式为垂直管线），两组互斥，故无需 j>i 去重
            int ix0 = Clamp((int)Math.Floor((q.MinX - minX) / cellW), 0, nx - 1);
            int ix1 = Clamp((int)Math.Floor((q.MaxX - minX) / cellW), 0, nx - 1);
            int iy0 = Clamp((int)Math.Floor((q.MinY - minY) / cellH), 0, ny - 1);
            int iy1 = Clamp((int)Math.Floor((q.MaxY - minY) / cellH), 0, ny - 1);

            for (int iy = iy0; iy <= iy1; iy++)
            {
                int rowBase = iy * nx;
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    List<int> list;
                    if (cells.TryGetValue(rowBase + ix, out list))
                        AddOverlaps(q, pipes, list, cand, stamp, tick);
                }
            }
            AddOverlaps(q, pipes, bigOnes, cand, stamp, tick);
        }

        private static void AddOverlaps(PipeInfo q, List<PipeInfo> pipes, List<int> src,
                                        List<int> cand, int[] stamp, int tick)
        {
            for (int i = 0; i < src.Count; i++)
            {
                int idx = src[i];
                if (stamp[idx] == tick) continue;
                PipeInfo p = pipes[idx];
                // 包围盒快速排除（纯托管计算，比 IntersectWith 便宜两个数量级）
                if (p.MaxX < q.MinX || p.MinX > q.MaxX || p.MaxY < q.MinY || p.MinY > q.MaxY)
                    continue;
                stamp[idx] = tick;
                cand.Add(idx);
            }
        }

        // ---------- 几何小工具 ----------

        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static double DistSq(Point3d a, Point3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>点是否靠近管线端点/顶点（用缓存顶点，纯托管比较）</summary>
        private static bool IsNearNode(PipeInfo p, Point3d pt, double tolSq)
        {
            Point3d[] nodes = p.Nodes;
            if (nodes == null) return false;
            for (int i = 0; i < nodes.Length; i++)
                if (DistSq(nodes[i], pt) < tolSq) return true;
            return false;
        }

        /// <summary>
        /// 找出距交点 &lt; 容差的最近端点/顶点：命中时返回所属管线，并输出该顶点坐标与偏差距离，
        /// 未命中返回 null。IsNearNode 只回答「是不是」，这里要连偏差一起报给用户看，所以单独一个方法。
        /// </summary>
        private static PipeInfo FindNearNode(PipeInfo p, Point3d pt, double tolSq,
                                             out Point3d node, out double dist)
        {
            node = Point3d.Origin;
            dist = 0.0;

            Point3d[] nodes = p.Nodes;
            if (nodes == null) return null;

            double best = double.MaxValue;
            Point3d bestNode = Point3d.Origin;
            for (int i = 0; i < nodes.Length; i++)
            {
                double d = DistSq(nodes[i], pt);
                if (d < best) { best = d; bestNode = nodes[i]; }
            }
            if (best >= tolSq) return null;

            node = bestNode;
            dist = Math.Sqrt(best);
            return p;
        }

        /// <summary>
        /// 求避让管线上的两个分割点：按弧长定位（O(1)~O(log n)），
        /// 替代 v1「新建临时 Circle → IntersectWith → 过滤 → 排序」的做法。
        /// 若弧长接口在该图元上不可用，回退到临时辅助圆方案。
        /// </summary>
        private static bool TryGetSplitPoints(PipeInfo pipe, Point3d onCurve, double half,
                                              out Point3d splitA, out Point3d splitB)
        {
            splitA = splitB = Point3d.Origin;
            Curve c = pipe.Curve;

            if (double.IsNaN(pipe.Length))
            {
                try { pipe.Length = c.GetDistanceAtParameter(c.EndParam); }
                catch { pipe.Length = 0.0; }
            }

            if (pipe.Length > 0.0)
            {
                try
                {
                    double dist = c.GetDistAtPoint(onCurve);
                    double dA = dist - half;
                    double dB = dist + half;
                    if (dA <= 0.0 || dB >= pipe.Length) return false;
                    splitA = c.GetPointAtDist(dA);
                    splitB = c.GetPointAtDist(dB);
                    if (DistSq(splitA, splitB) <= Tolerance.Global.EqualPoint * Tolerance.Global.EqualPoint)
                        return false;
                    return true;
                }
                catch
                {
                    // 落到下面的辅助圆方案
                }
            }
            return TryGetSplitPointsByCircle(c, onCurve, half, out splitA, out splitB);
        }

        /// <summary>回退方案：内存临时辅助圆求交（不加入数据库，不留在图纸）</summary>
        private static bool TryGetSplitPointsByCircle(Curve avoidCurve, Point3d center, double half,
                                                     out Point3d splitA, out Point3d splitB)
        {
            splitA = splitB = Point3d.Origin;
            double tol = Tolerance.Global.EqualPoint * 2;

            using (Circle tempCircle = new Circle(center, Vector3d.ZAxis, half))
            {
                var pts = new Point3dCollection();
                try
                {
                    avoidCurve.IntersectWith(tempCircle, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                }
                catch
                {
                    return false;
                }
                if (pts.Count < 2) return false;

                // 取距离交叉点最近的两个点（避免 LINQ 排序带来的装箱与迭代器开销）
                double best1 = double.MaxValue, best2 = double.MaxValue;
                Point3d p1 = Point3d.Origin, p2 = Point3d.Origin;
                for (int i = 0; i < pts.Count; i++)
                {
                    Point3d q = pts[i];
                    double d = DistSq(q, center);
                    if (d <= tol * tol) continue;
                    if (d < best1) { best2 = best1; p2 = p1; best1 = d; p1 = q; }
                    else if (d < best2) { best2 = d; p2 = q; }
                }
                if (best2 == double.MaxValue) return false;

                splitA = avoidCurve.GetClosestPointTo(p1, false);
                splitB = avoidCurve.GetClosestPointTo(p2, false);
                return DistSq(splitA, splitB) > tol * tol;
            }
        }

        // ==================== 阶段2：分组分割 ====================

        private static int ProcessGroups(Transaction tr, List<PipeInfo> pipes,
                                         List<BreakRecord> records, BlockTableRecord modelSpace,
                                         Editor ed, BreakRequest req)
        {
            // 线性分组：先按 AvoidIndex 排序再切段（v1 用 GroupBy，多一次哈希开销）
            records.Sort(CompareByAvoidIndex);

            int success = 0;
            int i = 0;
            while (i < records.Count)
            {
                int avoidIndex = records[i].AvoidIndex;
                int j = i + 1;
                while (j < records.Count && records[j].AvoidIndex == avoidIndex) j++;
                success += ProcessOnePipe(tr, pipes[avoidIndex], records, i, j - i,
                                          modelSpace, ed, req);
                i = j;
            }
            return success;
        }

        private static int CompareByAvoidIndex(BreakRecord a, BreakRecord b)
        {
            return a.AvoidIndex.CompareTo(b.AvoidIndex);
        }

        private static int ProcessOnePipe(Transaction tr, PipeInfo pipe, List<BreakRecord> recs,
                                          int start, int count, BlockTableRecord modelSpace,
                                          Editor ed, BreakRequest req)
        {
            Curve curve = pipe.Curve;
            if (curve == null) return 0;

            if (!curve.IsWriteEnabled)
            {
                try { curve.UpgradeOpen(); }
                catch
                {
                    curve = tr.GetObject(pipe.Id, OpenMode.ForWrite) as Curve;
                    if (curve == null) return 0;
                }
            }

            double tStart = curve.StartParam;
            double tEnd = curve.EndParam;
            if (tEnd - tStart <= 1e-12) return 0;

            // 1) 收集删除区间（全部换算到参数空间）
            var intervals = new List<Interval>(count);
            for (int k = start; k < start + count; k++)
            {
                BreakRecord rec = recs[k];
                double ta = GetParamSafe(curve, rec.SplitA);
                double tb = GetParamSafe(curve, rec.SplitB);
                if (double.IsNaN(ta) || double.IsNaN(tb)) continue;
                double tMin = Math.Min(ta, tb);
                double tMax = Math.Max(ta, tb);
                if (tMin < tStart) tMin = tStart;
                if (tMax > tEnd) tMax = tEnd;
                if (tMax - tMin <= 1e-9) continue;
                intervals.Add(new Interval(tMin, tMax));
            }
            if (intervals.Count == 0) return 0;

            List<Interval> merged = MergeIntervals(intervals);

            // 2) 只在缺口边界点分割（v1 还额外在分割点处切，产生多余片段）
            var bounds = new Point3dCollection();
            foreach (Interval iv in merged)
            {
                bounds.Add(curve.GetPointAtParameter(iv.Start));
                bounds.Add(curve.GetPointAtParameter(iv.End));
            }

            DBObjectCollection splitObjs;
            try
            {
                splitObjs = curve.GetSplitCurves(bounds);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\n" + req.AvoidLabel + "分割失败: " + ex.Message);
                return 0;
            }

            // 3) 判定每段保留/删除
            //    快速路径：GetSplitCurves 按曲线走向顺序返回，边界点严格递增，
            //    因此段的取舍是 keep / gap / keep / gap / ... / keep 交替。
            bool ordered = splitObjs.Count == merged.Count * 2 + 1;
            if (ordered)
            {
                Curve segFirst = splitObjs[0] as Curve;
                Curve segLast = splitObjs[splitObjs.Count - 1] as Curve;
                double tol = Tolerance.Global.EqualPoint * 4;
                double tolSq = tol * tol;
                if (segFirst == null || segLast == null) ordered = false;
                else if (DistSq(segFirst.StartPoint, curve.StartPoint) > tolSq) ordered = false;
                else if (DistSq(segLast.EndPoint, curve.EndPoint) > tolSq) ordered = false;
            }

            EntityProps props = CaptureProps(curve);
            int kept = 0;

            for (int s = 0; s < splitObjs.Count; s++)
            {
                Curve segment = splitObjs[s] as Curve;
                if (segment == null)
                {
                    splitObjs[s].Dispose();
                    continue;
                }

                bool shouldDelete;
                if (ordered)
                {
                    shouldDelete = (s % 2) == 1;
                }
                else
                {
                    // 稳健回退：取片段中点投影回原曲线，二分查找区间（v1 为线性扫描 O(k²)）
                    shouldDelete = IsInsideGap(curve, segment, merged);
                }

                if (shouldDelete)
                {
                    segment.Dispose();
                }
                else
                {
                    modelSpace.AppendEntity(segment);
                    tr.AddNewlyCreatedDBObject(segment, true);
                    ApplyProps(segment, props);
                    kept++;
                }
            }

            if (kept == 0) return 0; // 没有保留片段，异常，保持原样

            // 4) 补图形：架桥补半圆、S 型补样条，都严格贴合合并后的缺口
            if (req.Mode == BreakMode.Bridge)
            {
                foreach (Interval iv in merged)
                {
                    Point3d a = curve.GetPointAtParameter(iv.Start);
                    Point3d b = curve.GetPointAtParameter(iv.End);
                    CreateBridgePolyline(a, b, req.SideVector, pipe, props, modelSpace, tr);
                }
            }
            else if (req.Mode == BreakMode.SCurve)
            {
                foreach (Interval iv in merged)
                {
                    Point3d a = curve.GetPointAtParameter(iv.Start);
                    Point3d b = curve.GetPointAtParameter(iv.End);
                    CreateSCurveSpline(a, b, req.SCurveWidth, pipe, props, modelSpace, tr);
                }
            }

            curve.Erase();
            return count;
        }

        /// <summary>稳健回退：片段中点是否在任一删除区间内（二分查找）</summary>
        private static bool IsInsideGap(Curve curve, Curve segment, List<Interval> merged)
        {
            try
            {
                Point3d mid = segment.GetPointAtParameter((segment.StartParam + segment.EndParam) / 2.0);
                Point3d proj = curve.GetClosestPointTo(mid, false);
                double t = curve.GetParameterAtPoint(proj);

                int lo = 0, hi = merged.Count - 1;
                while (lo <= hi)
                {
                    int m = (lo + hi) / 2;
                    if (t < merged[m].Start) hi = m - 1;
                    else if (t > merged[m].End) lo = m + 1;
                    else return true;
                }
            }
            catch
            {
                // 无法判断时保留
            }
            return false;
        }

        private static double GetParamSafe(Curve curve, Point3d p)
        {
            try { return curve.GetParameterAtPoint(p); }
            catch { }
            try { return curve.GetParameterAtPoint(curve.GetClosestPointTo(p, false)); }
            catch { }
            return double.NaN;
        }

        /// <summary>合并重叠或相邻的参数区间</summary>
        private static List<Interval> MergeIntervals(List<Interval> intervals)
        {
            if (intervals.Count == 0) return intervals;

            // 避免 LINQ OrderBy：手写插入排序，区间数很少且基本有序
            var sorted = new List<Interval>(intervals);
            sorted.Sort(CompareByStart);

            var merged = new List<Interval> { sorted[0] };
            double eps = Tolerance.Global.EqualPoint;
            for (int i = 1; i < sorted.Count; i++)
            {
                Interval iv = sorted[i];
                Interval last = merged[merged.Count - 1];
                if (iv.Start <= last.End + eps)
                {
                    if (iv.End > last.End)
                        merged[merged.Count - 1] = new Interval(last.Start, iv.End);
                }
                else
                {
                    merged.Add(iv);
                }
            }
            return merged;
        }

        private static int CompareByStart(Interval a, Interval b)
        {
            return a.Start.CompareTo(b.Start);
        }

        // ==================== 实体属性与拱桥 ====================

        private static EntityProps CaptureProps(Entity src)
        {
            var p = new EntityProps
            {
                LayerId = src.LayerId,
                LinetypeId = src.LinetypeId,
                LinetypeScale = src.LinetypeScale,
                LineWeight = src.LineWeight,
                ColorIndex = src.ColorIndex,
                Transparency = src.Transparency
            };
            try
            {
                p.PlotStyleName = src.PlotStyleName;
                p.ApplyPlotStyle = !string.IsNullOrEmpty(p.PlotStyleName);
            }
            catch
            {
                p.ApplyPlotStyle = false;
            }
            return p;
        }

        /// <summary>用 Id 直接赋值，省掉按名称查符号表的开销；属性只需读一次源实体</summary>
        private static void ApplyProps(Entity target, EntityProps p)
        {
            if (!p.LayerId.IsNull) target.LayerId = p.LayerId;
            if (!p.LinetypeId.IsNull) target.LinetypeId = p.LinetypeId;
            target.LinetypeScale = p.LinetypeScale;
            target.LineWeight = p.LineWeight;
            target.ColorIndex = p.ColorIndex;
            target.Transparency = p.Transparency;
            if (p.ApplyPlotStyle)
            {
                try { target.PlotStyleName = p.PlotStyleName; }
                catch { p.ApplyPlotStyle = false; } // 只抛一次，后续不再尝试
            }
        }

        /// <summary>生成半圆拱桥多段线（Polyline 圆弧段，bulge = ±1）</summary>
        private static bool CreateBridgePolyline(Point3d A, Point3d B, Vector3d sideVector,
                                                 PipeInfo source, EntityProps props,
                                                 BlockTableRecord modelSpace, Transaction tr)
        {
            try
            {
                var pline = new Polyline();
                double elevation = A.Z;
                double width = 0.0;
                if (source.Curve is Polyline srcPline)
                {
                    elevation = srcPline.Elevation;
                    width = srcPline.ConstantWidth;
                }
                pline.Elevation = elevation;
                pline.ConstantWidth = width;

                Vector3d chord = B - A;
                if (chord.Length < Tolerance.Global.EqualPoint) return false;

                // bulge 符号：2D 叉积判断朝向点位于 A->B 的哪一侧
                double cross = chord.X * sideVector.Y - chord.Y * sideVector.X;
                double bulge = cross >= 0 ? 1.0 : -1.0;

                pline.AddVertexAt(0, new Point2d(A.X, A.Y), bulge, 0, 0);
                pline.AddVertexAt(1, new Point2d(B.X, B.Y), 0, 0, 0);

                // 先驻留数据库再设置属性（未驻留实体设置属性会抛 eNoDatabase）
                modelSpace.AppendEntity(pline);
                tr.AddNewlyCreatedDBObject(pline, true);
                ApplyProps(pline, props);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 生成 S 型「断线符号」：在缺口两端各画一个 S，S 的波长方向横跨管线（垂直于管线），
        /// 跨度 = <paramref name="sWidth"/>（默认与缺口宽度一致，可单独指定），振幅沿管线方向
        /// ±<paramref name="sWidth"/>/8 —— 只要宽度定了，内部比例（跨度:振幅 = 8:1）就固定。
        /// 每个 S 是**一条 5 个拟合点的样条**（P0-P1-P2-P3-P4），P2 正好落在断点上、与原管线端点相接。
        /// 与参考图逐像素一致：AutoCAD 自然样条（弦长参数化、3 次），与手工 SPLINE 命令同构。
        /// </summary>
        private static int CreateSCurveSpline(Point3d A, Point3d B, double sWidth,
                                              PipeInfo source, EntityProps props,
                                              BlockTableRecord modelSpace, Transaction tr)
        {
            try
            {
                double z = A.Z;
                if (source.Curve is Polyline srcPline) z = srcPline.Elevation;

                Point3d end1 = new Point3d(A.X, A.Y, z);
                Point3d end2 = new Point3d(B.X, B.Y, z);
                Vector3d chord = end2 - end1;
                double gapLength = chord.Length;
                if (gapLength < Tolerance.Global.EqualPoint) return 0;

                // u = 沿管线方向（从缺口一端指向另一端）；across = 横跨管线的方向
                Vector3d u = chord / gapLength;
                Vector3d across = Vector3d.ZAxis.CrossProduct(u);
                if (across.Length < 1e-9) return 0;
                across = across.GetNormal();

                // S 的振幅方向 = 沿管线方向（负号决定相位：参考图里 S 的左侧拱起、右侧下凹）
                Vector3d rise = u * -1.0;

                // 符号尺寸只看 sWidth（缺口本身仍按 TotalWidth 切），内部比例固定 8:1
                double width = sWidth > Tolerance.Global.EqualPoint ? sWidth : gapLength;
                double half = width * 0.5;
                double quarter = half * 0.5;
                double amplitude = width / 8.0;

                int created = 0;
                foreach (Point3d p in new[] { end1, end2 })
                {
                    Point3d p0 = p - across * half;
                    Point3d p1 = p - across * quarter + rise * amplitude;
                    Point3d p2 = p;
                    Point3d p3 = p + across * quarter - rise * amplitude;
                    Point3d p4 = p + across * half;

                    var fitPoints = new Point3dCollection { p0, p1, p2, p3, p4 };
                    var spline = new Spline(fitPoints, KnotParameterizationEnum.Chord, 3, 0.0);

                    // 先驻留数据库再设置属性（未驻留实体设置属性会抛 eNoDatabase）
                    modelSpace.AppendEntity(spline);
                    tr.AddNewlyCreatedDBObject(spline, true);
                    ApplyProps(spline, props);
                    created++;
                }
                return created;
            }
            catch
            {
                return 0;
            }
        }
    }
}
#endif