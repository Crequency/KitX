using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using KitX_Dashboard.ViewModels.Pages;
using System.Threading;

namespace KitX_Dashboard.Views.Pages
{
    public partial class LibPage : UserControl
    {
        private readonly LibViewModel libViewModel = new();

        public LibPage()
        {
            InitializeComponent();

            DataContext = libViewModel;

            //if (Program.ViewModelPool.ContainsKey("LibViewModel"))
            //    Program.ViewModelPool["LibViewModel"] = libViewModel;
            //else Program.ViewModelPool.Add("LibViewModel", libViewModel);

            //Program.libPage = this;

            //Program.DirectControls.Add("LibViewWrapPanel", LibViewWrapPanel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
