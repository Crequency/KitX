using KitX.Contract.CSharp;
using System;
using System.Collections.Generic;
using System.Windows;

namespace TestPlugin.WPF.Winform
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IIdentityInterface
    {
        private readonly Controller controller;

        public MainWindow()
        {
            InitializeComponent();
            controller = new Controller(this);

            Closed += (sender, e) => Environment.Exit(0);
        }

        /// <summary>
        /// 获取插件名称
        /// </summary>
        /// <returns>插件名称</returns>
        public string GetName() => "插件名称";

        /// <summary>
        /// 获取插件版本
        /// </summary>
        /// <returns>插件版本</returns>
        public string GetVersion() => "插件版本";

        /// <summary>
        /// 获取显示名称
        /// </summary>
        /// <returns>显示名称</returns>
        public Dictionary<string, string> GetDisplayName() => new Dictionary<string, string>()
        {
            { "zh-cn", "显示名称" },
            { "zh-cnt", "顯示名稱" },
            { "en-us", "Display Name" },
            { "ja-jp", "番組名" }
        };

        /// <summary>
        /// 获取作者名称
        /// </summary>
        /// <returns>作者名称</returns>
        public string GetAuthorName() => "作者名称";

        /// <summary>
        /// 获取发行者名称
        /// </summary>
        /// <returns>发行者名称</returns>
        public string GetPublisherName() => "发行者名称";

        /// <summary>
        /// 获取作者链接
        /// </summary>
        /// <returns>作者链接</returns>
        public string GetAuthorLink() => "作者链接";

        /// <summary>
        /// 获取发行者链接
        /// </summary>
        /// <returns>发行者链接</returns>
        public string GetPublisherLink() => "发行者链接";

        /// <summary>
        /// 获取简单描述
        /// </summary>
        /// <returns>简单描述</returns>
        public string GetSimpleDescription() => "简单描述";

        /// <summary>
        /// 获取复杂描述
        /// </summary>
        /// <returns>复杂描述</returns>
        public string GetComplexDescription() => "复杂描述";

        /// <summary>
        /// 获取 MarkDown 语法的完整介绍
        /// </summary>
        /// <returns>完整介绍</returns>
        public string GetTotalDescriptionInMarkdown() => "完整介绍";

        /// <summary>
        /// 获取 Base64 编码的图标
        /// </summary>
        /// <returns>Base64 编码的图标</returns>
        public string GetIconInBase64() => "图标";

        /// <summary>
        /// 获取发行日期
        /// </summary>
        /// <returns>发行日期</returns>
        public DateTime GetPublishDate() => DateTime.Now;

        /// <summary>
        /// 获取最近更新日期
        /// </summary>
        /// <returns>最近更新日期</returns>
        public DateTime GetLastUpdateDate() => DateTime.Now;

        /// <summary>
        /// 获取控制器
        /// </summary>
        /// <returns>控制器</returns>
        public IController GetController() => controller;

        /// <summary>
        /// 指示是否是市场版本
        /// </summary>
        /// <returns>是否是市场版本</returns>
        public bool IsMarketVersion() => false;

        /// <summary>
        /// 获取市场版本插件协议
        /// </summary>
        /// <returns>市场版本插件协议</returns>
        public IMarketPluginContract GetMarketPluginContract() => null;
    }
}