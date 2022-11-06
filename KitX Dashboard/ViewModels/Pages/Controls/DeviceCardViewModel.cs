using Avalonia;
using Avalonia.Controls;
using KitX.Web.Rules;
using Material.Icons;
using System.ComponentModel;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class DeviceCardViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public DeviceCardViewModel()
        {

        }

        internal DeviceInfoStruct deviceInfo = new();

        /// <summary>
        /// 检查字符串是否为空, 如果为空则根据 key 返回提示
        /// </summary>
        /// <param name="str">字符串</param>
        /// <param name="key">提示语言文件键值</param>
        /// <returns>字符串或者提示</returns>
        internal static string CheckString(string str, string key)
        {
            if (str.Equals("") || str.Equals(string.Empty) || str == null)
            {
                if (Application.Current != null
                    && Application.Current.TryFindResource(key, out object? result))
                    if (result != null) return (string)result;
                    else return "null";
                else return "null";
            }
            else
            {
                return str;
            }
        }

        internal DeviceInfoStruct DeviceInfo
        {
            get => deviceInfo;
            set
            {
                deviceInfo = value;
                DeviceName = DeviceInfo.DeviceName;
                DeviceMacAddress = CheckString(DeviceInfo.DeviceMacAddress, "Text_Device_NoMacAddress");
                LastOnlineTime = DeviceInfo.SendTime.ToString("yyyy.MM.dd HH:mm:ss");
                DeviceVersion = DeviceInfo.DeviceOSVersion;
                DeviceOSKind = DeviceInfo.DeviceOSType switch
                {
                    OperatingSystems.Android => MaterialIconKind.Android,
                    OperatingSystems.Browser => MaterialIconKind.MicrosoftEdge,
                    OperatingSystems.FreeBSD => MaterialIconKind.Freebsd,
                    OperatingSystems.IOS => MaterialIconKind.AppleIos,
                    OperatingSystems.Linux => MaterialIconKind.Linux,
                    OperatingSystems.MacCatalyst => MaterialIconKind.AppleFinder,
                    OperatingSystems.MacOS => MaterialIconKind.AppleFinder,
                    OperatingSystems.TvOS => MaterialIconKind.Apple,
                    OperatingSystems.WatchOS => MaterialIconKind.Apple,
                    OperatingSystems.Windows => MaterialIconKind.MicrosoftWindows,
                    OperatingSystems.Unknown => MaterialIconKind.QuestionMarkCircle,
                    _ => MaterialIconKind.QuestionMarkCircle,
                };
                IPv4 = $"{DeviceInfo.IPv4}:{DeviceInfo.ServingPort}";
                IPv6 = DeviceInfo.IPv6;
                PluginsCount = DeviceInfo.PluginsCount.ToString();

                PropertyChanged?.Invoke(this, new(nameof(DeviceName)));
                PropertyChanged?.Invoke(this, new(nameof(DeviceMacAddress)));
                PropertyChanged?.Invoke(this, new(nameof(LastOnlineTime)));
                PropertyChanged?.Invoke(this, new(nameof(DeviceVersion)));
                PropertyChanged?.Invoke(this, new(nameof(DeviceOSKind)));
                PropertyChanged?.Invoke(this, new(nameof(IPv4)));
                PropertyChanged?.Invoke(this, new(nameof(IPv6)));
                PropertyChanged?.Invoke(this, new(nameof(PluginsCount)));
            }
        }

        internal string? DeviceName { get; set; }

        internal string? DeviceMacAddress { get; set; }

        internal string? LastOnlineTime { get; set; }

        internal string? DeviceVersion { get; set; }

        internal MaterialIconKind? DeviceOSKind { get; set; }

        internal string? IPv4 { get; set; }

        internal string? IPv6 { get; set; }

        internal string? PluginsCount { get; set; }


        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//
//      /\
//      ||_____-----_____-----_____
//      ||   O                  O  \
//      ||    O\\    ___    //O    /
//      ||       \\ /   \//        \
//      ||         |_O O_|         /
//      ||          ^ | ^          \
//      ||        // UUU \\        /
//      ||    O//            \\O   \
//      ||   O                  O  /
//      ||_____-----_____-----_____\
//      ||
//      ||.
//
