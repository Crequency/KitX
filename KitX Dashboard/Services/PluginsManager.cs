using Avalonia.Controls;
using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.ViewModels.Pages;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

#pragma warning disable CS8605 // 取消装箱可能为 null 的值。
#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX_Dashboard.Services
{
    internal class PluginsManager
    {
        /// <summary>
        /// 执行 Socket 消息
        /// </summary>
        /// <param name="msg">消息</param>
        internal static void Execute(string msg)
        {
            var card = new PluginCard(
                (PluginStruct)JsonSerializer.Deserialize(msg, typeof(PluginStruct))
            );

            //if (Program.ViewModelPool.ContainsKey("LibViewModel"))
            //(Program.ViewModelPool["LibViewModel"] as LibViewModel).PluginCards.Add(card);
            //Program.PluginCards.Add(card);

            //Program.libPage.LibViewWrapPanel.Children.Add(card);

            //Dispatcher.UIThread.Post(() =>
            //{
            //    (Program.DirectControls["LibViewWrapPanel"] as WrapPanel).Children.Add(card);

            //    Program.libPage.LibViewWrapPanel.Children.Add(card);
            //});

            //Dispatcher.UIThread.Post(() =>
            //{
            //    Program.PluginCards.Add(card);
            //});

            Program.PluginCards.Add(card);
        }

        /// <summary>
        /// 断开了连接
        /// </summary>
        /// <param name="id">插件 id</param>
        internal static void Disconnect(string id)
        {

        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8605 // 取消装箱可能为 null 的值。
