using System;

namespace ChemForge.Utilities
{
    /// <summary>
    /// 通用扩展方法
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// 将角度从度转换为弧度
        /// </summary>
        /// <param name="degrees">角度值（度）</param>
        /// <returns>弧度值</returns>
        public static double ToRadians(this double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}
