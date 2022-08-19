using KitX_Dashboard.Commands;
using KitX_Dashboard.Models;
using KitX_Dashboard.Services;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Text;

namespace KitX_Dashboard.ViewModels.Pages.Controls
{
    internal class PluginBarViewModel : ViewModelBase
    {
        public PluginBarViewModel()
        {

            InitCommands();

        }


        internal void InitCommands()
        {
            ViewDetailsCommand = new(ViewDetails);
            RemoveCommand = new(Remove);
            DeleteCommand = new(Delete);
            LaunchCommand = new(Launch);
        }

        internal PluginBar? PluginBar { get; set; }

        internal Plugin? PluginDetail { get; set; }

        internal ObservableCollection<PluginBar>? PluginBars { get; set; }

        public Bitmap IconDisplay
        {
            get
            {
                try
                {
                    if (PluginDetail != null)
                    {
                        byte[] src = Convert.FromBase64String(PluginDetail.PluginDetails.IconInBase64);
                        using var ms = new MemoryStream(src);
                        return new(ms);
                    }
                    else return new("./Assets/KitX-Background.png");
                }
                catch (Exception e)
                {
                    Program.LocalLogger.Log("Logger_Error",
                        $"Icon transform error from base64 to byte[] or " +
                        $"create bitmap from MemoryStream error\n" +
                        $"{new StringBuilder().Append(' ', 20)}{e.Message}");
                    return new("./Assets/KitX-Background.png");
                }
            }
        }

        internal DelegateCommand? ViewDetailsCommand { get; set; }

        internal DelegateCommand? RemoveCommand { get; set; }

        internal DelegateCommand? DeleteCommand { get; set; }

        internal DelegateCommand? LaunchCommand { get; set; }

        /// <summary>
        /// 查看详细信息
        /// </summary>
        /// <param name="_"></param>
        internal void ViewDetails(object _)
        {

        }

        /// <summary>
        /// 移除
        /// </summary>
        /// <param name="_"></param>
        internal void Remove(object _)
        {
            if (PluginDetail != null && PluginBar != null)
            {
                PluginBars?.Remove(PluginBar);
                PluginsManager.RequireRemovePlugin(PluginDetail);
            }
        }

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="_"></param>
        internal void Delete(object _)
        {
            if (PluginDetail != null && PluginBar != null)
            {
                PluginBars?.Remove(PluginBar);
                PluginsManager.RequireDeletePlugin(PluginDetail);
            }
        }

        /// <summary>
        /// 启动
        /// </summary>
        /// <param name="_"></param>
        internal void Launch(object _)
        {

        }
    }
}
