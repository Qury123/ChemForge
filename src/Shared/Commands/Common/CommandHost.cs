using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using Document = Autodesk.AutoCAD.ApplicationServices.Document;
using Editor = Autodesk.AutoCAD.EditorInput.Editor;

namespace ChemForge.Commands.Common
{
    /// <summary>
    /// 命令层公用件：宿主判断、异常上报、命令行输出、弹窗隔离。
    ///
    /// 这些代码原先在每个命令类里各抄了一份（TCOLOR / BLKATTCHANGE / BLKNUM /
    /// CHEMFORGE 关于），四份内容几乎逐字相同。抄写本身不是问题，
    /// 真正的问题是**它们抄错了同一处细节**：
    ///
    /// <c>ShowModalDialog</c> 必须隔离在独立方法里，否则 JIT 编译调用方时就会去加载
    /// AcWindows.dll，在无界面宿主里直接 AV 崩进程（详见 <see cref="ShowDialogSafely"/>）。
    /// 而原来那份隔离方法是**只有一行的私有静态方法** —— 恰恰是 JIT 最乐意内联的形状。
    /// 一旦内联，隔离就白做了，而且没有任何迹象。
    ///
    /// 所以这里不只做去重：所有触碰 AcWindows 的方法都显式标了
    /// <see cref="MethodImplOptions.NoInlining"/>，把"靠方法边界隔离"从约定变成保证。
    /// 命令类里那些私有副本随之删除，避免再出现"改了这份、漏了那份"。
    /// </summary>
    internal static class CommandHost
    {
        // ==================================================================
        //  宿主与界面判断（绝不引用 Autodesk.AutoCAD.Windows 下任何类型）
        // ==================================================================

