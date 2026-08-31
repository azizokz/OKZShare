using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PosInstaller
{
    public class InstallerForm : Form
    {
        private TextBox txtInstallPath;
        private Button btnBrowse;
        private CheckBox chkDesktopShortcut;
        private CheckBox chkStartMenuShortcut;
        private CheckBox chkLaunchApp;
        private Button btnInstall;
        private ProgressBar progressBar;
        private Label lblStatus;
        private bool isInstalling = false;
        private bool isFinished = false;

        public InstallerForm()
        {
            this.Text = "OKZ Share — Setup & Installer";
            this.Size = new Size(520, 440);
            this.MinimumSize = new Size(520, 440);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42); // #0f172a
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            LoadWindowIcon();
            InitializeUI();
        }

        private void LoadWindowIcon()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(name))
                        {
                            if (stream != null)
                            {
                                using (Bitmap bmp = new Bitmap(stream))
                                {
                                    IntPtr hIcon = bmp.GetHicon();
                                    this.Icon = Icon.FromHandle(hIcon);
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void InitializeUI()
        {
            this.SuspendLayout();

            Panel mainPanel = new Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(24, 20, 24, 20);
            this.Controls.Add(mainPanel);

            // Header Icon / Banner
            Label lblHeaderTitle = new Label();
            lblHeaderTitle.UseMnemonic = false;
            lblHeaderTitle.Text = "📦 OKZ Share Setup";
            lblHeaderTitle.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            lblHeaderTitle.ForeColor = Color.FromArgb(56, 189, 248); // #38bdf8
            lblHeaderTitle.Location = new Point(20, 16);
            lblHeaderTitle.AutoSize = true;
            mainPanel.Controls.Add(lblHeaderTitle);

            Label lblHeaderSub = new Label();
            lblHeaderSub.UseMnemonic = false;
            lblHeaderSub.Text = "Install OKZ Share — Local & Internet with portable runtime and desktop shortcut.";
            lblHeaderSub.Font = new Font("Segoe UI", 9F);
            lblHeaderSub.ForeColor = Color.FromArgb(148, 163, 184); // #94a3b8
            lblHeaderSub.Location = new Point(22, 48);
            lblHeaderSub.Size = new Size(460, 36);
            mainPanel.Controls.Add(lblHeaderSub);

            // Path Card
            Panel cardPath = new Panel();
            cardPath.Location = new Point(20, 96);
            cardPath.Size = new Size(462, 82);
            cardPath.BackColor = Color.FromArgb(19, 27, 46); // #131b2e
            cardPath.BorderStyle = BorderStyle.FixedSingle;
            mainPanel.Controls.Add(cardPath);

            Label lblPathTag = new Label();
            lblPathTag.UseMnemonic = false;
            lblPathTag.Text = "Destination Folder:";
            lblPathTag.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblPathTag.ForeColor = Color.FromArgb(203, 213, 225);
            lblPathTag.Location = new Point(14, 12);
            lblPathTag.AutoSize = true;
            cardPath.Controls.Add(lblPathTag);

            string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OKZShare");

            txtInstallPath = new TextBox();
            txtInstallPath.Location = new Point(14, 38);
            txtInstallPath.Size = new Size(330, 26);
            txtInstallPath.BackColor = Color.FromArgb(23, 32, 51);
            txtInstallPath.ForeColor = Color.FromArgb(226, 232, 240);
            txtInstallPath.BorderStyle = BorderStyle.FixedSingle;
            txtInstallPath.Font = new Font("Segoe UI", 9.5F);
            txtInstallPath.Text = defaultPath;
            cardPath.Controls.Add(txtInstallPath);

            btnBrowse = new Button();
            btnBrowse.UseMnemonic = false;
            btnBrowse.Text = "Browse...";
            btnBrowse.Location = new Point(352, 36);
            btnBrowse.Size = new Size(94, 30);
            btnBrowse.FlatStyle = FlatStyle.Flat;
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            btnBrowse.BackColor = Color.FromArgb(30, 41, 59);
            btnBrowse.ForeColor = Color.White;
            btnBrowse.Font = new Font("Segoe UI", 9F);
            btnBrowse.Cursor = Cursors.Hand;
            btnBrowse.Click += (s, e) => BrowseDestinationFolder();
            cardPath.Controls.Add(btnBrowse);

            // Options Checkboxes
            chkDesktopShortcut = new CheckBox();
            chkDesktopShortcut.UseMnemonic = false;
            chkDesktopShortcut.Text = "Create a Desktop shortcut";
            chkDesktopShortcut.Checked = true;
            chkDesktopShortcut.ForeColor = Color.FromArgb(226, 232, 240);
            chkDesktopShortcut.Location = new Point(24, 192);
            chkDesktopShortcut.AutoSize = true;
            chkDesktopShortcut.Cursor = Cursors.Hand;
            mainPanel.Controls.Add(chkDesktopShortcut);

            chkStartMenuShortcut = new CheckBox();
            chkStartMenuShortcut.UseMnemonic = false;
            chkStartMenuShortcut.Text = "Create Start Menu shortcut";
            chkStartMenuShortcut.Checked = true;
            chkStartMenuShortcut.ForeColor = Color.FromArgb(226, 232, 240);
            chkStartMenuShortcut.Location = new Point(24, 222);
            chkStartMenuShortcut.AutoSize = true;
            chkStartMenuShortcut.Cursor = Cursors.Hand;
            mainPanel.Controls.Add(chkStartMenuShortcut);

            chkLaunchApp = new CheckBox();
            chkLaunchApp.UseMnemonic = false;
            chkLaunchApp.Text = "Launch OKZ Share after install";
            chkLaunchApp.Checked = true;
            chkLaunchApp.ForeColor = Color.FromArgb(226, 232, 240);
            chkLaunchApp.Location = new Point(24, 252);
            chkLaunchApp.AutoSize = true;
            chkLaunchApp.Cursor = Cursors.Hand;
            mainPanel.Controls.Add(chkLaunchApp);

            // Progress Bar
            progressBar = new ProgressBar();
            progressBar.Location = new Point(20, 290);
            progressBar.Size = new Size(462, 14);
            progressBar.Visible = false;
            mainPanel.Controls.Add(progressBar);

            // Status Label
            lblStatus = new Label();
            lblStatus.UseMnemonic = false;
            lblStatus.Text = "Ready to install.";
            lblStatus.ForeColor = Color.FromArgb(148, 163, 184);
            lblStatus.Font = new Font("Segoe UI", 8.5F);
            lblStatus.Location = new Point(22, 312);
            lblStatus.AutoSize = true;
            mainPanel.Controls.Add(lblStatus);

            // Install / Finish Button
            btnInstall = new Button();
            btnInstall.UseMnemonic = false;
            btnInstall.Text = "Install OKZ Share";
            btnInstall.Location = new Point(292, 345);
            btnInstall.Size = new Size(190, 38);
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.BackColor = Color.FromArgb(2, 132, 199); // #0284c7
            btnInstall.ForeColor = Color.White;
            btnInstall.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnInstall.Cursor = Cursors.Hand;
            btnInstall.Click += (s, e) => HandleActionButtonClick();
            mainPanel.Controls.Add(btnInstall);

            this.ResumeLayout(false);
        }

        private void BrowseDestinationFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select installation folder:";
                if (!string.IsNullOrEmpty(txtInstallPath.Text))
                {
                    dlg.SelectedPath = txtInstallPath.Text;
                }
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtInstallPath.Text = Path.Combine(dlg.SelectedPath, "OKZShare");
                }
            }
        }

        private void HandleActionButtonClick()
        {
            if (isFinished)
            {
                this.Close();
                return;
            }

            if (isInstalling) return;

            string targetDir = txtInstallPath.Text.Trim();
            if (string.IsNullOrEmpty(targetDir))
            {
                MessageBox.Show("Please enter a valid destination folder path.", "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            isInstalling = true;
            btnInstall.Enabled = false;
            btnBrowse.Enabled = false;
            txtInstallPath.Enabled = false;
            chkDesktopShortcut.Enabled = false;
            chkStartMenuShortcut.Enabled = false;
            chkLaunchApp.Enabled = false;

            progressBar.Visible = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            lblStatus.Text = "Extracting files and setting up runtime...";
            lblStatus.ForeColor = Color.FromArgb(56, 189, 248);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    PerformInstallation(targetDir);
                }
                catch (Exception ex)
                {
                    this.Invoke(new Action(delegate
                    {
                        isInstalling = false;
                        progressBar.Visible = false;
                        lblStatus.Text = "Installation failed: " + ex.Message;
                        lblStatus.ForeColor = Color.FromArgb(239, 68, 68);
                        btnInstall.Enabled = true;
                        MessageBox.Show("Installation encountered an error:\n\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void PerformInstallation(string targetDir)
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Extract payload.zip from embedded resource
            string tempZip = Path.Combine(Path.GetTempPath(), "OKZShare_Payload_" + Guid.NewGuid().ToString("N") + ".zip");
            Assembly assembly = Assembly.GetExecutingAssembly();

            using (Stream resourceStream = assembly.GetManifestResourceStream("payload.zip"))
            {
                if (resourceStream == null)
                {
                    throw new FileNotFoundException("Embedded payload.zip was not found in installer package.");
                }

                using (FileStream fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
                {
                    byte[] buffer = new byte[64 * 1024];
                    int read;
                    while ((read = resourceStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fs.Write(buffer, 0, read);
                    }
                }
            }

            // Extract zip file to destination
            ZipFile.ExtractToDirectory(tempZip, targetDir);

            try { File.Delete(tempZip); } catch { }

            string targetExe = Path.Combine(targetDir, "OKZShare.exe");
            if (!File.Exists(targetExe))
            {
                targetExe = Path.Combine(targetDir, "LocalFileShare.exe");
            }
            string iconFile = Path.Combine(targetDir, "app.ico");

            // Create Desktop Shortcut
            if (chkDesktopShortcut.Checked)
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktopPath, "OKZ Share.lnk");
                CreateShortcut(shortcutPath, targetExe, targetDir, iconFile);
            }

            // Create Start Menu Shortcut
            if (chkStartMenuShortcut.Checked)
            {
                string startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string shortcutPath = Path.Combine(startMenuPath, "OKZ Share.lnk");
                CreateShortcut(shortcutPath, targetExe, targetDir, iconFile);
            }

            this.Invoke(new Action(delegate
            {
                isInstalling = false;
                isFinished = true;
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;
                lblStatus.Text = "✅ Installation completed successfully!";
                lblStatus.ForeColor = Color.FromArgb(16, 185, 129); // emerald-500

                btnInstall.Text = "Finish";
                btnInstall.BackColor = Color.FromArgb(16, 185, 129);
                btnInstall.Enabled = true;

                if (chkLaunchApp.Checked && File.Exists(targetExe))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = targetDir });
                    }
                    catch { }
                    this.Close();
                }
            }));
        }

        private static void CreateShortcut(string shortcutPath, string targetExePath, string workingDir, string iconPath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    if (shortcut != null)
                    {
                        Type scType = shortcut.GetType();
                        scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExePath });
                        scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                        scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "OKZ Share — Local & Internet" });
                        if (File.Exists(iconPath))
                        {
                            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" });
                        }
                        scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                    }
                }
            }
            catch { }
        }
    }

    static class InstallerProgram
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }
    }
}
