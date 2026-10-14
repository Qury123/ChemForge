using ChemForge.UI.Forms.Block.Numbering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Application
{
    /// <summary>
    /// 「属性块批量编号」用法说明文案的单元测试。
    ///
    /// 这份文案是**单一来源**：命令行命令 BLKNUMHELP 与「属性块批量编号」对话框右上角
    /// 「编号用法说明」按钮弹出的窗口都读它（BlockNumberingCommands 直接引用本常量）。
    /// 用例锁住它的外壳与关键小节，避免改文案时把结构改坏。
    /// </summary>
    [TestClass]
    public sealed class NumberingHelpTextTests
    {
        [TestMethod]
        public void CommandLineText_StartsAndEndsWithBannerLines()
        {
            string text = NumberingHelpText.CommandLineText;

            // 以换行开头：命令行 WriteMessage 紧接上一个提示，需要自己换行
            Assert.IsTrue(text.StartsWith("\n", System.StringComparison.Ordinal));
            Assert.IsTrue(text.Contains("===== 属性块批量编号 (BLKNUM) ====="));
            Assert.IsTrue(text.EndsWith("\n==================================", System.StringComparison.Ordinal));
        }

        [TestMethod]
        public void CommandLineText_KeepsDocumentedSections()
        {
            string text = NumberingHelpText.CommandLineText;

            Assert.IsTrue(text.Contains("用途："));
            Assert.IsTrue(text.Contains("流程："));
            Assert.IsTrue(text.Contains("① 在图纸中选一个样板块"));
            Assert.IsTrue(text.Contains("② 选择范围"));
            Assert.IsTrue(text.Contains("对话框中可设置："));
            Assert.IsTrue(text.Contains("刷新预览"));
        }

        [TestMethod]
        public void CommandLineText_UsesLfOnly()
        {
            // 命令行那份只能是 LF：AutoCAD 命令行按 \n 断行，混进 \r 会多出控制字符
            Assert.IsFalse(NumberingHelpText.CommandLineText.Contains("\r"), "命令行文本不应含 CR。");
        }

        [TestMethod]
        public void DialogText_DropsLeadingNewlineKeepsSameContent()
        {
            string commandLine = NumberingHelpText.CommandLineText;
            string dialog = NumberingHelpText.DialogText;

            Assert.IsTrue(dialog.StartsWith("===== 属性块批量编号", System.StringComparison.Ordinal));
            Assert.IsFalse(dialog.StartsWith("\r\n", System.StringComparison.Ordinal), "开头那个用于分隔命令行的换行应去掉。");

            // 内容必须与命令行那份逐字一致，差别只在换行风格
            Assert.AreEqual(
                commandLine.TrimStart('\n').Replace("\r\n", "\n"),
                dialog.Replace("\r\n", "\n"),
                "弹窗文本与命令行文本应当只差换行风格。");
        }

        [TestMethod]
        public void DialogText_UsesCrlfOnlyAndIsReallyMultiline()
        {
            string dialog = NumberingHelpText.DialogText;

            // WinForms TextBox（原生 EDIT 控件）只认 CRLF，只给 LF 会挤成一行 —— 本次回归点
            Assert.IsFalse(dialog.Replace("\r\n", string.Empty).Contains("\n"), "弹窗文本不得残留孤立 LF。");
            Assert.IsTrue(
                dialog.Split(new string[] { "\r\n" }, System.StringSplitOptions.None).Length >= 10,
                "弹窗文本应当是十来行以上的多行文本。");
        }
    }
}
