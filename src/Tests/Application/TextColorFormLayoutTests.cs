using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using ChemForge.Models.Text.TextColor;
using ChemForge.UI.Forms.Text.TextColor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Application
{
    /// <summary>
    /// 「批量修改多行文字颜色」对话框的排版测试。
    ///
    /// 回归点（用户截图报的「处理选项处叠在一起」）：
    /// 最长的复选框「深入块定义内部（影响所有同名块参照）」约 235px 宽，
    /// 却只有 210px 的格子、又没关 AutoSize，于是被折成两行；
    /// 第二行顶出「处理选项」分组框被裁掉，剩下的笔画还压到下面的「当前选择」上。
    ///
    /// 这里用**真实窗体**量，不是照抄一遍坐标（抄一遍的话，改坏了测试照样绿）：
    /// ① 四个复选框两两不重叠；
    /// ② 每个都完整落在分组框客户区里（越界就是被裁）；
    /// ③ 分组框不压到「当前选择」和确定/取消按钮；
    /// ④ 按控件自己的字体量文字宽度，保证每条都不折行、也不被高度裁掉；
    /// ⑤ 窗体上所有直接子控件（三个分组框、两个标签、两个按钮）两两不重叠。
    ///
    /// 窗体构造函数收一个取色委托，所以这里能完全绕开 acmgd
    /// （详见 TextColorForm 的 (context, aciColorResolver) 重载）。
    /// </summary>
    [TestClass]
    public sealed class TextColorFormLayoutTests
    {
        /// <summary>
        /// 复选框方块 + 左右内边距占掉的宽度。默认主题下约 19px，这里留 22px 更保守：
        /// 真机上排得下的，本测试一定也判它排得下。
        /// </summary>
        private const int CheckBoxGlyphWidth = 22;

        [TestMethod]
        public void OptionCheckBoxes_StayInsideGroupBoxAndDoNotWrap()
        {
            List<string> problems = null;
            string failure = null;

            // WinForms 控件要在 STA 线程上创建
            Thread thread = new Thread(delegate()
            {
                try
                {
                    problems = Measure();
                }
                catch (Exception ex)
                {
                    failure = ex.ToString();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "排版测量线程超时。");
            Assert.IsNull(failure, "排版测量抛异常：" + failure);
            Assert.IsNotNull(problems, "排版测量没有返回结果。");
            Assert.AreEqual(
                0,
                problems.Count,
                "对话框排版有问题：" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// 点「① 在图纸中选择要改色的文字」：要以 PickObjects 动作关窗，并带着面板上的当前选项。
        ///
        /// 这条给「去框选一次，颜色就丢了」兜底：命令那边全靠 form.Options 把选项带到下一轮面板。
        /// </summary>
        [TestMethod]
        public void PickObjectsButton_ReturnsPickObjectsAndKeepsCurrentOptions()
        {
            string failure = null;
            TextColorDialogAction action = TextColorDialogAction.Cancel;
            TextColorOptions options = null;

            Thread thread = new Thread(delegate()
            {
                try
                {
                    TextColorFormContext context = new TextColorFormContext
                    {
                        MTextCount = 0,
                        TextCount = 0,
                        MultiLeaderCount = 0,
                        Options = new TextColorOptions
                        {
                            Mode = TextColorMode.TrueColor,
                            Red = 12,
                            Green = 34,
                            Blue = 56
                        }
                    };

                    using (TextColorForm form = new TextColorForm(context, StubColor))
                    {
                        // 不 Show 的话子控件的 CanSelect 为 false，PerformClick 不会触发 Click。
                        form.Show();
                        System.Windows.Forms.Application.DoEvents();

                        Button pick = FindButtonByName(form, "PickObjectsButton");
                        Assert.IsNotNull(pick, "找不到「① 在图纸中选择要改色的文字」按钮。");

                        pick.PerformClick();
                        System.Windows.Forms.Application.DoEvents();

                        action = form.Action;
                        options = form.Options;
                    }
                }
                catch (Exception ex)
                {
                    failure = ex.ToString();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(60)), "按钮测试线程超时。");
            Assert.IsNull(failure, "按钮测试抛异常：" + failure);
            Assert.AreEqual(
                TextColorDialogAction.PickObjects,
                action,
                "点「① 在图纸中选择要改色的文字」应返回 PickObjects 动作。");
            Assert.IsNotNull(options, "这个按钮也要把面板上的选项带回去，否则去框选一次颜色就丢了。");
            Assert.AreEqual(TextColorMode.TrueColor, options.Mode);
            Assert.AreEqual(12, options.Red);
            Assert.AreEqual(34, options.Green);
            Assert.AreEqual(56, options.Blue);
        }

        /// <summary>
        /// 面板没弹出来时（<c>ShowModalDialog</c> 抛异常、异常被吞掉），
        /// <c>form.Action</c> 还停在枚举的默认值上 —— 默认值必须是 Cancel。
        ///
        /// 默认成 Apply 的话，命令会拿「用户根本没看过的默认选项」直接改图；
        /// 这个 bug 只有在弹窗失败时才发作，平时跑一百遍也看不出来，
        /// 所以用一条断言把默认值钉住。
        /// </summary>
        [TestMethod]
        public void DialogAction_DefaultsToCancel()
        {
            Assert.AreEqual(
                TextColorDialogAction.Cancel,
                default(TextColorDialogAction),
                "枚举默认值必须是 Cancel（0），不能是 Apply。");
        }

        private static List<string> Measure()
        {
            List<string> problems = new List<string>();

            TextColorFormContext context = new TextColorFormContext
            {
                MTextCount = 1,
                TextCount = 0,
                MultiLeaderCount = 0,
                Options = new TextColorOptions()
            };

            using (TextColorForm form = new TextColorForm(context, StubColor))
            {
                form.CreateControl();
                form.PerformLayout();
                System.Windows.Forms.Application.DoEvents();

                GroupBox optionGroup = FindGroupBox(form, "处理选项");
                if (optionGroup == null)
                {
                    problems.Add("找不到「处理选项」分组框。");
                    return problems;
                }

                List<CheckBox> boxes = FindAll<CheckBox>(optionGroup);
                if (boxes.Count != 4)
                {
                    problems.Add("「处理选项」里应有 4 个复选框，实际 " + boxes.Count + " 个。");
                    return problems;
                }

                CheckNoOverlap(problems, optionGroup, boxes);
                CheckInsideGroup(problems, optionGroup, boxes);
                CheckGroupDoesNotCoverBelow(problems, form, optionGroup);
                CheckTextFits(problems, boxes);
                CheckPickObjectsButton(problems, form);
                CheckFormChildren(problems, form);
            }

            return problems;
        }

        /// <summary>① 四个复选框两两不重叠（坐标换算到「处理选项」分组框里比）。</summary>
        private static void CheckNoOverlap(List<string> problems, Control ancestor, List<CheckBox> boxes)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    Rectangle a = BoundsIn(ancestor, boxes[i]);
                    Rectangle b = BoundsIn(ancestor, boxes[j]);

                    if (a.IntersectsWith(b))
                    {
                        problems.Add(string.Format(
                            "复选框「{0}」{1} 与「{2}」{3} 重叠。",
                            boxes[i].Text, Show(a), boxes[j].Text, Show(b)));
                    }
                }
            }
        }

        /// <summary>② 每个复选框都完整落在分组框客户区里。</summary>
        private static void CheckInsideGroup(List<string> problems, GroupBox group, List<CheckBox> boxes)
        {
            foreach (CheckBox box in boxes)
            {
                Rectangle rect = BoundsIn(group, box);

                if (rect.Left < 0 || rect.Top < 0
                    || rect.Right > group.ClientSize.Width || rect.Bottom > group.ClientSize.Height)
                {
                    problems.Add(string.Format(
                        "复选框「{0}」{1} 超出了「处理选项」客户区 {2}，会被分组框裁掉。",
                        box.Text, Show(rect), Show(new Rectangle(0, 0, group.ClientSize.Width, group.ClientSize.Height))));
                }
            }
        }

        /// <summary>③ 分组框不压到下面的「当前选择」和两个按钮（坐标都换算到窗体客户区）。</summary>
        private static void CheckGroupDoesNotCoverBelow(List<string> problems, Form form, GroupBox group)
        {
            Rectangle groupRect = BoundsIn(form, group);

            Control[] below =
            {
                FindLabel(form, "当前选择"),
                FindButton(form, "确定"),
                FindButton(form, "取消")
            };

            string[] names = { "「当前选择」标签", "「确定」按钮", "「取消」按钮" };

            for (int i = 0; i < below.Length; i++)
            {
                if (below[i] == null)
                {
                    problems.Add("找不到" + names[i] + "。");
                    continue;
                }

                Rectangle rect = BoundsIn(form, below[i]);
                if (groupRect.IntersectsWith(rect))
                {
                    problems.Add(string.Format(
                        "「处理选项」分组框 {0} 压住了{1} {2}。", Show(groupRect), names[i], Show(rect)));
                }
            }
        }

        /// <summary>④ 每条文字在控件里排得下：既不折行，也不被高度裁掉。</summary>
        private static void CheckTextFits(List<string> problems, List<CheckBox> boxes)
        {
            foreach (CheckBox box in boxes)
            {
                Size single = TextRenderer.MeasureText(
                    box.Text,
                    box.Font,
                    new Size(10000, 10000),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

                Size wrapped = TextRenderer.MeasureText(
                    box.Text,
                    box.Font,
                    new Size(box.Width - CheckBoxGlyphWidth, 10000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

                if (wrapped.Height > single.Height)
                {
                    problems.Add(string.Format(
                        "复选框「{0}」宽 {1}px，扣掉复选框方块只剩 {2}px，文字要 {3}px 才排得下一行，会被折行。",
                        box.Text, box.Width, box.Width - CheckBoxGlyphWidth, single.Width));
                }

                if (single.Height > box.Height)
                {
                    problems.Add(string.Format(
                        "复选框「{0}」高 {1}px，装不下单行文字（需 {2}px）。",
                        box.Text, box.Height, single.Height));
                }
            }
        }

        /// <summary>
        /// ⑤ 顶部「① 在图纸中选择要改色的文字」按钮：在窗体内、文字排得下。
        ///
        /// 用户要求照 BLKATTCHANGE 来：命令先弹面板，选择由面板上的按钮发起。
        /// 这个按钮被挪出窗体或被裁字，等于把唯一的选字入口弄坏了。
        /// </summary>
        private static void CheckPickObjectsButton(List<string> problems, Form form)
        {
            Button button = FindButtonByName(form, "PickObjectsButton");
            if (button == null)
            {
                problems.Add("找不到「① 在图纸中选择要改色的文字」按钮。");
                return;
            }

            Rectangle rect = BoundsIn(form, button);
            if (rect.Left < 0 || rect.Top < 0
                || rect.Right > form.ClientSize.Width || rect.Bottom > form.ClientSize.Height)
            {
                problems.Add(string.Format(
                    "「{0}」按钮 {1} 超出了窗体客户区 {2}。",
                    button.Text, Show(rect), Show(new Rectangle(0, 0, form.ClientSize.Width, form.ClientSize.Height))));
            }

            Size text = TextRenderer.MeasureText(
                button.Text,
                button.Font,
                new Size(10000, 10000),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // 系统按钮左右各有约 6px 内边距；按 6px 算，刚好够就是刚好够。
            const int ButtonPaddingX = 6;
            if (text.Width + ButtonPaddingX * 2 > button.Width)
            {
                problems.Add(string.Format(
                    "「{0}」按钮宽 {1}px，文字要 {2}px（加内边距共 {3}px），会被裁字。",
                    button.Text, button.Width, text.Width, text.Width + ButtonPaddingX * 2));
            }
        }

        /// <summary>⑤ 五个区（选择 / 基本颜色 / 索引色 / 处理选项 / 底部行）两两不重叠。</summary>
        private static void CheckFormChildren(List<string> problems, Form form)
        {
            // 排版是"窗体 → 一个根 TableLayoutPanel → 五个区"，要比的就是这五个区。
            Control host = form;
            if (form.Controls.Count == 1 && form.Controls[0] is TableLayoutPanel)
            {
                host = form.Controls[0];
            }

            List<Control> children = new List<Control>();
            foreach (Control child in host.Controls)
            {
                children.Add(child);
            }

            if (children.Count < 5)
            {
                problems.Add("窗体上应分出 5 个区（选择 / 基本颜色 / 索引色 / 处理选项 / 底部），实际 " + children.Count + " 个。");
            }

            for (int i = 0; i < children.Count; i++)
            {
                for (int j = i + 1; j < children.Count; j++)
                {
                    Rectangle a = BoundsIn(form, children[i]);
                    Rectangle b = BoundsIn(form, children[j]);

                    if (a.IntersectsWith(b))
                    {
                        problems.Add(string.Format(
                            "「{0}」{1} 与「{2}」{3} 重叠。",
                            Describe(children[i]), Show(a), Describe(children[j]), Show(b)));
                    }
                }
            }
        }

        /// <summary>取色桩：不碰 AutoCAD 色表，免得为了量一张界面去加载 acmgd。</summary>
        private static Color StubColor(int aci)
        {
            return Color.FromArgb(255, 255, 255);
        }

        /// <summary>
        /// 控件相对某个祖先客户区的矩形。
        ///
        /// 排版改成 TableLayoutPanel 嵌套之后，兄弟控件的 Left/Top 不再共用同一个父坐标系，
        /// 沿 Parent 链把偏移累加到这个祖先上才能直接比较。
        /// </summary>
        private static Rectangle BoundsIn(Control ancestor, Control control)
        {
            int left = 0;
            int top = 0;

            Control current = control;
            while (current != null && current != ancestor)
            {
                left += current.Left;
                top += current.Top;
                current = current.Parent;
            }

            return new Rectangle(left, top, control.Width, control.Height);
        }

        private static string Show(Rectangle rect)
        {
            return string.Format("({0},{1}) {2}x{3}", rect.Left, rect.Top, rect.Width, rect.Height);
        }

        private static string Describe(Control control)
        {
            return string.IsNullOrEmpty(control.Text)
                ? control.GetType().Name
                : control.Text.Replace(Environment.NewLine, " ");
        }

        private static GroupBox FindGroupBox(Control parent, string caption)
        {
            foreach (Control child in parent.Controls)
            {
                GroupBox box = child as GroupBox;
                if (box != null && box.Text == caption)
                {
                    return box;
                }

                GroupBox nested = FindGroupBox(child, caption);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Label FindLabel(Control parent, string prefix)
        {
            foreach (Control child in parent.Controls)
            {
                Label label = child as Label;
                if (label != null && label.Text != null && label.Text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return label;
                }

                Label nested = FindLabel(child, prefix);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Button FindButton(Control parent, string caption)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null && button.Text == caption)
                {
                    return button;
                }

                Button nested = FindButton(child, caption);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Button FindButtonByName(Control parent, string name)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Button && string.Equals(child.Name, name, StringComparison.Ordinal))
                {
                    return (Button)child;
                }

                Button nested = FindButtonByName(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static List<T> FindAll<T>(Control parent) where T : Control
        {
            List<T> found = new List<T>();

            foreach (Control child in parent.Controls)
            {
                T match = child as T;
                if (match != null)
                {
                    found.Add(match);
                }

                found.AddRange(FindAll<T>(child));
            }

            return found;
        }
    }
}
