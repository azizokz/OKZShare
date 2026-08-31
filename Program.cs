using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PosInstaller
{
    public class MainForm : Form
    {
        private LocalShareControl shareControl;

        public MainForm()
        {
            this.Text = "OKZ Share — Local & Internet";
            this.Size = new Size(510, 770);
            this.MinimumSize = new Size(480, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42); // #0f172a (slate-900)
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            // Set Window Icon from icon.png / embedded resource
            Icon appIcon = LoadApplicationIcon();
            if (appIcon != null)
            {
                this.Icon = appIcon;
            }

            shareControl = new LocalShareControl();
            shareControl.Dock = DockStyle.Fill;
            this.Controls.Add(shareControl);

            this.FormClosing += (s, e) =>
            {
                // Ensure server and tunnel process are cleanly terminated on application close
                if (shareControl != null)
                {
                    shareControl.Shutdown();
                }
            };
        }

        private Icon LoadApplicationIcon()
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
                                    return Icon.FromHandle(hIcon);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string iconPath = Path.Combine(baseDir, "icon.png");
                if (File.Exists(iconPath))
                {
                    using (Bitmap bmp = new Bitmap(iconPath))
                    {
                        IntPtr hIcon = bmp.GetHicon();
                        return Icon.FromHandle(hIcon);
                    }
                }
            }
            catch { }

            return null;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Resolve embedded assemblies (e.g. QRCoder.dll) so the executable is 100% portable
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string dllName = new AssemblyName(args.Name).Name + ".dll";
                Assembly executingAssembly = Assembly.GetExecutingAssembly();

                foreach (string resourceName in executingAssembly.GetManifestResourceNames())
                {
                    if (resourceName.EndsWith(dllName, StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream stream = executingAssembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream != null)
                            {
                                byte[] assemblyRawBytes = new byte[stream.Length];
                                stream.Read(assemblyRawBytes, 0, assemblyRawBytes.Length);
                                return Assembly.Load(assemblyRawBytes);
                            }
                        }
                    }
                }
                return null;
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
