using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using File = System.IO.File;

namespace KitX_Installer_for_Windows_in.NET_Framework
{
    public partial class UninstallForm : Form
    {
        public UninstallForm()
        {
            InitializeComponent();

            if (MessageBox.Show("Are you sure to remove all KitX components and KitX?\r\n" +
                "您是否要完全移除 KitX 及其组件?", "Tip | 提示", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) == DialogResult.OK)
            {
                new Thread(() =>
                {
                    Uninstall(this);
                }).Start();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Drawer.PixelBackground(e.Graphics, Drawer.Theme.Light);
        }

        private void UpdatePro(int value)
        {
            ProgressBar_Uninstall.Invoke(new Action(() =>
            {
                if (ProgressBar_Uninstall.Style != ProgressBarStyle.Blocks)
                    ProgressBar_Uninstall.Style = ProgressBarStyle.Blocks;
                ProgressBar_Uninstall.Value = value;
            }));
        }

        public static void Uninstall(UninstallForm form = null)
        {
            TaskbarManager.SetProgressState(TaskbarProgressBarState.Indeterminate);

            RegistryKey software = Registry.LocalMachine.OpenSubKey("SOFTWARE", true)
                .OpenSubKey("Microsoft", true).OpenSubKey("Windows", true)
                .OpenSubKey("CurrentVersion", true);
            RegistryKey appPaths = software.OpenSubKey("App Paths", true);
            RegistryKey uninstall = software.OpenSubKey("Uninstall", true);

            RegistryKey appPaths_KitX = appPaths.OpenSubKey("KitX Dashboard.exe");
            string installPath = (string)appPaths_KitX.GetValue("Path");
            appPaths_KitX.Dispose();

            Process[] processes = Process.GetProcesses();
            foreach (var item in processes)
            {
                try
                {
                    if (item.MainModule.FileName.StartsWith(installPath))
                        item.Kill();
                }
                catch
                {

                }
            }

            Thread.Sleep(2000);
            TaskbarManager.SetProgressState(TaskbarProgressBarState.Normal);
            TaskbarManager.SetProgressValue(10, 100);
            if (form != null) form.UpdatePro(10);
            Thread.Sleep(100);

            DeleteFolderAndFiles(installPath);
            Directory.Delete(installPath, true);

            TaskbarManager.SetProgressValue(70, 100);
            if (form != null) form.UpdatePro(70);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string startmn = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            if (File.Exists($"{desktop}\\KitX Dashboard.lnk"))
                File.Delete($"{desktop}\\KitX Dashboard.lnk");

            if (File.Exists($"{startmn}\\Crequency KitX Dashboard.lnk"))
                File.Delete($"{startmn}\\Crequency KitX Dashboard.lnk");

            TaskbarManager.SetProgressValue(75, 100);
            if (form != null) form.UpdatePro(75);
            Thread.Sleep(500);

            appPaths.DeleteSubKeyTree("KitX Dashboard.exe");
            uninstall.DeleteSubKeyTree("KitX");

            Registry.ClassesRoot.DeleteSubKeyTree(".kxp");
            Registry.ClassesRoot.DeleteSubKeyTree("KitX.kxp");

            appPaths.Dispose();
            uninstall.Dispose();
            software.Dispose();

            TaskbarManager.SetProgressValue(100, 100);
            if (form != null) form.UpdatePro(100);
            Thread.Sleep(1500);

            if (form != null)
                form.Invoke(new Action(() =>
                {
                    MessageBox.Show("KitX Uninstalled!\r\nKitX 已成功卸载.", "Tip | 提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    form.Close();
                }));
        }

        internal static void DeleteFolderAndFiles(string path)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            foreach (var item in directoryInfo.GetFiles())
            {
                item.Delete();
            }

            DirectoryInfo[] subDirs = directoryInfo.GetDirectories();
            foreach (var item in subDirs)
            {
                DeleteFolderAndFiles(item.FullName);
                item.Delete(true);
            }
        }
    }
}
