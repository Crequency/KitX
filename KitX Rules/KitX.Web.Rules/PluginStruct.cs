using System.Text.Json.Serialization;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 插件结构
    /// </summary>
    public struct PluginStruct
    {
        public PluginStruct()
        {

        }

        [JsonInclude]
        public string Name { get; set; } = "Name";

        [JsonInclude]
        public string Version { get; set; } = "Version";

        [JsonInclude]
        public string DisplayName { get; set; } = "DisplayName";

        [JsonInclude]
        public string AuthorName { get; set; } = "AuthorName";

        [JsonInclude]
        public string PublisherName { get; set; } = "PublisherName";

        [JsonInclude]
        public string AuthorLink { get; set; } = "AuthorLink";

        [JsonInclude]
        public string PublisherLink { get; set; } = "PublisherLink";

        [JsonInclude]
        public string SimpleDescription { get; set; } = "SimpleDescription";

        [JsonInclude]
        public string ComplexDescription { get; set; } = "ComplexDescription";

        [JsonInclude]
        public string TotalDescriptionInMarkdown { get; set; } = "TotalDescriptionInMarkdown";

        [JsonInclude]
        public string IconInBase64 { get; set; } = "IconInBase64";

        [JsonInclude]
        public DateTime PublishDate { get; set; } = DateTime.Parse("2019.06.27");

        [JsonInclude]
        public DateTime LastUpdateDate { get; set; } = DateTime.Parse("2022.05.02");

        [JsonInclude]
        public bool IsMarketVersion { get; set; } = false;

        [JsonInclude]
        public Dictionary<string, string> Tags { get; set; } = new();

        [JsonInclude]
        public Function Functions { get; set; } = new();
    }
}
