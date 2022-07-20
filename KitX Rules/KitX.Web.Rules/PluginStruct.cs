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

        public string Name { get; set; } = "Name";

        public string Version { get; set; } = "Version";

        public string DisplayName { get; set; } = "DisplayName";

        public string AuthorName { get; set; } = "AuthorName";

        public string PublisherName { get; set; } = "PublisherName";

        public string AuthorLink { get; set; } = "AuthorLink";

        public string PublisherLink { get; set; } = "PublisherLink";

        public string SimpleDescription { get; set; } = "SimpleDescription";

        public string ComplexDescription { get; set; } = "ComplexDescription";

        public string TotalDescriptionInMarkdown { get; set; } = "TotalDescriptionInMarkdown";

        public string IconInBase64 { get; set; } = "IconInBase64";

        public DateTime PublishDate { get; set; } = DateTime.Parse("2019.06.27");

        public DateTime LastUpdateDate { get; set; } = DateTime.Parse("2022.05.02");

        public bool IsMarketVersion { get; set; } = false;

        public Dictionary<string, string> Tags { get; set; } = new();
    }
}
