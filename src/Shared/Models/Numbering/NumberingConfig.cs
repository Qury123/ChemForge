namespace ChemForge.Models.Numbering
{
    /// <summary>
    /// 自动编号配置
    /// </summary>
    public sealed class NumberingConfig
    {
        /// <summary>
        /// 获取或设置编号前缀
        /// </summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置编号后缀
        /// </summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>
        /// 获取或设置起始编号
        /// </summary>
        public int StartNumber { get; set; } = 1;

        /// <summary>
        /// 获取或设置编号增量
        /// </summary>
        public int Increment { get; set; } = 1;

        /// <summary>
        /// 获取或设置编号位数
        /// </summary>
        public int Digits { get; set; } = 2;
    }
}
