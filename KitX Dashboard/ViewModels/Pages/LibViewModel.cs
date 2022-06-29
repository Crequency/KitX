using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class LibViewModel : ViewModelBase
    {
        public LibViewModel()
        {
            for (int i = 0; i < 20; i++)
                PluginCards.Add(new());
        }

        public static List<PluginCard> PluginCards { get; } = new();
    }
}
