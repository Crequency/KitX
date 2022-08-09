using KitX.Web.Rules;
using Material.Icons;
using System.ComponentModel;

#pragma warning disable CS0108 // 成员隐藏继承的成员；缺少关键字 new

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class DeviceCardViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public DeviceCardViewModel()
        {

        }

        internal DeviceInfoStruct deviceInfo = new();

        internal DeviceInfoStruct DeviceInfo
        {
            get => deviceInfo;
            set
            {
                deviceInfo = value;
                DeviceName = DeviceInfo.DeviceName;
                DeviceMacAddress = DeviceInfo.DeviceMacAddress;
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
                IPv4 = DeviceInfo.IPv4;
                IPv6 = DeviceInfo.IPv6;
                PropertyChanged?.Invoke(this, new(nameof(DeviceName)));
                PropertyChanged?.Invoke(this, new(nameof(DeviceMacAddress)));
                PropertyChanged?.Invoke(this, new(nameof(LastOnlineTime)));
                PropertyChanged?.Invoke(this, new(nameof(DeviceVersion)));
                PropertyChanged?.Invoke(this, new(nameof(DeviceOSKind)));
                PropertyChanged?.Invoke(this, new(nameof(IPv4)));
                PropertyChanged?.Invoke(this, new(nameof(IPv6)));
            }
        }

        internal string? DeviceName { get; set; }

        internal string? DeviceMacAddress { get; set; }

        internal string? LastOnlineTime { get; set; }

        internal string? DeviceVersion { get; set; }

        internal MaterialIconKind? DeviceOSKind { get; set; }

        internal string? IPv4 { get; set; }

        internal string? IPv6 { get; set; }


        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS0108 // 成员隐藏继承的成员；缺少关键字 new
