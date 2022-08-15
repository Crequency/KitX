using Avalonia.Controls;
using KitX_Dashboard.Commands;
using System.ComponentModel;

#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels.Pages
{
    internal class RepoPageViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public RepoPageViewModel()
        {

            InitEvents();

            InitCommands();
        }

        /// <summary>
        /// 初始化事件
        /// </summary>
        private void InitEvents()
        {
            Models.EventHandlers.ConfigSettingsChanged += () =>
            {
                ImportButtonVisibility = Program.GlobalConfig.App.DeveloperSetting;
            };
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        private void InitCommands()
        {
            ImportPluginCommand = new(ImportPlugin);
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
            get => Program.GlobalConfig.App.DeveloperSetting;
            set
            {
                Program.GlobalConfig.App.DeveloperSetting = value;
                PropertyChanged?.Invoke(this, new(nameof(ImportButtonVisibility)));
            }
        }

        internal DelegateCommand? ImportPluginCommand { get; set; }

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
            string[]? files = await ofd.ShowAsync(win as Window);
            if(files?.Length > 0)
            {

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
