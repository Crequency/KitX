using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Drawing;
using System.Drawing.Imaging;
using HandyControl.Tools;
using BasicHelper.IO;
using System.Windows.Media.Imaging;
using Aska.WPF.Helper;
using Aska.WPF.Converter;
using Aska.WPF.Effect.Blur;
using BasicHelper.Windows;

#pragma warning disable CA2211 // 非常量字段应当不可见

namespace KitX
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 全局配置类唯一实例
        /// </summary>
        public static GlobalInfo globalInfo = new();

        /// <summary>
        /// 主窗体唯一实例
        /// </summary>
        public static MainWindow mainWin = new();

        /// <summary>
        /// 主窗体帮助类唯一实例
        /// </summary>
        public static Helper.MainWindowHelper mwh = new();

        /// <summary>
        /// 全局桌面壁纸唯一实例
        /// </summary>
        //public static BitmapImage Desktop_Background = new BitmapImage(new
        //    Uri(GetDesktopBackground.GetDesktopBackgroundPath(), UriKind.Absolute));

        /// <summary>
        /// 全局高斯模糊后的桌面壁纸唯一实例
        /// </summary>
        //public static BitmapImage Desktop_Background_Blur;

        /// <summary>
        /// 全局标记 - 应用存活标记
        /// </summary>
        public static bool IsAlive = true;

        /// <summary>
        /// 应用启动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            #region 单例启动
            if (!MutexHelper.CheckApplicationMutex())
            {
                MutexHelper.Restore();
                Environment.Exit(0);
            }
            #endregion

            #region 初始化模糊壁纸
            //Bitmap Bmp = new Bitmap(GetDesktopBackground.GetDesktopBackgroundPath());
            //Rectangle Rect = new Rectangle(0, 0, Bmp.Width, Bmp.Height);
            //Bmp.GaussianBlur(ref Rect, 150, false);
            //Desktop_Background_Blur = Bitmap2BitmapImage.BitmapToBitmapImage(Bmp, ImageFormat.Png);
            #endregion

            Helper.Conf.LoadInfo(globalInfo, $"{globalInfo.WorkBase}\\Conf\\config.xml"); // 加载配置
            mwh.mainwin = mainWin;
            mainWin.Init();
            if (!Directory.Exists($"{globalInfo.WorkBase}\\Log"))
                Directory.CreateDirectory($"{globalInfo.WorkBase}\\Log"); // 创建日志文件夹
            Log("App started.");
            if (!globalInfo.hideWhenStart) mainWin.Show(); // 显示主窗体
            else
            {
                mainWin.Show();
                mainWin.Hide();
            }
        }

        /// <summary>
        /// 写入日志函数
        /// </summary>
        /// <param name="content">内容</param>
        public static void Log(string content) => FileHelper.Append($"{globalInfo.WorkBase}\\Log\\" +
            $"KitX-{globalInfo.runType}-{DateTime.Now:yyyy-MM-dd}.log",
            $"{DateTime.Now:HH:mm:ss} - {content}");

        /// <summary>
        /// 更改语言
        /// </summary>
        /// <param name="fileName">语言文件名</param>
        public static void ChangeLanguage(string fileName)
        {
            globalInfo.using_lang = fileName;
            ResourceDictionary langRd = new()
            {
                Source = new Uri($"{globalInfo.WorkBase}\\Resources\\Languages\\{fileName}.xaml", UriKind.Absolute)
            };
            ResourceDictionary rd = Current.Resources;
            ResourceDictionary? tmp = null;
            foreach (ResourceDictionary item in rd.MergedDictionaries)
            {
                if (item.Source.ToString().IndexOf("Languages") != -1)
                {
                    tmp = item;
                    break;
                }
            }
            rd.MergedDictionaries.Remove(tmp);
            rd.MergedDictionaries.Add(langRd);
        }

        /// <summary>
        /// 主题枚举选项
        /// </summary>
        public enum KitX_Theme
        {
            Light = 0,
            Dark = 1
        }

        /// <summary>
        /// 更改主题
        /// </summary>
        /// <param name="ktheme">主题</param>
        public static void ChangeTheme(KitX_Theme ktheme)
        {
            globalInfo.using_theme = ktheme;
            List<ResourceDictionary> willDel = new();
            ResourceDictionary rd = Current.Resources;
            foreach (ResourceDictionary item in rd.MergedDictionaries)
            {
                var src = item.Source.ToString();
                if (src.IndexOf("MaterialDesign") != -1 || src.IndexOf("HandyControl") != -1)
                    willDel.Add(item);
            }
            foreach (ResourceDictionary item in willDel)
                rd.MergedDictionaries.Remove(item);
            rd.MergedDictionaries.Add(new ResourceDictionary()
            {
                Source = new Uri($"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.{(ktheme == KitX_Theme.Light ? "Light" : "Dark")}.xaml", UriKind.Absolute)
            });
            rd.MergedDictionaries.Add(ktheme == KitX_Theme.Light ? ResourceHelper.GetSkin(HandyControl.Data.SkinType.Default) : ResourceHelper.GetSkin(HandyControl.Data.SkinType.Dark));
            foreach (string item in GlobalInfo.NecessaryDict)
            {
                rd.MergedDictionaries.Add(new ResourceDictionary()
                {
                    Source = new Uri(item, UriKind.Absolute)
                });
            }
            mainWin.OnApplyTemplate();
        }

        /// <summary>
        /// 通知保存配置文件
        /// </summary>
        public static void ToastSaveConf()
        {
            Helper.Conf.SaveInfo(globalInfo, $"{globalInfo.WorkBase}\\Conf\\config.xml");
        }
    }
}

#pragma warning restore CA2211 // 非常量字段应当不可见
