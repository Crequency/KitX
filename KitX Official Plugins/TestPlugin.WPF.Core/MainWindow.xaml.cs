using KitX.Contract.CSharp;
using System;
using System.Collections.Generic;
using System.Windows;

#pragma warning disable CS8603 // 可能返回 null 引用。

namespace TestPlugin.WPF.Core
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
            controller = new(this);

            Closed += (_, _) => Environment.Exit(0);
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
        public Dictionary<string, string> GetDisplayName() => new()
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
        public Dictionary<string, string> GetSimpleDescription() => new()
        {
            { "zh-cn", "简单描述" },
            { "zh-cnt", "簡單描述" },
            { "en-us", "Simple Description" },
            { "ja-jp", "簡単な説明" }
        };

        /// <summary>
        /// 获取复杂描述
        /// </summary>
        /// <returns>复杂描述</returns>
        public Dictionary<string, string> GetComplexDescription() => new()
        {
            { "zh-cn", "复杂描述" },
            { "zh-cnt", "複雜描述" },
            { "en-us", "Complex Description" },
            { "ja-jp", "複雑な説明" }
        };

        /// <summary>
        /// 获取 MarkDown 语法的完整介绍
        /// </summary>
        /// <returns>完整介绍</returns>
        public Dictionary<string, string> GetTotalDescriptionInMarkdown() => new()
        {
            { "zh-cn", "完整描述" },
            { "zh-cnt", "完整描述" },
            { "en-us", "Total Description" },
            { "ja-jp", "完全な説明" }
        };

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

#pragma warning restore CS8603 // 可能返回 null 引用。
