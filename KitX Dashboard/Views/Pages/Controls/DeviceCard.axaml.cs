using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX.Web.Rules;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class DeviceCard : UserControl
    {
        internal readonly DeviceCardViewModel viewModel = new();

        public DeviceCard()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        public DeviceCard(DeviceInfoStruct deviceInfo)
        {
            InitializeComponent();

            viewModel.DeviceInfo = deviceInfo;

            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
