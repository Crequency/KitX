using Ionic.Zip;
using IWshRuntimeLibrary;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.AccessControl;
using System.Threading;
using System.Windows.Forms;
using File = System.IO.File;
using ThreadState = System.Threading.ThreadState;

namespace KitX_Installer_for_Windows_in.NET_Framework
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

            Thread_Install = new Thread(InstallProcess);
            Thread_Cancel = new Thread(CancelProcess);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Drawer.PixelBackground(e.Graphics, Drawer.Theme.Dark);
        }

        private bool InstallingStarted = false;

        private bool CanExecute = true;

        private void Btn_BeginInstall_Click(object sender, EventArgs e)
        {
            if (InstallingStarted)
            {
                if (CanExecute) BeginCancel();
            }
            else
            {
                try
                {
                    string folder = "";
                    try
                    {
                        folder = Path.GetFullPath(TextBox_InstallPath.Text);
                    }
                    catch
                    {
                        MessageBox.Show("Illegal Path | 非法路径", "Error | 错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    DirectoryInfo directory = new DirectoryInfo(folder);
                    if (!directory.Exists)
                    {
                        directory.Create();
                    }
                }
                catch (Exception o)
                {
                    UpdateTip($"未知异常: {o.Message}");
                }
                BeginInstall();
            }
        }

        private void UpdateTip(string text) => Label_Tip.Invoke(new Action(() =>
            { Label_Tip.Text = text; })
        );

        private void UpdatePro(int value) => ProgressBar_Installing.Invoke(new Action(() =>
            { ProgressBar_Installing.Value = value; })
        );

        private Thread Thread_Install, Thread_Cancel;

        private void InstallProcess()
        {
            UpdateTip("开始安装 ...");

            string stfolder = Path.GetFullPath(TextBox_InstallPath.Text);
            string linkbase = "https://source.catrol.cn/download/apps/kitx/latest/";
            string filepath = $"{stfolder}\\kitx-latest.zip";

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string startmenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            string shortcutName = "KitX Dashboard.lnk";
            string shortcutNameStartMenu = "Crequency KitX Dashboard.lnk";
            string targetPath = $"{stfolder}\\KitX Dashboard.exe";
            string modulePath = $"{stfolder}\\KitX Dashboard.dll";
            string uninstallPath = $"C:\\Windows\\Installer\\KitX Installer.exe";
            string descr = "KitX Dashboard | KitX 控制面板";
            string uninstallString = $"\"{uninstallPath}\" --uninstall";
            string helpLink = "https://apps.catrol.cn/kitx/help/";
            string infoLink = "https://apps.catrol.cn/kitx/";

            WebClient webClient = new WebClient();

            TaskbarManager.SetProgressState(TaskbarProgressBarState.Indeterminate);

            while (!File.Exists(filepath))
            {
                UpdateTip("正在下载 ...");
                Thread.Sleep(400);
                try
                {
                    webClient.DownloadFile($"{linkbase}kitx-win-x64-latest-single.zip", filepath);
                }
                catch (Exception e)
                {
                    UpdateTip($"下载发生异常, 请检查网络环境: {e.Message}");
                }

                if (!File.Exists(filepath))
                {
                    bool choosed = false;
                    Invoke(new Action(() =>
                    {
                        if (MessageBox.Show("Download failed! | 下载失败", "KitX",
                            MessageBoxButtons.RetryCancel, MessageBoxIcon.Error)
                            == DialogResult.Cancel)
                        {
                            webClient.Dispose();
                            BeginCancel();
                            return;
                        }
                        choosed = true;
                    }));

                    while (!choosed) { }
                }
            }

            webClient.Dispose();

            TaskbarManager.SetProgressState(TaskbarProgressBarState.Normal);

            UpdateTip("下载完毕, 正在解压 ...");
            Invoke(new Action(() =>
            {
                ProgressBar_Installing.Style = ProgressBarStyle.Blocks;
                ProgressBar_Installing.Value = 0;
            }));
            UpdatePro(40);
            TaskbarManager.SetProgressValue(40, 100);
            Thread.Sleep(500);

            ZipFile zip = new ZipFile();
            try
            {
                UpdateTip("读取压缩包 ...");
                Thread.Sleep(200);
                zip = ZipFile.Read(filepath);
                TaskbarManager.SetProgressValue(45, 100);
                UpdatePro(45);
                UpdateTip("解压压缩包 ...");
                Thread.Sleep(200);
                zip.ExtractAll(stfolder, ExtractExistingFileAction.OverwriteSilently);
                TaskbarManager.SetProgressValue(50, 100);
                UpdatePro(50);
                zip.Dispose();
                UpdateTip("解压完毕, 更新注册表 ...");
                Thread.Sleep(200);
            }
            catch (Exception e)
            {
                zip.Dispose();
                UpdateTip($"解压失败: {e.Message}");
                Invoke(new Action(() =>
                {
                    MessageBox.Show("Cancel Setup! | 安装取消", "KitX",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
                BeginCancel();
            }
            File.Delete(filepath);
            TaskbarManager.SetProgressValue(60, 100);
            UpdatePro(60);

            UpdateTip("打开注册表 ...");
            Thread.Sleep(200);

            try
            {
                RegistryKey software = Registry.LocalMachine.OpenSubKey("SOFTWARE", true)
                    .OpenSubKey("Microsoft", true).OpenSubKey("Windows", true)
                    .OpenSubKey("CurrentVersion", true);
                RegistryKey appPaths = software.OpenSubKey("App Paths", true);
                RegistryKey uninstall = software.OpenSubKey("Uninstall", true);

                #region 更新 AppPaths
                UpdateTip("写入注册表 App Paths ...");
                Thread.Sleep(200);
                {
                    RegistryKey appPaths_KitX = appPaths.CreateSubKey("KitX Dashboard.exe");
                    appPaths_KitX.SetValue("", targetPath);
                    appPaths_KitX.SetValue("Path", stfolder);
                    appPaths_KitX.Dispose();
                }
                #endregion

                TaskbarManager.SetProgressValue(65, 100);
                UpdatePro(65);

                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(targetPath);
                string version = fvi.ProductVersion;

                #region 更新 控制面板中所对应的程序信息
                UpdateTip("写入注册表 Uninstall ...");
                Thread.Sleep(200);
                {
                    RegistryKey uninstall_KitX = uninstall.CreateSubKey("KitX");
                    uninstall_KitX.SetValue("DisplayName", "KitX Dashboard");
                    uninstall_KitX.SetValue("DisplayVersion", version);
                    uninstall_KitX.SetValue("DisplayIcon", targetPath);
                    uninstall_KitX.SetValue("Publisher", "Crequency Studio");
                    uninstall_KitX.SetValue("InstallLocation", stfolder);
                    uninstall_KitX.SetValue("UninstallString", uninstallString);
                    uninstall_KitX.SetValue("QuietUninstallString", $"{uninstallString} --silent");
                    uninstall_KitX.SetValue("HelpLink", helpLink);
                    uninstall_KitX.SetValue("URLInfoAbout", infoLink);
                    uninstall_KitX.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstall_KitX.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    uninstall_KitX.SetValue("EstimatedSize", GetDirectoryLength(stfolder) / 1000,
                        RegistryValueKind.DWord);
                    uninstall_KitX.Dispose();
                }
                #endregion

                TaskbarManager.SetProgressValue(70, 100);
                UpdatePro(70);

                #region 更新 文件关联
                UpdateTip("写入注册表 文件关联 ...");
                Thread.Sleep(200);
                {
                    RegistryKey fileCon = Registry.ClassesRoot.CreateSubKey(".kxp");
                    fileCon.SetValue("", "KitX.kxp");
                    fileCon.SetValue("Content Type", "application/kitx-extensions-package");
                    RegistryKey filePro = Registry.ClassesRoot.CreateSubKey("KitX.kxp");
                    filePro.SetValue("", "KitX Extensions Package");
                    RegistryKey icon = filePro.CreateSubKey("DefaultIcon");
                    icon.SetValue("", $"{stfolder}\\Assets\\kxp.ico");
                    RegistryKey shell = filePro.CreateSubKey("Shell");
                    RegistryKey open = shell.CreateSubKey("Open");
                    open.SetValue("FriendlyAppName", "KitX");
                    RegistryKey com = open.CreateSubKey("Command");
                    com.SetValue("", $"\"{targetPath}\" --import-plugin \"%1\"");
                    com.Dispose();
                    open.Dispose();
                    shell.Dispose();
                    icon.Dispose();
                    filePro.Dispose();
                    fileCon.Dispose();
                }
                #endregion

                appPaths.Dispose();
                uninstall.Dispose();
            }
            catch (Exception e)
            {
                UpdateTip($"写入注册表失败: {e.Message}");
                Invoke(new Action(() =>
                {
                    MessageBox.Show("Cancel Setup! | 安装取消", "KitX",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
                BeginCancel();
            }

            TaskbarManager.SetProgressValue(75, 100);
            UpdatePro(75);

            UpdateTip("更新快捷方式 ...");
            Thread.Sleep(200);

            try
            {
                WshShell shell = new WshShell();
                CreateShortCut(ref shell, $"{desktop}\\{shortcutName}",
                    targetPath, stfolder, descr, targetPath);
                CreateShortCut(ref shell, $"{startmenu}\\{shortcutNameStartMenu}",
                    targetPath, stfolder, descr, targetPath);
            }
            catch (Exception e)
            {
                UpdateTip($"创建快捷方式失败: {e.Message}");
                Invoke(new Action(() =>
                {
                    MessageBox.Show("Cancel Setup! | 安装取消", "KitX",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
                BeginCancel();
            }

            TaskbarManager.SetProgressValue(80, 100);
            UpdatePro(80);

            UpdateTip("生成卸载程序 ...");
            Thread.Sleep(200);

            try
            {
                if (File.Exists(uninstallPath))
                {
                    File.Delete(uninstallPath);
                }
                string me = Process.GetCurrentProcess().MainModule.FileName;
                File.Copy(me, uninstallPath);
            }
            catch (Exception e)
            {
                UpdateTip($"生成卸载程序失败: {e.Message}");
                Invoke(new Action(() =>
                {
                    MessageBox.Show("Cancel Setup! | 安装取消", "KitX",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
                BeginCancel();
            }

            TaskbarManager.SetProgressValue(90, 100);
            UpdatePro(90);

            UpdateTip("更新安装目录权限 ...");
            Thread.Sleep(300);

            try
            {
                DirectoryInfo dir = new DirectoryInfo(stfolder);
                DirectorySecurity dirSecurity = dir.GetAccessControl(AccessControlSections.All);
                InheritanceFlags inherits = /*InheritanceFlags.None;*/
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                FileSystemAccessRule usersFileSystemAccessRule = new FileSystemAccessRule("Users",
                    FileSystemRights.FullControl, inherits, PropagationFlags.None,
                    AccessControlType.Allow);
                dirSecurity.ModifyAccessRule(AccessControlModification.Add, usersFileSystemAccessRule,
                    out bool isModified);
                dir.SetAccessControl(dirSecurity);

                if (!isModified)
                {
                    UpdateTip($"安装目录权限未能更改");
                    Invoke(new Action(() =>
                    {
                        MessageBox.Show("Cancel Setup! | 安装取消", "KitX",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                    BeginCancel();
                }
            }
            catch (Exception e)
            {
                UpdateTip($"安装目录权限更改失败: {e.Message}");
                Invoke(new Action(() =>
                {
                    MessageBox.Show("Cancel Setup! | 安装取消", "KitX",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
                BeginCancel();
            }

            TaskbarManager.SetProgressValue(100, 100);
            UpdatePro(100);

            UpdateTip("安装目录权限更新成功 ...");
            Thread.Sleep(500);

            UpdateTip("安装成功!");
            Invoke(new Action(() =>
            {
                MessageBox.Show("Install succeed! | 安装成功", "KitX",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                TaskbarManager.SetProgressState(TaskbarProgressBarState.NoProgress);
            }));

            try
            {
                Process.Start("cmd.exe", $"/C cd /d {stfolder} && runas.exe /TrustLevel:0x20000 \"{targetPath}\"");

                //Process.Start("explorer.exe", targetPath);

                //Process process = new Process();
                //process.StartInfo.FileName = $"{stfolder}\\KitX Dashboard.exe";
                //process.StartInfo.WorkingDirectory = stfolder;
                //process.StartInfo.UserName = Environment.UserName;
                //process.StartInfo.UseShellExecute = false;
                //process.Start();
            }
            catch (Exception e)
            {
                UpdateTip($"启动 KitX Dashboard 失败: {e.Message}");
                Invoke(new Action(() =>
                {
                    MessageBox.Show($"Failed to start KitX Dashboard!\r\n无法启动 KitX 仪表盘\r\n{e.Message}",
                        "Error | 错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
            Invoke(new Action(() => { Close(); }));
        }

        public static long GetDirectoryLength(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return 0;
            long len = 0;
            DirectoryInfo di = new DirectoryInfo(dirPath);
            foreach (FileInfo fi in di.GetFiles()) len += fi.Length;
            DirectoryInfo[] dis = di.GetDirectories();
            if (dis.Length > 0)
                for (int i = 0; i < dis.Length; ++i)
                    len += GetDirectoryLength(dis[i].FullName);
            return len;
        }

        /// <summary>
        /// 创建快捷方式
        /// </summary>
        /// <param name="location">快捷方式位置</param>
        /// <param name="targetPath">目标路径</param>
        /// <param name="workingDir">工作目录</param>
        /// <param name="descr">描述</param>
        /// <param name="iconPath">图标路径</param>
        /// <param name="windowStyle">窗口样式</param>
        private void CreateShortCut(ref WshShell shell, string location, string targetPath,
            string workingDir, string descr, string iconPath = null, int windowStyle = 1)
        {
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(location);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = descr;
            shortcut.WindowStyle = windowStyle;
            if (iconPath != null) shortcut.IconLocation = iconPath;
            shortcut.Save();
        }

        private void CancelProcess()
        {
            TaskbarManager.SetProgressState(TaskbarProgressBarState.Paused);

            Invoke(new Action(() =>
            {
                Btn_BeginInstall.Enabled = false;
                ProgressBar_Installing.Style = ProgressBarStyle.Marquee;
                ProgressBar_Installing.Value = 50;
            }));

            string stfolder = Path.GetFullPath(TextBox_InstallPath.Text);

            UpdateTip("正在取消 ...");

            Thread_Install.Abort();

            while (Thread_Install.ThreadState != ThreadState.Aborted) { }

            UpdateTip("正在回滚 ...");

            Directory.Delete(stfolder, true);

            TaskbarManager.SetProgressState(TaskbarProgressBarState.NoProgress);

            Invoke(new Action(() =>
            {
                UpdateTip("等待用户操作 ...");

                Btn_BeginInstall.Enabled = false;

                InstallingStarted = false;

                Set_Btn_BeginInstall_Install();
                TextBox_InstallPath.Enabled = true;
                ProgressBar_Installing.Visible = false;
                ProgressBar_Installing.Style = ProgressBarStyle.Continuous;
                ProgressBar_Installing.Value = 0;
            }));
        }

        private void BeginInstall()
        {
            CanExecute = false;
            InstallingStarted = true;

            Btn_BeginInstall.Enabled = false;
            TextBox_InstallPath.Enabled = false;

            Set_Btn_BeginInstall_Cancel();
            ProgressBar_Installing.Visible = true;
            ProgressBar_Installing.Style = ProgressBarStyle.Marquee;
            ProgressBar_Installing.Value = 30;

            if (Thread_Install.ThreadState != ThreadState.Unstarted)
                Thread_Install = new Thread(InstallProcess);
            Thread_Install.Start();

            while (Thread_Install.ThreadState == ThreadState.Unstarted) { }

            CanExecute = true;
        }

        private void BeginCancel()
        {
            if (Thread_Cancel.ThreadState != ThreadState.Unstarted)
                Thread_Cancel = new Thread(CancelProcess);
            Thread_Cancel.Start();

            while (Thread_Cancel.ThreadState == ThreadState.Unstarted) { }
        }

        private void Set_Btn_BeginInstall_Install()
        {
            AcceptButton = Btn_BeginInstall;
            CancelButton = null;
            Btn_BeginInstall.Enabled = true;
            Btn_BeginInstall.Text = "Install | 安装";
            Btn_BeginInstall.Size = new Size(180, 50);
            Btn_BeginInstall.Location = new Point(310, 480);
        }

        private void Set_Btn_BeginInstall_Cancel()
        {
            AcceptButton = null;
            CancelButton = Btn_BeginInstall;
            Btn_BeginInstall.Enabled = true;
            Btn_BeginInstall.Text = "Cancel Installing | 取消安装";
            Btn_BeginInstall.Size = new Size(300, 50);
            Btn_BeginInstall.Location = new Point(250, 480);
        }
    }
}
