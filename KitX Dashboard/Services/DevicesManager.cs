using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Views.Pages.Controls;
using System;
using System.Collections.Generic;
using System.Threading;

namespace KitX_Dashboard.Services
{
    internal class DevicesManager
    {
        internal static readonly Queue<DeviceInfoStruct> deviceInfoStructs = new();

        internal static readonly Queue<DeviceCard> deviceOfflined = new();

        internal static Thread keepCheckAndRemoveThread = new(KeepCheckAndRemove);

        internal static Thread cleanOfflinedDeviceThread = new(CleanOfflinedDevice);

        /// <summary>
        /// 持续检查并移除
        /// </summary>
        internal static void KeepCheckAndRemove()
        {
            while (GlobalInfo.Running)
            {
                if (deviceInfoStructs.Count > 0)
                {
                    DeviceInfoStruct deviceInfoStruct = deviceInfoStructs.Dequeue();
                    List<string> MacAddressVisited = new();
                    bool findThis = false;
                    foreach (var item in Program.DeviceCards)
                    {
                        if (item.viewModel.DeviceInfo.DeviceName.Equals(deviceInfoStruct.DeviceName)
                            && !MacAddressVisited.Contains(item.viewModel.DeviceInfo.DeviceMacAddress))
                        {
                            MacAddressVisited.Add(item.viewModel.DeviceInfo.DeviceMacAddress);
                            item.viewModel.DeviceInfo = deviceInfoStruct;
                            findThis = true;
                        }
                        else
                        {
                            if (DateTime.Now - item.viewModel.DeviceInfo.SendTime > new TimeSpan(0, 0, 5))
                                deviceOfflined.Enqueue(item);
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
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// 清除掉线的设备
        /// </summary>
        internal static void CleanOfflinedDevice()
        {
            while (GlobalInfo.Running)
            {
                if (deviceOfflined.Count > 0)
                {
                    DeviceCard deviceCard = deviceOfflined.Dequeue();
                    Program.DeviceCards.Remove(deviceCard);
                }
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// 更新收到的UDP包
        /// </summary>
        /// <param name="deviceInfo">设备信息结构</param>
        internal static void Update(DeviceInfoStruct deviceInfo)
        {
            #region 如果持续检查并移除线程尚未运行则启动它

            try
            {
                if (keepCheckAndRemoveThread.ThreadState == ThreadState.Unstarted)
                    keepCheckAndRemoveThread.Start();

                if (cleanOfflinedDeviceThread.ThreadState == ThreadState.Unstarted)
                    cleanOfflinedDeviceThread.Start();
            }
            catch
            {

            }

            #endregion

            deviceInfoStructs.Enqueue(deviceInfo);
        }
    }
}
