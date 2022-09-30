using Avalonia.Threading;
using KitX_Dashboard.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Common.Update.Checker;
using Component = KitX_Dashboard.Models.Component;
using System.IO;
using MessageBox.Avalonia;
using Serilog;
using System.Text;
using System.Text.Unicode;
using System.Text.Encodings.Web;

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
                CanUpdateCount = $"{Components.Count}";
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

        internal static ObservableCollection<Component> Components { get; } = new();

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
            new Thread(() =>
            {
                string? wd = Path.GetFullPath(Environment.CurrentDirectory);
                string? ld = $"{wd}/Languages/";

                if (wd != null)
                {
                    Checker checker = new Checker()
                        .SetRootDirectory(wd)
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
                        MessageBoxManager.GetMessageBoxStandardWindow("Success",
                            "Checking update finished!", MessageBox.Avalonia.Enums.ButtonEnum.Ok,
                            MessageBox.Avalonia.Enums.Icon.Warning).Show();

                        foreach (var item in result)
                        {
                            Components.Add(new()
                            {
                                CanUpdate = false,
                                Name = item.Key,
                                MD5 = item.Value.Item1,
                                SHA1 = item.Value.Item2
                            });
                        }

                        UpdateCommand = new(Update);
                        PropertyChanged?.Invoke(this, new(nameof(UpdateCommand)));
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

