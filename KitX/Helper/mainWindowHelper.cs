using Aska.WPF.Effect.Blur;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Aska.WPF.Converter;
using Image = System.Windows.Controls.Image;
using Rectangle = System.Drawing.Rectangle;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Threading;

#pragma warning disable CS8601 // 引用类型赋值可能为 null。
#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。

namespace KitX.Helper
{
    public class MainWindowHelper
    {
        public MainWindow mainwin;

        public Dictionary<string, FrameworkElement> FoundControls = new();

        /// <summary>
        /// 在主窗体的控件中查找 - 带记忆优化
        /// </summary>
        /// <param name="name">控件名称</param>
        /// <returns></returns>
        private FrameworkElement Find(string name)
        {
            if (FoundControls.ContainsKey(name))
                return FoundControls[name];
            else
            {
                FoundControls[name] = mainwin.Template.FindName(name, mainwin) as FrameworkElement;
                return FoundControls[name];
            }
        }

        /// <summary>
        /// 初始化主窗体事件
        /// </summary>
        public void InitEvent()
        {
            mainwin.Activated += (_, _) => (Find("TitleImg") as Image).Source = new BitmapImage(new Uri($"{App.globalInfo.WorkBase}\\Resources\\KitX.png", UriKind.Absolute));
            mainwin.Deactivated += (_, _) => (Find("TitleImg") as Image).Source = new BitmapImage(new Uri($"{App.globalInfo.WorkBase}\\Resources\\KitX_NoFocus.png", UriKind.Absolute));
            //mainwin.SizeChanged += (_, _) => UpdateImage();
            mainwin.Loaded += (_, _) =>
            {
                (Find("PageFrame") as Frame).Navigate(mainwin.page_home);
                (Find("panel") as Grid).Width = 0;
                (Find("CB_Languages") as ComboBox).SelectionChanged += (x, _) =>
                {
                    switch ((x as ComboBox).SelectedIndex)
                    {
                        case 0: // 简体中文
                            App.ChangeLanguage("zh-cn");
                            break;
                        case 1: // 繁體中文
                            App.ChangeLanguage("zh-cnt");
                            break;
                        case 2: // English(US)
                            App.ChangeLanguage("en-us");
                            break;
                        case 3: // 日本
                            App.ChangeLanguage("ja-jp");
                            break;
                    }
                };
                (Find("CB_Themes") as ComboBox).SelectionChanged += (x, _) =>
                {
                    switch ((x as ComboBox).SelectedIndex)
                    {
                        case 0: // 浅色
                            App.ChangeTheme(App.KitX_Theme.Light);
                            break;
                        case 1: // 深色
                            App.ChangeTheme(App.KitX_Theme.Dark);
                            break;
                    }
                };
                (Find("KeepToper") as ToggleButton).Checked += (_, _) =>
                {
                    mainwin.Topmost = true;
                };
                (Find("KeepToper") as ToggleButton).Unchecked += (_, _) =>
                {
                    mainwin.Topmost = false;
                };
                //(Find("MainWindow_Background") as Image).Source = App.Desktop_Background_Blur;
            };
            mainwin.Closed += (_, _) =>
            {
                App.IsAlive = false;
            };

            #region 定时更新高斯模糊背景
            //new Thread(() =>
            //{
            //    double lw = mainwin.ActualWidth, lh = mainwin.ActualHeight;
            //    while (App.IsAlive)
            //    {
            //        if (mainwin.ActualWidth != lw || mainwin.ActualHeight != lh)
            //        {
            //            lw = mainwin.ActualWidth;
            //            lh = mainwin.ActualHeight;
            //            mainwin.Dispatcher.BeginInvoke(new Action(() =>
            //            {
            //                UpdateImage();
            //            }));
            //        }
            //        Thread.Sleep(1000);
            //    }
            //}).Start(); 
            #endregion
        }

        /// <summary>
        /// 更新高斯模糊壁纸
        /// </summary>
        //public void UpdateImage()
        //{
        //    if (mainwin.WindowState == WindowState.Normal)
        //    {
        //        using Bitmap tmp_bitmap = BitmapImage2Bitmap.BitmapImageToBitmap(App.Desktop_Background_Blur);
        //        using Bitmap bitmap = new Bitmap(tmp_bitmap, (int)GlobalInfo.ScreenWidth, (int)GlobalInfo.ScreenHeight);
        //        tmp_bitmap.Dispose();
        //        Rectangle rect = new Rectangle((int)mainwin.Left, (int)mainwin.Top,
        //            (int)mainwin.ActualWidth, (int)mainwin.ActualHeight);
        //        using Bitmap nbitmap = bitmap.Clone(rect, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        //        bitmap.Dispose();
        //        (Find("MainWindow_Background") as Image).Source =
        //            Bitmap2BitmapImage.BitmapToBitmapImage(nbitmap, ImageFormat.Png);
        //        nbitmap.Dispose();
        //    }
        //    else
        //    {
        //        (Find("MainWindow_Background") as Image).Source = App.Desktop_Background_Blur;
        //    }
        //}
    }
}

#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。
#pragma warning restore CS8601 // 引用类型赋值可能为 null。