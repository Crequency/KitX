using KitX.Web.Rules;
using KitX_Dashboard.Converters;
using KitX_Dashboard.Data;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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

            new Thread(() =>
            {
                DevicesManager.KeepCheckAndRemove();
                PluginsManager.KeepCheckAndRemove();
                PluginsManager.KeepCheckAndRemoveOrDelete();
                FindSurpportNetworkInterface(new()
                {
                    UdpClient_Send, UdpClient_Receive
                }, IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress));
                MultiDevicesBroadCastSend();
                MultiDevicesBroadCastReceive();
            }).Start();
        }

        #region TCP Socket 服务于 Loaders 的服务器

        /// <summary>
        /// 开始执行
        /// </summary>
        public void Start()
        {
            listener.Start();

            int port = ((IPEndPoint)listener.LocalEndpoint).Port; // 取服务端口号
            GlobalInfo.ServerPortNumber = port; // 全局端口号标明

            Log.Information($"Server Port: {port}");

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

                        Log.Information($"New connection: {endpoint}");

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
                Log.Error($"In AcceptClient() : {ex.Message}");
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
                    byte[] data = new byte[Program.Config.Web.SocketBufferSize];
                    //如果远程主机已关闭连接,Read将立即返回零字节
                    //int length = await stream.ReadAsync(data, 0, data.Length);
                    int length = await stream.ReadAsync(data);
                    if (length > 0)
                    {
                        string msg = Encoding.UTF8.GetString(data, 0, length);

                        Log.Information($"From: {endpoint}\tReceive: {msg}");

                        PluginsManager.Execute(msg, endpoint);

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
                Log.Error($"Error: In ReciveMessage() : {ex.Message}");
                Log.Information($"Connection broke from: {endpoint}");

                //Read是阻塞方法 客户端退出是会引发异常 释放资源 结束此线程
            }
            finally
            {
                //释放资源
                PluginsManager.Disconnect(endpoint); //注销插件
                stream.Close();
                stream.Dispose();
                clients.Remove(endpoint.ToString());
                client.Dispose();
            }
        }
        #endregion

        #region UDP Socket 服务于自发现自组网

        private static readonly List<int> SurpportedNetworkInterfaces = new();

        /// <summary>
        /// UDP 发包客户端
        /// </summary>
        private static readonly UdpClient UdpClient_Send
            = new(Program.Config.Web.UDPPortSend, AddressFamily.InterNetwork)
            {
                EnableBroadcast = true,
                MulticastLoopback = true
            };

        /// <summary>
        /// UDP 收包客户端
        /// </summary>
        private static readonly UdpClient UdpClient_Receive
            = new(new IPEndPoint(IPAddress.Any, Program.Config.Web.UDPPortReceive));

        /// <summary>
        /// 寻找受支持的网络适配器并把UDP客户端加入组播
        /// </summary>
        private static void FindSurpportNetworkInterface(List<UdpClient> clients, IPAddress multicastAddress)
        {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nics)
            {
                IPInterfaceProperties ip_properties = adapter.GetIPProperties();
                if (adapter.GetIPProperties().MulticastAddresses.Count == 0
                    // most of VPN adapters will be skipped
                    || !adapter.SupportsMulticast
                    // multicast is meaningless for this type of connection
                    || OperationalStatus.Up != adapter.OperationalStatus
                    // this adapter is off or not connected
                    || !adapter.Supports(NetworkInterfaceComponent.IPv4)
                    ) continue;
                IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
                UnicastIPAddressInformationCollection unicastIPAddresses
                    = adapterProperties.UnicastAddresses;
                IPv4InterfaceProperties p = adapterProperties.GetIPv4Properties();
                if (p == null) continue;    // IPv4 is not configured on this adapter
                SurpportedNetworkInterfaces.Add(IPAddress.HostToNetworkOrder(p.Index));
                IPAddress ipAddress = null;
                foreach (UnicastIPAddressInformation unicastIPAddress in unicastIPAddresses)
                {
                    if (unicastIPAddress.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    ipAddress = unicastIPAddress.Address;
                    break;
                }
                if (ipAddress == null) continue;
                foreach (var udpClient in clients)
                    udpClient.JoinMulticastGroup(multicastAddress, ipAddress);
            }
        }

        /// <summary>
        /// 多设备广播发送方法
        /// </summary>
        public static void MultiDevicesBroadCastSend()
        {
            #region 初始化 UDP 客户端

            UdpClient udpClient = UdpClient_Send;
            IPEndPoint multicast = new(IPAddress.Parse(Program.Config.Web.UDPBroadcastAddress),
                Program.Config.Web.UDPPortReceive);

            #endregion

            System.Timers.Timer timer = new()
            {
                Interval = 2000,
                AutoReset = true
            };
            timer.Elapsed += (_, _) =>
            {
                try
                {
                    string sendText = JsonSerializer.Serialize(GetDeviceInfo());
                    byte[] sendBytes = Encoding.UTF8.GetBytes(sendText);

                    foreach (var item in SurpportedNetworkInterfaces)
                    {
                        udpClient.Client.SetSocketOption(SocketOptionLevel.IP,
                            SocketOptionName.MulticastInterface, item);
                        udpClient.Send(sendBytes, sendBytes.Length, multicast);
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"In MultiDevicesBroadCastSend: {e.Message}");
                }
                if (!GlobalInfo.Running)
                {
                    udpClient.Close();

                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 多设备广播接收方法
        /// </summary>
        public static void MultiDevicesBroadCastReceive()
        {
            #region 初始化 UDP 客户端

            UdpClient udpClient = UdpClient_Receive;
            IPEndPoint multicast = new(IPAddress.Any, 0);

            #endregion

            new Thread(() =>
            {
                try
                {
                    while (GlobalInfo.Running)
                    {
                        byte[] bytes = udpClient.Receive(ref multicast);
                        string result = Encoding.UTF8.GetString(bytes);
                        Log.Information($"UDP Receive: {result}");
                        DeviceInfoStruct deviceInfo = JsonSerializer.Deserialize<DeviceInfoStruct>(result);
                        DevicesManager.Update(deviceInfo);
                    }
                    udpClient.Close();
                }
                catch (Exception e)
                {
                    Log.Error(e.Message);
                }
            }).Start();
        }

        /// <summary>
        /// 将 IPv4 的十进制表示按点分制拆分
        /// </summary>
        /// <param name="ip">IPv4 的十进制表示</param>
        /// <returns>拆分</returns>
        private static (int, int, int, int) IPv4_2_4Parts(string ip)
        {
            string[] p = ip.Split('.');
            int a = int.Parse(p[0]), b = int.Parse(p[1]), c = int.Parse(p[2]), d = int.Parse(p[3]);
            return (a, b, c, d);
        }

        /// <summary>
        /// 获取本机内网 IPv4 地址
        /// </summary>
        /// <returns>使用点分十进制表示法的本机内网IPv4地址</returns>
        private static string GetInterNetworkIPv4()
        {
            return (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetwork
                        && !ip.ToString().Equals("127.0.0.1")
                        && (ip.ToString().StartsWith("192.168")                         //  192.168.x.x
                            || ip.ToString().StartsWith("10")                           //  10.x.x.x
                            || (IPv4_2_4Parts(ip.ToString()).Item1 == 172               //  172.16-31.x.x
                                && IPv4_2_4Parts(ip.ToString()).Item2 >= 16
                                && IPv4_2_4Parts(ip.ToString()).Item2 <= 31))
                        && ip.ToString().StartsWith(Program.Config.Web.IPFilter)  //  满足自定义规则
                    select ip).First().ToString();
        }

        /// <summary>
        /// 获取设备信息
        /// </summary>
        /// <returns>设备信息结构体</returns>
        private static DeviceInfoStruct GetDeviceInfo() => new()
        {
            DeviceName = Environment.MachineName,
            DeviceMacAddress = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                    && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString()).FirstOrDefault(),
            IsMainDevice = GlobalInfo.IsMainMachine,
            SendTime = DateTime.Now,
            DeviceOSType = OperatingSystem2Enum.GetOSType(),
            DeviceOSVersion = Environment.OSVersion.VersionString,
            IPv4 = GetInterNetworkIPv4(),
            IPv6 = (from ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    where ip.AddressFamily == AddressFamily.InterNetworkV6
                        && !ip.ToString().Equals("::1")
                    select ip).First().ToString(),
            ServingPort = GlobalInfo.ServerPortNumber,
            PluginsCount = Program.PluginCards.Count,
        };

        #endregion

        /// <summary>
        /// 释放资源
        /// </summary>
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

//                                         .....
//                                    .e$$$$$$$$$$$$$$e.
//                                  z$$ ^$$$$$$$$$$$$$$$$$.
//                                .$$$* J$$$$$$$$$$$$$$$$$$$e
//                               .$"  .$$$$$$$$$$$$$$$$$$$$$$*-
//                              .$  $$$$$$$$$$$$$$$$***$$  .ee"
//                 z**$$        $$r ^**$$$$$$$$$*" .e$$$$$$*"
//                " -\e$$      4$$$$.         .ze$$$""""
//               4 z$$$$$      $$$$$$$$$$$$$$$$$$$$"
//               $$$$$$$$     .$$$$$$$$$$$**$$$$*"
//             z$$"    $$     $$$$P*""     J$*$$c
//            $$"      $$F   .$$$          $$ ^$$
//           $$        *$$c.z$$$          $$   $$
//          $P          $$$$$$$          4$F   4$
//         dP            *$$$"           $$    '$r
//        .$                            J$"     $"
//        $                             $P     4$
//        F                            $$      4$
//                                    4$%      4$
//                                    $$       4$
//                                   d$"       $$
//                                   $P        $$
//                                  $$         $$
//                                 4$%         $$
//                                 $$          $$
//                                d$           $$
//                                $F           "3
//                         r=4e="  ...  ..rf   .  ""%
//                        $**$*"^""=..^4*=4=^""  ^"""
