using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class LibViewModel : ViewModelBase
    {
        public LibViewModel()
        {
            Program.PluginCards = PluginCards;
        }

        /// <summary>
        /// 插件卡片集合
        /// </summary>
        public ObservableCollection<PluginCard> PluginCards { get; } = new();

        /// <summary>
        /// 搜索框文字
        /// </summary>
        public string? SearchingText { get; set; }
    }
}
