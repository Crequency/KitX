using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
#pragma warning disable IDE1006 // 命名样式
#pragma warning disable CA2211 // 非常量字段应当不可见

namespace KitX
{
    public class GlobalInfo
    {
        public GlobalInfo()
        {
            //App.Log("Init GlobalInfo ...");
        }

        #region 动态配置

        /// <summary>
        /// 工作空间
        /// </summary>
        public string WorkBase = Environment.CurrentDirectory;

        /// <summary>
        /// 版本
        /// </summary>
        public string Version = "v3.0.0";

        /// <summary>
        /// 应用名称
        /// </summary>
        public string App_Name = "KitX";

        /// <summary>
        /// Build ID
        /// </summary>
        public string App_Build = "120200";

        /// <summary>
        /// 作者
        /// </summary>
        public string App_Author = "Catrol";

        /// <summary>
        /// 应用链接
        /// </summary>
        public string App_Link = "https://works.catrol.cn/KitX/";

        /// <summary>
        /// 运行类别
        /// </summary>
        public RunType runType = RunType.Release;

        public enum RunType
        {
            Release = 0,
            Debug = 1
        }

        /// <summary>
        /// 是否为第一次使用
        /// </summary>
        public bool firstUse { get; set; }

        /// <summary>
        /// 启动时是否隐藏主窗口
        /// </summary>
        public bool hideWhenStart { get; set; }

        /// <summary>
        /// 插件存放路径
        /// </summary>
        public string pgpath { get; set; }

        /// <summary>
        /// 插件缓存路径
        /// </summary>
        public string pgcache { get; set; }

        /// <summary>
        /// 正在使用的主题
        /// </summary>
        public App.KitX_Theme using_theme = App.KitX_Theme.Light;

        /// <summary>
        /// 正在使用的语言
        /// </summary>
        public string using_lang { get; set; }

        #endregion

        #region 静态资源

        /// <summary>
        /// 必要资源字典
        /// </summary>
        public static string[] NecessaryDict = new string[4]
        {
            "pack://application:,,,/HandyControl;component/Themes/Theme.xaml",
            "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml",
            "pack://application:,,,/MaterialDesignColors;component/Themes/Recommended/Primary/MaterialDesignColor.DeepPurple.xaml",
            "pack://application:,,,/MaterialDesignColors;component/Themes/Recommended/Accent/MaterialDesignColor.Lime.xaml"
        };

        /// <summary>
        /// 屏幕信息
        /// </summary>
        public static double WorkAreaWidth = SystemParameters.WorkArea.Width;//获取工作区宽度
        public static double WorkAreaHeight = SystemParameters.WorkArea.Height;//获取工作区高度
        public static double ScreenWidth = SystemParameters.PrimaryScreenWidth;//获取屏幕宽度
        public static double ScreenHeight = SystemParameters.PrimaryScreenHeight;//获取屏幕高度

        #endregion
    }
}

#pragma warning restore CA2211 // 非常量字段应当不可见
#pragma warning restore IDE1006 // 命名样式
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。