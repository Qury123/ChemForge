namespace ChemForge.Application
{
    /// <summary>
    /// AutoCAD 命令名的**唯一来源**。
    ///
    /// 同一个命令名原本散落三处：命令类的 <c>[CommandMethod("TCOLOR")]</c>、
    /// MenuDefinition 里的菜单宏（<c>"TCOLOR "</c>）、RibbonBuilder.IconMap 的图标键。
    /// 三处任何一处改名都会造成"菜单点了没反应 / 图标丢失"这类静默故障，
    /// 故集中到这里，其余位置一律引用常量。
    /// 注意：命令名是**对外契约**（用户会直接敲命令名，CUIx 宏也已写进用户支持目录），
    /// 改名等同于破坏兼容性，需要同步改安装脚本与文档。
    /// </summary>
    public static class CommandNames
    {
        /// <summary>批量修改多行文字颜色</summary>
        public const string TextColor = "TCOLOR";

        /// <summary>批量修改块增强属性（内容 / 文字选项 / 特性）</summary>
        public const string BatchAttrChange = "BLKATTCHANGE";

        /// <summary>属性块批量编号（BLKNUM 命令层的主命令）</summary>
        public const string BlockNumber = "BLKNUM";

        /// <summary>属性块批量编号：命令行用法说明</summary>
        public const string BlockNumberHelp = "BLKNUMHELP";

        /// <summary>属性块批量编号：插件自检（版本 / 路径 / SECURELOAD / TRUSTEDPATHS / 已注册命令）</summary>
        public const string BlockNumberInfo = "BLKNUMINFO";

        /// <summary>管线自动打断（缺口 / 半圆架桥）</summary>
        public const string PipeBreak = "PIPEBREAK";

        /// <summary>仪表线自动打断（仪表线 × 管线，固定打断仪表线）</summary>
        public const string InstrumentBreak = "INSTBREAK";

        /// <summary>仪表块自动编号</summary>
        public const string NumberInst = "TAGNO";

        /// <summary>版本信息 / 关于</summary>
        public const string ChemForge = "CHEMFORGE";
    }
}
