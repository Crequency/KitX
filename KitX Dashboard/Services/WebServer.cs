using Avalonia.Threading;
using KitX_Dashboard.Data;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

#pragma warning disable CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8604 // 引用类型参数可能为 null。

namespace KitX_Dashboard.Services
{
    public class WebServer : IDisposable
    {
        public WebServer()
        {
            listener = new(IPAddress.Any, 0);
            acceptClientThread = new(AcceptClient);
        }

        /// <summary>
        /// 开始执行
        /// </summary>
        public void Start()
        {
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号
            GlobalInfo.ServerPortNumber = port; // 全局端口号标明

            Program.LocalLogger.Log("Logger_Debug", $"Server Port: {port}",
                BasicHelper.LiteLogger.LoggerManager.LogLevel.Debug); // 日志记录

            acceptClientThread.Start();
        }

        /// <summary>
        /// 停止进程
        /// </summary>
        public void Stop()
        {
            keepListen = false;

            foreach (KeyValuePair<string, TcpClient> item in clients)
            {
                item.Value.Close();
                item.Value.Dispose();
            }

            acceptClientThread.Join();
        }

        public Thread acceptClientThread;
        public TcpListener listener;
        public bool keepListen = true;

        public readonly Dictionary<string, TcpClient> clients = new();

        /// <summary>
        /// 接收客户端
        /// </summary>
        private void AcceptClient()
        {
            try
            {
                while (keepListen)
                {
                    if (listener.Pending())
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        IPEndPoint endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                        clients.Add(endpoint.ToString(), client);

                        Program.LocalLogger.Log("Logger_Debug",
                            $"New connection: {endpoint}",
                            BasicHelper.LiteLogger.LoggerManager.LogLevel.Debug);

                        // 新建并运行接收消息线程
                        new Thread(() =>
                        {
                            ReciveMessage(client);
                        }).Start();
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LocalLogger.Log("Logger_Error",
                    $"Error: In AcceptClient() : {ex.Message}",
                    BasicHelper.LiteLogger.LoggerManager.LogLevel.Error);
            }
        }

        /// <summary>
        /// 接收消息
        /// </summary>
        /// <param name="obj">TcpClient</param>
        private async void ReciveMessage(object obj)
        {
            TcpClient client = obj as TcpClient;
            IPEndPoint endpoint = null;
            NetworkStream stream = null;

            try
            {
                endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                stream = client.GetStream();

                while (keepListen)
                {
                    byte[] data = new byte[1024];
                    //如果远程主机已关闭连接,Read将立即返回零字节
                    //int length = await stream.ReadAsync(data, 0, data.Length);
                    int length = await stream.ReadAsync(data);
                    if (length > 0)
                    {
                        string msg = Encoding.UTF8.GetString(data, 0, length);

                        Program.LocalLogger.Log("Logger_Debug",
                            $"From: {endpoint}\tReceive: {msg}",
                            BasicHelper.LiteLogger.LoggerManager.LogLevel.Debug);

                        Dispatcher.UIThread.Post(() =>
                        {
                            PluginsManager.Execute(msg, endpoint);
                        });

                        //发送到其他客户端
                        //foreach (KeyValuePair<string, TcpClient> kvp in clients)
                        //{
                        //    if (kvp.Value != client)
                        //    {
                        //        byte[] writeData = Encoding.UTF8.GetBytes(msg);
                        //        NetworkStream writeStream = kvp.Value.GetStream();
                        //        writeStream.Write(writeData, 0, writeData.Length);
                        //    }
                        //}
                    }
                    else
                    {

                        break; //客户端断开连接 跳出循环
                    }
                }
            }
            catch (Exception ex)
            {
                Program.LocalLogger.Log("Logger_Error",
                    $"Error: In ReciveMessage() : {ex.Message}",
                    BasicHelper.LiteLogger.LoggerManager.LogLevel.Error);

                Program.LocalLogger.Log("Logger_Debug",
                    $"Connection broke from: {endpoint}",
                    BasicHelper.LiteLogger.LoggerManager.LogLevel.Debug);

                PluginsManager.Disconnect(endpoint);

                //Read是阻塞方法 客户端退出是会引发异常 释放资源 结束此线程
            }
            finally
            {
                //释放资源
                stream.Close();
                stream.Dispose();
                clients.Remove(endpoint.ToString());
                client.Dispose();
            }
        }

        public void Dispose()
        {
            keepListen = false;
            listener.Stop();
            acceptClientThread.Join();
            GC.SuppressFinalize(this);
        }
    }
}

#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8600 // 将 null 字面量或可能为 null 的值转换为非 null 类型。
