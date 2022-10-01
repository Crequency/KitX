using Avalonia.Threading;
using Common.Update.Checker;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using MessageBox.Avalonia;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Component = KitX_Dashboard.Models.Component;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class Settings_UpdateViewModel : ViewModelBase, INotifyPropertyChanged
    {
        internal Settings_UpdateViewModel()
        {
            InitEvents();

            InitCommands();

            InitData();
        }

        /// <summary>
        /// 初始化事件
        /// </summary>
        internal void InitEvents()
        {
            Components.CollectionChanged += (_, _) =>
            {
                CanUpdateCount = $"{Components.Count(x => x.CanUpdate)}";
                PropertyChanged?.Invoke(this, new(nameof(ComponentsCount)));
            };
        }

        /// <summary>
        /// 初始化命令
        /// </summary>
        internal void InitCommands()
        {
            CheckUpdateCommand = new(CheckUpdate);
        }

        /// <summary>
        /// 初始化数据
        /// </summary>
        internal void InitData()
        {

        }

        internal string canUpdateCount = "0";

        /// <summary>
        /// 可以更新的插件数量
        /// </summary>
        internal string CanUpdateCount
        {
            get => canUpdateCount;
            set
            {
                canUpdateCount = value;
                PropertyChanged?.Invoke(this, new(nameof(CanUpdateCount)));
            }
        }

        /// <summary>
        /// 找到的组件数量
        /// </summary>
        internal static int ComponentsCount { get => Components.Count; }

        internal static ObservableCollection<Component> Components { get; } = new();

        /// <summary>
        /// 启用或禁用检查更新命令
        /// </summary>
        /// <param name="enable">启用或禁用</param>
        private void AbleCheckUpdateCommand(bool enable)
        {
            CheckUpdateCommand = enable ? new(CheckUpdate) : null;
            PropertyChanged?.Invoke(this, new(nameof(CheckUpdateCommand)));
        }

        /// <summary>
        /// 启用或禁用更新命令
        /// </summary>
        /// <param name="enable">启用或禁用</param>
        private void AbleUpdateCommand(bool enable)
        {
            UpdateCommand = enable ? new(Update) : null;
            PropertyChanged?.Invoke(this, new(nameof(UpdateCommand)));
        }

        /// <summary>
        /// 检查更新命令
        /// </summary>
        internal DelegateCommand? CheckUpdateCommand { get; set; }

        /// <summary>
        /// 更新命令
        /// </summary>
        internal DelegateCommand? UpdateCommand { get; set; }

        private void CheckUpdate(object _)
        {
            Components.Clear();
            AbleUpdateCommand(false);
            AbleCheckUpdateCommand(false);
            new Thread(() =>
            {
                string? wd = Path.GetFullPath("./");
                string? ld = Path.GetFullPath(GlobalInfo.LanguageFilePath);

                if (wd != null)
                {
                    Checker checker = new Checker()
                        .SetRootDirectory(wd)
                        .SetPerThreadFilesCount(Program.Config.IO.UpdatingCheckPerThreadFilesCount)
                        .SetTransHash2String(true)
                        .AppendIgnoreFolder("Config")
                        .AppendIgnoreFolder("Languages")
                        .AppendIgnoreFolder("Log")
                        .AppendIncludeFile($"{ld}/zh-cn.axaml")
                        .AppendIncludeFile($"{ld}/zh-cnt.axaml")
                        .AppendIncludeFile($"{ld}/en-us.axaml")
                        .AppendIncludeFile($"{ld}/ja-jp.axaml");
                    checker.Scan();
                    checker.Calculate();
                    var result = checker.GetCalculateResult();

                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var item in result)
                        {
                            Components.Add(new()
                            {
                                CanUpdate = false,
                                Name = item.Key,
                                MD5 = item.Value.Item1.ToUpper(),
                                SHA1 = item.Value.Item2.ToUpper()
                            });
                        }

                        AbleUpdateCommand(true);
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        MessageBoxManager.GetMessageBoxStandardWindow("Error",
                            "Can't get working directory!", MessageBox.Avalonia.Enums.ButtonEnum.Ok,
                            MessageBox.Avalonia.Enums.Icon.Warning).Show();
                    });
                }

                Dispatcher.UIThread.Post(() =>
                {
                    AbleCheckUpdateCommand(true);
                });
            }).Start();
        }

        private void Update(object _)
        {

        }

        public new event PropertyChangedEventHandler? PropertyChanged;
    }
}

//                         ,  o ' .
//                        .  -  -  o
//                        o . _ ` '  ` ; .
//       ________________________________  H___
//      P                           MK'98| H ..\
//      |                         _______| H |_\\
//      |                        |.========H -  |
//      B_,-._,-._______________/ |,-._,-._H_,-.)
//      --`-'-`-'------------------`-'-`-'---`-'--MK

