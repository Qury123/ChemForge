namespace ChemForge.UI.Forms.Block.Numbering
{
    /// <summary>
    /// 「属性块批量编号」用法说明的唯一文案来源。
    ///
    /// 同一份文案供两处使用：
    ///   1) 命令行命令 BLKNUMHELP —— 打印到 AutoCAD 命令行；
    ///   2) 「属性块批量编号」对话框右上角「编号用法说明」按钮弹出的窗口。
    /// 为保证两处永远一致，只保留这一份：BlockNumberingCommands 里的 BLKNUMHELP
    /// 直接引用本常量，全库没有第二份字符串字面量。
    /// </summary>
    public static class NumberingHelpText
    {
        /// <summary>命令行用法说明正文（以换行开头，供 Editor.WriteMessage 直接打印）。</summary>
        public const string CommandLineText =
            "\n===== 属性块批量编号 (BLKNUM) =====" +
            "\n用途：给同一种属性块的指定属性（例如 NO / 编号 / TAG）按“先上后下、同一行从左到右”批量填序号。" +
            "\n" +
            "\n流程：执行 BLKNUM 后直接弹出对话框，在里面两步选块：" +
            "\n  ① 在图纸中选一个样板块 —— 点选一个块，决定“按哪种块编号”" +
            "\n  ② 选择范围           —— 回图纸用 AutoCAD 自带的框选挑一批块" +
            "\n      （拖窗口/交叉框或逐点点选，选中时对象本身就是高亮的；回车结束）" +
            "\n      只收集与样板同名的块；不选任何对象直接回车 = 整个图形" +
            "\n  选完自动回到对话框，再点“确定”写入图形。" +
            "\n" +
            "\n对话框中可设置：" +
            "\n  · 目标属性 Tag（只写入该 Tag 的属性）" +
            "\n  · 起始值 / 步长 / 补零位数 / 前缀 / 后缀" +
            "\n  · 排序方式（行优先或列优先，各自的两种方向）" +
            "\n  · 排序基准（插入点 / 包围盒中心 / 属性文字位置）；排序按世界坐标(WCS)计算" +
            "\n  · 行(列)容差：自动按块高度中位数估算，也可手工指定" +
            "\n  · 只填空白 / 跳过外部参照块 / 预览表格按行(列)聚合" +
            "\n" +
            "\n“刷新预览”只更新对话框表格，不改动图纸；" +
            "\n“确定”把表格中的新值写入块属性。写入后可输入 U 一次性撤销。" +
            "\n写入完成后，被编号的块会自动保持为选中状态（Esc 可取消选择），" +
            "\n便于立刻核对改了哪些块。" +
            "\n==================================";

        /// <summary>
        /// 弹窗显示用文本：去掉开头那一个用于分隔命令行的换行，并把换行统一成 CRLF。
        ///
        /// 必须转成 CRLF：WinForms 的 TextBox 底层是原生 EDIT 控件，**只认 \r\n 为换行**；
        /// 只给 \n 时整段文案会挤成一行（本机实测：同一段文本 LF 版 EM_GETLINECOUNT = 1，
        /// CRLF 版 = 3）。命令行那份保持 LF 不动 —— Editor.WriteMessage 按 \n 断行。
        /// </summary>
        public static string DialogText
        {
            get
            {
                return CommandLineText.TrimStart('\n')
                                      .Replace("\r\n", "\n")
                                      .Replace("\n", "\r\n");
            }
        }
    }
}
