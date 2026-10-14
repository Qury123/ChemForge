using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using ChemForge.UI.Forms.Block.Numbering;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Application
{
    /// <summary>
    /// 「编号用法说明」按钮的排版测试。
    ///
    /// 回归点：这个按钮是"从外面挂"到编号面板第一个分组框上的
    /// （见 NumberingHelpButton），位置全靠自己算。曾经因为把"相对分组框客户区"的 y
    /// 直接当成"相对宿主面板"的 y，导致按钮整整低了 18px（用户截图报"没对齐"）。
    /// 这里按对话框里的真实参数离线复刻那一个分组框，量出绝对坐标来锁住对齐。
    ///
    /// 复刻的参数抄自 src\Shared\UI\Forms\Block\Numbering\NumberingOptionsForm.cs：
    ///   NewGroup(…, 76)        → Dock=Fill / Margin(0,0,0,6) / Padding(8,2,8,4)
    ///   NewFlow()              → Dock=Fill / LeftToRight / WrapContents / AutoScroll
    ///   NewButton(text, width) → Height=28 / Margin(6,0,0,0)
    ///   _lblSelection          → AutoSize / Margin(14,8,0,0)
    /// 这些参数如果跟着面板改了，本测试也要一起改。
    /// </summary>
    [TestClass]
    public sealed class NumberingHelpButtonLayoutTests
    {
        [TestMethod]
        public void HelpButton_AlignsWithTheFirstRowButton()
        {
            int deltaY = int.MinValue;
            int deltaHeight = int.MinValue;
            int leftGap = int.MinValue;
            int rightGap = int.MinValue;
            Exception failure = null;

            // WinForms 控件要在 STA 线程上创建
            Thread thread = new Thread(delegate()
            {
                try
                {
                    Measure(out deltaY, out deltaHeight, out leftGap, out rightGap);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "布局测量线程超时。");
            Assert.IsNull(failure, "布局测量抛异常：" + failure);

            string detail = string.Format(
                "（ΔY={0}，Δ高={1}，左边距={2}，右边距={3}）", deltaY, deltaHeight, leftGap, rightGap);

            Assert.AreEqual(0, deltaY, "说明按钮顶边应与「① 在图纸中选一个样板块」齐平。" + detail);
            Assert.AreEqual(0, deltaHeight, "说明按钮高度应与同一行的按钮一致。" + detail);
            Assert.AreEqual(leftGap, rightGap, "说明按钮右边距应与①号按钮左边距对称。" + detail);
            Assert.IsTrue(deltaY == 0 && leftGap == rightGap, "布局测量值应已写入。" + detail);
        }

        private static void Measure(out int deltaY, out int deltaHeight, out int leftGap, out int rightGap)
        {
            deltaY = int.MinValue;
            deltaHeight = int.MinValue;
            leftGap = int.MinValue;
            rightGap = int.MinValue;

            using (Form form = new Form())
            {
                form.ClientSize = new Size(820, 620);

                GroupBox box = new GroupBox();
                box.Text = "选择要编号的块（两步）";
                box.Dock = DockStyle.Fill;
                box.Height = 76;
                box.Margin = new Padding(0, 0, 0, 6);
                box.Padding = new Padding(8, 2, 8, 4);

                Button first = new Button();
                first.Text = "① 在图纸中选一个样板块";
                first.Width = 210;
                first.Height = 28;
                first.Margin = new Padding(6, 0, 0, 0);

                Button second = new Button();
                second.Text = "② 选择范围";
                second.Width = 120;
                second.Height = 28;
                second.Margin = new Padding(6, 0, 0, 0);

                Label hint = new Label();
                hint.AutoSize = true;
                hint.Text = "尚未选择：请先点【① 在图纸中选一个样板块】";
                hint.Margin = new Padding(14, 8, 0, 0);

                FlowLayoutPanel flow = new FlowLayoutPanel();
                flow.Dock = DockStyle.Fill;
                flow.FlowDirection = FlowDirection.LeftToRight;
                flow.WrapContents = true;
                flow.AutoScroll = true;

                flow.Controls.Add(first);
                flow.Controls.Add(second);
                flow.Controls.Add(hint);
                box.Controls.Add(flow);

                Panel holder = new Panel();
                holder.Dock = DockStyle.Top;
                holder.Height = 93;
                holder.Padding = new Padding(11, 7, 11, 0);
                holder.Controls.Add(box);
                form.Controls.Add(holder);

                AttachHelpButton(box);

                form.CreateControl();
                holder.PerformLayout();
                box.PerformLayout();
                flow.PerformLayout();
                System.Windows.Forms.Application.DoEvents();

                Button help = FindHelpButton(box);
                Assert.IsNotNull(help, "分组框里没有找到「编号用法说明」按钮。");

                // 不用屏幕坐标换算：表单没有 Show，窗口原点不可靠，这里把各级控件的
                // Top/Left 沿父链累加到分组框客户区（与画面上看到的相对位置一致）。
                deltaY = OffsetIn(box, help, true) - OffsetIn(box, first, true);
                deltaHeight = help.Height - first.Height;
                leftGap = OffsetIn(box, first, false);
                rightGap = box.ClientSize.Width - (OffsetIn(box, help, false) + help.Width);
            }
        }

        /// <summary>控件相对分组框客户区的 Top（vertical=true）或 Left。</summary>
        private static int OffsetIn(Control box, Control child, bool vertical)
        {
            int offset = 0;
            Control control = child;
            while (control != null && !ReferenceEquals(control, box))
            {
                offset += vertical ? control.Top : control.Left;
                control = control.Parent;
            }

            Assert.IsNotNull(control, "控件不在分组框里，量不了位置。");
            return offset;
        }

        /// <summary>NumberingHelpButton 是 internal（只给本插件用），这里用反射调它的 Attach。</summary>
        private static void AttachHelpButton(GroupBox box)
        {
            Type type = typeof(NumberingHelpText).Assembly.GetType(
                "ChemForge.UI.Forms.Block.Numbering.NumberingHelpButton", throwOnError: true);
            MethodInfo attach = type.GetMethod("Attach", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(attach, "NumberingHelpButton.Attach 方法签名变了？");
            attach.Invoke(null, new object[] { box });
        }

        private static Button FindHelpButton(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null && button.Text == "编号用法说明")
                {
                    return button;
                }

                Button nested = FindHelpButton(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
