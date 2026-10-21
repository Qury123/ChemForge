using System;
using System.Threading;
using System.Windows.Forms;
using ChemForge.UI.Forms.Text.TextColor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Application
{
    /// <summary>
    /// 「处理结果」提示框 <see cref="TextColorResultForm"/> 的测试。
    ///
    /// 这个窗口是用户投诉的另一半："点了确定，除了命令行输出，应该有弹窗给用户看"。
    /// 正常路径上弹的是 AutoCAD 原生提示框（CommandHost.ShowAlertSafely），
    /// 只有原生那条路抛异常时才会走到这个窗体，所以它得跟原生提示框一样：
    /// 一段短文字 + 一个「确定」，窗口跟着文字走 —— 不能再长出一个大标题、
    /// 一个大 logo 和半屏内容（那是上一版，用户原话"简单点行不行"）。
    /// </summary>
    [TestClass]
    public sealed class TextColorResultFormTests
    {
        private const string Brief =
            "完成！共修改 3 个对象（改颜色 3 处、清除内部颜色码 2 处）\r\n"
            + "目标颜色：索引色 1（红）\r\n"
            + "范围：所选对象\r\n"
            + "如需撤销：Ctrl+Z 一次即可全部还原。";

        [TestMethod]
        public void ResultForm_ShowsTheBriefTextAndOnlyAnOkButton()
        {
            string failure = null;
            string messageText = null;
            string caption = null;
            bool hasOk = false;
            bool hasOtherButton = false;
            bool hasBigTitle = false;
            int width = 0;
            int height = 0;

            RunOnStaThread(delegate()
            {
                using (TextColorResultForm form = new TextColorResultForm(Brief))
                {
                    form.CreateControl();
                    form.PerformLayout();
                    System.Windows.Forms.Application.DoEvents();

                    caption = form.Text;
                    width = form.Width;
                    height = form.Height;

                    Label message = FindLabel(form, "ResultMessage");
                    messageText = message == null ? null : message.Text;

                    hasOk = FindButton(form, "ResultOkButton") != null;

                    foreach (Control child in form.Controls)
                    {
                        Button button = child as Button;
                        if (button != null && button.Name != "ResultOkButton")
                        {
                            hasOtherButton = true;
                        }

                        Label label = child as Label;
                        if (label != null && label.Font.Bold && label.Font.Size > form.Font.Size)
                        {
                            hasBigTitle = true;
                        }
                    }
                }
            },
            delegate(Exception ex) { failure = ex.ToString(); });

            Assert.IsNull(failure, "结果提示框构造/排版抛异常：" + failure);
            Assert.AreEqual("批量修改多行文字颜色", caption, "提示框标题应是命令名。");
            Assert.AreEqual(Brief, messageText, "提示框正文就是短版报告，一字不改。");
            Assert.IsTrue(hasOk, "提示框要有「确定」按钮。");
            Assert.IsFalse(hasOtherButton, "提示框只该有「确定」一个按钮。");
            Assert.IsFalse(hasBigTitle, "提示框不该再出现品牌大标题。");

            // 上一版是 560×420 的品牌窗口，用户嫌"这么多内容、这么大 logo"。
            Assert.IsTrue(width <= 560, "提示框宽度该跟着文字走，实际 " + width + "px。");
            Assert.IsTrue(height <= 220, "提示框高度该跟着文字走，实际 " + height + "px。");
        }

        [TestMethod]
        public void ResultForm_NormalizesLoneLineFeedsToCrLf()
        {
            string failure = null;
            string messageText = null;

            RunOnStaThread(delegate()
            {
                using (TextColorResultForm form = new TextColorResultForm("甲\n乙\n丙"))
                {
                    form.CreateControl();

                    Label message = FindLabel(form, "ResultMessage");
                    messageText = message == null ? null : message.Text;
                }
            },
            delegate(Exception ex) { failure = ex.ToString(); });

            Assert.IsNull(failure, "结果提示框构造抛异常：" + failure);
            Assert.AreEqual("甲\r\n乙\r\n丙", messageText, "只给 LF 的正文要补成 CRLF，行距才与命令行一致。");
        }

        [TestMethod]
        public void ResultForm_WrapsLongTextInsteadOfGrowingSideways()
        {
            string failure = null;
            int clientWidth = 0;
            int height = 0;
            int labelHeight = 0;
            int fontHeight = 0;

            string longLine = "完成！共修改 " + new string('8', 200) + " 个对象";

            RunOnStaThread(delegate()
            {
                using (TextColorResultForm form = new TextColorResultForm(longLine))
                {
                    form.CreateControl();
                    form.PerformLayout();

                    clientWidth = form.ClientSize.Width;
                    height = form.Height;
                    fontHeight = form.Font.Height;

                    Label message = FindLabel(form, "ResultMessage");
                    labelHeight = message == null ? 0 : message.Height;
                }
            },
            delegate(Exception ex) { failure = ex.ToString(); });

            Assert.IsNull(failure, "结果提示框构造抛异常：" + failure);
            Assert.AreEqual(552, clientWidth, "超长行该在上限处折行，客户区宽 520 + 左右各 16。");
            Assert.IsTrue(labelHeight > fontHeight, "折行后正文高度要跟着涨，实际 " + labelHeight + "px。");
            Assert.IsTrue(height < 400, "折行后窗口也不该高过一屏的小半，实际 " + height + "px。");
        }

        [TestMethod]
        public void ResultForm_ToleratesEmptyMessage()
        {
            string failure = null;
            string messageText = null;

            RunOnStaThread(delegate()
            {
                using (TextColorResultForm form = new TextColorResultForm(null))
                {
                    form.CreateControl();

                    Label message = FindLabel(form, "ResultMessage");
                    messageText = message == null ? null : message.Text;
                }
            },
            delegate(Exception ex) { failure = ex.ToString(); });

            Assert.IsNull(failure, "正文为空时提示框也不该抛异常：" + failure);
            Assert.AreEqual(string.Empty, messageText);
        }

        private static void RunOnStaThread(ThreadStart action, Action<Exception> onError)
        {
            // WinForms 控件要在 STA 线程上创建
            Thread thread = new Thread(delegate()
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "结果提示框测试线程超时。");
        }

        private static Label FindLabel(Control parent, string name)
        {
            foreach (Control child in parent.Controls)
            {
                Label label = child as Label;
                if (label != null && label.Name == name)
                {
                    return label;
                }

                Label nested = FindLabel(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Button FindButton(Control parent, string name)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null && button.Name == name)
                {
                    return button;
                }

                Button nested = FindButton(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
