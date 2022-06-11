using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;

namespace KitX_Dashboard.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();

            DataContext = new ViewModels.SettingsViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void ThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {

        }

        private readonly List<Key> keyqueue = new();

        private readonly List<Key> rightkey = new()
        {
            Key.W, Key.W, Key.S, Key.S, Key.A, Key.D, Key.A, Key.D, Key.B, Key.A
        };

        private void AppNameButtonKeyDown(object sender, KeyEventArgs e)
        {

            if (keyqueue.Count >= 10)
                keyqueue.RemoveAt(0);
            keyqueue.Add(e.Key);

            //string tip = "";
            //foreach (Key item in keyqueue)
            //{
            //    tip += $"{item}\n";
            //}
            //MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow("KeyQueue", tip).Show();

            if (keyqueue.Count == 10)
            {
                bool pass = true;
                for (int i = 0; i < keyqueue.Count; ++i)
                    if (keyqueue[i] != (rightkey[i]))
                    {
                        pass = false;
                        break;
                    }
                if (pass)
                {
                    //TODO: 彩蛋内容
                }
            }
        }
    }
}
