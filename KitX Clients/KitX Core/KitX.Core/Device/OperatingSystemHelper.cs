using System;
using KitX.Shared.CSharp.Device;

namespace KitX.Core.Device;

/// <summary>
/// Operating system utilities for Core
/// Phase 5: Simplified version without UI dependencies
/// </summary>
internal static class OperatingSystemHelper
{
    /// <summary>
    /// Gets the current operating system type
    /// </summary>
    /// <returns>Operating system type</returns>
    public static OperatingSystems GetOSType()
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
