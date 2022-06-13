using KitX.Contract.CSharp;
using System;
using System.ComponentModel.Composition;
using System.Windows;

namespace TestPlugin.WPF.Core
{
    [Export(typeof(IIdentityInterface))]
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
        }

        public string GetAuthorLink() => "作者链接";

        public string GetAuthorName() => "作者名称";

        public string GetComplexDescription() => "复杂描述";

        public IController GetController() => controller;

        public string GetDisplayName() => "显示名称";

        public DateTime GetLastUpdateDate() => DateTime.Now;

        public string GetName() => "插件名称";

        public DateTime GetPublishDate() => DateTime.Now;

        public string GetPublisherLink() => "发行商链接";

        public string GetPublisherName() => "发行者名称";

        public string GetSimpleDescription() => "简单描述";

        public string GetTotalDescriptionInMarkdown() => "Markdown 语法的完整描述";

        public string GetVersion() => "版本";
    }
}
