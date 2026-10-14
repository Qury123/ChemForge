using System;
using System.Collections.Generic;
using ChemForge.Models.Numbering;
using ChemForge.Utilities;

namespace ChemForge.Core.Numbering
{
    /// <summary>
    /// 自动编号引擎 - 负责按规则生成唯一编号
    /// </summary>
    public sealed class NumberingEngine
    {
        /// <summary>
        /// 根据配置生成指定数量的编号
        /// </summary>
        /// <param name="config">编号配置</param>
        /// <param name="count">要生成的编号数量</param>
        /// <returns>编号列表</returns>
        /// <exception cref="ArgumentNullException">当 config 为空时抛出</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// 当 count 小于等于 0、config.Increment 等于 0 或 config.Digits 小于等于 0 时抛出
        /// </exception>
        public IEnumerable<string> GenerateNumbers(NumberingConfig config, int count)
        {
            Guard.NotNull(config, nameof(config));
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "生成数量必须大于零。");
            if (config.Increment == 0)
                throw new ArgumentOutOfRangeException($"{nameof(config)}.{nameof(config.Increment)}", config.Increment, "编号增量不能为零，否则会生成重复编号。");
            if (config.Digits <= 0)
                throw new ArgumentOutOfRangeException($"{nameof(config)}.{nameof(config.Digits)}", config.Digits, "编号位数必须大于零，否则无法格式化编号。");

            var numbers = new List<string>(count);
            var current = config.StartNumber;

            for (var i = 0; i < count; i++)
            {
                numbers.Add(FormatNumber(config, current));
                current += config.Increment;
            }

            Logger.Info($"NumberingEngine.GenerateNumbers 生成 {count} 个编号。");
            return numbers;
        }

        private static string FormatNumber(NumberingConfig config, int number)
        {
            var formatted = number.ToString($"D{config.Digits}");
            return $"{config.Prefix}{formatted}{config.Suffix}";
        }
    }
}
