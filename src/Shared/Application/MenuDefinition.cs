using System;
using System.Collections.Generic;

namespace ChemForge.Application
{
    /// <summary>
    /// 菜单项定义 - 描述菜单树中的一个节点（下拉子菜单、命令项或分隔线）
    /// </summary>
    public sealed class MenuItemDefinition
    {
        /// <summary>
        /// 菜单显示名称
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// 菜单别名（CUIx 内部引用标识，须为 ASCII 且全局唯一）
        /// </summary>
        public string Alias { get; }

        /// <summary>
        /// 命令宏（纯命令名 + 尾随空格），子菜单节点为 null
        /// </summary>
        public string Macro { get; }

        /// <summary>
        /// 功能区/上下文使用的纯显示名：去掉 "\t" 后的命令名提示段。
        /// 下拉菜单的 Title 用「功能描述\t命令名」（\t 后命令名右对齐，Express 风格）；
        /// 但功能区按钮不适合显示 \t 段，故用本属性取主显示文本。
        /// </summary>
        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrEmpty(Title)) return string.Empty;
                int tab = Title.IndexOf('\t');
                return tab < 0 ? Title : Title.Substring(0, tab);
            }
        }

        /// <summary>
        /// 子菜单项集合
        /// </summary>
        public IList<MenuItemDefinition> Children { get; }

        /// <summary>
        /// 是否为命令项（挂接了命令宏的叶子节点）
        /// </summary>
        public bool IsCommand
        {
            get { return !string.IsNullOrEmpty(Macro); }
        }

        /// <summary>
        /// 是否为分隔线（Express 风格的命令组分隔）
        /// </summary>
        public bool IsSeparator { get; }

        /// <summary>
        /// 创建分隔线节点
        /// </summary>
        public static MenuItemDefinition Separator(string alias)
        {
            return new MenuItemDefinition(alias);
        }

        private MenuItemDefinition(string alias)
        {
            Title = string.Empty;
            Alias = alias;
            Macro = null;
            IsSeparator = true;
            Children = new List<MenuItemDefinition>();
        }

        /// <summary>
        /// 创建子菜单节点
        /// </summary>
        /// <param name="title">菜单显示名称</param>
        /// <param name="alias">菜单别名</param>
        public MenuItemDefinition(string title, string alias)
            : this(title, alias, null)
        {
        }

        /// <summary>
        /// 创建命令项节点
        /// </summary>
        /// <param name="title">菜单显示名称</param>
        /// <param name="alias">菜单别名</param>
        /// <param name="macro">命令宏</param>
        public MenuItemDefinition(string title, string alias, string macro)
        {
            if (string.IsNullOrEmpty(title))
                throw new ArgumentException("菜单名称不能为空。", nameof(title));
            if (string.IsNullOrEmpty(alias))
                throw new ArgumentException("菜单别名不能为空。", nameof(alias));

            Title = title;
            Alias = alias;
            Macro = macro;
            IsSeparator = false;
            Children = new List<MenuItemDefinition>();
        }
    }

    /// <summary>
    /// ChemForge 菜单定义 - 菜单结构的单一数据源，
    /// CUIx 菜单文件由 MenuBuilder 依据本定义在运行时生成
    /// </summary>
    public static class MenuDefinition
    {
        /// <summary>
        /// 菜单栏根菜单显示名称
        /// </summary>
        public const string RootMenuTitle = "ChemForge";

        /// <summary>
        /// 菜单栏根菜单别名
        /// </summary>
        public const string RootMenuAlias = "ID_ChemForgeMenu";

        /// <summary>
        /// 让 AutoCAD 在加载 CUIx 时**自动**把根菜单挂上菜单栏的第二个别名。
        ///
        /// 官方规则（AutoCAD 2025 帮助「关于创建和自定义下拉式菜单」原文）：
        /// "下拉式菜单应具有一个别名，其范围为 POP1 到 POP499。在默认情况下，
        ///  在加载自定义 (CUIx) 文件时将在菜单栏上显示具有别名 POP1 到 POP16 的菜单。
        ///  其他所有菜单必须添加到工作空间中才能显示。"
        ///
        /// 本机 Support 目录实测别名占用：PnP.cuix = POP0–POP13、acad.CUIX = POP0–POP12、
        /// acetmain.cuix(Express) = POP1、dbcon.cuix = POP0/1 ⇒ POP0–POP13 已占，
        /// 取 POP16 可命中官方规则且排在菜单栏末尾。
        /// 有了它，菜单栏不再依赖"改宿主 PnP.cuix 写 WSPop"（HGCAD 用的也是同类机制）。
        /// </summary>
        public const string RootMenuBarAlias = "POP16";

        /// <summary>
        /// 声明式功能区选项卡（局部 CUIx 内 RibbonTabSource）的别名。
        /// 用自家别名而不是 ID_ADDINSTAB，避免面板被并进系统"附加模块"选项卡
        /// </summary>
        public const string RibbonTabAlias = "ID_ChemForgeTab";

        /// <summary>
        /// CUIx 菜单组名称
        /// </summary>
        public const string MenuGroupName = "CHEMFORGE";

        /// <summary>
        /// 二级子菜单集合（Express 风格菜单，宽幅留白布局）
        ///
        /// 设计说明：
        /// 1) 一级分组名、二级命令名均为原始中文名称（不附加任何英文提示）；
        /// 2) 宽度处理：AutoCAD 菜单项文本支持 \t 制表符（右侧右对齐列）。
        ///    这里在 \t 之后填充「全角空格」——右侧只留白、不显示任何文字，
        ///    菜单宽度由左侧标题 + 右侧留白共同决定，整体宽幅、疏朗；
        /// 3) 菜单宏使用「原始命令名」（管线打断用 PIPEBREAK），
        ///    与命令注册名统一，命令行为显示真实命令名；宏的尾随空格 = 回车确认，
        ///    点击后命令自动执行（^C^C 前缀在 2025 环境下不可靠，已弃用）。
        /// </summary>
        public static readonly IReadOnlyList<MenuItemDefinition> Menus = new List<MenuItemDefinition>
        {
            new MenuItemDefinition("文字格式处理\t　　　　　　", "ID_TextMenu")
            {
                Children =
                {
                    new MenuItemDefinition("批量修改多行文字颜色\t　　　　　　　　", "ID_TextColor", CommandNames.TextColor + " ")
                }
            },
            new MenuItemDefinition("图块批量处理\t　　　　　　", "ID_BlockMenu")
            {
                Children =
                {
                    new MenuItemDefinition("批量修改块属性（内容 / 文字选项 / 特性）\t　　　　　　　　", "ID_BatchAttrChange", CommandNames.BatchAttrChange + " ")
                }
            },
            new MenuItemDefinition("图块编号\t　　　　　　", "ID_BlockNumberMenu")
            {
                Children =
                {
                    new MenuItemDefinition("属性块批量编号（先弹窗，再两步选块）\t　　　　　　　　", "ID_BlockNumber", CommandNames.BlockNumber + " ")
                }
            },
            new MenuItemDefinition("管线与仪表\t　　　　　　", "ID_ToolsMenu")
            {
                Children =
                {
                    new MenuItemDefinition("管线自动打断（缺口 / 半圆架桥）\t　　　　　　　　", "ID_BreakPipe", CommandNames.PipeBreak + " "),
                    // 仪表线自动打断（INSTBREAK）：与上一项同引擎，差别是图层要点选两次
                    // （仪表线图层 + 管线图层）、不询问缺口方向（固定打断仪表线）。
                    new MenuItemDefinition("仪表线自动打断（缺口 / 半圆架桥）\t　　　　　　　　", "ID_BreakInstrument", CommandNames.InstrumentBreak + " "),
                    // 仪表块自动编号（TAGNO）2026-10-14 从菜单与功能区摘掉：命令本身仍在
                    // （PackageContents.xml 里登记，命令行可直接敲），但业务尚未实现 ——
                    // NumberInstCommand 里仍是 TODO，只打印编号、不读不写图纸，
                    // 挂在界面上会让人以为点了就能用。
                }
            },
            new MenuItemDefinition("关于 ChemForge\t　　　　　　", "ID_AboutMenu")
            {
                Children =
                {
                    MenuItemDefinition.Separator("ID_AboutSep"),
                    new MenuItemDefinition("关于 ChemForge（版本信息）\t　　　　　　　　", "ID_About", CommandNames.ChemForge + " ")
                }
            }
        };
    }
}
