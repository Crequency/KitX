using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.Generic;
using System.Timers;

namespace KitX_Dashboard.Services
{
    internal class DevicesManager
    {
        internal static readonly Queue<DeviceInfoStruct> deviceInfoStructs = new();

        /// <summary>
        /// 持续检查并移除
        /// </summary>
        internal static void KeepCheckAndRemove()
        {
            Timer timer = new()
            {
                Interval = 1000,
                AutoReset = true
            };
            timer.Elapsed += (_, _) =>
            {
                while (deviceInfoStructs.Count > 0)
                {
                    DeviceInfoStruct deviceInfoStruct = deviceInfoStructs.Dequeue();
                    bool findThis = false;
                    foreach (var item in Program.DeviceCards)
                    {
                        if (item.viewModel.DeviceInfo.DeviceName.Equals(deviceInfoStruct.DeviceName))
                        {
                            item.viewModel.DeviceInfo = deviceInfoStruct;
                            findThis = true;
                            break;
                        }
                    }
                    if (!findThis)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            Program.DeviceCards.Add(new(deviceInfoStruct));
                        });
                    }
                }

                List<string> MacAddressVisited = new();
                List<string> IPv4AddressVisited = new();
                List<string> IPv6AddressVisited = new();
                List<DeviceCard> DevicesNeed2BeRemoved = new();

                foreach (var item in Program.DeviceCards)
                {
                    if (MacAddressVisited.Contains(item.viewModel.DeviceInfo.DeviceMacAddress))
                    {
                        DevicesNeed2BeRemoved.Add(item);
                        continue;
                    }
                    if (IPv4AddressVisited.Contains(item.viewModel.DeviceInfo.IPv4))
                    {
                        DevicesNeed2BeRemoved.Add(item);
                        continue;
                    }
                    if(IPv6AddressVisited.Contains(item.viewModel.DeviceInfo.IPv6))
                    {
                        DevicesNeed2BeRemoved.Add(item);
                        continue;
                    }
                    MacAddressVisited.Add(item.viewModel.DeviceInfo.DeviceMacAddress);
                    IPv4AddressVisited.Add(item.viewModel.DeviceInfo.IPv4);
                    IPv6AddressVisited.Add(item.viewModel.DeviceInfo.IPv6);
                    if (DateTime.Now - item.viewModel.DeviceInfo.SendTime
                        > new TimeSpan(0, 0, Program.Config.Web.DeviceInfoStructTTLSeconds))
                        DevicesNeed2BeRemoved.Add(item);
                }
                foreach (var item in DevicesNeed2BeRemoved)
                {
                    Program.DeviceCards.Remove(item);
                }
            };
            timer.Start();
        }

        /// <summary>
        /// 更新收到的UDP包
        /// </summary>
        /// <param name="deviceInfo">设备信息结构</param>
        internal static void Update(DeviceInfoStruct deviceInfo)
        {
            deviceInfoStructs.Enqueue(deviceInfo);
        }
    }
}

//
//                                        ___-------___
//                                    _-~~             ~~-_
//                                 _-~                    /~-_
//              /^\__/^\         /~  \                   /    \
//            /|  O|| O|        /      \_______________/        \
//           | |___||__|      /       /                \          \
//           |          \    /      /                    \          \
//           |   (_______) /______/                        \_________ \
//           |         / /         \                      /            \
//            \         \^\         \                  /               \     /
//              \         ||           \______________/      _-_       //\__//
//                \       ||------_-~~-_ ------------- \ --/~   ~\    || __/
//                  ~-----||====/~     |==================|       |/~~~~~
//                   (_(__/  ./     /                    \_\      \.
//                          (_(___/                         \_____)_)-jurcy
// 
//
