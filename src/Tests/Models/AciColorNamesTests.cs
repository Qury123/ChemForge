using ChemForge.Models.Text.TextColor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Models
{
    /// <summary>
    /// 颜色名表与选项归一化的单元测试。
    /// </summary>
    [TestClass]
    public sealed class AciColorNamesTests
    {
        [TestMethod]
        public void Name_CoversSpecialValues()
        {
            Assert.AreEqual("随层 ByLayer", AciColorNames.Name(AciColorNames.ByLayerValue));
            Assert.AreEqual("随块 ByBlock", AciColorNames.Name(AciColorNames.ByBlockValue));
        }

        [TestMethod]
        public void Name_CoversBasicColors()
        {
            Assert.AreEqual("红", AciColorNames.Name(1));
            Assert.AreEqual("黄", AciColorNames.Name(2));
            Assert.AreEqual("白/黑", AciColorNames.Name(7));
            Assert.AreEqual("深灰", AciColorNames.Name(8));
        }

        [TestMethod]
        public void Name_CoversGrayRamp()
        {
            Assert.AreEqual("黑", AciColorNames.Name(250));
            Assert.AreEqual("白", AciColorNames.Name(255));
        }

        [TestMethod]
        public void Name_BuildsHueGroupsFromTheTable()
        {
            // 10..249 是"色相组 + 组内序号"，序号从 1 开始。
            Assert.AreEqual("红-1", AciColorNames.Name(10));
            Assert.AreEqual("红-10", AciColorNames.Name(19));
            Assert.AreEqual("深灰-10", AciColorNames.Name(249));
        }

        [TestMethod]
        public void Name_WithOutOfRangeValue_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, AciColorNames.Name(300));
        }

        [TestMethod]
        public void ListItem_PadsNumberToThreeColumns()
        {
            Assert.AreEqual("  1  红", AciColorNames.ListItem(1));
            Assert.AreEqual("256  随层 ByLayer", AciColorNames.ListItem(256));
        }

        [TestMethod]
        public void ListIndexToAci_PutsByLayerAndByBlockFirst()
        {
            Assert.AreEqual(AciColorNames.ByLayerValue, AciColorNames.ListIndexToAci(0));
            Assert.AreEqual(AciColorNames.ByBlockValue, AciColorNames.ListIndexToAci(1));
            Assert.AreEqual(1, AciColorNames.ListIndexToAci(2));
        }

        [TestMethod]
        public void ListIndex_RoundTripsForEveryIndexedColor()
        {
            for (int aci = AciColorNames.MinIndexedColor; aci <= AciColorNames.MaxIndexedColor; aci++)
            {
                int index = AciColorNames.AciToListIndex(aci);
                Assert.AreEqual(aci, AciColorNames.ListIndexToAci(index), "色号 " + aci);
            }
        }

        [TestMethod]
        public void ListItemCount_MatchesTheNumberOfEntries()
        {
            Assert.AreEqual(257, AciColorNames.ListItemCount);
            Assert.AreEqual(
                AciColorNames.MaxIndexedColor,
                AciColorNames.ListIndexToAci(AciColorNames.ListItemCount - 1));
        }
    }

    /// <summary>
    /// 选项归一化与描述的单元测试。
    /// </summary>
    [TestClass]
    public sealed class TextColorOptionsTests
    {
        [TestMethod]
        public void Normalize_ClampsAciIndexIntoIndexedRange()
        {
            var options = new TextColorOptions { AciIndex = 999 };
            options.Normalize();
            Assert.AreEqual(AciColorNames.MaxIndexedColor, options.AciIndex);

            options.AciIndex = -3;
            options.Normalize();
            Assert.AreEqual(AciColorNames.MinIndexedColor, options.AciIndex);
        }

        [TestMethod]
        public void Normalize_ClampsRgbComponents()
        {
            var options = new TextColorOptions { Red = 300, Green = -5, Blue = 128 };
            options.Normalize();

            Assert.AreEqual(255, options.Red);
            Assert.AreEqual(0, options.Green);
            Assert.AreEqual(128, options.Blue);
        }

        [TestMethod]
        public void HasValidAciIndex_AcceptsOnlyOneThrough255()
        {
            Assert.IsTrue(new TextColorOptions { AciIndex = 1 }.HasValidAciIndex());
            Assert.IsTrue(new TextColorOptions { AciIndex = 255 }.HasValidAciIndex());
            Assert.IsFalse(new TextColorOptions { AciIndex = 0 }.HasValidAciIndex());
            Assert.IsFalse(new TextColorOptions { AciIndex = 256 }.HasValidAciIndex());
        }

        [TestMethod]
        public void Describe_ReportsTheTargetColor()
        {
            Assert.AreEqual(
                "索引色 1（红）",
                new TextColorOptions { Mode = TextColorMode.Aci, AciIndex = 1 }.Describe());

            Assert.AreEqual(
                "真彩色 RGB(1,2,3)",
                new TextColorOptions { Mode = TextColorMode.TrueColor, Red = 1, Green = 2, Blue = 3 }.Describe());

            Assert.AreEqual("随层 ByLayer", new TextColorOptions { Mode = TextColorMode.ByLayer }.Describe());
            Assert.AreEqual("随块 ByBlock", new TextColorOptions { Mode = TextColorMode.ByBlock }.Describe());
        }
    }
}
