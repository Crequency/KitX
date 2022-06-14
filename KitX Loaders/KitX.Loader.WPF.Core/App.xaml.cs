using KitX.Contract.CSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Net.Sockets;
using System.Text;
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
            for (int i = 0; i < e.Args.Length; ++i)
            {
                if (i != e.Args.Length - 1)
                {
                    switch (e.Args[i])
                    {
                        case "--load":
                            ++i;
                            LoadPlugin(e.Args[i]);
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
                                    Console.WriteLine($"Connection failed!\n{ex.Message}");
                                }
                            }
                            else Console.WriteLine("Bad port number!");
                            break;
                    }
                }
            }
        }

        private static bool StillReceiving = true;
        private static Thread? reciveMessageThread;
        private static TcpClient? client;
        private static IController? controller;

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
                    controller = item.GetController();
                    controller.Start();
                    break;
                }
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="content">消息内容</param>
        private void SendMessage(string content)
        {
            NetworkStream stream = client.GetStream();
            byte[] data = Encoding.UTF8.GetBytes(content);
            try
            {
                stream.Write(data, 0, data.Length);
                stream.Flush();
                stream.Close();
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
        private void ReciveMessage()
        {
            NetworkStream stream = client.GetStream();
            try
            {
                while (StillReceiving)
                {
                    byte[] data = new byte[1024];
                    int length = stream.Read(data, 0, data.Length);
                    if (length > 0)
                    {
                        string msg = Encoding.UTF8.GetString(data, 0, length);
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
            catch
            {
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
