using System.Collections.Generic;

namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 面板「确定」后的返回值：勾选的属性标签 + 校验通过的设置。
    /// 即对话框确定之后读出来的
    /// 选中项索引（映射回标签集合）与全局参数表。
    /// </summary>
    public sealed class BatchAttrChangeSelection
    {
        /// <summary>校验通过的设置（含修改范围）</summary>
        public BatchAttrChangeOptions Options { get; set; }

        /// <summary>用户勾选的属性标签</summary>
        public IList<string> SelectedTags { get; set; } = new List<string>();
    }
}
