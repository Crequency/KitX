using KitX.Web.Rules;
using KitX_Dashboard.Views.Pages.Controls;
using System.Net;
using System.Text.Json;

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
        internal static void Execute(string msg, IPEndPoint endPoint)
        {
            var pluginStruct = (PluginStruct)JsonSerializer.Deserialize(msg, typeof(PluginStruct));

            // 标注实例注册 ID
            pluginStruct.Tags.Add("Authorized_ID",
                $"{pluginStruct.PublisherName}" +
                $"." +
                $"{pluginStruct.Name}" +
                $"." +
                $"{pluginStruct.Version}"
            );

            // 标注 IPEndPoint
            pluginStruct.Tags.Add("IPEndPoint", endPoint.ToString());

            // 创建插件卡片
            var card = new PluginCard(pluginStruct);

            #region A foolish try.

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

            #endregion

            // 插件卡片添加到前台
            Program.PluginCards.Add(card);

            //Program.PluginsCount = $"Program.PluginCards.Count";
        }

        /// <summary>
        /// 断开了连接
        /// </summary>
        /// <param name="id">插件 id</param>
        internal static void Disconnect(IPEndPoint endPoint)
        {
            PluginCard? remove_target = null;
            foreach (var item in Program.PluginCards)
            {
                if (item.pluginStruct.Tags["IPEndPoint"].Equals(endPoint.ToString()))
                {
                    remove_target = item;
                    break;
                }
            }
            if (remove_target != null)
                Program.PluginCards.Remove(remove_target);
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8605 // 取消装箱可能为 null 的值。

//                                   _----_
//                              ,__./#%  o `,
//                        .__.--%##%%     ,' \.
//                       /%%########%%    .====\
//                   __.;%##///         ,/
//             ____--     ,,,,,/      ,/
//            __====..,'''''''      ,/
//      ..__--      ,,,,,, ,,,,  ,./
//     /    ,,,,.'''`````'///////
//    /"""""              \/ \/
//                       &lt;&lt; &lt;&lt;
//                        \\ \\
//                         \\ \\
//                        --``-``===;
//                            \,  \,

