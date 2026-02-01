namespace Funbit.Ets.Telemetry.Server
{
    partial class MainForm
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.trayIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayMenuSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.trayStartWithWindowsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.trayMenuSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.closeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusUpdateTimer = new System.Windows.Forms.Timer(this.components);
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.ipAddressLabel = new System.Windows.Forms.Label();
            this.interfacesDropDown = new System.Windows.Forms.ComboBox();
            this.networkInterfaceTitleLabel = new System.Windows.Forms.Label();
            this.serverIpTitleLabel = new System.Windows.Forms.Label();
            this.statusLabel = new System.Windows.Forms.Label();
            this.statusTitleLabel = new System.Windows.Forms.Label();
            this.appUrlLabel = new System.Windows.Forms.LinkLabel();
            this.appUrlTitleLabel = new System.Windows.Forms.Label();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.ets2PathLabel = new System.Windows.Forms.Label();
            this.ets2PluginStatusLabel = new System.Windows.Forms.Label();
            this.atsPathLabel = new System.Windows.Forms.Label();
            this.atsPluginStatusLabel = new System.Windows.Forms.Label();
            this.ets2CopyPluginButton = new System.Windows.Forms.Button();
            this.atsCopyPluginButton = new System.Windows.Forms.Button();
            this.broadcastTimer = new System.Windows.Forms.Timer(this.components);
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.serverToolStripMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.rerunSetupToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.startWithWindowsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenu = new System.Windows.Forms.ToolStripMenuItem();
            this.websiteToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.helpToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.donateToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.checkForUpdatesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ets2PathTitleLabel = new System.Windows.Forms.Label();
            this.atsPathTitleLabel = new System.Windows.Forms.Label();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.contextMenuStrip.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.menuStrip1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.SuspendLayout();
            // 
            // trayIcon
            // 
            this.trayIcon.BalloonTipTitle = "TruckSim GPS Telemetry Server is running...";
            this.trayIcon.ContextMenuStrip = this.contextMenuStrip;
            this.trayIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("trayIcon.Icon")));
            this.trayIcon.Text = "TruckSim GPS Telemetry Server is running...";
            this.trayIcon.BalloonTipClicked += new System.EventHandler(this.trayIcon_BalloonTipClicked);
            this.trayIcon.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.trayIcon_MouseDoubleClick);
            // 
            // contextMenuStrip
            // 
            this.contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.openToolStripMenuItem, this.trayMenuSeparator1, this.trayStartWithWindowsMenuItem, this.trayMenuSeparator2, this.closeToolStripMenuItem });
            this.contextMenuStrip.Name = "contextMenuStrip";
            this.contextMenuStrip.Size = new System.Drawing.Size(180, 82);
            //
            // openToolStripMenuItem
            //
            this.openToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.Size = new System.Drawing.Size(179, 22);
            this.openToolStripMenuItem.Text = "Open";
            this.openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            //
            // trayMenuSeparator1
            //
            this.trayMenuSeparator1.Name = "trayMenuSeparator1";
            this.trayMenuSeparator1.Size = new System.Drawing.Size(176, 6);
            //
            // trayStartWithWindowsMenuItem
            //
            this.trayStartWithWindowsMenuItem.CheckOnClick = true;
            this.trayStartWithWindowsMenuItem.Name = "trayStartWithWindowsMenuItem";
            this.trayStartWithWindowsMenuItem.Size = new System.Drawing.Size(179, 22);
            this.trayStartWithWindowsMenuItem.Text = "Start with Windows";
            this.trayStartWithWindowsMenuItem.Click += new System.EventHandler(this.trayStartWithWindowsMenuItem_Click);
            //
            // trayMenuSeparator2
            //
            this.trayMenuSeparator2.Name = "trayMenuSeparator2";
            this.trayMenuSeparator2.Size = new System.Drawing.Size(176, 6);
            //
            // closeToolStripMenuItem
            //
            this.closeToolStripMenuItem.Name = "closeToolStripMenuItem";
            this.closeToolStripMenuItem.Size = new System.Drawing.Size(179, 22);
            this.closeToolStripMenuItem.Text = "Close";
            this.closeToolStripMenuItem.Click += new System.EventHandler(this.closeToolStripMenuItem_Click);
            // 
            // statusUpdateTimer
            // 
            this.statusUpdateTimer.Interval = 1000;
            this.statusUpdateTimer.Tick += new System.EventHandler(this.statusUpdateTimer_Tick);
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.ipAddressLabel);
            this.groupBox1.Controls.Add(this.interfacesDropDown);
            this.groupBox1.Controls.Add(this.networkInterfaceTitleLabel);
            this.groupBox1.Controls.Add(this.serverIpTitleLabel);
            this.groupBox1.Controls.Add(this.statusLabel);
            this.groupBox1.Controls.Add(this.statusTitleLabel);
            this.groupBox1.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox1.Location = new System.Drawing.Point(12, 33);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(580, 160);
            this.groupBox1.TabIndex = 11;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Server status";
            // 
            // ipAddressLabel
            // 
            this.ipAddressLabel.AutoSize = true;
            this.ipAddressLabel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(248)))), ((int)(((byte)(255)))));
            this.ipAddressLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ipAddressLabel.ForeColor = System.Drawing.Color.DarkBlue;
            this.ipAddressLabel.Location = new System.Drawing.Point(139, 131);
            this.ipAddressLabel.Name = "ipAddressLabel";
            this.ipAddressLabel.Size = new System.Drawing.Size(101, 17);
            this.ipAddressLabel.TabIndex = 21;
            this.ipAddressLabel.Text = "111.222.333.444";
            this.toolTip.SetToolTip(this.ipAddressLabel, "Use this IP address for mobile application");
            // 
            // interfacesDropDown
            // 
            this.interfacesDropDown.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.interfacesDropDown.Font = new System.Drawing.Font("Segoe UI Light", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.interfacesDropDown.FormattingEnabled = true;
            this.interfacesDropDown.Location = new System.Drawing.Point(144, 91);
            this.interfacesDropDown.Name = "interfacesDropDown";
            this.interfacesDropDown.Size = new System.Drawing.Size(369, 25);
            this.interfacesDropDown.TabIndex = 20;
            this.interfacesDropDown.TabStop = false;
            this.interfacesDropDown.SelectedIndexChanged += new System.EventHandler(this.interfaceDropDown_SelectedIndexChanged);
            // 
            // networkInterfaceTitleLabel
            // 
            this.networkInterfaceTitleLabel.AutoSize = true;
            this.networkInterfaceTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.networkInterfaceTitleLabel.Location = new System.Drawing.Point(18, 94);
            this.networkInterfaceTitleLabel.Name = "networkInterfaceTitleLabel";
            this.networkInterfaceTitleLabel.Size = new System.Drawing.Size(120, 17);
            this.networkInterfaceTitleLabel.TabIndex = 19;
            this.networkInterfaceTitleLabel.Text = "Network Interfaces:";
            // 
            // serverIpTitleLabel
            // 
            this.serverIpTitleLabel.AutoSize = true;
            this.serverIpTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.serverIpTitleLabel.Location = new System.Drawing.Point(76, 131);
            this.serverIpTitleLabel.Name = "serverIpTitleLabel";
            this.serverIpTitleLabel.Size = new System.Drawing.Size(62, 17);
            this.serverIpTitleLabel.TabIndex = 17;
            this.serverIpTitleLabel.Text = "Server IP:";
            // 
            // statusLabel
            // 
            this.statusLabel.AutoSize = true;
            this.statusLabel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(248)))), ((int)(((byte)(255)))));
            this.statusLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.statusLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.statusLabel.Location = new System.Drawing.Point(141, 41);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(69, 17);
            this.statusLabel.TabIndex = 13;
            this.statusLabel.Text = "Checking...";
            // 
            // statusTitleLabel
            // 
            this.statusTitleLabel.AutoSize = true;
            this.statusTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.statusTitleLabel.Location = new System.Drawing.Point(92, 41);
            this.statusTitleLabel.Name = "statusTitleLabel";
            this.statusTitleLabel.Size = new System.Drawing.Size(46, 17);
            this.statusTitleLabel.TabIndex = 11;
            this.statusTitleLabel.Text = "Status:";
            // 
            // appUrlLabel
            // 
            this.appUrlLabel.AutoSize = true;
            this.appUrlLabel.Font = new System.Drawing.Font("Segoe UI Light", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.appUrlLabel.Location = new System.Drawing.Point(140, 125);
            this.appUrlLabel.Name = "appUrlLabel";
            this.appUrlLabel.Size = new System.Drawing.Size(72, 17);
            this.appUrlLabel.TabIndex = 16;
            this.appUrlLabel.TabStop = true;
            this.appUrlLabel.Text = "appUrlLabel";
            this.toolTip.SetToolTip(this.appUrlLabel, "Use this URL to view HTML5 mobile dashboard in desktop or mobile browsers (click " + "to open)");
            this.appUrlLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.appUrlLabel_LinkClicked);
            // 
            // appUrlTitleLabel
            // 
            this.appUrlTitleLabel.AutoSize = true;
            this.appUrlTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.appUrlTitleLabel.Location = new System.Drawing.Point(18, 125);
            this.appUrlTitleLabel.Name = "appUrlTitleLabel";
            this.appUrlTitleLabel.Size = new System.Drawing.Size(112, 17);
            this.appUrlTitleLabel.TabIndex = 15;
            this.appUrlTitleLabel.Text = "Browser Test URL:";
            // 
            // toolTip
            // 
            this.toolTip.AutomaticDelay = 250;
            this.toolTip.AutoPopDelay = 6000;
            this.toolTip.InitialDelay = 250;
            this.toolTip.ReshowDelay = 50;
            this.toolTip.ToolTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            // 
            // ets2PathLabel
            // 
            this.ets2PathLabel.AutoSize = true;
            this.ets2PathLabel.Font = new System.Drawing.Font("Segoe UI Light", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ets2PathLabel.ForeColor = System.Drawing.Color.Purple;
            this.ets2PathLabel.Location = new System.Drawing.Point(80, 45);
            this.ets2PathLabel.Name = "ets2PathLabel";
            this.ets2PathLabel.Size = new System.Drawing.Size(149, 17);
            this.ets2PathLabel.TabIndex = 23;
            this.ets2PathLabel.Text = "Directory: Not configured";
            this.toolTip.SetToolTip(this.ets2PathLabel, "ETS2 installation directory");
            // 
            // ets2PluginStatusLabel
            // 
            this.ets2PluginStatusLabel.AutoSize = true;
            this.ets2PluginStatusLabel.Font = new System.Drawing.Font("Segoe UI Light", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ets2PluginStatusLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.ets2PluginStatusLabel.Location = new System.Drawing.Point(70, 25);
            this.ets2PluginStatusLabel.Name = "ets2PluginStatusLabel";
            this.ets2PluginStatusLabel.Size = new System.Drawing.Size(110, 13);
            this.ets2PluginStatusLabel.TabIndex = 24;
            this.ets2PluginStatusLabel.Text = "Plugin: Not configured";
            this.toolTip.SetToolTip(this.ets2PluginStatusLabel, "ETS2 plugin installation status");
            // 
            // atsPathLabel
            // 
            this.atsPathLabel.AutoSize = true;
            this.atsPathLabel.Font = new System.Drawing.Font("Segoe UI Light", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.atsPathLabel.ForeColor = System.Drawing.Color.Purple;
            this.atsPathLabel.Location = new System.Drawing.Point(80, 95);
            this.atsPathLabel.Name = "atsPathLabel";
            this.atsPathLabel.Size = new System.Drawing.Size(149, 17);
            this.atsPathLabel.TabIndex = 26;
            this.atsPathLabel.Text = "Directory: Not configured";
            this.toolTip.SetToolTip(this.atsPathLabel, "ATS installation directory");
            // 
            // atsPluginStatusLabel
            // 
            this.atsPluginStatusLabel.AutoSize = true;
            this.atsPluginStatusLabel.Font = new System.Drawing.Font("Segoe UI Light", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.atsPluginStatusLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.atsPluginStatusLabel.Location = new System.Drawing.Point(65, 75);
            this.atsPluginStatusLabel.Name = "atsPluginStatusLabel";
            this.atsPluginStatusLabel.Size = new System.Drawing.Size(110, 13);
            this.atsPluginStatusLabel.TabIndex = 27;
            this.atsPluginStatusLabel.Text = "Plugin: Not configured";
            this.toolTip.SetToolTip(this.atsPluginStatusLabel, "ATS plugin installation status");
            // 
            // ets2CopyPluginButton
            // 
            this.ets2CopyPluginButton.Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ets2CopyPluginButton.Location = new System.Drawing.Point(350, 22);
            this.ets2CopyPluginButton.Name = "ets2CopyPluginButton";
            this.ets2CopyPluginButton.Size = new System.Drawing.Size(120, 23);
            this.ets2CopyPluginButton.TabIndex = 28;
            this.ets2CopyPluginButton.Text = "Copy missing plugins";
            this.ets2CopyPluginButton.UseVisualStyleBackColor = true;
            this.ets2CopyPluginButton.Visible = false;
            this.ets2CopyPluginButton.Click += new System.EventHandler(this.ets2CopyPluginButton_Click);
            // 
            // atsCopyPluginButton
            // 
            this.atsCopyPluginButton.Font = new System.Drawing.Font("Segoe UI", 7.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.atsCopyPluginButton.Location = new System.Drawing.Point(350, 72);
            this.atsCopyPluginButton.Name = "atsCopyPluginButton";
            this.atsCopyPluginButton.Size = new System.Drawing.Size(120, 23);
            this.atsCopyPluginButton.TabIndex = 29;
            this.atsCopyPluginButton.Text = "Copy missing plugins";
            this.atsCopyPluginButton.UseVisualStyleBackColor = true;
            this.atsCopyPluginButton.Visible = false;
            this.atsCopyPluginButton.Click += new System.EventHandler(this.atsCopyPluginButton_Click);
            // 
            // broadcastTimer
            // 
            this.broadcastTimer.Interval = 1000;
            this.broadcastTimer.Tick += new System.EventHandler(this.broadcastTimer_Tick);
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.serverToolStripMenu, this.helpToolStripMenu });
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(605, 24);
            this.menuStrip1.TabIndex = 12;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // serverToolStripMenu
            // 
            this.serverToolStripMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { this.rerunSetupToolStripMenuItem, this.startWithWindowsToolStripMenuItem });
            this.serverToolStripMenu.Name = "serverToolStripMenu";
            this.serverToolStripMenu.Size = new System.Drawing.Size(51, 20);
            this.serverToolStripMenu.Text = "Server";
            // 
            // rerunSetupToolStripMenuItem
            // 
            this.rerunSetupToolStripMenuItem.Name = "rerunSetupToolStripMenuItem";
            this.rerunSetupToolStripMenuItem.Size = new System.Drawing.Size(186, 22);
            this.rerunSetupToolStripMenuItem.Text = "Re-run Setup...";
            this.rerunSetupToolStripMenuItem.Click += new System.EventHandler(this.rerunSetupToolStripMenuItem_Click);
            //
            // startWithWindowsToolStripMenuItem
            //
            this.startWithWindowsToolStripMenuItem.CheckOnClick = true;
            this.startWithWindowsToolStripMenuItem.Name = "startWithWindowsToolStripMenuItem";
            this.startWithWindowsToolStripMenuItem.Size = new System.Drawing.Size(186, 22);
            this.startWithWindowsToolStripMenuItem.Text = "Start with Windows";
            this.startWithWindowsToolStripMenuItem.Click += new System.EventHandler(this.startWithWindowsToolStripMenuItem_Click);
            //
            // helpToolStripMenu
            // 
            this.helpToolStripMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] { this.websiteToolStripMenuItem, this.helpToolStripMenuItem, this.donateToolStripMenuItem, this.toolStripSeparator2, this.checkForUpdatesToolStripMenuItem, this.aboutToolStripMenuItem });
            this.helpToolStripMenu.Name = "helpToolStripMenu";
            this.helpToolStripMenu.Size = new System.Drawing.Size(44, 20);
            this.helpToolStripMenu.Text = "Help";
            // 
            // websiteToolStripMenuItem
            // 
            this.websiteToolStripMenuItem.Name = "websiteToolStripMenuItem";
            this.websiteToolStripMenuItem.Size = new System.Drawing.Size(185, 22);
            this.websiteToolStripMenuItem.Text = "TruckSim GPS website";
            this.websiteToolStripMenuItem.Click += new System.EventHandler(this.websiteToolStripMenuItem_Click);
            // 
            // helpToolStripMenuItem
            // 
            this.helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            this.helpToolStripMenuItem.Size = new System.Drawing.Size(185, 22);
            this.helpToolStripMenuItem.Text = "Browse on Github";
            this.helpToolStripMenuItem.Click += new System.EventHandler(this.helpToolStripMenuItem_Click);
            // 
            // donateToolStripMenuItem
            // 
            this.donateToolStripMenuItem.Name = "donateToolStripMenuItem";
            this.donateToolStripMenuItem.Size = new System.Drawing.Size(185, 22);
            this.donateToolStripMenuItem.Text = "Get support on Discord";
            this.donateToolStripMenuItem.Click += new System.EventHandler(this.donateToolStripMenuItem_Click);
            //
            // toolStripSeparator2
            //
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(182, 6);
            //
            // checkForUpdatesToolStripMenuItem
            //
            this.checkForUpdatesToolStripMenuItem.Name = "checkForUpdatesToolStripMenuItem";
            this.checkForUpdatesToolStripMenuItem.Size = new System.Drawing.Size(185, 22);
            this.checkForUpdatesToolStripMenuItem.Text = "Check for Updates...";
            this.checkForUpdatesToolStripMenuItem.Click += new System.EventHandler(this.checkForUpdatesToolStripMenuItem_Click);
            //
            // aboutToolStripMenuItem
            //
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(185, 22);
            this.aboutToolStripMenuItem.Text = "About";
            this.aboutToolStripMenuItem.Visible = false;
            this.aboutToolStripMenuItem.Click += new System.EventHandler(this.aboutToolStripMenuItem_Click);
            // 
            // ets2PathTitleLabel
            // 
            this.ets2PathTitleLabel.AutoSize = true;
            this.ets2PathTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ets2PathTitleLabel.Location = new System.Drawing.Point(18, 25);
            this.ets2PathTitleLabel.Name = "ets2PathTitleLabel";
            this.ets2PathTitleLabel.Size = new System.Drawing.Size(39, 17);
            this.ets2PathTitleLabel.TabIndex = 22;
            this.ets2PathTitleLabel.Text = "ETS2:";
            // 
            // atsPathTitleLabel
            // 
            this.atsPathTitleLabel.AutoSize = true;
            this.atsPathTitleLabel.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.atsPathTitleLabel.Location = new System.Drawing.Point(18, 75);
            this.atsPathTitleLabel.Name = "atsPathTitleLabel";
            this.atsPathTitleLabel.Size = new System.Drawing.Size(32, 17);
            this.atsPathTitleLabel.TabIndex = 25;
            this.atsPathTitleLabel.Text = "ATS:";
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.atsCopyPluginButton);
            this.groupBox2.Controls.Add(this.ets2CopyPluginButton);
            this.groupBox2.Controls.Add(this.atsPluginStatusLabel);
            this.groupBox2.Controls.Add(this.atsPathLabel);
            this.groupBox2.Controls.Add(this.atsPathTitleLabel);
            this.groupBox2.Controls.Add(this.ets2PluginStatusLabel);
            this.groupBox2.Controls.Add(this.ets2PathLabel);
            this.groupBox2.Controls.Add(this.ets2PathTitleLabel);
            this.groupBox2.Controls.Add(this.appUrlLabel);
            this.groupBox2.Controls.Add(this.appUrlTitleLabel);
            this.groupBox2.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.groupBox2.Location = new System.Drawing.Point(12, 210);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(580, 155);
            this.groupBox2.TabIndex = 13;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Game Configuration";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(605, 380);
            this.Controls.Add(this.menuStrip1);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.groupBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MainMenuStrip = this.menuStrip1;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "TruckSim GPS Telemetry Server";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.MainForm_FormClosed);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.Resize += new System.EventHandler(this.MainForm_Resize);
            this.contextMenuStrip.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.NotifyIcon trayIcon;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator trayMenuSeparator1;
        private System.Windows.Forms.ToolStripMenuItem trayStartWithWindowsMenuItem;
        private System.Windows.Forms.ToolStripSeparator trayMenuSeparator2;
        private System.Windows.Forms.ToolStripMenuItem closeToolStripMenuItem;
        private System.Windows.Forms.Timer statusUpdateTimer;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label serverIpTitleLabel;
        private System.Windows.Forms.LinkLabel appUrlLabel;
        private System.Windows.Forms.Label appUrlTitleLabel;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label statusTitleLabel;
        private System.Windows.Forms.Label ipAddressLabel;
        private System.Windows.Forms.ComboBox interfacesDropDown;
        private System.Windows.Forms.Label networkInterfaceTitleLabel;
        private System.Windows.Forms.ToolTip toolTip;
        private System.Windows.Forms.Timer broadcastTimer;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem serverToolStripMenu;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenu;
        private System.Windows.Forms.ToolStripMenuItem rerunSetupToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem startWithWindowsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem websiteToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem helpToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem donateToolStripMenuItem;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripMenuItem checkForUpdatesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.Label ets2PathTitleLabel;
        private System.Windows.Forms.Label ets2PathLabel;
        private System.Windows.Forms.Label ets2PluginStatusLabel;
        private System.Windows.Forms.Label atsPathTitleLabel;
        private System.Windows.Forms.Label atsPathLabel;
        private System.Windows.Forms.Label atsPluginStatusLabel;
        private System.Windows.Forms.Button ets2CopyPluginButton;
        private System.Windows.Forms.Button atsCopyPluginButton;
        private System.Windows.Forms.GroupBox groupBox2;
    }
}

