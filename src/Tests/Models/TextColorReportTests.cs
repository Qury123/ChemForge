using System;
using ChemForge.Models.Text.TextColor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Models
{
    /// <summary>
    /// 完成提示 <see cref="TextColorReport"/> 的测试。
    ///
    /// 两个口径各管一头：<see cref="TextColorReport.Build"/> 是命令行那份完整报告，
    /// 样例、核对信息、刷新提示一个都不能少；<see cref="TextColorReport.BuildBrief"/>
    /// 是结果提示框那份短版，只留结论 —— 用户投诉过"这么多内容"，短版要是又把
    /// 样例和 REGEN 提示带回来，这条投诉就白提了。
    /// </summary>
    [TestClass]
    public sealed class TextColorReportTests
    {
        [TestMethod]
        public void BuildBrief_KeepsTheConclusionAndDropsTheNoise()
        {
            TextColorResult result = BuildSampleResult();
            TextColorOptions options = new TextColorOptions { Mode = TextColorMode.Aci, AciIndex = 1 };

            string brief = TextColorReport.BuildBrief(result, options);

            Assert.IsTrue(
                brief.StartsWith("完成！共修改 3 个对象（改颜色 3 处、清除内部颜色码 2 处）", StringComparison.Ordinal),
                "短版首句必须是结论，实际：" + brief);
            Assert.IsTrue(brief.Contains("目标颜色："), "短版要留下目标颜色。实际：" + brief);
            Assert.IsTrue(brief.Contains("范围：所选对象"), "短版要留下作用范围。实际：" + brief);
            Assert.IsTrue(brief.Contains("如需撤销"), "短版要留下撤销提示。实际：" + brief);

            Assert.IsFalse(brief.Contains("[样例"), "短版不该带样例。实际：" + brief);
            Assert.IsFalse(brief.Contains("[核对]"), "短版不该带核对信息。实际：" + brief);
            Assert.IsFalse(brief.Contains("REGEN"), "短版不该带刷新提示。实际：" + brief);
        }

        [TestMethod]
        public void BuildBrief_KeepsSkipStatistics()
        {
            TextColorResult result = BuildSampleResult();
            result.FieldSkippedCount = 6;
            result.LockedSkippedCount = 4;
            result.FailedCount = 3;

            string brief = TextColorReport.BuildBrief(result, new TextColorOptions());

            Assert.IsTrue(brief.Contains("6 个含字段的文字"), "少了跳过统计，用户会以为丢了对象。实际：" + brief);
            Assert.IsTrue(brief.Contains("4 个文字位于锁定图层"), "实际：" + brief);
            Assert.IsTrue(brief.Contains("3 个对象的内容读取失败"), "实际：" + brief);
        }

        [TestMethod]
        public void BuildBrief_SharesItsLinesWithTheFullReport()
        {
            TextColorResult result = BuildSampleResult();
            TextColorOptions options = new TextColorOptions { Mode = TextColorMode.Aci, AciIndex = 1 };

            string[] briefLines = TextColorReport.BuildBrief(result, options).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] reportLines = TextColorReport.Build(result, options).Split(new[] { "\r\n" }, StringSplitOptions.None);

            Assert.AreEqual(4, briefLines.Length, "短版就四行：结论、目标颜色、范围、撤销。实际：" + string.Join(" / ", briefLines));

            // 前三行在完整报告里也是开头三行，逐字一致；
            // 末行「如需撤销」在完整报告里是最后一行（中间隔着样例与核对信息）。
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(reportLines[i], briefLines[i], "短版第 " + (i + 1) + " 行该与完整报告逐字一致。");
            }

            Assert.AreEqual(reportLines[reportLines.Length - 1], briefLines[3], "短版末行该是完整报告的最后一行。");
        }

        [TestMethod]
        public void Build_StillCarriesSamplesProbeMessagesAndUndo()
        {
            TextColorResult result = BuildSampleResult();

            string report = TextColorReport.Build(result, new TextColorOptions());

            Assert.IsTrue(report.Contains("[样例·清理前] \\C1;图纸会审记录"), "命令行报告丢了样例。实际：" + report);
            Assert.IsTrue(report.Contains("[样例·清理后] 图纸会审记录"), "实际：" + report);
            Assert.IsTrue(report.Contains("[核对] 颜色组码 62=1"), "实际：" + report);
            Assert.IsTrue(report.Contains("只重画了改动对象"), "实际：" + report);
            Assert.IsTrue(report.EndsWith("如需撤销：Ctrl+Z 一次即可全部还原。", StringComparison.Ordinal), "实际：" + report);
        }

        [TestMethod]
        public void BuildBrief_ToleratesNullResult()
        {
            Assert.AreEqual(string.Empty, TextColorReport.BuildBrief(null, new TextColorOptions()));
        }

        private static TextColorResult BuildSampleResult()
        {
            TextColorResult result = new TextColorResult
            {
                ChangedCount = 3,
                ColorChangedCount = 3,
                CodeStrippedCount = 2,
                BeforeSample = "\\C1;图纸会审记录",
                AfterSample = "图纸会审记录",
                ColorProbe = "颜色组码 62=1"
            };

            result.Messages.Add("（本次只重画了改动对象，未全图重生成；若显示未刷新请执行 REGEN）");
            return result;
        }
    }
}
