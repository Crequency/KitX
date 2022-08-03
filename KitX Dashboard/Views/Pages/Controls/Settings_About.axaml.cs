using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages.Controls;
using System.Collections.Generic;

namespace KitX_Dashboard.Views.Pages.Controls
{
    public partial class Settings_About : UserControl
    {
        private readonly Settings_AboutViewModel viewModel = new();

        public Settings_About()
        {
            InitializeComponent();

            DataContext = viewModel;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private readonly List<Key> keyqueue = new();

        private readonly List<Key> rightkey = new()
        {
            Key.W, Key.W, Key.S, Key.S, Key.A, Key.D, Key.A, Key.D, Key.B, Key.A
        };

        private void AppNameButtonKeyDown(object sender, KeyEventArgs e)
        {
            if (viewModel.clickCount >= 7)
            {
                if (keyqueue.Count >= 10)
                    keyqueue.RemoveAt(0);
                keyqueue.Add(e.Key);

                if (keyqueue.Count == 10)
                {
                    bool pass = true;
                    for (int i = 0; i < keyqueue.Count; ++i)
                        if (keyqueue[i] != (rightkey[i]))
                        {
                            pass = false;
                            break;
                        }
                    viewModel.AuthorsListVisibility = pass;
                }
            }
        }
    }
}
