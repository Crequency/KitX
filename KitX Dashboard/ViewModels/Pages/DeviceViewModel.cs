using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;

#pragma warning disable CS0108 // 成员隐藏继承的成员；缺少关键字 new

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class DeviceViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public DeviceViewModel()
        {
            DeviceCards.CollectionChanged += (_, _) =>
            {
                NoDevice_TipHeight = DeviceCards.Count == 0 ? 300 : 0;
            };
        }

        internal double noDevice_TipHeight = DeviceCards.Count == 0 ? 300 : 0;

        internal double NoDevice_TipHeight
        {
            get => noDevice_TipHeight;
            set
            {
                noDevice_TipHeight = value;
                PropertyChanged?.Invoke(this, new(nameof(NoDevice_TipHeight)));
            }
        }

        //internal static MaterialIconKind SystemIcon => Converters.OperatingSystem2Enum.GetOSType() switch
        //{
        //    OperatingSystems.Android => MaterialIconKind.Android,
        //    OperatingSystems.Browser => MaterialIconKind.MicrosoftEdge,
        //    OperatingSystems.FreeBSD => MaterialIconKind.Freebsd,
        //    OperatingSystems.IOS => MaterialIconKind.AppleIos,
        //    OperatingSystems.Linux => MaterialIconKind.Linux,
        //    OperatingSystems.MacCatalyst => MaterialIconKind.AppleFinder,
        //    OperatingSystems.MacOS => MaterialIconKind.AppleFinder,
        //    OperatingSystems.TvOS => MaterialIconKind.Apple,
        //    OperatingSystems.WatchOS => MaterialIconKind.Apple,
        //    OperatingSystems.Windows => MaterialIconKind.MicrosoftWindows,
        //    OperatingSystems.Unknown => MaterialIconKind.QuestionMarkCircle,
        //    _ => MaterialIconKind.QuestionMarkCircle,
        //};

        //internal static string SystemName => Converters.OperatingSystem2Enum.GetOSType() switch
        //{
        //    OperatingSystems.Android => "Android",
        //    OperatingSystems.Browser => "Browser",
        //    OperatingSystems.FreeBSD => "FreeBSD",
        //    OperatingSystems.IOS => "IOS",
        //    OperatingSystems.Linux => "Linux",
        //    OperatingSystems.MacCatalyst => "MacCatalyst",
        //    OperatingSystems.MacOS => "MacOS",
        //    OperatingSystems.TvOS => "TvOS",
        //    OperatingSystems.WatchOS => "WatchOS",
        //    OperatingSystems.Windows => "Windows",
        //    OperatingSystems.Unknown => "Unknown",
        //    _ => "Unknown",
        //};

        //internal static string SystemVersion => Environment.OSVersion.VersionString;

        //internal static string SystemArchitecture => Environment.Is64BitOperatingSystem ? "x64" : "x32";

        //internal static string DeviceInfo => $"{SystemVersion} - {SystemArchitecture}";

        //internal string cpu_load = string.Empty, ram_load = string.Empty,
        //                net_upload = string.Empty, net_download = string.Empty;

        //internal string CPU_Load
        //{
        //    get => cpu_load;
        //    set
        //    {
        //        cpu_load = value;
        //        PropertyChanged?.Invoke(this, new(nameof(CPU_Load)));
        //    }
        //}

        //internal string RAM_Load
        //{
        //    get => ram_load;
        //    set
        //    {
        //        ram_load = value;
        //        PropertyChanged?.Invoke(this, new(nameof(RAM_Load)));
        //    }
        //}

        //internal string NET_Upload
        //{
        //    get => net_upload;
        //    set
        //    {
        //        net_upload = value;
        //        PropertyChanged?.Invoke(this, new(nameof(NET_Upload)));
        //    }
        //}

        //internal string NET_Download
        //{
        //    get => net_download;
        //    set
        //    {
        //        net_download = value;
        //        PropertyChanged?.Invoke(this, new(nameof(NET_Download)));
        //    }
        //}

        internal static ObservableCollection<DeviceCard> DeviceCards => Program.DeviceCards;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS0108 // 成员隐藏继承的成员；缺少关键字 new

//        ___________________________________
//       |.-.--.--.--.--.--.--.--.--.--.--.-.|
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||_|__|__|__|__|__|__|__|__|__|__|_||
//       [_&gt;_______________________________&lt;_]
//       ||"|""|""|""|""|""|""|""|""|""|""|"||
//       || |  |  |  |  |  |  |  |  |  |  | ||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//       ||(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)(_)||
//  aac  |'-'--'--'--'--'--'--'--'--'--'--'-'|
//       `"""""""""""""""""""""""""""""""""""`
