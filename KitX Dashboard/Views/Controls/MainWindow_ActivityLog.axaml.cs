using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Controls;

namespace KitX_Dashboard.Views.Controls
{
    public partial class MainWindow_ActivityLog : UserControl
    {
        private readonly MainWindow_ActivityLogViewModel viewModel = new();

        public MainWindow_ActivityLog()
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
