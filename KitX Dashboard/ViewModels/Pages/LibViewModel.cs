using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class LibViewModel : ViewModelBase
    {
        public LibViewModel()
        {
            //for (int i = 0; i < 20; i++)
            //    Program.PluginCards.Add(new());

            //for (int i = 0; i < 20; i++)
            //    PluginCards.Add(new());

            Program.PluginCards = PluginCards;
        }

        //public static List<PluginCard> PluginCards { get => Program.PluginCards; }

        public ObservableCollection<PluginCard> PluginCards { get; } = new();

        /// <summary>
        /// 搜索框文字
        /// </summary>
        public string SearchingText { get; set; }
    }
}
