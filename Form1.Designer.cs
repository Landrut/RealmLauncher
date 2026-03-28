namespace RealmLauncher
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lblConfigUrl = new System.Windows.Forms.Label();
            this.txtConfigUrl = new System.Windows.Forms.TextBox();
            this.lblSteamCmdTitle = new System.Windows.Forms.Label();
            this.lblSteamCmdStatus = new System.Windows.Forms.Label();
            this.btnCheckUpdates = new System.Windows.Forms.Button();
            this.btnCheckSteamCmd = new System.Windows.Forms.Button();
            this.lblConanExe = new System.Windows.Forms.Label();
            this.txtConanExe = new System.Windows.Forms.TextBox();
            this.btnBrowseConanExe = new System.Windows.Forms.Button();
            this.lblServerPassword = new System.Windows.Forms.Label();
            this.txtServerPassword = new System.Windows.Forms.TextBox();
            this.chkDisableIntro = new System.Windows.Forms.CheckBox();
            this.chkAutoSubscribe = new System.Windows.Forms.CheckBox();
            this.btnPlay = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.progressMods = new System.Windows.Forms.ProgressBar();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.toolTipOptions = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // lblConfigUrl
            // 
            this.lblConfigUrl.AutoSize = true;
            this.lblConfigUrl.Location = new System.Drawing.Point(12, 12);
            this.lblConfigUrl.Name = "lblConfigUrl";
            this.lblConfigUrl.Size = new System.Drawing.Size(122, 16);
            this.lblConfigUrl.TabIndex = 0;
            this.lblConfigUrl.Text = "URL JSON сервера";
            // 
            // txtConfigUrl
            // 
            this.txtConfigUrl.Location = new System.Drawing.Point(15, 31);
            this.txtConfigUrl.Name = "txtConfigUrl";
            this.txtConfigUrl.Size = new System.Drawing.Size(885, 22);
            this.txtConfigUrl.TabIndex = 1;
            // 
            // lblSteamCmdTitle
            // 
            this.lblSteamCmdTitle.AutoSize = true;
            this.lblSteamCmdTitle.Location = new System.Drawing.Point(12, 64);
            this.lblSteamCmdTitle.Name = "lblSteamCmdTitle";
            this.lblSteamCmdTitle.Size = new System.Drawing.Size(141, 16);
            this.lblSteamCmdTitle.TabIndex = 2;
            this.lblSteamCmdTitle.Text = "Проверка SteamCMD";
            // 
            // lblSteamCmdStatus
            // 
            this.lblSteamCmdStatus.AutoSize = true;
            this.lblSteamCmdStatus.Location = new System.Drawing.Point(12, 87);
            this.lblSteamCmdStatus.Name = "lblSteamCmdStatus";
            this.lblSteamCmdStatus.Size = new System.Drawing.Size(151, 16);
            this.lblSteamCmdStatus.TabIndex = 3;
            this.lblSteamCmdStatus.Text = "SteamCMD: неизвестно";
            // 
            // btnCheckUpdates
            // 
            this.btnCheckUpdates.Location = new System.Drawing.Point(300, 78);
            this.btnCheckUpdates.Name = "btnCheckUpdates";
            this.btnCheckUpdates.Size = new System.Drawing.Size(244, 32);
            this.btnCheckUpdates.TabIndex = 4;
            this.btnCheckUpdates.Text = "Проверить обновление лаунчера";
            this.btnCheckUpdates.UseVisualStyleBackColor = true;
            this.btnCheckUpdates.Click += new System.EventHandler(this.btnCheckUpdates_Click);
            // 
            // btnCheckSteamCmd
            // 
            this.btnCheckSteamCmd.Location = new System.Drawing.Point(550, 78);
            this.btnCheckSteamCmd.Name = "btnCheckSteamCmd";
            this.btnCheckSteamCmd.Size = new System.Drawing.Size(352, 32);
            this.btnCheckSteamCmd.TabIndex = 5;
            this.btnCheckSteamCmd.Text = "Проверить / Установить";
            this.btnCheckSteamCmd.UseVisualStyleBackColor = true;
            this.btnCheckSteamCmd.Click += new System.EventHandler(this.btnCheckSteamCmd_Click);
            // 
            // lblConanExe
            // 
            this.lblConanExe.AutoSize = true;
            this.lblConanExe.Location = new System.Drawing.Point(12, 123);
            this.lblConanExe.Name = "lblConanExe";
            this.lblConanExe.Size = new System.Drawing.Size(170, 16);
            this.lblConanExe.TabIndex = 5;
            this.lblConanExe.Text = "Путь к ConanSandbox.exe";
            // 
            // txtConanExe
            // 
            this.txtConanExe.Location = new System.Drawing.Point(15, 142);
            this.txtConanExe.Name = "txtConanExe";
            this.txtConanExe.Size = new System.Drawing.Size(789, 22);
            this.txtConanExe.TabIndex = 6;
            // 
            // btnBrowseConanExe
            // 
            this.btnBrowseConanExe.Location = new System.Drawing.Point(810, 141);
            this.btnBrowseConanExe.Name = "btnBrowseConanExe";
            this.btnBrowseConanExe.Size = new System.Drawing.Size(90, 24);
            this.btnBrowseConanExe.TabIndex = 7;
            this.btnBrowseConanExe.Text = "Обзор...";
            this.btnBrowseConanExe.UseVisualStyleBackColor = true;
            this.btnBrowseConanExe.Click += new System.EventHandler(this.btnBrowseConanExe_Click);
            // 
            // lblServerPassword
            // 
            this.lblServerPassword.AutoSize = true;
            this.lblServerPassword.Location = new System.Drawing.Point(12, 175);
            this.lblServerPassword.Name = "lblServerPassword";
            this.lblServerPassword.Size = new System.Drawing.Size(109, 16);
            this.lblServerPassword.TabIndex = 8;
            this.lblServerPassword.Text = "Пароль сервера";
            // 
            // txtServerPassword
            // 
            this.txtServerPassword.Location = new System.Drawing.Point(15, 194);
            this.txtServerPassword.Name = "txtServerPassword";
            this.txtServerPassword.PasswordChar = '*';
            this.txtServerPassword.Size = new System.Drawing.Size(265, 22);
            this.txtServerPassword.TabIndex = 9;
            // 
            // chkDisableIntro
            // 
            this.chkDisableIntro.AutoSize = true;
            this.chkDisableIntro.Location = new System.Drawing.Point(300, 195);
            this.chkDisableIntro.Name = "chkDisableIntro";
            this.chkDisableIntro.Size = new System.Drawing.Size(254, 20);
            this.chkDisableIntro.TabIndex = 10;
            this.chkDisableIntro.Text = "Отключить вступительный ролик";
            this.toolTipOptions.SetToolTip(this.chkDisableIntro, "Заменяет вступительный ролик Conan черным экраном при загрузке игры.");
            this.chkDisableIntro.UseVisualStyleBackColor = true;
            // 
            // chkAutoSubscribe
            // 
            this.chkAutoSubscribe.AutoSize = true;
            this.chkAutoSubscribe.Checked = true;
            this.chkAutoSubscribe.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoSubscribe.Location = new System.Drawing.Point(560, 195);
            this.chkAutoSubscribe.Name = "chkAutoSubscribe";
            this.chkAutoSubscribe.Size = new System.Drawing.Size(340, 20);
            this.chkAutoSubscribe.TabIndex = 11;
            this.chkAutoSubscribe.Text = "Автоматически подписываться на моды Workshop";
            this.toolTipOptions.SetToolTip(this.chkAutoSubscribe, "При запуске лаунчер автоматически устанавливает и обновляет моды Workshop из списка сервера.");
            this.chkAutoSubscribe.UseVisualStyleBackColor = true;
            // 
            // btnPlay
            // 
            this.btnPlay.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.btnPlay.Location = new System.Drawing.Point(15, 228);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(885, 44);
            this.btnPlay.TabIndex = 12;
            this.btnPlay.Text = "Играть (проверить моды и подключиться)";
            this.btnPlay.UseVisualStyleBackColor = true;
            this.btnPlay.Click += new System.EventHandler(this.btnPlay_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(12, 280);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(56, 16);
            this.lblStatus.TabIndex = 13;
            this.lblStatus.Text = "Готово.";
            // 
            // progressMods
            // 
            this.progressMods.Location = new System.Drawing.Point(15, 299);
            this.progressMods.Name = "progressMods";
            this.progressMods.Size = new System.Drawing.Size(885, 16);
            this.progressMods.TabIndex = 14;
            // 
            // txtLog
            // 
            this.txtLog.Location = new System.Drawing.Point(15, 321);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(885, 192);
            this.txtLog.TabIndex = 15;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(918, 528);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.progressMods);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnPlay);
            this.Controls.Add(this.chkAutoSubscribe);
            this.Controls.Add(this.chkDisableIntro);
            this.Controls.Add(this.txtServerPassword);
            this.Controls.Add(this.lblServerPassword);
            this.Controls.Add(this.btnBrowseConanExe);
            this.Controls.Add(this.txtConanExe);
            this.Controls.Add(this.lblConanExe);
            this.Controls.Add(this.btnCheckUpdates);
            this.Controls.Add(this.btnCheckSteamCmd);
            this.Controls.Add(this.lblSteamCmdStatus);
            this.Controls.Add(this.lblSteamCmdTitle);
            this.Controls.Add(this.txtConfigUrl);
            this.Controls.Add(this.lblConfigUrl);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "REALM RolePlay Launcher";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblConfigUrl;
        private System.Windows.Forms.TextBox txtConfigUrl;
        private System.Windows.Forms.Label lblSteamCmdTitle;
        private System.Windows.Forms.Label lblSteamCmdStatus;
        private System.Windows.Forms.Button btnCheckUpdates;
        private System.Windows.Forms.Button btnCheckSteamCmd;
        private System.Windows.Forms.Label lblConanExe;
        private System.Windows.Forms.TextBox txtConanExe;
        private System.Windows.Forms.Button btnBrowseConanExe;
        private System.Windows.Forms.Label lblServerPassword;
        private System.Windows.Forms.TextBox txtServerPassword;
        private System.Windows.Forms.CheckBox chkDisableIntro;
        private System.Windows.Forms.CheckBox chkAutoSubscribe;
        private System.Windows.Forms.Button btnPlay;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.ProgressBar progressMods;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.ToolTip toolTipOptions;
    }
}
