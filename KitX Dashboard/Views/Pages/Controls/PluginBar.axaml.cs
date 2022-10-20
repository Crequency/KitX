using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.Models;
using KitX_Dashboard.ViewModels.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class PluginBar : UserControl
    {
        private readonly PluginBarViewModel viewModel = new();

        public PluginBar()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        public PluginBar(Plugin plugin, ref ObservableCollection<PluginBar> pluginBars)
        {
            InitializeComponent();

            viewModel.PluginDetail = plugin;
            viewModel.PluginBars = pluginBars;
            viewModel.PluginBar = this;

            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
