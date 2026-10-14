using System.Collections.Generic;

namespace ChemForge.Models.Block.BlockAttr
{
    /// <summary>
    /// 「上次确定时填写的值」的记忆体。
    ///
    /// 记忆里存的是上一轮勾选的标签集合，以及按块名分别记下的一组值。
    /// 记忆是**跨图纸、跨启动**的，且**三个修改开关的勾选状态不记忆**
    /// （回填时恒把 MODVAL / MODFMT / MODPROP 置 "0"）。
    ///
    /// 存储位置是 ChemForge 自己的配置文件（<c>ConfigManager</c> 提供的目录），
    /// 不占用 AutoCAD 的环境变量空间。
    ///
    /// 数值型字段一律以**字符串**保存：既保留用户原样输入的写法（"3.50" / "3.5"），
    /// 也天然表达「留空 = 不修改」。下拉框字段保存**索引**（静态表）或**文本**（动态表），
    /// 取舍的原因：静态表跨图纸稳定，存索引即可；动态表候选来自当前图纸，
    /// 存索引换张图就会错位，故存文本。
    /// </summary>
    public sealed class BatchAttrChangeMemory
    {
        /// <summary>上次勾选的属性标签（"|" 连接）</summary>
        public IList<string> Tags { get; set; } = new List<string>();

        /// <summary>上次的文字内容（原 NEWVAL）</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>上次的字高（原 NEWH）</summary>
        public string Height { get; set; } = string.Empty;

        /// <summary>上次的宽度因子（原 NEWWF）</summary>
        public string WidthFactor { get; set; } = string.Empty;

        /// <summary>上次的旋转角度（原 NEWROT）</summary>
        public string Rotation { get; set; } = string.Empty;

        /// <summary>上次的倾斜角度（原 NEWOBL）</summary>
        public string Oblique { get; set; } = string.Empty;

        /// <summary>上次的自定义颜色号（原 NEWCOLNUM）</summary>
        public string ColorNumber { get; set; } = string.Empty;

        /// <summary>上次的文字对齐下拉索引（原 JUSTLIST）</summary>
        public int JustifyIndex { get; set; }

        /// <summary>上次的反向下拉索引（原 BKLIST）</summary>
        public int BackwardsIndex { get; set; }

        /// <summary>上次的倒置下拉索引（原 USLIST）</summary>
        public int UpsideDownIndex { get; set; }

        /// <summary>上次的颜色下拉索引（原 COLORLIST）</summary>
        public int ColorIndex { get; set; }

        /// <summary>上次的线宽下拉索引（原 LWTIST）</summary>
        public int LineWeightIndex { get; set; }

        /// <summary>上次的文字样式名（原 STYLELIST，存文本）</summary>
        public string StyleName { get; set; } = string.Empty;

        /// <summary>上次的图层名（原 LAYERLIST，存文本）</summary>
        public string LayerName { get; set; } = string.Empty;

        /// <summary>上次的线型名（原 LTYPELIST，存文本）</summary>
        public string LinetypeName { get; set; } = string.Empty;

        /// <summary>
        /// 把记忆里的下拉索引钳到候选表范围内，越界一律回落为 0（= 不修改）。
        ///
        /// 候选表长度会随版本变化
        /// （例如以后往线宽表里补一档），旧记忆里的索引可能越界，
        /// 那时必须安全回落而不是抛异常。
        /// </summary>
        /// <param name="index">记忆中的索引</param>
        /// <param name="count">候选表长度</param>
        /// <returns>钳位后的索引</returns>
        public static int SafeIndex(int index, int count)
        {
            if (index < 0 || index >= count)
            {
                return 0;
            }

            return index;
        }

        /// <summary>
        /// 在候选表里按显示文本找回索引（找不到返回 0 = 不修改）。
        /// 用于动态下拉的记忆回填：
        /// 上次用的图层这次图纸里没有，就安静地回到「不修改」。
        /// </summary>
        /// <param name="items">候选文本列表（第 0 项是「不修改」）</param>
        /// <param name="text">记忆中的文本</param>
        /// <returns>索引</returns>
        public static int TextToIndex(IList<string> items, string text)
        {
            if (items == null || string.IsNullOrEmpty(text))
            {
                return 0;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], text, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
