using System.Text.Json.Serialization;

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

        [JsonInclude]
        public string DeviceName { get; set; } = "Unknown Device";

        [JsonInclude]
        public string DeviceOSVersion { get; set; } = "Unknown OS Version";

        [JsonInclude]
        public string IPv4 { get; set; } = "Getting...";

        [JsonInclude]
        public string IPv6 { get; set; } = "Getting...";

        [JsonInclude]
        public string DeviceMacAddress { get; set; } = string.Empty;

        [JsonInclude]
        public int ServingPort { get; set; } = 0;

        [JsonInclude]
        public int PluginsCount { get; set; } = 0;

        [JsonInclude]
        public DateTime SendTime { get; set; } = DateTime.Now;

        [JsonInclude]
        public bool IsMainDevice { get; set; } = false;

        [JsonInclude]
        public OperatingSystems DeviceOSType { get; set; } = OperatingSystems.Unknown;
    }
}
