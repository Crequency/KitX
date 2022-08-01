using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Controls;

namespace KitX_Dashboard.Views.Controls
{
    public partial class MainWindow_Count : UserControl
    {
        private readonly MainWindow_CountViewModel viewModel = new();

        public MainWindow_Count()
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
