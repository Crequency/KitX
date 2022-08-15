using System.Text.Json.Serialization;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 加载器结构
    /// </summary>
    public struct LoaderStruct
    {
        public LoaderStruct()
        {

        }

        [JsonInclude]
        public string LoaderName { get; set; } = "KitX.Loader";

        [JsonInclude]
        public string LoaderVersion { get; set; } = "v1.0.0";

        [JsonInclude]
        public string LoaderLanguage { get; set; } = "WDOScript";

        [JsonInclude]
        public string LoaderFramework { get; set; } = "Console";

        [JsonInclude]
        public RunType LoaderRunType { get; set; } = RunType.Console;

        [JsonInclude]
        public List<OperatingSystems> SupportOS { get; set; } = new()
        {
            OperatingSystems.Windows, OperatingSystems.Linux, OperatingSystems.MacOS,
            OperatingSystems.Android, OperatingSystems.IOS,
            OperatingSystems.Browser,
            OperatingSystems.MacCatalyst, OperatingSystems.TvOS, OperatingSystems.WatchOS,
            OperatingSystems.FreeBSD
        };

        public enum RunType
        {
            Console, Desktop, Browser
        }
    }
}
