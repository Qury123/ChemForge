using System;
using System.Collections.Generic;
using System.Linq;
using ChemForge.Application;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ChemForge.Tests.Application
{
    /// <summary>
    /// MenuDefinition 单元测试
    /// （菜单项标题 = 原始中文名，无 \t 留白、无括号说明；宽度由 AutoCAD 按最长标签自动撑开）
    /// </summary>
    [TestClass]
    public sealed class MenuDefinitionTests
    {
        [TestMethod]
        public void RootMenu_IsChemForge()
        {
            Assert.AreEqual("ChemForge", MenuDefinition.RootMenuTitle);
            Assert.AreEqual("ID_ChemForgeMenu", MenuDefinition.RootMenuAlias);
            Assert.AreEqual("CHEMFORGE", MenuDefinition.MenuGroupName);
        }

        [TestMethod]
        public void Menus_ContainsFiveGroupsInOrder()
        {
            Assert.AreEqual(5, MenuDefinition.Menus.Count);

            // 分组名保持原始中文名（DisplayTitle 为剥离 \t 留白后的纯名称）
            Assert.AreEqual("文字格式处理", MenuDefinition.Menus[0].DisplayTitle);
            Assert.AreEqual("ID_TextMenu", MenuDefinition.Menus[0].Alias);
            Assert.AreEqual("图块批量处理", MenuDefinition.Menus[1].DisplayTitle);
            Assert.AreEqual("ID_BlockMenu", MenuDefinition.Menus[1].Alias);
            // 图块编号
            Assert.AreEqual("图块编号", MenuDefinition.Menus[2].DisplayTitle);
            Assert.AreEqual("ID_BlockNumberMenu", MenuDefinition.Menus[2].Alias);
            Assert.AreEqual("管线与仪表", MenuDefinition.Menus[3].DisplayTitle);
            Assert.AreEqual("ID_ToolsMenu", MenuDefinition.Menus[3].Alias);
            Assert.AreEqual("关于 ChemForge", MenuDefinition.Menus[4].DisplayTitle);
            Assert.AreEqual("ID_AboutMenu", MenuDefinition.Menus[4].Alias);
        }

        [TestMethod]
        public void Menus_SecondLevelItemsAreSubmenus()
        {
            foreach (var menu in MenuDefinition.Menus)
            {
                Assert.IsFalse(menu.IsCommand, $"二级菜单 {menu.DisplayTitle} 不应挂接命令宏。");
                Assert.IsNull(menu.Macro);
                Assert.IsFalse(menu.IsSeparator);
            }
        }

        [TestMethod]
        public void GroupTitles_HaveNoPadding()
        {
            // 2026-10-26：不再用 \t + 全角空格把菜单撑宽，宽度交给 AutoCAD 按最长标签自动撑开
            foreach (var menu in MenuDefinition.Menus)
            {
                Assert.AreEqual(menu.DisplayTitle, menu.Title);
                Assert.IsFalse(menu.Title.Contains("\t"), $"分组「{menu.DisplayTitle}」标题不应再含 \\t 留白。");
                Assert.IsFalse(menu.Title.Contains('\u3000'), $"分组「{menu.DisplayTitle}」标题不应含全角空格。");
                Assert.AreEqual(menu.Title.Trim(), menu.Title, $"分组「{menu.DisplayTitle}」标题不应有首尾空白。");
            }
        }

        [TestMethod]
        public void TextMenu_ContainsOnlyColorCommand()
        {
            var items = MenuDefinition.Menus[0].Children;
            Assert.AreEqual(1, items.Count);

            Assert.AreEqual("批量修改多行文字颜色", items[0].DisplayTitle);
            Assert.AreEqual("TCOLOR ", items[0].Macro);

            // 批量修改多行文字倾角 / 宽度因子两项已取消（命令 TANGLE / TWIDTH 一并删除）
            Assert.IsTrue(items.All(i => i.IsCommand && !i.IsSeparator));
        }

        [TestMethod]
        public void BlockMenu_ContainsOnlyBatchAttrChangeCommand()
        {
            var items = MenuDefinition.Menus[1].Children;
            Assert.AreEqual(1, items.Count);

            // 批量修改块属性。命令名 BLKATTCHANGE = BLocK + ATTribute + CHANGE。
            // 「统一块名」（RENBLK）与「图块统计」（BLKCNT）尚未实现，已从菜单中移除。
            Assert.AreEqual("批量修改块属性", items[0].DisplayTitle);
            Assert.AreEqual("BLKATTCHANGE ", items[0].Macro);
        }

        [TestMethod]
        public void BlockNumberMenu_ContainsOnlyNumberCommand()
        {
            var items = MenuDefinition.Menus[2].Children;
            Assert.AreEqual(1, items.Count);

            Assert.AreEqual("属性块批量编号", items[0].DisplayTitle);
            Assert.AreEqual("BLKNUM ", items[0].Macro);

            // 两条命令只登记、不进菜单（它们没有菜单项）：
            //   BLKNUMINFO 自检命令；
            //   BLKNUMHELP 用法说明 —— 菜单项已取消，改由「属性块批量编号」对话框
            //   右上角的「编号用法说明」按钮弹出（与命令行共用同一份文案）。
            Assert.IsTrue(items.All(i => i.IsCommand && !i.IsSeparator));
        }

        [TestMethod]
        public void ToolsMenu_ContainsBreakPipeAndBreakInstrumentCommands()
        {
            var items = MenuDefinition.Menus[3].Children;
            Assert.AreEqual(2, items.Count);

            Assert.AreEqual("管线自动打断", items[0].DisplayTitle);
            // 管线打断命令名即 PIPEBREAK（历史命令名，无别名）
            Assert.AreEqual("PIPEBREAK ", items[0].Macro);

            // 仪表线自动打断：与管线打断同引擎，差别是图层点选两次、固定打断仪表线，
            // 因此不询问缺口方向。命令名 INSTBREAK = INSTRument + BREAK。
            Assert.AreEqual("仪表线自动打断", items[1].DisplayTitle);
            Assert.AreEqual("INSTBREAK ", items[1].Macro);

            // 仪表块自动编号（TAGNO）2026-10-14 从菜单与功能区摘掉：业务尚未实现
            // （NumberInstCommand 里仍是 TODO，只打印编号、不读不写图纸）。
            // 命令本身仍登记在 PackageContents.xml 里，命令行敲 TAGNO 依然可调用，
            // 因此这里断言的是"菜单里不再有它"，而不是"命令不存在"。
            Assert.IsTrue(items.All(i => !i.IsCommand || i.Macro.Trim() != CommandNames.NumberInst),
                "TAGNO 不应再出现在菜单中（业务未实现，入口已摘）。");
        }

        [TestMethod]
        public void AboutMenu_ContainsSeparatorAndAboutCommand()
        {
            var items = MenuDefinition.Menus[4].Children;
            Assert.AreEqual(2, items.Count);

            Assert.IsTrue(items[0].IsSeparator, "第一项应为分隔线。");
            Assert.IsFalse(items[0].IsCommand);
            Assert.AreEqual(string.Empty, items[0].Title);

            Assert.AreEqual("关于 ChemForge", items[1].DisplayTitle);
            Assert.AreEqual("CHEMFORGE ", items[1].Macro);
            Assert.IsTrue(items[1].IsCommand);
        }

        [TestMethod]
        public void CommandTitles_ArePlainChineseNames()
        {
            // 命令项标题：纯名称，不带 \t 留白、不带括号说明、不带英文命令名提示
            var commands = new List<MenuItemDefinition>();
            CollectCommands(MenuDefinition.Menus, commands);

            foreach (var command in commands)
            {
                Assert.IsFalse(string.IsNullOrEmpty(command.DisplayTitle));
                Assert.AreEqual(command.DisplayTitle, command.Title);
                Assert.IsFalse(command.Title.Contains("\t"), $"命令项「{command.DisplayTitle}」标题不应再含 \\t 留白。");
                Assert.IsFalse(command.Title.Contains('\u3000'), $"命令项「{command.DisplayTitle}」标题不应含全角空格。");
                Assert.IsFalse(command.Title.Contains("（") || command.Title.Contains("("),
                    $"命令项「{command.DisplayTitle}」标题不应带括号说明。");
            }
        }

        [TestMethod]
        public void AllCommandMacros_EndWithSpaceForAutoExecute()
        {
            var commands = new List<MenuItemDefinition>();
            CollectCommands(MenuDefinition.Menus, commands);

            Assert.IsTrue(commands.Count >= 6, "应至少登记 6 个命令项（TANGLE / TWIDTH 取消、RENBLK / BLKCNT 下架、TAGNO 摘掉、新增 INSTBREAK 后为 6 项）。");
            foreach (var command in commands)
            {
                Assert.IsTrue(command.Macro.EndsWith(" ", StringComparison.Ordinal),
                    $"命令项「{command.DisplayTitle}」的宏必须以空格结尾（AutoCAD 菜单宏的空格 = 回车确认）。");
            }
        }

        /// <summary>
        /// 菜单宏必须由 <see cref="CommandNames"/> 常量拼出（命令名唯一来源）。
        /// 命令名原本三处维护（[CommandMethod]、菜单宏、RibbonBuilder.IconMap），
        /// 任一处漂移都会造成"点了菜单没反应"这类静默故障，本用例把菜单宏锁到常量上。
        /// </summary>
        [TestMethod]
        public void AllCommandMacros_ComeFromCommandNamesConstants()
        {
            var expected = new Dictionary<string, string>
            {
                { "ID_TextColor", CommandNames.TextColor },
                { "ID_BatchAttrChange", CommandNames.BatchAttrChange },
                { "ID_BreakPipe", CommandNames.PipeBreak },
                { "ID_BreakInstrument", CommandNames.InstrumentBreak },
                { "ID_BlockNumber", CommandNames.BlockNumber },
                { "ID_About", CommandNames.ChemForge }
            };

            var commands = new List<MenuItemDefinition>();
            CollectCommands(MenuDefinition.Menus, commands);

            Assert.AreEqual(expected.Count, commands.Count, "命令项数量与 CommandNames 登记数量不一致。");
            foreach (var command in commands)
            {
                Assert.IsTrue(expected.ContainsKey(command.Alias),
                    $"菜单里出现了未登记的命令别名：{command.Alias}");
                Assert.AreEqual(expected[command.Alias] + " ", command.Macro,
                    $"命令项「{command.DisplayTitle}」的宏与 CommandNames 常量不一致。");
            }
        }

        [TestMethod]
        public void AllAliases_AreUnique()
        {
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            CollectAliases(MenuDefinition.Menus, aliases);

            Assert.AreEqual(aliases.Count, CountNodes(MenuDefinition.Menus), "菜单树中的别名必须全部唯一。");
        }

        [TestMethod]
        public void Constructor_WithEmptyTitle_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() => new MenuItemDefinition("", "ID_X"));
            Assert.ThrowsException<ArgumentException>(() => new MenuItemDefinition("菜单", ""));
        }

        [TestMethod]
        public void Constructor_WithSubmenu_IsNotCommand()
        {
            var item = new MenuItemDefinition("子菜单", "ID_SubMenu");
            Assert.IsFalse(item.IsCommand);
            Assert.IsFalse(item.IsSeparator);
            Assert.AreEqual("子菜单", item.DisplayTitle);
            Assert.AreEqual(0, item.Children.Count);
        }

        [TestMethod]
        public void SeparatorFactory_CreatesSeparatorNode()
        {
            var separator = MenuItemDefinition.Separator("ID_Sep");
            Assert.IsTrue(separator.IsSeparator);
            Assert.IsFalse(separator.IsCommand);
            Assert.AreEqual("ID_Sep", separator.Alias);
            Assert.AreEqual(0, separator.Children.Count);
        }

        private static void CollectCommands(IEnumerable<MenuItemDefinition> items, List<MenuItemDefinition> commands)
        {
            foreach (var item in items)
            {
                if (item.IsCommand)
                {
                    commands.Add(item);
                }

                CollectCommands(item.Children, commands);
            }
        }

        private static void CollectAliases(IEnumerable<MenuItemDefinition> items, HashSet<string> aliases)
        {
            foreach (var item in items)
            {
                aliases.Add(item.Alias);
                CollectAliases(item.Children, aliases);
            }
        }

        private static int CountNodes(IEnumerable<MenuItemDefinition> items)
        {
            var count = 0;
            foreach (var item in items)
            {
                count += 1 + CountNodes(item.Children);
            }
            return count;
        }
    }
}