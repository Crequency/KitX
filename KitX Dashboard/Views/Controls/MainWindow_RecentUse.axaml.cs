using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Controls;

namespace KitX_Dashboard.Views.Controls
{
    public partial class MainWindow_RecentUse : UserControl
    {
        private readonly MainWindow_RecentUseViewModel viewModel = new();

        public MainWindow_RecentUse()
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
