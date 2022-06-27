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

        public string Name = "Name";

        public string Version = "Version";

        public string DisplayName = "DisplayName";

        public string AuthorName = "AuthorName";

        public string PublisherName = "PublisherName";

        public string AuthorLink = "AuthorLink";

        public string PublisherLink = "PublisherLink";

        public string SimpleDescription = "SimpleDescription";

        public string ComplexDescription = "ComplexDescription";

        public string TotalDescriptionInMarkdown = "TotalDescriptionInMarkdown";

        public string IconInBase64 = "IconInBase64";

        public DateTime PublishDate = DateTime.Parse("2019.06.27");

        public DateTime LastUpdateDate = DateTime.Parse("2022.05.02");

        public bool IsMarketVersion = false;
    }
}
