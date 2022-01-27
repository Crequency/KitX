using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Aska.WPF.Animation;

#pragma warning disable CS8602 // 解引用可能出现空引用。
#pragma warning disable CS8604 // 引用类型参数可能为 null。
#pragma warning disable CS8603 // 可能返回 null 引用。
#pragma warning disable CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
#pragma warning disable CA1822 // 将成员标记为 static

namespace KitX
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Helper.MainWindowHelper helper;

        #region [预]界面预生成
        public View.Home page_home = new();
        public View.Lib page_lib = new();
        public View.Market page_market = new();
        public View.Settings page_settings = new();
        #endregion

        /// <summary>
        /// 操作面板是否已经展开
        /// </summary>
        private bool oper_expanded = false;

        /// <summary>
        /// 已经展开的面板是
        /// </summary>
        private string panel_now_expanded = "";

        /// <summary>
        /// 构造函数
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            helper = App.mwh;
            helper.InitEvent();
        }

        /// <summary>
        /// 通过名字查找控件
        /// </summary>
        /// <param name="name">控件名字</param>
        /// <returns>控件基类</returns>
        private FrameworkElement Find(string name) => Template.FindName(name, this) as FrameworkElement;

        /// <summary>
        /// 窗口移动事件
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Move(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
            //helper.UpdateImage();
        }

        /// <summary>
        /// 取消窗体关闭事件
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(CancelEventArgs e)
        {
            App.ToastSaveConf();
            e.Cancel = true;
            Hide();
        }

        /// <summary>
        /// 重构 - 基础动画单元
        /// </summary>
        /// <param name="obj">控件</param>
        /// <param name="dp">属性</param>
        /// <param name="a1">从</param>
        /// <param name="a2">到</param>
        private void ExecuteAnimation(FrameworkElement obj, DependencyProperty dp, double a1, double a2) => obj.BeginAnimation(dp,
                AnimationHelper.CreateAnimation(new TimeSpan(0, 0, 0, 0, 400),
                    a1, a2, System.Windows.Media.Animation.FillBehavior.HoldEnd,
                    AnimationHelper.EasingFunction.Cubic,
                    System.Windows.Media.Animation.EasingMode.EaseInOut, 0, 0));

        /// <summary>
        /// 交换面板
        /// </summary>
        /// <param name="cmd"></param>
        private void Panel_Swap(string cmd)
        {
            switch (cmd)
            {
                case "options":
                    if ((Find("panel_user") as Grid).Opacity == 1)
                        ExecuteAnimation(Find("panel_user") as Grid, OpacityProperty, 1, 0);
                    if ((Find("panel_control") as ScrollViewer).Opacity == 0)
                        ExecuteAnimation(Find("panel_control") as ScrollViewer, OpacityProperty, 0, 1);
                    panel_now_expanded = "options";
                    break;
                case "userinfo":
                    if ((Find("panel_user") as Grid).Opacity == 0)
                        ExecuteAnimation(Find("panel_user") as Grid, OpacityProperty, 0, 1);
                    if ((Find("panel_control") as ScrollViewer).Opacity == 1)
                        ExecuteAnimation(Find("panel_control") as ScrollViewer, OpacityProperty, 1, 0);
                    panel_now_expanded = "userinfo";
                    break;
            }
        }

        /// <summary>
        /// 执行命令
        /// </summary>
        /// <param name="sender">触发事件的控件</param>
        /// <param name="e">事件参数(放弃的)</param>
        private void ExecuteEvent(object sender, RoutedEventArgs e)
        {

            string[] cmd = (sender as FrameworkElement).Tag.ToString().Split(":");
            switch (cmd[0])
            {
                case "goto":
                    switch (cmd[1])
                    {
                        case "Home":
                            (Find("PageFrame") as Frame).Navigate(page_home);
                            break;
                        case "Lib":
                            (Find("PageFrame") as Frame).Navigate(page_lib);
                            break;
                        case "Market":
                            (Find("PageFrame") as Frame).Navigate(page_market);
                            break;
                        case "Settings":
                            (Find("PageFrame") as Frame).Navigate(page_settings);
                            break;
                    }
                    break;
                case "uri": Process.Start(cmd[1]); break;
                case "show":
                    if (oper_expanded)
                    {
                        if (cmd[1].Equals(panel_now_expanded))
                        {
                            (Find("panel") as Grid).BeginAnimation(WidthProperty,
                            BasicHelper.UI.Animation.AnimationHelper.CreateAnimation(new TimeSpan(0, 0, 0, 0, 400),
                                300, 0, System.Windows.Media.Animation.FillBehavior.HoldEnd,
                                BasicHelper.UI.Animation.AnimationHelper.EasingFunction.Cubic,
                                System.Windows.Media.Animation.EasingMode.EaseInOut, 0, 0));
                            oper_expanded = false;
                        }
                        else
                        {
                            Panel_Swap(cmd[1]);
                        }
                    }
                    else
                    {
                        (Find("panel") as Grid).BeginAnimation(WidthProperty,
                        BasicHelper.UI.Animation.AnimationHelper.CreateAnimation(new TimeSpan(0, 0, 0, 0, 400),
                            0, 300, System.Windows.Media.Animation.FillBehavior.HoldEnd,
                            BasicHelper.UI.Animation.AnimationHelper.EasingFunction.Cubic,
                            System.Windows.Media.Animation.EasingMode.EaseInOut, 0, 0));
                        oper_expanded = true;
                        Panel_Swap(cmd[1]);
                    }
                    break;
            }
        }
    }
}

#pragma warning restore CA1822 // 将成员标记为 static
#pragma warning restore CS8618 // 在退出构造函数时，不可为 null 的字段必须包含非 null 值。请考虑声明为可以为 null。
#pragma warning restore CS8603 // 可能返回 null 引用。
#pragma warning restore CS8604 // 引用类型参数可能为 null。
#pragma warning restore CS8602 // 解引用可能出现空引用。