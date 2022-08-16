using System;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 设备信息结构
    /// </summary>
    public struct DeviceInfoStruct
    {
        public string DeviceName { get; set; }

        public string DeviceOSVersion { get; set; }

        public string IPv4 { get; set; }

        public string IPv6 { get; set; }

        public string DeviceMacAddress { get; set; }

        public int ServingPort { get; set; }

        public int PluginsCount { get; set; }

        public DateTime SendTime { get; set; }

        public bool IsMainDevice { get; set; }

        public OperatingSystems DeviceOSType { get; set; }
    }
}
