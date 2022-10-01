using Avalonia.Controls;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class RepoPageViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public RepoPageViewModel()
        {

            InitEvents();

            InitCommands();

            SearchingText = "";
            PluginsCount = PluginBars.Count.ToString();

            PluginBars.CollectionChanged += (_, _) =>
            {
                PluginsCount = PluginBars.Count.ToString();
                NoPlugins_TipHeight = PluginBars.Count == 0 ? 300 : 0;
            };
        }

        /// <summary>
        /// 初始化事件
        /// </summary>
        private void InitEvents()
        {
            EventHandlers.ConfigSettingsChanged += () =>
            {
                ImportButtonVisibility = Program.Config.App.DeveloperSetting;
            };
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitCommands()
        {
            ImportPluginCommand = new(ImportPlugin);
            RefreshPluginsCommand = new(RefreshPlugins);
        }

        internal string SearchingText { get; set; }

        internal string pluginsCount = "0";

        internal string PluginsCount
        {
            get => pluginsCount;
            set
            {
                pluginsCount = value;
                PropertyChanged?.Invoke(this, new(nameof(PluginsCount)));
            }
        }

        internal double noPlugins_tipHeight = 300;

        internal double NoPlugins_TipHeight
        {
            get => noPlugins_tipHeight;
            set
            {
                noPlugins_tipHeight = value;
                PropertyChanged?.Invoke(this, new(nameof(NoPlugins_TipHeight)));
            }
        }

        internal bool ImportButtonVisibility
        {
            get => Program.Config.App.DeveloperSetting;
            set
            {
                Program.Config.App.DeveloperSetting = value;
                PropertyChanged?.Invoke(this, new(nameof(ImportButtonVisibility)));
            }
        }

        internal ObservableCollection<PluginBar> pluginBars = new();

        internal ObservableCollection<PluginBar> PluginBars
        {
            get => pluginBars;
            set => pluginBars = value;
        }

        internal DelegateCommand? ImportPluginCommand { get; set; }

        internal DelegateCommand? RefreshPluginsCommand { get; set; }

        /// <summary>
        /// 导入插件
        /// </summary>
        /// <param name="_"></param>
        internal async void ImportPlugin(object win)
        {
            OpenFileDialog ofd = new();
            ofd.Filters?.Add(new()
            {
                Name = "KitX Extensions Packages",
                Extensions = { "kxp" }
            });
            ofd.AllowMultiple = true;
            string[]? files = await ofd.ShowAsync(win as Window);
            if (files != null && files?.Length > 0)
            {
                new Thread(() =>
                {
                    PluginsManager.ImportPlugin(files, true);
                }).Start();
            }
        }

        /// <summary>
        /// 刷新插件
        /// </summary>
        /// <param name="_"></param>
        internal void RefreshPlugins(object _)
        {
            //LiteDatabase? pgdb = Program.PluginsDataBase;

            PluginBars.Clear();
            foreach (var item in Program.PluginsList.Plugins)
            {
                PluginBars.Add(new(item, ref pluginBars));
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。

//
//            ~                  ~
//      *                   *                *       *
//                   *               *
//   ~       *                *         ~    *          
//               *       ~        *              *   ~
//                   )         (         )              *
//     *    ~     ) (_)   (   (_)   )   (_) (  *
//            *  (_) # ) (_) ) # ( (_) ( # (_)       *
//               _#.-#(_)-#-(_)#(_)-#-(_)#-.#_    
//   *         .' #  # #  #  # # #  #  # #  # `.   ~     *
//            :   #    #  #  #   #  #  #    #   :   
//     ~      :.       #     #   #     #       .:      *
//         *  | `-.__                     __.-' | *
//            |      `````"""""""""""`````      |         *
//      *     |         | ||\ |~)|~)\ /         |    
//            |         |~||~\|~ |~  |          |       ~
//    ~   *   |                                 | * 
//            |      |~)||~)~|~| ||~\|\ \ /     |         *
//    *    _.-|      |~)||~\ | |~|| /|~\ |      |-._  
//       .'   '.      ~            ~           .'   `.  *
//  1117 :      `-.__                     __.-'      :
//        `.         `````"""""""""""`````         .'
//          `-.._                             _..-'
//               `````""""-----------""""`````
// 
//
