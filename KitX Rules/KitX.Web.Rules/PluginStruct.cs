using System;
using System.Collections.Generic;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 插件结构
    /// </summary>
    public struct PluginStruct
    {
        public string Name { get; set; }

        public string Version { get; set; }

        public string DisplayName { get; set; }

        public string AuthorName { get; set; }

        public string PublisherName { get; set; }

        public string AuthorLink { get; set; }

        public string PublisherLink { get; set; }

        public string SimpleDescription { get; set; }

        public string ComplexDescription { get; set; }

        public string TotalDescriptionInMarkdown { get; set; }

        public string IconInBase64 { get; set; }

        public DateTime PublishDate { get; set; }

        public DateTime LastUpdateDate { get; set; }

        public bool IsMarketVersion { get; set; }

        public Dictionary<string, string> Tags { get; set; }

        public Function Functions { get; set; }

        public string RootStartupFileName { get; set; }
    }
}
