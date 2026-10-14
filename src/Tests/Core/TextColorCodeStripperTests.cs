using ChemForge.Core.Text.TextColor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Core
{
    /// <summary>
    /// 颜色覆盖码剥离的单元测试。
    ///
    /// 这些用例锁的是几条容易被"顺手优化"掉的规则：
    /// 字面反斜杠要成对保留、非颜色格式码一律不动、
    /// 颜色码没写终止分号时其后内容整体丢弃。
    /// </summary>
    [TestClass]
    public sealed class TextColorCodeStripperTests
    {
        [TestMethod]
        public void HasColorCode_WithoutAnyCode_ReturnsFalse()
        {
            Assert.IsFalse(TextColorCodeStripper.HasColorCode("普通文字"));
            Assert.IsFalse(TextColorCodeStripper.HasColorCode("\\\\"));
            Assert.IsFalse(TextColorCodeStripper.HasColorCode("\\P下一行"));
            Assert.IsFalse(TextColorCodeStripper.HasColorCode(string.Empty));
        }

        [TestMethod]
        public void HasColorCode_WithIndexedCode_ReturnsTrue()
        {
            Assert.IsTrue(TextColorCodeStripper.HasColorCode("\\C1;红"));
        }

        [TestMethod]
        public void HasColorCode_WithTrueColorCode_ReturnsTrue()
        {
            Assert.IsTrue(TextColorCodeStripper.HasColorCode("\\c16711680;蓝"));
        }

        [TestMethod]
        public void Strip_WithoutColorCode_ReturnsTextUnchanged()
        {
            const string text = "普通文字\\P下一行";

            Assert.AreEqual(text, TextColorCodeStripper.Strip(text));
        }

        [TestMethod]
        public void Strip_RemovesIndexedColorCode()
        {
            Assert.AreEqual("红", TextColorCodeStripper.Strip("\\C1;红"));
        }

        [TestMethod]
        public void Strip_RemovesTrueColorCode()
        {
            Assert.AreEqual("蓝", TextColorCodeStripper.Strip("\\c16711680;蓝"));
        }

        [TestMethod]
        public void Strip_RemovesBracePairLeftEmptyByRemoval()
        {
            Assert.AreEqual(string.Empty, TextColorCodeStripper.Strip("{\\C1;}"));
        }

        [TestMethod]
        public void Strip_KeepsLiteralBackslashPair()
        {
            const string text = "a\\\\b";

            Assert.AreEqual(text, TextColorCodeStripper.Strip(text));
        }

        [TestMethod]
        public void Strip_KeepsNonColorFormatCodes()
        {
            // \P（换行）与包住它的花括号必须原样留下，只有颜色码消失。
            Assert.AreEqual("{绿}\\P{红}", TextColorCodeStripper.Strip("{\\C3;绿}\\P{\\C1;红}"));
        }

        [TestMethod]
        public void Strip_WithUnterminatedColorCode_DropsRemainder()
        {
            // 没有终止分号说明这条码是坏的，其后内容无法可靠切分，只能整体丢弃。
            Assert.AreEqual("a", TextColorCodeStripper.Strip("a\\C1b"));
        }

        [TestMethod]
        public void Strip_HandlesColorCodeFollowedByNewlineCode()
        {
            Assert.AreEqual("{红\\P绿}", TextColorCodeStripper.Strip("{\\C1;红\\P绿}"));
        }

        [TestMethod]
        public void Strip_WithEmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, TextColorCodeStripper.Strip(string.Empty));
            Assert.IsNull(TextColorCodeStripper.Strip(null));
        }
    }
}
