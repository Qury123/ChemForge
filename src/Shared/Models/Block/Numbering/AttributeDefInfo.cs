using System.Collections.Generic;

namespace ChemForge.Models.Block.Numbering
{
    /// <summary>块定义里的属性定义(ATTDEF)信息，用于给用户列属性清单。</summary>
    public sealed class AttributeDefInfo
    {
        public string Tag = string.Empty;
        public string Prompt = string.Empty;

        /// <summary>常量属性：插入时固定，不能改（AttributeReference.IsConstant）。</summary>
        public bool Constant;

        /// <summary>不可见（ATTDEF 的 Invisible 标志）。</summary>
        public bool Invisible;

        /// <summary>预设属性：插入时不提示，可用这个标志做提示。</summary>
        public bool Preset;

        public string Describe()
        {
            string text = Tag;
            if (!string.IsNullOrEmpty(Prompt))
            {
                text += "  (" + Prompt + ")";
            }
            List<string> flags = new List<string>();
            if (Constant) flags.Add("常量");
            if (Invisible) flags.Add("不可见");
            if (Preset) flags.Add("预设");
            if (flags.Count > 0)
            {
                text += "  [" + string.Join("/", flags.ToArray()) + "]";
            }
            return text;
        }
    }
}
