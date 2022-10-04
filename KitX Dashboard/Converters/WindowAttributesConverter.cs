using Avalonia.Controls;

namespace KitX_Dashboard.Converters
{
    internal class WindowAttributesConverter
    {
        /// <summary>
        /// 坐标回正
        /// </summary>
        /// <param name="input">传入的坐标</param>
        /// <param name="isLeft">是否是距左距离</param>
        /// <returns>回正的坐标</returns>
        internal static int PositionCameCenter(int input, bool isLeft, Screens screens) => isLeft
            ? (input == -1 ? (900 - 1280) / 2 : input)
            : (input == -1 ? (900 - 720) / 2 : input);
    }
}
