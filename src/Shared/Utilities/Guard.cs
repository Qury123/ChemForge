using System;

namespace ChemForge.Utilities
{
    /// <summary>
    /// 参数验证辅助类
    /// </summary>
    public static class Guard
    {
        /// <summary>
        /// 验证参数不为空
        /// </summary>
        /// <typeparam name="T">参数类型</typeparam>
        /// <param name="value">参数值</param>
        /// <param name="parameterName">参数名</param>
        /// <exception cref="ArgumentNullException">当参数为空时抛出</exception>
        public static void NotNull<T>(T value, string parameterName) where T : class
        {
            if (value == null)
                throw new ArgumentNullException(parameterName);
        }

        /// <summary>
        /// 验证字符串参数不为空或空白
        /// </summary>
        /// <param name="value">字符串值</param>
        /// <param name="parameterName">参数名</param>
        /// <exception cref="ArgumentException">当字符串为空或空白时抛出</exception>
        public static void NotNullOrEmpty(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("参数不能为空或空白。", parameterName);
        }
    }
}
