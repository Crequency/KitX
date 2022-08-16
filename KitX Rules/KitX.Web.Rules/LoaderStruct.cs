using System.Collections.Generic;

namespace KitX.Web.Rules
{
    /// <summary>
    /// 加载器结构
    /// </summary>
    public struct LoaderStruct
    {
        public string LoaderName { get; set; }

        public string LoaderVersion { get; set; }

        public string LoaderLanguage { get; set; }

        public string LoaderFramework { get; set; }

        public RunType LoaderRunType { get; set; }

        public List<OperatingSystems> SupportOS { get; set; }

        public enum RunType
        {
            Console, Desktop, Browser
        }
    }
}
