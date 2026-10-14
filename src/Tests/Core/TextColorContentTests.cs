using System;
using ChemForge.Core.Text.TextColor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Core
{
    /// <summary>
    /// 文字内容工具方法的单元测试。
    ///
    /// 这些判断是"要不要去碰内容属性"的闸门：判错一次，
    /// 要么白读一遍跨进程属性（慢），要么把带字段的文字写坏。
    /// </summary>
    [TestClass]
    public sealed class TextColorContentTests
    {
        [TestMethod]
        public void HasBackslash_WithBackslash_ReturnsTrue()
        {
            Assert.IsTrue(TextColorContent.HasBackslash("{\\C1;红}"));
        }

        [TestMethod]
        public void HasBackslash_WithoutBackslash_ReturnsFalse()
        {
            Assert.IsFalse(TextColorContent.HasBackslash("纯文字"));
            Assert.IsFalse(TextColorContent.HasBackslash(string.Empty));
            Assert.IsFalse(TextColorContent.HasBackslash(null));
        }

        [TestMethod]
        public void ContainsField_WithFieldMarker_ReturnsTrue()
        {
            Assert.IsTrue(TextColorContent.ContainsField("面积：%<\\AcExpr 1+1>%"));
        }

        [TestMethod]
        public void ContainsField_WithoutFieldMarker_ReturnsFalse()
        {
            Assert.IsFalse(TextColorContent.ContainsField("面积：100"));
            Assert.IsFalse(TextColorContent.ContainsField(null));
        }

        [TestMethod]
        public void Head_WithinLimit_ReturnsTextUnchanged()
        {
            Assert.AreEqual("abc", TextColorContent.Head("abc"));
            Assert.AreEqual(string.Empty, TextColorContent.Head(string.Empty));
            Assert.IsNull(TextColorContent.Head(null));
        }

        [TestMethod]
        public void Head_OverLimit_TruncatesAndAppendsEllipsis()
        {
            string text = new string('x', 200);
            string head = TextColorContent.Head(text);

            Assert.AreEqual(63, head.Length);
            Assert.AreEqual(new string('x', 60) + "...", head);
        }

        [TestMethod]
        public void Head_WithExplicitLimit_UsesThatLimit()
        {
            Assert.AreEqual("abc...", TextColorContent.Head("abcdef", 3));
        }

        [TestMethod]
        public void ToSingleLine_TurnsNewlinesIntoVisibleEscape()
        {
            Assert.AreEqual("a\\nb\\nc\\nd", TextColorContent.ToSingleLine("a\r\nb\nc\rd"));
        }

        [TestMethod]
        public void ToSingleLine_TurnsTabIntoSpace()
        {
            Assert.AreEqual("a b", TextColorContent.ToSingleLine("a\tb"));
        }

        [TestMethod]
        public void ToSingleLine_WithNull_ReturnsNull()
        {
            Assert.IsNull(TextColorContent.ToSingleLine(null));
        }

        [TestMethod]
        public void IsBlank_WithEmptyOrWhitespace_ReturnsTrue()
        {
            Assert.IsTrue(TextColorContent.IsBlank(null));
            Assert.IsTrue(TextColorContent.IsBlank(string.Empty));
            Assert.IsTrue(TextColorContent.IsBlank("   "));
        }

        [TestMethod]
        public void IsBlank_WithText_ReturnsFalse()
        {
            Assert.IsFalse(TextColorContent.IsBlank("a"));
        }

        [TestMethod]
        public void IsDigitsOnly_AcceptsOnlyDigits()
        {
            Assert.IsTrue(TextColorContent.IsDigitsOnly("123"));
            Assert.IsFalse(TextColorContent.IsDigitsOnly("12a"));
            Assert.IsFalse(TextColorContent.IsDigitsOnly("1 2"));
            Assert.IsFalse(TextColorContent.IsDigitsOnly("-5"));
            Assert.IsFalse(TextColorContent.IsDigitsOnly(string.Empty));
            Assert.IsFalse(TextColorContent.IsDigitsOnly(null));
        }

        [TestMethod]
        public void ParseByte_ClampsToByteRange()
        {
            Assert.AreEqual(255, TextColorContent.ParseByte("255", -1));
            Assert.AreEqual(255, TextColorContent.ParseByte("300", -1));
            Assert.AreEqual(0, TextColorContent.ParseByte("0", -1));
        }

        [TestMethod]
        public void ParseByte_WithNonNumericInput_ReturnsFallback()
        {
            Assert.AreEqual(7, TextColorContent.ParseByte("12a", 7));
            Assert.AreEqual(7, TextColorContent.ParseByte(string.Empty, 7));
            Assert.AreEqual(7, TextColorContent.ParseByte(null, 7));
        }

        [TestMethod]
        public void ParseByte_WithHugeNumber_ReturnsFallbackInsteadOfOverflowing()
        {
            // int.TryParse 对超出 int 范围的数字返回 false，不能被当成 0 或 255。
            Assert.AreEqual(9, TextColorContent.ParseByte("99999999999999999999", 9));
        }

        [TestMethod]
        public void ToSingleLine_TruncatesLongContent()
        {
            string text = new string('a', 100);
            string line = TextColorContent.ToSingleLine(text);

            Assert.AreEqual(63, line.Length);
            Assert.IsTrue(line.EndsWith("...", StringComparison.Ordinal));
        }
    }
}
