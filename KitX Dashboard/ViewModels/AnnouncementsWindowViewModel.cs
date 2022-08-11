using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Views;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.ViewModels
{
    internal class AnnouncementsWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public AnnouncementsWindowViewModel()
        {
            InitCommands();
        }

        private void InitCommands()
        {
            ConfirmReceivedCommand = new(ConfirmReceived);
            ConfirmReceivedAllCommand = new(ConfirmReceivedAll);
        }

        internal static double Window_Width
        {
            get => Program.GlobalConfig.Config_Windows.Config_AnnouncementWindow.Window_Width;
            set => Program.GlobalConfig.Config_Windows.Config_AnnouncementWindow.Window_Width = value;
        }

        internal static double Window_Height
        {
            get => Program.GlobalConfig.Config_Windows.Config_AnnouncementWindow.Window_Height;
            set => Program.GlobalConfig.Config_Windows.Config_AnnouncementWindow.Window_Height = value;
        }

        private NavigationViewItem? selectedMenuItem;

        internal NavigationViewItem? SelectedMenuItem
        {
            get => selectedMenuItem;
            set
            {
                selectedMenuItem = value;
                Markdown = Sources[SelectedMenuItem.Content.ToString()];
                PropertyChanged?.Invoke(this, new(nameof(SelectedMenuItem)));
            }
        }

        private string markdown = string.Empty;

        internal string Markdown
        {
            get => markdown;
            set
            {
                markdown = value;
                PropertyChanged?.Invoke(this, new(nameof(Markdown)));
            }
        }

        internal List<NavigationViewItem> MenuItems { get; set; } = new();

        private Dictionary<string, string> src = new();

        internal Dictionary<string, string> Sources
        {
            get => src;
            set
            {
                src = value;
                MenuItems.Clear();
                foreach (var item in Sources)
                {
                    MenuItems.Add(new()
                    {
                        Content = item.Key
                    });
                }
                SelectedMenuItem = MenuItems.First();
            }
        }

        internal AnnouncementsWindow? Window { get; set; }

        internal List<string>? Readed { get; set; }

        /// <summary>
        /// 确认收到命令
        /// </summary>
        internal DelegateCommand? ConfirmReceivedCommand { get; set; }

        /// <summary>
        /// 确认收到命令
        /// </summary>
        internal DelegateCommand? ConfirmReceivedAllCommand { get; set; }

        private async void ConfirmReceived(object _)
        {
            if (!Readed.Contains(SelectedMenuItem.Content.ToString()))
                Readed.Add(SelectedMenuItem.Content.ToString());

            string ConfigFilePath = Path.GetFullPath(GlobalInfo.AnnouncementsJsonPath);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            await File.WriteAllTextAsync(ConfigFilePath, JsonSerializer.Serialize(Readed, options));

            bool finded = false;
            foreach (var item in MenuItems)
            {
                if (finded)
                {
                    SelectedMenuItem = item;
                    break;
                }
                if (item == SelectedMenuItem)
                    finded = true;
            }
        }

        private async void ConfirmReceivedAll(object _)
        {
            foreach (var item in MenuItems)
            {
                if (!Readed.Contains(item.Content.ToString()))
                    Readed.Add(item.Content.ToString());
            }

            string ConfigFilePath = Path.GetFullPath(GlobalInfo.AnnouncementsJsonPath);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true,
                IncludeFields = true,
            };

            await File.WriteAllTextAsync(ConfigFilePath, JsonSerializer.Serialize(Readed, options));

            Window.Close();
        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。

//
//        .         .      /\      .:  *       .          .              .
//                  *    .'  `.      .     .     *      .                  .
//   :             .    /      \  _ .________________  .                    .
//        |            `.+-~~-+.'/.' `.^^^^^^^^\~~~~~\.                      .
//  .    -*-   . .       |u--.|  /     \~~~~~~~|~~~~~|
//        |              |   u|.'       `." "  |" " "|                        .
//     :            .    |.u-./ _..---.._ \" " | " " |
//    -*-            *   |    ~-|U U U U|-~____L_____L_                      .
//     :         .   .   |.-u.| |..---..|"//// ////// /\       .            .
//           .  *        |u   | |       |// /// // ///==\     / \          .
//  .          :         |.--u| |..---..|//////~\////====\   /   \       .
//       .               | u  | |       |~~~~/\u |~~|++++| .`+~~~+'  .
//                       |.-|~U~U~|---..|u u|u | |u ||||||   |  U|
//                    /~~~~/-\---.'     |===|  |u|==|++++|   |   |
//           aaa      |===| _ | ||.---..|u u|u | |u ||HH||U~U~U~U~|        aa@@
//      aaa@@@@@@aa   |===|||||_||      |===|_.|u|_.|+HH+|_/_/_/_/aa    a@@@@@@
//  aa@@@@@@@@@@@@@@a |~~|~~~~\---/~-.._|--.---------.~~~`.__ _.@@@@@@a    ~~~~
//    ~~~~~~    ~~~    \_\\ \  \/~ //\  ~,~|  __   | |`.   :||  ~~~~
//                      a\`| `   _//  | / _| || |  | `.'  ,''|     aa@@@@@@@a
//  aaa   aaaa       a@@@@\| \  //'   |  // \`| |  `.'  .' | |  aa@@@@@@@@@@@@@
// @@@@@a@@@@@@a      ~~~~~ \\`//| | \ \//   \`  .-'  .' | '/      ~~~~~~~  ~~
// @S.C.E.S.W.@@@@a          \// |.`  ` ' /~  :-'   .'|  '/~aa
// ~~~~~~ ~~~~~~         a@@@|   \\ |   // .'    .'| |  |@@@@@@a
//                     a@@@@@@@\   | `| ''.'     .' | ' /@@@@@@@@@a       _
//                                                                      _| |_
//
