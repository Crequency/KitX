namespace KitX.Web.Rules
{
    /// <summary>
    /// 设备信息结构
    /// </summary>
    public struct DeviceInfoStruct
    {
        public DeviceInfoStruct()
        {

        }

        public string DeviceName { get; set; } = "Unknown Device";

        public string DeviceOSVersion { get; set; } = "Unknown OS Version";

        public string IPv4 { get; set; } = "Getting...";

        public string IPv6 { get; set; } = "Getting...";

        public string DeviceMacAddress { get; set; } = string.Empty;

        public DateTime SendTime { get; set; } = DateTime.Now;

        public bool IsMainDevice { get; set; } = false;

        public OperatingSystems DeviceOSType { get; set; } = OperatingSystems.Unknown;
    }
}