        /// <summary>宿主进程名（accoreconsole / acad 等），用于判断有没有图形界面。</summary>
        internal static string HostProcessName
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                }
                catch (System.Exception)
                {
                    return "(未知)";
                }
            }
        }

        /// <summary>
        /// 当前宿主是否有图形界面（可以用对话框）。
        ///
        /// 实现上刻意**不访问 Application.MainWindow**，也刻意不引用任何
        /// Autodesk.AutoCAD.Windows 下的类型。原因：
        ///   1) 这些成员由 AcWindows.dll 实现，只要方法体里引用到，JIT 编译该方法时
        ///      就要加载 AcWindows.dll；
        ///   2) accoreconsole 这类无界面宿主里，AcWindows 的**模块初始化器**
        ///      (Autodesk.AutoCAD.Windows.Module.Initialize → ComponentHostService.Initialize)
        ///      会调用 acedGetAcadFrame()，直接抛 AccessViolationException 打死进程；
        ///   3) 这个崩溃发生在**方法体第一条语句执行之前**，所以 try/catch 完全无效
        ///      —— 这也是"加了保护还是崩"的真正原因。
        /// 只用 .NET 自身的进程名 + 主窗口句柄判断，完全不碰 AutoCAD 的 UI 程序集。
        /// </summary>
        internal static bool HasUserInterface(out string reason)
        {
            reason = null;

            try
            {
                System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
                string name = process.ProcessName ?? string.Empty;

                // 核心控制台宿主：一定没有图形界面
                if (name.IndexOf("accoreconsole", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = "当前宿主是 AutoCAD 核心控制台（" + name + "），没有图形界面";
                    return false;
                }

                // acad.exe / acadlt.exe 这类完整版 AutoCAD：一定有界面
                if (name.IndexOf("acad", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // 其它宿主（第三方二次开发壳等）：按有没有主窗口判断
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    reason = "当前宿主（" + name + "）没有主窗口";
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                // fail-closed：判断不出来时宁可拒绝执行，也不要把宿主弹崩
                reason = "无法判断宿主界面状态：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 需要弹窗的命令在入口处调用的闸门：没界面就打印原因并返回 false。
        ///
        /// 必须在**碰任何文档/数据库之前**调用 —— 无界面宿主里访问
        /// <c>Document.Database</c> 或让 AutoCAD 预处理 UsePickSet 拾取集都可能直接
        /// 原生崩溃（AV 0xC0000005），那种崩溃在托管代码里 catch 不到。
        /// </summary>
        /// <param name="editor">用于打印原因；为 null 时静默返回 false。</param>
        /// <param name="commandLabel">命令名，用于拼提示（如 "TCOLOR"）。</param>
        /// <param name="hint">第二行提示，各命令自己决定要不要给替代办法。</param>
        internal static bool RequireUserInterface(Editor editor, string commandLabel, string hint)
        {
            string reason;
            if (HasUserInterface(out reason))
            {
                return true;
            }

            if (editor != null)
            {
                try
                {
                    editor.WriteMessage(
                        "\n无法运行 " + commandLabel + "：" + reason
                        + "\n" + hint);
                }
                catch (System.Exception)
                {
                }
            }

            return false;
        }

        // ==================================================================
        //  异常上报与收尾
        // ==================================================================

        /// <summary>
        /// 命令入口的统一兜底：把异常打到命令行。
        ///
        /// 每个 [CommandMethod] 都必须包这一层 —— 否则 AutoCAD 只会静静地什么都不做，
        /// 用户完全无从判断是命令没注册、还是执行到一半失败了。
        /// </summary>
        internal static void ReportFailure(string command, System.Exception ex)
        {
            try
            {
                Document doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    return;
                }

                Editor editor = doc.Editor;
                if (editor == null)
                {
                    return;
                }

                editor.WriteMessage("\n[" + command + "] 执行失败：");
                WriteException(editor, ex);
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>把异常（含内层与 ErrorStatus）逐层写到命令行。</summary>
        internal static void WriteException(Editor editor, System.Exception ex)
        {
            if (editor == null || ex == null)
            {
                return;
            }

            int depth = 0;
            while (ex != null && depth < 6)
            {
                string prefix = depth == 0 ? "  " : new string(' ', depth * 2 + 2) + "-> ";

                try
                {
                    editor.WriteMessage("\n" + prefix + ex.GetType().Name + ": " + ex.Message);

                    AcException acEx = ex as AcException;
                    if (acEx != null)
                    {
                        editor.WriteMessage("\n" + prefix + "ErrorStatus = " + acEx.ErrorStatus);
                    }
                }
                catch (System.Exception)
                {
                    return;
                }

                ex = ex.InnerException;
                depth++;
            }
        }

        /// <summary>整图重生成；失败不影响已经写入的数据，所以静默吞掉。</summary>
        internal static void RegenerateQuietly(Editor editor)
        {
            if (editor == null)
            {
                return;
            }

            try
            {
                editor.Regen();
            }
            catch (System.Exception)
            {
            }
        }

        // ==================================================================
        //  命令行输出：压住 F12 动态输入灰框
        // ==================================================================

        /// <summary>
        /// 写命令行消息，同时压掉 F12 动态输入的浮动灰框。
        ///
        /// ---- 背景 ----
        /// AutoCAD 开着 F12 动态输入时，命令行输出的最后一条消息会被"动态输入提示"
        /// 抓到一个跟着光标浮动的灰框里显示；而且那个灰框**不自己刷新**（要切出 CAD
        /// 再切回来才消失）。用户从图纸拾取回到面板后，就会看到它挡着对话框。
        ///
        /// ---- 为什么不能用"临时关 DYNPROMPT" ----
        /// 那是在**改用户的 CAD 设置**（虽然会还原）。换个用户、换台机器、换个配置
        /// 语境都可能出别的问题 —— 插件不该去动不属于自己的东西。
        ///
        /// ---- 正解 ----
        /// 这行字**本来就是插件自己打的**，所以插件完全有权决定"它要不要进动态输入提示"。
        /// AutoCAD 为此导出了消息级开关：
        ///     bool acedSetDynInputDisplayMessage(bool displayMessageOnce);
        /// 传 false 表示"接下来这条消息不要显示到动态输入提示里"，命令行照常输出。
        /// 它不读写任何系统变量、不碰注册表、不改变用户的 F12 开关状态。
        ///
        /// ---- ⚠️ 调用后**不要**立刻还原成 true ----
        /// 参数名叫 displayMessageOnce —— 它是个**一次性标志**：设 false 之后，
        /// 由 AutoCAD 的 UI 线程在**稍后**渲染那条消息时才去读取它。
        /// 如果像起初那样 `try { WriteMessage } finally { 设回 true }`，
        /// UI 线程读到的就是刚被还原的 `true`，浮动框照样弹出（实测确认）。
        /// 因此这里设完 false 就不管了 —— 标志被消费掉即自动失效，
        /// 本来就是一次性语义，**不需要也不能**由我们还原。
        ///
        /// 在无界面宿主（accoreconsole）里该导出同样拿得到，调用是安全的（只影响后续提示）。
        ///
        /// ---- 第二道保险 ----
        /// 上面那个导出的语义在各版本 AutoCAD 里并非都有保证（它没进公开 SDK 文档），
        /// 所以再叠一层**完全不依赖任何内部 API** 的兜底：
        /// 动态输入提示框显示的是"最后一条命令行消息"，那我们就在真正要说的话之后
        /// 再补发一个**空白**消息，把提示位的内容顶掉。灰框即便弹出也是空的，
        /// 不会挡住对话框。代价只是命令行多一个看不见的空行。
        /// 两条独立机制同时生效 → 任一条有效，用户都不会再看到遮挡。
        /// </summary>
        internal static void WriteMessageNoDynPrompt(Editor editor, string message)
        {
            if (editor == null)
            {
                return;
            }

            TrySetDynInputDisplayMessage(false);

            try
            {
                editor.WriteMessage(message);

                // 擦掉动态输入提示位。只发空格，命令行里是一行空白（不可见），
                // 但足以让浮动提示框的内容变成空白。
                editor.WriteMessage(" ");
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>格式化版本，避免调用方自己拼字符串。</summary>
        internal static void WriteMessageNoDynPrompt(Editor editor, string format, params object[] args)
        {
            WriteMessageNoDynPrompt(
                editor,
                (args == null || args.Length == 0)
                    ? format
                    : string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args));
        }

        /// <summary>
        /// 调用 AutoCAD 导出的 <c>acedSetDynInputDisplayMessage</c>。
        /// 返回是否调用成功（找不到导出时返回 false，调用方照常打印）。
        /// </summary>
        private static bool TrySetDynInputDisplayMessage(bool displayMessageOnce)
        {
            try
            {
                IntPtr module = NativeMethods.GetModuleHandle("accore.dll");
                if (module == IntPtr.Zero)
                {
                    return false;
                }

                // 优先按修饰名找（x64 下导出的是 C++ 修饰名）
                IntPtr proc = NativeMethods.GetProcAddress(
                    module, "?acedSetDynInputDisplayMessage@@YA_N_N@Z");

                if (proc == IntPtr.Zero)
                {
                    // 有些版本可能导出的不是修饰名，退回按函数名找
                    proc = NativeMethods.GetProcAddress(module, "acedSetDynInputDisplayMessage");
                }

                if (proc == IntPtr.Zero)
                {
                    return false;
                }

                SetDynInputDisplayMessageFn fn =
                    (SetDynInputDisplayMessageFn)System.Runtime.InteropServices.Marshal
                        .GetDelegateForFunctionPointer(proc, typeof(SetDynInputDisplayMessageFn));
                fn(displayMessageOnce);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 诊断用：报告 acedSetDynInputDisplayMessage 这个导出到底能不能拿到。
        /// 输出形如 <c>ok(accore.dll, 修饰名)</c> / <c>模块未加载(accore.dll)</c> /
        /// <c>导出未找到(accore.dll)</c>。
        /// </summary>
        internal static string DescribeDynInputHook()
        {
            try
            {
                IntPtr h = NativeMethods.GetModuleHandle("accore.dll");
                if (h == IntPtr.Zero)
                {
                    return "模块未加载(accore.dll)";
                }

                if (NativeMethods.GetProcAddress(h, "?acedSetDynInputDisplayMessage@@YA_N_N@Z") != IntPtr.Zero)
                {
                    return "ok(accore.dll, 修饰名)";
                }

                if (NativeMethods.GetProcAddress(h, "acedSetDynInputDisplayMessage") != IntPtr.Zero)
                {
                    return "ok(accore.dll, 裸名)";
                }

                return "导出未找到(accore.dll)";
            }
            catch (System.Exception ex)
            {
                return "异常: " + ex.GetType().Name;
            }
        }

        [System.Runtime.InteropServices.UnmanagedFunctionPointer(
            System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private delegate bool SetDynInputDisplayMessageFn(
            [System.Runtime.InteropServices.MarshalAs(
                System.Runtime.InteropServices.UnmanagedType.I1)] bool displayMessageOnce);

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("kernel32.dll",
                CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true)]
            internal static extern IntPtr GetModuleHandle(string lpModuleName);

            [System.Runtime.InteropServices.DllImport("kernel32.dll",
                CharSet = System.Runtime.InteropServices.CharSet.Ansi, SetLastError = true)]
            internal static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        }

        // ==================================================================
        //  ⚠ 下面两个方法是**全插件唯一**接触 Autodesk.AutoCAD.Windows 的地方
        // ==================================================================

        /// <summary>
        /// 弹模态对话框。
        ///
        /// ShowModalDialog 由 Autodesk.AutoCAD.Windows 实现，JIT 编译**调用方**时就会去
        /// 加载 AcWindows.dll；无界面宿主里这次加载会在方法体执行之前直接 AV 崩进程，
        /// 于是调用方开头的界面判断根本没机会执行。隔离成独立方法后，
        /// 只有真正要弹窗时才会 JIT 到这里。
        ///
        /// <see cref="MethodImplOptions.NoInlining"/> 不是可选项：本方法体只有一行，
        /// 正是 JIT 最乐意内联的形状。一旦被内联进调用方，上面的隔离就完全失效，
        /// 而且不会有任何编译期或运行期提示 —— 只有 accoreconsole 里的一次崩溃。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ShowDialogSafely(Form form)
        {
            try
            {
                AcadApp.ShowModalDialog(form);
            }
            catch (System.Exception)
            {
            }
        }

        /// <summary>
        /// 弹一个只带"确定"的提示框。同样必须独立成方法且禁止内联，
        /// 理由见 <see cref="ShowDialogSafely"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ShowAlertSafely(string message)
        {
            try
            {
                AcadApp.ShowAlertDialog(message);
            }
            catch (System.Exception)
            {
            }
        }
    }
}
