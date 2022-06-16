using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages;

namespace KitX_Dashboard.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly SettingsViewModel viewModel = new();

        public SettingsPage()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
