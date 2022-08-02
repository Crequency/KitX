using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages;

namespace KitX_Dashboard.Views.Pages
{
    public partial class MarketPage : UserControl
    {
        private readonly MarketViewModel viewModel = new();

        public MarketPage()
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
