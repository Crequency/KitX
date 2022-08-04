using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Material.Icons;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class DeviceViewModel : ViewModelBase
    {
        public DeviceViewModel()
        {

        }

        internal double NoDevice_TipHeight { get; set; } = 300;

        internal static MaterialIconKind SystemIcon
        {
            get
            {
                if (OperatingSystem.IsAndroid())
                    return MaterialIconKind.Android;
                if (OperatingSystem.IsBrowser())
                    return MaterialIconKind.MicrosoftEdge;
                if (OperatingSystem.IsFreeBSD())
                    return MaterialIconKind.Freebsd;
                if (OperatingSystem.IsIOS())
                    return MaterialIconKind.AppleIos;
                if (OperatingSystem.IsLinux())
                    return MaterialIconKind.Linux;
                if (OperatingSystem.IsMacCatalyst())
                    return MaterialIconKind.AppleFinder;
                if (OperatingSystem.IsMacOS())
                    return MaterialIconKind.AppleFinder;
                if (OperatingSystem.IsTvOS())
                    return MaterialIconKind.Apple;
                if (OperatingSystem.IsWatchOS())
                    return MaterialIconKind.Apple;
                if (OperatingSystem.IsWindows())
                    return MaterialIconKind.MicrosoftWindows;

                return MaterialIconKind.QuestionMarkCircle;

                //return Environment.OSVersion.Platform switch
                //{
                //    PlatformID.Win32S => MaterialIconKind.MicrosoftWindowsClassic,
                //    PlatformID.Win32Windows => MaterialIconKind.MicrosoftWindows,
                //    PlatformID.Win32NT => MaterialIconKind.MicrosoftWindows,
                //    PlatformID.WinCE => MaterialIconKind.MicrosoftWindows,
                //    PlatformID.Unix => MaterialIconKind.Xi,
                //    PlatformID.Xbox => MaterialIconKind.MicrosoftXbox,
                //    PlatformID.MacOSX => MaterialIconKind.AppleFinder,
                //    PlatformID.Other => MaterialIconKind.QuestionMark,
                //    _ => MaterialIconKind.QuestionMark,
                //};
            }
        }

        internal static string SystemName
        {
            get
            {
                if (OperatingSystem.IsAndroid())
                    return "Android";
                if (OperatingSystem.IsBrowser())
                    return "Browser";
                if (OperatingSystem.IsFreeBSD())
                    return "FreeBSD";
                if (OperatingSystem.IsIOS())
                    return "IOS";
                if (OperatingSystem.IsLinux())
                    return "Linux";
                if (OperatingSystem.IsMacCatalyst())
                    return "Mac Catalyst";
                if (OperatingSystem.IsMacOS())
                    return "MacOS";
                if (OperatingSystem.IsTvOS())
                    return "TvOS";
                if (OperatingSystem.IsWatchOS())
                    return "WatchOS";
                if (OperatingSystem.IsWindows())
                    return "Windows";

                return "Unknown";

                //PlatformID.Win32S => "Microsoft Windows",
                //PlatformID.Win32Windows => "Microsoft Windows",
                //PlatformID.Win32NT => "Microsoft Windows",
                //PlatformID.WinCE => "Microsoft Windows",
                //PlatformID.Unix => "Unix",
                //PlatformID.Xbox => "Xbox",
                //PlatformID.MacOSX => "MacOSX",
                //PlatformID.Other => "Unknown",
                //_ => "Unknown",
            }
        }

        internal static string SystemVersion => Environment.OSVersion.VersionString;

        internal static string SystemArchitecture => Environment.Is64BitOperatingSystem ? "x64" : "x32";

        internal static string DeviceInfo => $"{SystemVersion} - {SystemArchitecture}";
    }
}
