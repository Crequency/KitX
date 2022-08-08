using KitX.Web.Rules;
using System;

namespace KitX_Dashboard.Converters
{
    internal class OperatingSystem2Enum
    {
        internal static OperatingSystems GetOSType()
        {
            if (OperatingSystem.IsAndroid())
                return OperatingSystems.Android;
            if (OperatingSystem.IsBrowser())
                return OperatingSystems.Browser;
            if (OperatingSystem.IsFreeBSD())
                return OperatingSystems.FreeBSD;
            if (OperatingSystem.IsIOS())
                return OperatingSystems.IOS;
            if (OperatingSystem.IsLinux())
                return OperatingSystems.Linux;
            if (OperatingSystem.IsMacCatalyst())
                return OperatingSystems.MacCatalyst;
            if (OperatingSystem.IsMacOS())
                return OperatingSystems.MacOS;
            if (OperatingSystem.IsTvOS())
                return OperatingSystems.TvOS;
            if (OperatingSystem.IsWatchOS())
                return OperatingSystems.WatchOS;
            if (OperatingSystem.IsWindows())
                return OperatingSystems.Windows;

            return OperatingSystems.Unknown;
        }
    }
}
