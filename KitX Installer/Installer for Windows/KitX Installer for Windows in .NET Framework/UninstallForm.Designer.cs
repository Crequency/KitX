namespace KitX_Installer_for_Windows_in.NET_Framework
{
    partial class UninstallForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(UninstallForm));
            this.ProgressBar_Uninstall = new System.Windows.Forms.ProgressBar();
            this.SuspendLayout();
            // 
            // ProgressBar_Uninstall
            // 
            this.ProgressBar_Uninstall.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.ProgressBar_Uninstall.Location = new System.Drawing.Point(0, 426);
            this.ProgressBar_Uninstall.Name = "ProgressBar_Uninstall";
            this.ProgressBar_Uninstall.Size = new System.Drawing.Size(784, 5);
            this.ProgressBar_Uninstall.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.ProgressBar_Uninstall.TabIndex = 0;
            this.ProgressBar_Uninstall.Value = 50;
            // 
            // UninstallForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Black;
            this.ClientSize = new System.Drawing.Size(784, 431);
            this.Controls.Add(this.ProgressBar_Uninstall);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "UninstallForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "KitX Uninstaller | KitX 卸载向导";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ProgressBar ProgressBar_Uninstall;
    }
}