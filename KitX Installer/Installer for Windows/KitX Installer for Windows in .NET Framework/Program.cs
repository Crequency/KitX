using System;
using System.Windows.Forms;

namespace KitX_Installer_for_Windows_in.NET_Framework
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            bool isUninstall = false;
            bool silentUninstall = false;

            foreach (var item in args)
            {
                switch (item)
                {
                    case "--uninstall":
                        isUninstall = true;
                        break;
                    case "--silent":
                        silentUninstall = true;
                        break;
                }
            }

            if (isUninstall)
            {
                if (silentUninstall)
                {
                    UninstallForm.Uninstall();
                }
                else
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new UninstallForm());
                }
            }
            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
