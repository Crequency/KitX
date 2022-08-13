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
            else BeginInstall();
        }

        private void UpdateTip(string text) => Label_Tip.Invoke(new Action(() =>
            { Label_Tip.Text = text; })
        );

        private Thread Thread_Install, Thread_Cancel;

        private void InstallProcess()
        {
            UpdateTip("开始安装 ...");

            string linkbase = "https://source.catrol.cn/download/apps/kitx/latest/";
            string filepath = @"C:\kitx-latest.zip";

            WebClient webClient = new WebClient();

            while (!File.Exists(filepath))
            {
                try
                {
                    webClient.DownloadFile($"{linkbase}kitx-win-x64-latest-single.zip", filepath);
                }
                catch
                {

                }

                if (!File.Exists(filepath))
                {
                    if (MessageBox.Show("Download failed! | 下载失败", "KitX",
                        MessageBoxButtons.RetryCancel, MessageBoxIcon.Error)
                        == DialogResult.Cancel)
                    {
                        webClient.Dispose();
                        BeginCancel();
                        return;
                    }
                }
            }

            webClient.Dispose();



            MessageBox.Show("Install succeed! | 安装成功", "KitX",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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
