using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Net;
using System.IO;
using Ionic.Zip;
using System.Diagnostics;
using ThreadState = System.Threading.ThreadState;

namespace KitX_Installer_for_Windows_in.NET_Framework
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

            //Paint += (_, e) =>
            //{
            //    PixelBackground(e.Graphics);

            //    e.Graphics.CopyFromScreen(Left, Top, Left, Top, new Size(800, 400),
            //        CopyPixelOperation.SourceCopy);
            //};

            Thread_Install = new Thread(InstallProcess);
            Thread_Cancel = new Thread(CancelProcess);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Drawer.PixelBackground(e.Graphics);
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

            WebClient webClient = new WebClient();

            while (!File.Exists(filepath))
            {
                UpdateTip("正在下载 ...");
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

            UpdateTip("下载完毕, 正在解压 ...");
            Invoke(new Action(() =>
            {
                ProgressBar_Installing.Style = ProgressBarStyle.Blocks;
                ProgressBar_Installing.Value = 0;
            }));
            UpdatePro(10);

            ZipFile zip = new ZipFile();
            try
            {
                zip = ZipFile.Read(filepath);
                UpdatePro(15);
                zip.ExtractAll(stfolder, ExtractExistingFileAction.OverwriteSilently);
                UpdatePro(45);
                zip.Dispose();
                UpdatePro(50);
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
            UpdatePro(55);

            //TODO: 更新注册表项

            UpdatePro(75);

            //TODO: 创建桌面和开始菜单的快捷方式

            UpdatePro(95);


            UpdatePro(100);
            Invoke(new Action(() =>
            {
                MessageBox.Show("Install succeed! | 安装成功", "KitX",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));

            Process process = new Process();
            process.StartInfo.FileName = $"{stfolder}\\KitX Dashboard.exe";
            process.StartInfo.WorkingDirectory = stfolder;
            process.Start();
            Invoke(new Action(() => { Close(); }));
        }

        private void CancelProcess()
        {
            Invoke(new Action(() =>
            {
                Btn_BeginInstall.Enabled = false;
                ProgressBar_Installing.Style = ProgressBarStyle.Marquee;
                ProgressBar_Installing.Value = 50;
            }));

            UpdateTip("正在取消 ...");

            Thread_Install.Abort();

            while (Thread_Install.ThreadState != ThreadState.Aborted) { }

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
