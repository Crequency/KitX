using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

#pragma warning disable CS8602 // 解引用可能出现空引用。

namespace KitX.Helper
{
    public class Conf
    {
        private const string Xpath_mainwin_size = "/conf/app/mainwin/size";
        private const string Xpath_mainwin_loca = "/conf/app/mainwin/location";
        private const string Xpath_mainwin_hidewenstart = "/conf/app/mainwin/hideWhenStart";
        private const string Xpath_mainwin_topmost = "/conf/app/mainwin/topMost";
        private const string Xpath_app_pg = "/conf/app/plugins";
        private const string Xpath_app_firstuse = "/conf/app/firstUse";
        private const string Xpath_app_theme = "/conf/app/theme";
        private const string Xpath_app_lang = "/conf/app/lang";

        /// <summary>
        /// 读取配置文件并加载到内存
        /// </summary>
        /// <param name="ginfo">全局信息存储池</param>
        /// <param name="fpath">配置文件路径</param>
        public static void LoadInfo(GlobalInfo ginfo, string fpath)
        {
            XmlDocument xmldoc = new();
            xmldoc.Load(fpath);
            ginfo.firstUse = bool.Parse(xmldoc.SelectSingleNode(Xpath_app_firstuse).InnerText);
            if (xmldoc.SelectSingleNode(Xpath_app_theme).InnerText.Equals("Dark"))
                App.ChangeTheme(App.KitX_Theme.Dark);
            App.ChangeLanguage($"{xmldoc.SelectSingleNode(Xpath_app_lang).Attributes["lang"].InnerText}");
            if (!xmldoc.SelectSingleNode(Xpath_mainwin_size).Attributes["width"].InnerText.Equals("default"))
                App.mainWin.Width = double.Parse(xmldoc.SelectSingleNode(Xpath_mainwin_size)
                    .Attributes["width"].InnerText);
            if (!xmldoc.SelectSingleNode(Xpath_mainwin_size).Attributes["height"].InnerText.Equals("default"))
                App.mainWin.Width = double.Parse(xmldoc.SelectSingleNode(Xpath_mainwin_size)
                    .Attributes["height"].InnerText);
            if (!xmldoc.SelectSingleNode(Xpath_mainwin_loca).Attributes["center"].InnerText.Equals("True"))
            {
                App.mainWin.Left = double.Parse(xmldoc.SelectSingleNode(Xpath_mainwin_loca)
                    .Attributes["X"].InnerText);
                App.mainWin.Top = double.Parse(xmldoc.SelectSingleNode(Xpath_mainwin_loca)
                    .Attributes["Y"].InnerText);
            }
            else App.mainWin.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            ginfo.hideWhenStart = bool.Parse(xmldoc.SelectSingleNode(Xpath_mainwin_hidewenstart).InnerText);
            App.mainWin.Topmost = bool.Parse(xmldoc.SelectSingleNode(Xpath_mainwin_topmost).InnerText);
            ginfo.pgpath = xmldoc.SelectSingleNode(Xpath_app_pg).Attributes["location"].InnerText;
            ginfo.pgcache = xmldoc.SelectSingleNode(Xpath_app_pg).Attributes["cache"].InnerText;
        }

        /// <summary>
        /// 读取全局信息存储池并存储到配置文件
        /// </summary>
        /// <param name="ginfo">全局信息存储池</param>
        /// <param name="fpath">配置文件路径</param>
        public static void SaveInfo(GlobalInfo ginfo, string fpath)
        {
            XmlDocument xmldoc = new();
            xmldoc.Load(fpath);
            xmldoc.SelectSingleNode(Xpath_app_firstuse).InnerText = ginfo.firstUse ? "True" : "False";
            switch (ginfo.using_theme)
            {
                case App.KitX_Theme.Light: xmldoc.SelectSingleNode(Xpath_app_theme).InnerText = "Light"; break;
                case App.KitX_Theme.Dark: xmldoc.SelectSingleNode(Xpath_app_theme).InnerText = "Dark"; break;
            }
            xmldoc.SelectSingleNode(Xpath_app_lang).Attributes["lang"].InnerText = ginfo.using_lang;

            if (App.mainWin.Width != 1080) xmldoc.SelectSingleNode(Xpath_mainwin_size)
                    .Attributes["width"].InnerText = App.mainWin.ActualWidth.ToString();
            else xmldoc.SelectSingleNode(Xpath_mainwin_size).Attributes["width"].InnerText = "default";

            if (App.mainWin.Height != 700) xmldoc.SelectSingleNode(Xpath_mainwin_size)
                    .Attributes["height"].InnerText = App.mainWin.ActualHeight.ToString();
            else xmldoc.SelectSingleNode(Xpath_mainwin_size).Attributes["height"].InnerText = "default";

            if (App.mainWin.Left != (int)(GlobalInfo.WorkAreaWidth - App.mainWin.Width) / 2)
                xmldoc.SelectSingleNode(Xpath_mainwin_loca).Attributes["X"]
                    .InnerText = App.mainWin.Left.ToString();
            else xmldoc.SelectSingleNode(Xpath_mainwin_loca).Attributes["center"].InnerText = "False";

            if (App.mainWin.Top != (int)(GlobalInfo.WorkAreaHeight - App.mainWin.Height) / 2)
                xmldoc.SelectSingleNode(Xpath_mainwin_loca).Attributes["Y"]
                    .InnerText = App.mainWin.Top.ToString();
            else xmldoc.SelectSingleNode(Xpath_mainwin_loca).Attributes["center"].InnerText = "False";

            xmldoc.SelectSingleNode(Xpath_mainwin_loca).Attributes["center"].InnerText =
                App.mainWin.Left == (int)(GlobalInfo.WorkAreaWidth - App.mainWin.Width) / 2
                && App.mainWin.Top == (int)(GlobalInfo.WorkAreaHeight - App.mainWin.Height) / 2
                ? "True" : "False";
            xmldoc.SelectSingleNode(Xpath_mainwin_hidewenstart).InnerText = ginfo.hideWhenStart ? "True" : "False";
            xmldoc.SelectSingleNode(Xpath_mainwin_topmost).InnerText = App.mainWin.Topmost ? "True" : "False";
            xmldoc.SelectSingleNode(Xpath_app_pg).Attributes["location"].InnerText = ginfo.pgpath;
            xmldoc.SelectSingleNode(Xpath_app_pg).Attributes["cache"].InnerText = ginfo.pgcache;
            xmldoc.Save(fpath);
        }
    }
}

#pragma warning restore CS8602 // 解引用可能出现空引用。
