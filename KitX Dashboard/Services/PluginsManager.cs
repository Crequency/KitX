using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Views.Pages.Controls;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;

#pragma warning disable CS8605 // 取消装箱可能为 null 的值。

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

            pluginsToAdd.Enqueue(pluginStruct);
        }

        internal static readonly Queue<IPEndPoint> pluginsToRemove = new();

        internal static readonly Queue<PluginStruct> pluginsToAdd = new();

        /// <summary>
        /// 持续检查并移除
        /// </summary>
        internal static void KeepCheckAndRemove()
        {
            System.Timers.Timer timer = new()
            {
                Interval = 10,
                AutoReset = true
            };
            timer.Elapsed += (_, _) =>
            {
                if (pluginsToAdd.Count > 0)
                {
                    List<PluginCard> pluginCardsToAdd = new();
                    int needAddCount = 0, addedCount = 0;
                    while (pluginsToAdd.Count > 0)
                    {
                        ++needAddCount;

                        PluginStruct pluginStruct = pluginsToAdd.Dequeue();

                        Dispatcher.UIThread.Post(() =>
                        {
                            PluginCard card = new(pluginStruct);
                            pluginCardsToAdd.Add(card);
                            lock ((object)addedCount)
                            {
                                ++addedCount;
                            }
                        });
                    }
                    while (needAddCount != addedCount) { }
                    foreach (var item in pluginCardsToAdd)
                    {
                        Program.PluginCards.Add(item);
                    }
                }

                if (pluginsToRemove.Count > 0)
                {
                    List<PluginCard> pluginCardsToRemove = new();
                    while (pluginsToRemove.Count > 0)
                    {
                        IPEndPoint endPoint = pluginsToRemove.Dequeue();
                        foreach (var item in Program.PluginCards)
                        {
                            if (item.pluginStruct.Tags["IPEndPoint"].Equals(endPoint.ToString()))
                            {
                                pluginCardsToRemove.Add(item);
                                break;
                            }
                        }
                    }
                    foreach (var item in pluginCardsToRemove)
                    {
                        Program.PluginCards.Remove(item);
                    }
                }

                if (!GlobalInfo.Running)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 断开了连接
        /// </summary>
        /// <param name="id">插件 id</param>
        internal static void Disconnect(IPEndPoint endPoint)
        {
            pluginsToRemove.Enqueue(endPoint);
        }
    }
}

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

