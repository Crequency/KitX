using KitX.Contract.CSharp;
using KitX.Web.Rules;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;

#pragma warning disable CS8604 // 引用类型参数可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX.Loader.WPF.Core
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 启动事件
        /// </summary>
        /// <param name="sender">...</param>
        /// <param name="e">...</param>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                for (int i = 0; i < e.Args.Length; ++i)
                {
                    if (i != e.Args.Length - 1)
                    {
                        switch (e.Args[i])
                        {
                            case "--load":
                                ++i;
                                pluginPath = e.Args[i];
                                break;
                            case "--connect":
                                ++i;
                                string hostname = e.Args[i].Split(':')[0];
                                string port = e.Args[i].Split(':')[1];
                                int portNum;
                                if (int.TryParse(port, out portNum))
                                {
                                    try
                                    {
                                        client = new();
                                        client.Connect(hostname, portNum);
                                        reciveMessageThread = new(ReciveMessage);
                                        reciveMessageThread.Start();
                                    }
                                    catch (Exception ex)
                                    {
                                        client.Dispose();
                                        MessageBox.Show($"Connection failed!\n{ex.Message}");
                                        Console.WriteLine($"Connection failed!\n{ex.Message}");
                                    }
                                }
                                else Console.WriteLine("Bad port number!");
                                break;
                        }
                    }
                }
                LoadPlugin(pluginPath);
            }
            catch (Exception o)
            {
                MessageBox.Show(o.Message, "Loader Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine(o.Message);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// 注册插件结构
        /// </summary>
        /// <param name="identity">插件结构</param>
        private static void RegisterPluginStruct(IIdentityInterface identity)
        {
            pluginStruct = new()
            {
                Name = identity.GetName(),
                Version = identity.GetVersion(),
                DisplayName = identity.GetDisplayName(),
                AuthorName = identity.GetAuthorName(),
                PublisherName = identity.GetPublisherName(),
                AuthorLink = identity.GetAuthorLink(),
                PublisherLink = identity.GetPublisherLink(),
                SimpleDescription = identity.GetSimpleDescription(),
                ComplexDescription = identity.GetComplexDescription(),
                TotalDescriptionInMarkdown = identity.GetTotalDescriptionInMarkdown(),
                IconInBase64 = identity.GetIconInBase64(),
                PublishDate = identity.GetPublishDate(),
                LastUpdateDate = identity.GetLastUpdateDate(),
                IsMarketVersion = identity.IsMarketVersion(),
                Tags = new(),
                Functions = identity.GetController().GetFunctions(),
                RootStartupFileName = identity.GetRootStartupFileName(),
            };
        }

        /// <summary>
        /// 获取插件结构
        /// </summary>
        /// <returns>插件结构</returns>
        private static PluginStruct GetPluginStruct() => pluginStruct;

        /// <summary>
        /// 获取序列化的插件结构
        /// </summary>
        /// <param name="ps">插件结构</param>
        /// <returns>序列化的 Json 字符串</returns>
        private static string GetPluginStructInJson(PluginStruct ps) => JsonSerializer.Serialize(ps);

        private static string pluginPath = string.Empty;
        private static bool StillReceiving = true;
        private static Thread? reciveMessageThread;
        private static TcpClient? client;
        private static IController? controller;
        private static PluginStruct pluginStruct;

        /// <summary>
        /// 加载插件
        /// </summary>
        /// <param name="path">插件路径</param>
        private static void LoadPlugin(string path)
        {
            if (File.Exists(path))
            {
                DirectoryCatalog catalog = new(Path.GetDirectoryName(path), Path.GetFileName(path));
                CompositionContainer container = new(catalog);
                IEnumerable<IIdentityInterface> sub = container.GetExportedValues<IIdentityInterface>();
                foreach (var item in sub)
                {
                    RegisterPluginStruct(item);
                    controller = item.GetController();
                    controller.Start();
                    break;
                }
                //MessageBox.Show(GetPluginStructInJson(GetPluginStruct()));
                SendMessage($"PluginStruct: {GetPluginStructInJson(GetPluginStruct())}");
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="content">消息内容</param>
        private static void SendMessage(string content)
        {
            NetworkStream stream = client.GetStream();
            byte[] data = Encoding.UTF8.GetBytes(content);
            try
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
                //stream.Close();
            }
            catch
            {
                stream.Close();
                stream.Dispose();
            }
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        private static void ReciveMessage()
        {
            NetworkStream stream = client.GetStream();
            try
            {
                while (StillReceiving)
                {
                    byte[] data = new byte[1024 * 100];
                    int length = stream.Read(data, 0, data.Length);
                    if (length > 0)
                    {
                        string msg = Encoding.UTF8.GetString(data, 0, length);
                        MessageBox.Show(msg);
                    }
                    else
                    {
                        stream.Dispose();
                        break;
                    }
                }
                stream.Close();
                stream.Dispose();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Loader Error in ReciveMessage", MessageBoxButton.OK,
                    MessageBoxImage.Error);

                stream.Close();
                stream.Dispose();
                client.Close();
                client.Dispose();
            }
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8604 // 引用类型参数可能为 null。
