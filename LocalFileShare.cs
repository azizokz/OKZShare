using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using QRCoder;

namespace PosInstaller
{
    /// <summary>
    /// Handles reading and writing user settings to share_settings.txt next to the executable.
    /// </summary>
    public static class ShareSettings
    {
        private static string SettingsFilePath
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, "share_settings.txt");
            }
        }

        public static string LoadLastFolder()
        {
            try
            {
                string path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string folder = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        return folder;
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        public static void SaveLastFolder(string folderPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(folderPath))
                {
                    File.WriteAllText(SettingsFilePath, folderPath.Trim(), Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Serves a chosen folder over HTTP via HttpListener on the local network (LAN / Wi-Fi).
    /// </summary>
    public class LocalFileShareServer
    {
        private HttpListener listener;
        private Thread listenerThread;
        private string sharedFolder;
        private int port = 8090;
        private bool isRunning;

        public bool IsRunning { get { return isRunning; } }
        public string SharedFolder { get { return sharedFolder; } }
        public int Port { get { return port; } }

        public string GetLocalIPAddress()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530); // UDP socket pick interface - no packets sent
                    IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                    if (endPoint != null)
                    {
                        return endPoint.Address.ToString();
                    }
                }
            }
            catch
            {
                try
                {
                    IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (IPAddress ip in host.AddressList)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        {
                            return ip.ToString();
                        }
                    }
                }
                catch { }
            }
            return "127.0.0.1";
        }

        public string GetShareUrl()
        {
            return string.Format("http://{0}:{1}/", GetLocalIPAddress(), port);
        }

        public void Start(string folderPath, int listenPort = 8090)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                throw new DirectoryNotFoundException("Shared folder does not exist: " + folderPath);

            if (isRunning) Stop();

            sharedFolder = folderPath;
            port = listenPort;

            listener = new HttpListener();
            listener.Prefixes.Add(string.Format("http://+:{0}/", port));

            try
            {
                listener.Start();
            }
            catch (HttpListenerException)
            {
                listener.Prefixes.Clear();
                string lanIp = GetLocalIPAddress();
                listener.Prefixes.Add(string.Format("http://{0}:{1}/", lanIp, port));
                if (lanIp != "127.0.0.1")
                {
                    try { listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", port)); } catch { }
                    try { listener.Prefixes.Add(string.Format("http://localhost:{0}/", port)); } catch { }
                }
                listener.Start();
            }

            isRunning = true;
            listenerThread = new Thread(Listen);
            listenerThread.IsBackground = true;
            listenerThread.Start();
        }

        public void Stop()
        {
            isRunning = false;
            try
            {
                if (listener != null && listener.IsListening)
                {
                    listener.Stop();
                    listener.Close();
                }
            }
            catch { }
            finally
            {
                listener = null;
            }
        }

        private void Listen()
        {
            while (isRunning && listener != null)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(HandleRequest, context);
                }
                catch
                {
                    break;
                }
            }
        }

        private void HandleRequest(object state)
        {
            HttpListenerContext context = (HttpListenerContext)state;
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                string rawPath = request.Url.AbsolutePath;
                string relativePath = Uri.UnescapeDataString(rawPath.TrimStart('/'));
                string fullPath = Path.GetFullPath(Path.Combine(sharedFolder, relativePath));

                // Security check: Strictly prevent directory traversal attacks outside root
                string basePath = Path.GetFullPath(sharedFolder);
                if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    basePath += Path.DirectorySeparatorChar;

                if (!fullPath.Equals(basePath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                    !fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    response.StatusCode = 403;
                    WriteText(response, "403 - Forbidden: Access outside the shared root directory is prohibited.");
                    return;
                }

                if (Directory.Exists(fullPath))
                {
                    WriteDirectoryListing(response, fullPath, relativePath);
                }
                else if (File.Exists(fullPath))
                {
                    ServeFile(response, fullPath);
                }
                else
                {
                    response.StatusCode = 404;
                    WriteText(response, "404 - File or directory not found.");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    response.StatusCode = 500;
                    WriteText(response, "500 - Server Error: " + ex.Message);
                }
                catch { }
            }
            finally
            {
                try
                {
                    response.OutputStream.Close();
                }
                catch { }
            }
        }

        private void WriteDirectoryListing(HttpListenerResponse response, string fullPath, string relativePath)
        {
            StringBuilder html = new StringBuilder();
            string folderName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(folderName)) folderName = "Shared Root";

            html.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            html.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            html.Append("<title>OKZ Share - " + WebUtility.HtmlEncode(folderName) + "</title>");
            html.Append("<style>");
            html.Append("*{box-sizing:border-box;margin:0;padding:0}");
            html.Append("body{font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,Roboto,Helvetica,sans-serif;background:#0f172a;color:#e2e8f0;padding:24px 16px;min-height:100vh;display:flex;flex-direction:column;align-items:center}");
            html.Append(".container{width:100%;max-width:920px;background:#131b2e;border-radius:14px;border:1px solid #1e293b;padding:24px;box-shadow:0 12px 36px rgba(0,0,0,0.55)}");
            html.Append(".header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #1e293b;padding-bottom:18px;margin-bottom:18px;flex-wrap:wrap;gap:10px}");
            html.Append(".title{font-size:1.35rem;font-weight:700;color:#38bdf8;display:flex;align-items:center;gap:10px}");
            html.Append(".badge{background:#0284c7;color:#f0f9ff;font-size:0.75rem;padding:4px 10px;border-radius:6px;font-weight:600;letter-spacing:0.5px;text-transform:uppercase}");
            html.Append(".search-box{width:100%;padding:11px 16px;background:#172033;border:1px solid #334155;border-radius:8px;color:#fff;font-size:0.95rem;margin-bottom:16px;outline:none;transition:border-color 0.2s}");
            html.Append(".search-box:focus{border-color:#38bdf8;box-shadow:0 0 0 2px rgba(56,189,248,0.2)}");
            html.Append(".list{list-style:none;display:flex;flex-direction:column;gap:8px}");
            html.Append(".item{display:flex;align-items:center;justify-content:space-between;padding:12px 16px;background:#172033;border:1px solid #1e293b;border-radius:8px;text-decoration:none;color:#f8fafc;transition:all 0.15s ease}");
            html.Append(".item:hover{background:#1e293b;border-color:#38bdf8;transform:translateX(3px)}");
            html.Append(".item-left{display:flex;align-items:center;gap:12px;overflow:hidden}");
            html.Append(".icon{font-size:1.3rem;flex-shrink:0}");
            html.Append(".name{font-weight:500;font-size:0.95rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}");
            html.Append(".size{font-size:0.8rem;color:#94a3b8;flex-shrink:0;background:#0f172a;padding:4px 10px;border-radius:5px;font-family:Consolas,monospace}");
            html.Append(".nav-up{background:#1e293b;border-color:#334155;color:#93c5fd;margin-bottom:6px}");
            html.Append(".nav-up:hover{background:#334155;border-color:#60a5fa}");
            html.Append(".footer{margin-top:24px;text-align:center;font-size:0.8rem;color:#64748b}");
            html.Append("</style>");
            html.Append("<script>");
            html.Append("function filterList(){");
            html.Append("  var q = document.getElementById('search').value.toLowerCase();");
            html.Append("  var items = document.querySelectorAll('.file-item');");
            html.Append("  items.forEach(function(el){");
            html.Append("    var name = el.getAttribute('data-name').toLowerCase();");
            html.Append("    el.style.display = name.includes(q) ? 'flex' : 'none';");
            html.Append("  });");
            html.Append("}");
            html.Append("</script>");
            html.Append("</head><body>");
            html.Append("<div class='container'>");
            html.Append("<div class='header'>");
            html.Append("<div class='title'><span>📁</span> " + WebUtility.HtmlEncode(folderName) + "</div>");
            html.Append("<div class='badge'>OKZ Share</div>");
            html.Append("</div>");

            html.Append("<input type='text' id='search' class='search-box' placeholder='🔍 Filter files and folders...' onkeyup='filterList()'>");
            html.Append("<div class='list'>");

            if (!string.IsNullOrEmpty(relativePath))
            {
                string parent = Path.GetDirectoryName(relativePath);
                if (parent == null) parent = "";
                parent = parent.Replace('\\', '/');
                html.Append(string.Format("<a class='item nav-up' href='/{0}'><div class='item-left'><span class='icon'>⬅️</span><span class='name'>.. (Parent Directory)</span></div></a>", parent));
            }

            // Directories
            foreach (string dir in Directory.GetDirectories(fullPath))
            {
                string name = Path.GetFileName(dir);
                string href = CombineUrl(relativePath, name);
                html.Append(string.Format("<a class='item file-item' data-name='{1}' href='/{0}/'><div class='item-left'><span class='icon'>📁</span><span class='name'>{1}/</span></div><span class='size'>Folder</span></a>", href, WebUtility.HtmlEncode(name)));
            }

            // Files
            foreach (string file in Directory.GetFiles(fullPath))
            {
                string name = Path.GetFileName(file);
                string href = CombineUrl(relativePath, name);
                long bytes = new FileInfo(file).Length;
                string formattedSize = FormatBytes(bytes);
                string icon = GetFileIcon(file);
                html.Append(string.Format("<a class='item file-item' data-name='{1}' href='/{0}' download><div class='item-left'><span class='icon'>{3}</span><span class='name'>{1}</span></div><span class='size'>{2}</span></a>", href, WebUtility.HtmlEncode(name), formattedSize, icon));
            }

            html.Append("</div>");
            html.Append("<div class='footer'>OKZ Share &bull; Fast Direct Transfer</div>");
            html.Append("</div></body></html>");

            WriteText(response, html.ToString(), "text/html; charset=utf-8");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.1") + " MB";
            return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.2") + " GB";
        }

        private static string GetFileIcon(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp": case ".webp": case ".svg":
                    return "🖼️";
                case ".mp4": case ".mkv": case ".avi": case ".mov": case ".webm":
                    return "🎬";
                case ".mp3": case ".wav": case ".flac": case ".m4a": case ".ogg":
                    return "🎵";
                case ".zip": case ".rar": case ".7z": case ".tar": case ".gz":
                    return "📦";
                case ".pdf":
                    return "📕";
                case ".txt": case ".md": case ".log": case ".json": case ".xml": case ".csv": case ".cs": case ".js":
                    return "📝";
                case ".exe": case ".msi": case ".apk": case ".bat": case ".cmd":
                    return "⚙️";
                case ".doc": case ".docx":
                    return "📄";
                case ".xls": case ".xlsx":
                    return "📊";
                case ".ppt": case ".pptx":
                    return "📽️";
                default:
                    return "📄";
            }
        }

        private static string CombineUrl(string relativePath, string name)
        {
            if (string.IsNullOrEmpty(relativePath)) return Uri.EscapeDataString(name);
            return relativePath.Replace('\\', '/') + "/" + Uri.EscapeDataString(name);
        }

        private void ServeFile(HttpListenerResponse response, string fullPath)
        {
            response.ContentType = GetMimeType(fullPath);
            response.AddHeader("Content-Disposition", "attachment; filename=\"" + Path.GetFileName(fullPath) + "\"");
            using (FileStream fs = File.OpenRead(fullPath))
            {
                response.ContentLength64 = fs.Length;
                byte[] buffer = new byte[64 * 1024];
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    response.OutputStream.Write(buffer, 0, read);
                }
            }
        }

        private static void WriteText(HttpListenerResponse response, string text, string contentType = "text/plain; charset=utf-8")
        {
            response.ContentType = contentType;
            byte[] buffer = Encoding.UTF8.GetBytes(text);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain; charset=utf-8";
                case ".html": case ".htm": return "text/html; charset=utf-8";
                case ".json": return "application/json";
                case ".xml": return "application/xml";
                case ".zip": return "application/zip";
                case ".mp4": return "video/mp4";
                case ".mp3": return "audio/mpeg";
                case ".apk": return "application/vnd.android.package-archive";
                default: return "application/octet-stream";
            }
        }
    }

    /// <summary>
    /// Manages the bundled Node.js runtime and tunnel background process.
    /// </summary>
    public class TunnelManager
    {
        private Process tunnelProcess;
        private string publicUrl;
        private bool isRunning;
        private bool isConnecting;
        private SynchronizationContext syncContext;

        public event Action<string> OnPublicUrlReceived;
        public event Action<string> OnTunnelError;
        public event Action OnTunnelClosed;

        public bool IsRunning { get { return isRunning; } }
        public bool IsConnecting { get { return isConnecting; } }
        public string PublicUrl { get { return publicUrl; } }

        public TunnelManager()
        {
            syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        }

        private string GetNodeExecutablePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string bundledNode = Path.Combine(baseDir, @"runtime\node-win-x64\node.exe");
            if (File.Exists(bundledNode))
            {
                return bundledNode;
            }

            return "node.exe";
        }

        private string GetServerScriptPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, @"tunnel\server.js");
        }

        public void Start(int localPort = 8090)
        {
            if (isRunning || isConnecting) Stop();

            string scriptPath = GetServerScriptPath();
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException("Tunnel script not found at: " + scriptPath);
            }

            string nodeExe = GetNodeExecutablePath();

            isConnecting = true;
            publicUrl = null;

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = nodeExe;
            psi.Arguments = string.Format("\"{0}\" {1}", scriptPath, localPort);
            psi.WorkingDirectory = Path.GetDirectoryName(scriptPath);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;

            tunnelProcess = new Process();
            tunnelProcess.StartInfo = psi;
            tunnelProcess.EnableRaisingEvents = true;

            tunnelProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    HandleOutputLine(e.Data);
                }
            };

            tunnelProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    HandleErrorLine(e.Data);
                }
            };

            tunnelProcess.Exited += (s, e) =>
            {
                isConnecting = false;
                isRunning = false;
                publicUrl = null;
                PostToUI(delegate
                {
                    if (OnTunnelClosed != null) OnTunnelClosed();
                });
            };

            try
            {
                tunnelProcess.Start();
                tunnelProcess.BeginOutputReadLine();
                tunnelProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                isConnecting = false;
                isRunning = false;
                throw new InvalidOperationException("Failed to launch tunnel runtime: " + ex.Message, ex);
            }
        }

        public void Stop()
        {
            isConnecting = false;
            isRunning = false;
            publicUrl = null;

            if (tunnelProcess != null)
            {
                try
                {
                    if (!tunnelProcess.HasExited)
                    {
                        tunnelProcess.Kill();
                    }
                }
                catch { }
                finally
                {
                    try { tunnelProcess.Dispose(); } catch { }
                    tunnelProcess = null;
                }
            }
        }

        private void HandleOutputLine(string line)
        {
            Match match = Regex.Match(line, @"PUBLIC_URL:(https?://[^\s]+)");
            if (match.Success)
            {
                publicUrl = match.Groups[1].Value.Trim();
                isRunning = true;
                isConnecting = false;
                PostToUI(delegate
                {
                    if (OnPublicUrlReceived != null) OnPublicUrlReceived(publicUrl);
                });
            }
            else if (line.Contains("TUNNEL_CLOSED"))
            {
                isConnecting = false;
                isRunning = false;
                publicUrl = null;
                PostToUI(delegate
                {
                    if (OnTunnelClosed != null) OnTunnelClosed();
                });
            }
        }

        private void HandleErrorLine(string line)
        {
            Match match = Regex.Match(line, @"TUNNEL_ERROR:(.+)");
            string errMessage = match.Success ? match.Groups[1].Value : line;
            isConnecting = false;
            PostToUI(delegate
            {
                if (OnTunnelError != null) OnTunnelError(errMessage);
            });
        }

        private void PostToUI(Action action)
        {
            if (syncContext != null)
            {
                syncContext.Post(delegate { action(); }, null);
            }
            else
            {
                action();
            }
        }
    }

    /// <summary>
    /// Custom Card panel with sleek border and dark surface fill.
    /// </summary>
    public class ModernCardPanel : Panel
    {
        private Color borderColor = Color.FromArgb(30, 41, 59); // #1e293b

        public Color BorderColor
        {
            get { return borderColor; }
            set { borderColor = value; Invalidate(); }
        }

        public ModernCardPanel()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Color.FromArgb(19, 27, 46); // #131b2e
            this.Padding = new Padding(12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(borderColor, 1))
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                e.Graphics.DrawRectangle(pen, r);
            }
        }
    }

    /// <summary>
    /// Modern Pill-style Toggle Switch matching the user wireframe.
    /// </summary>
    public class ModernToggleSwitch : Control
    {
        private bool isChecked = false;
        private Color onColor = Color.FromArgb(16, 185, 129); // emerald-500 (#10b981)
        private Color offColor = Color.FromArgb(51, 65, 85);   // slate-700 (#334155)
        private Color knobColor = Color.White;
        private Color borderColor = Color.FromArgb(71, 85, 105);

        public event EventHandler ToggleClicked;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                isChecked = value;
                Invalidate();
            }
        }

        public ModernToggleSwitch()
        {
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.Size = new Size(68, 32);
            this.BackColor = Color.Transparent;
            this.Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (ToggleClicked != null)
            {
                ToggleClicked(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int diameter = Height - 6;
            Rectangle fillRect = new Rectangle(1, 1, Width - 2, Height - 2);

            using (GraphicsPath path = GetRoundedRectPath(fillRect, Height / 2))
            {
                using (SolidBrush brush = new SolidBrush(isChecked ? onColor : offColor))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(isChecked ? onColor : borderColor, 1))
                {
                    g.DrawPath(pen, path);
                }
            }

            int knobX = isChecked ? (Width - diameter - 4) : 4;
            Rectangle knobRect = new Rectangle(knobX, 3, diameter, diameter);

            using (SolidBrush knobBrush = new SolidBrush(knobColor))
            {
                g.FillEllipse(knobBrush, knobRect);
            }
        }

        private GraphicsPath GetRoundedRectPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// Interactive Windows Forms UI Panel matching OKZ Share wireframe.
    /// </summary>
    public class LocalShareControl : UserControl
    {
        private enum ActiveShareMode { None, Local, Online }
        private ActiveShareMode currentMode = ActiveShareMode.None;

        private LocalFileShareServer lanServer = new LocalFileShareServer();
        private TunnelManager tunnelManager = new TunnelManager();

        private string currentSharedFolder = string.Empty;
        private ToolTip toolTip;

        // Top Shared Folder Card
        private ModernCardPanel cardFolder;
        private Panel pnlInputGroup;
        private Label lblFolderIcon;
        private TextBox txtFolderPath;
        private Button btnChooseFolder;

        // Toggle Switch Cards (Middle Row)
        private TableLayoutPanel tableToggles;
        private ModernCardPanel cardLocalToggle;
        private ModernToggleSwitch switchLocal;
        private Label lblLocalTitle;

        private ModernCardPanel cardOnlineToggle;
        private ModernToggleSwitch switchOnline;
        private Label lblOnlineTitle;

        // Unified Large QR & Link Card (Bottom)
        private ModernCardPanel cardQrSection;
        private Label lblQrHeader;
        private Panel pnlQrWrapper;
        private Panel boxQr;
        private PictureBox picQr;
        private Label lblPlaceholder;
        private Panel pnlBottomInfo;
        private TextBox txtActiveUrl;
        private Button btnCopyUrl;
        private Button btnOpenBrowser;

        // Credit Footer
        private Panel pnlCreditFooter;
        private Label lblCreditText;
        private LinkLabel lnkCreditEmail;

        public LocalShareControl()
        {
            this.BackColor = Color.FromArgb(15, 23, 42); // #0f172a
            this.ForeColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.Dock = DockStyle.Fill;
            this.AutoScroll = true;

            toolTip = new ToolTip();
            toolTip.InitialDelay = 300;
            toolTip.ReshowDelay = 100;
            toolTip.AutoPopDelay = 10000;

            InitializeLayout();
            SetupTunnelEvents();
            LoadSavedFolder();
        }

        private void InitializeLayout()
        {
            this.SuspendLayout();

            Panel mainContainer = new Panel();
            mainContainer.Dock = DockStyle.Fill;
            mainContainer.Padding = new Padding(18, 14, 18, 8);
            mainContainer.BackColor = Color.FromArgb(15, 23, 42);
            this.Controls.Add(mainContainer);

            // ================= 1. DOCKED TO BOTTOM: CREDIT FOOTER =================
            pnlCreditFooter = new Panel();
            pnlCreditFooter.Dock = DockStyle.Bottom;
            pnlCreditFooter.Height = 28;
            pnlCreditFooter.BackColor = Color.Transparent;

            Panel pnlCreditCenter = new Panel();
            pnlCreditCenter.Height = 24;
            pnlCreditCenter.Width = 350;
            pnlCreditCenter.BackColor = Color.Transparent;
            pnlCreditFooter.Controls.Add(pnlCreditCenter);

            lblCreditText = new Label();
            lblCreditText.UseMnemonic = false;
            lblCreditText.Text = "Made with ❤️ by azizokz";
            lblCreditText.Font = new Font("Segoe UI", 8.5F);
            lblCreditText.ForeColor = Color.FromArgb(148, 163, 184); // #94a3b8
            lblCreditText.AutoSize = true;
            lblCreditText.Location = new Point(0, 4);
            pnlCreditCenter.Controls.Add(lblCreditText);

            lnkCreditEmail = new LinkLabel();
            lnkCreditEmail.UseMnemonic = false;
            lnkCreditEmail.Text = "(azizokz@gmail.com)";
            lnkCreditEmail.Font = new Font("Segoe UI", 8.5F);
            lnkCreditEmail.LinkColor = Color.FromArgb(56, 189, 248); // #38bdf8
            lnkCreditEmail.ActiveLinkColor = Color.FromArgb(14, 165, 233);
            lnkCreditEmail.VisitedLinkColor = Color.FromArgb(56, 189, 248);
            lnkCreditEmail.LinkBehavior = LinkBehavior.HoverUnderline;
            lnkCreditEmail.AutoSize = true;
            lnkCreditEmail.Cursor = Cursors.Hand;
            lnkCreditEmail.Location = new Point(144, 4);
            toolTip.SetToolTip(lnkCreditEmail, "Send email to azizokz@gmail.com");
            lnkCreditEmail.LinkClicked += (s, e) =>
            {
                try { Process.Start("mailto:azizokz@gmail.com"); } catch { }
            };
            pnlCreditCenter.Controls.Add(lnkCreditEmail);

            pnlCreditFooter.Resize += (s, e) =>
            {
                pnlCreditCenter.Location = new Point((pnlCreditFooter.Width - pnlCreditCenter.Width) / 2, 2);
            };

            // Spacer above Credit Footer
            Panel pnlSpacerFooter = new Panel();
            pnlSpacerFooter.Dock = DockStyle.Bottom;
            pnlSpacerFooter.Height = 8;
            pnlSpacerFooter.BackColor = Color.Transparent;

            // ================= 2. DOCKED TO TOP: HEADER, FOLDER, TOGGLES =================

            // Header Panel
            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 54;
            pnlHeader.BackColor = Color.Transparent;

            Label lblAppTitle = new Label();
            lblAppTitle.UseMnemonic = false;
            lblAppTitle.Text = "OKZ Share — Local & Internet";
            lblAppTitle.Font = new Font("Segoe UI", 13.5F, FontStyle.Bold);
            lblAppTitle.ForeColor = Color.FromArgb(56, 189, 248); // #38bdf8
            lblAppTitle.Location = new Point(0, 0);
            lblAppTitle.AutoSize = true;
            pnlHeader.Controls.Add(lblAppTitle);

            Label lblAppSub = new Label();
            lblAppSub.UseMnemonic = false;
            lblAppSub.Text = "Instant peer-to-peer sharing over Wi-Fi and secure worldwide internet tunneling.";
            lblAppSub.Font = new Font("Segoe UI", 8.5F);
            lblAppSub.ForeColor = Color.FromArgb(148, 163, 184); // #94a3b8
            lblAppSub.Location = new Point(2, 26);
            lblAppSub.AutoSize = true;
            pnlHeader.Controls.Add(lblAppSub);

            // Top Card: Shared Folder Selection
            cardFolder = new ModernCardPanel();
            cardFolder.Dock = DockStyle.Top;
            cardFolder.Height = 82;
            cardFolder.Padding = new Padding(12, 8, 12, 8);

            Label lblFolderHeader = new Label();
            lblFolderHeader.UseMnemonic = false;
            lblFolderHeader.Text = "Shared Folder";
            lblFolderHeader.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblFolderHeader.ForeColor = Color.FromArgb(203, 213, 225);
            lblFolderHeader.Location = new Point(10, 8);
            lblFolderHeader.AutoSize = true;
            cardFolder.Controls.Add(lblFolderHeader);

            pnlInputGroup = new Panel();
            pnlInputGroup.Location = new Point(10, 32);
            pnlInputGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            pnlInputGroup.Height = 36;
            pnlInputGroup.Width = cardFolder.Width - 20;
            pnlInputGroup.BackColor = Color.FromArgb(23, 32, 51); // #172033
            pnlInputGroup.BorderStyle = BorderStyle.FixedSingle;
            cardFolder.Controls.Add(pnlInputGroup);

            btnChooseFolder = new Button();
            btnChooseFolder.UseMnemonic = false;
            btnChooseFolder.Text = "📁 Choose folder";
            btnChooseFolder.Dock = DockStyle.Right;
            btnChooseFolder.Width = 140;
            btnChooseFolder.FlatStyle = FlatStyle.Flat;
            btnChooseFolder.FlatAppearance.BorderSize = 0;
            btnChooseFolder.BackColor = Color.FromArgb(2, 132, 199); // #0284c7
            btnChooseFolder.ForeColor = Color.White;
            btnChooseFolder.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnChooseFolder.Cursor = Cursors.Hand;
            btnChooseFolder.Click += (s, e) => ChooseFolderDialog();
            pnlInputGroup.Controls.Add(btnChooseFolder);

            lblFolderIcon = new Label();
            lblFolderIcon.UseMnemonic = false;
            lblFolderIcon.Text = "📂";
            lblFolderIcon.Dock = DockStyle.Left;
            lblFolderIcon.Width = 28;
            lblFolderIcon.TextAlign = ContentAlignment.MiddleCenter;
            lblFolderIcon.Font = new Font("Segoe UI", 9.5F);
            lblFolderIcon.BackColor = Color.Transparent;
            pnlInputGroup.Controls.Add(lblFolderIcon);

            Panel pnlTextWrap = new Panel();
            pnlTextWrap.Dock = DockStyle.Fill;
            pnlTextWrap.Padding = new Padding(4, 8, 8, 0);
            pnlInputGroup.Controls.Add(pnlTextWrap);

            txtFolderPath = new TextBox();
            txtFolderPath.Dock = DockStyle.Fill;
            txtFolderPath.ReadOnly = true;
            txtFolderPath.BackColor = Color.FromArgb(23, 32, 51); // #172033
            txtFolderPath.ForeColor = Color.FromArgb(226, 232, 240);
            txtFolderPath.BorderStyle = BorderStyle.None;
            txtFolderPath.Font = new Font("Consolas", 9.5F);
            txtFolderPath.Text = "No folder selected. Click \"Choose folder\" to begin.";
            pnlTextWrap.Controls.Add(txtFolderPath);

            // Spacer 1
            Panel pnlSpacer1 = new Panel();
            pnlSpacer1.Dock = DockStyle.Top;
            pnlSpacer1.Height = 10;
            pnlSpacer1.BackColor = Color.Transparent;

            // Middle Row: Toggle Switch Cards
            tableToggles = new TableLayoutPanel();
            tableToggles.Dock = DockStyle.Top;
            tableToggles.Height = 96;
            tableToggles.ColumnCount = 2;
            tableToggles.RowCount = 1;
            tableToggles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableToggles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableToggles.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Local Toggle Card
            cardLocalToggle = new ModernCardPanel();
            cardLocalToggle.Dock = DockStyle.Fill;
            cardLocalToggle.Margin = new Padding(0, 0, 6, 0);
            cardLocalToggle.Cursor = Cursors.Hand;
            cardLocalToggle.Click += (s, e) => HandleLocalToggleRequest();
            tableToggles.Controls.Add(cardLocalToggle, 0, 0);

            switchLocal = new ModernToggleSwitch();
            switchLocal.Size = new Size(64, 30);
            switchLocal.Location = new Point((cardLocalToggle.Width - switchLocal.Width) / 2, 12);
            switchLocal.Anchor = AnchorStyles.Top;
            switchLocal.ToggleClicked += (s, e) => HandleLocalToggleRequest();
            cardLocalToggle.Controls.Add(switchLocal);

            lblLocalTitle = new Label();
            lblLocalTitle.UseMnemonic = false;
            lblLocalTitle.Text = "Local Share";
            lblLocalTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblLocalTitle.ForeColor = Color.FromArgb(248, 250, 252);
            lblLocalTitle.AutoSize = true;
            lblLocalTitle.Location = new Point((cardLocalToggle.Width - 85) / 2, 52);
            lblLocalTitle.Anchor = AnchorStyles.Top;
            lblLocalTitle.Cursor = Cursors.Hand;
            lblLocalTitle.Click += (s, e) => HandleLocalToggleRequest();
            cardLocalToggle.Controls.Add(lblLocalTitle);

            // Online Toggle Card
            cardOnlineToggle = new ModernCardPanel();
            cardOnlineToggle.Dock = DockStyle.Fill;
            cardOnlineToggle.Margin = new Padding(6, 0, 0, 0);
            cardOnlineToggle.Cursor = Cursors.Hand;
            cardOnlineToggle.Click += (s, e) => HandleOnlineToggleRequest();
            tableToggles.Controls.Add(cardOnlineToggle, 1, 0);

            switchOnline = new ModernToggleSwitch();
            switchOnline.Size = new Size(64, 30);
            switchOnline.Location = new Point((cardOnlineToggle.Width - switchOnline.Width) / 2, 12);
            switchOnline.Anchor = AnchorStyles.Top;
            switchOnline.ToggleClicked += (s, e) => HandleOnlineToggleRequest();
            cardOnlineToggle.Controls.Add(switchOnline);

            lblOnlineTitle = new Label();
            lblOnlineTitle.UseMnemonic = false;
            lblOnlineTitle.Text = "Online Share";
            lblOnlineTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblOnlineTitle.ForeColor = Color.FromArgb(248, 250, 252);
            lblOnlineTitle.AutoSize = true;
            lblOnlineTitle.Location = new Point((cardOnlineToggle.Width - 92) / 2, 52);
            lblOnlineTitle.Anchor = AnchorStyles.Top;
            lblOnlineTitle.Cursor = Cursors.Hand;
            lblOnlineTitle.Click += (s, e) => HandleOnlineToggleRequest();
            cardOnlineToggle.Controls.Add(lblOnlineTitle);

            // Spacer 2
            Panel pnlSpacer2 = new Panel();
            pnlSpacer2.Dock = DockStyle.Top;
            pnlSpacer2.Height = 10;
            pnlSpacer2.BackColor = Color.Transparent;

            // ================= 3. DOCKED TO FILL: UNIFIED QR & ACTION CARD =================
            cardQrSection = new ModernCardPanel();
            cardQrSection.Dock = DockStyle.Fill;
            cardQrSection.Padding = new Padding(14, 10, 14, 10);

            // Status Header at Top of Card
            lblQrHeader = new Label();
            lblQrHeader.UseMnemonic = false;
            lblQrHeader.Text = "⚪ No Active Share — Toggle Local or Online Share above";
            lblQrHeader.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lblQrHeader.ForeColor = Color.FromArgb(148, 163, 184);
            lblQrHeader.Dock = DockStyle.Top;
            lblQrHeader.Height = 26;
            lblQrHeader.TextAlign = ContentAlignment.MiddleCenter;
            cardQrSection.Controls.Add(lblQrHeader);

            // Bottom Info Container (URL + Action Buttons) - Docked to bottom inside cardQrSection
            pnlBottomInfo = new Panel();
            pnlBottomInfo.Dock = DockStyle.Bottom;
            pnlBottomInfo.Height = 74;
            pnlBottomInfo.Padding = new Padding(0, 4, 0, 0);
            pnlBottomInfo.BackColor = Color.Transparent;
            cardQrSection.Controls.Add(pnlBottomInfo);

            txtActiveUrl = new TextBox();
            txtActiveUrl.Dock = DockStyle.Top;
            txtActiveUrl.Height = 26;
            txtActiveUrl.ReadOnly = true;
            txtActiveUrl.BackColor = Color.FromArgb(15, 23, 42); // #0f172a
            txtActiveUrl.ForeColor = Color.FromArgb(100, 116, 139);
            txtActiveUrl.BorderStyle = BorderStyle.FixedSingle;
            txtActiveUrl.Font = new Font("Consolas", 9.5F);
            txtActiveUrl.TextAlign = HorizontalAlignment.Center;
            txtActiveUrl.Text = "Share is idle";
            pnlBottomInfo.Controls.Add(txtActiveUrl);

            Panel pnlActions = new Panel();
            pnlActions.Dock = DockStyle.Bottom;
            pnlActions.Height = 34;
            pnlActions.BackColor = Color.Transparent;
            pnlBottomInfo.Controls.Add(pnlActions);

            TableLayoutPanel tableActions = new TableLayoutPanel();
            tableActions.Dock = DockStyle.Fill;
            tableActions.ColumnCount = 2;
            tableActions.RowCount = 1;
            tableActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            pnlActions.Controls.Add(tableActions);

            btnCopyUrl = CreateStyledButton("📋 Copy Link", Color.FromArgb(2, 132, 199), Color.FromArgb(56, 189, 248));
            btnCopyUrl.Dock = DockStyle.Fill;
            btnCopyUrl.Margin = new Padding(0, 1, 4, 1);
            btnCopyUrl.Enabled = false;
            btnCopyUrl.Click += (s, e) => CopyActiveUrl();
            tableActions.Controls.Add(btnCopyUrl, 0, 0);

            btnOpenBrowser = CreateStyledButton("🌐 Open Browser", Color.FromArgb(30, 41, 59), Color.FromArgb(71, 85, 105));
            btnOpenBrowser.Dock = DockStyle.Fill;
            btnOpenBrowser.Margin = new Padding(4, 1, 0, 1);
            btnOpenBrowser.Enabled = false;
            btnOpenBrowser.Click += (s, e) => OpenInBrowser();
            tableActions.Controls.Add(btnOpenBrowser, 1, 0);

            // Center Panel for QR Code Frame
            pnlQrWrapper = new Panel();
            pnlQrWrapper.Dock = DockStyle.Fill;
            pnlQrWrapper.Padding = new Padding(8, 4, 8, 8);
            pnlQrWrapper.BackColor = Color.Transparent;
            cardQrSection.Controls.Add(pnlQrWrapper);

            boxQr = new Panel();
            boxQr.Size = new Size(180, 180);
            boxQr.BackColor = Color.White;
            boxQr.Padding = new Padding(8);
            boxQr.Visible = false;
            pnlQrWrapper.Controls.Add(boxQr);

            picQr = new PictureBox();
            picQr.Dock = DockStyle.Fill;
            picQr.BackColor = Color.White;
            picQr.SizeMode = PictureBoxSizeMode.Zoom;
            boxQr.Controls.Add(picQr);

            lblPlaceholder = new Label();
            lblPlaceholder.UseMnemonic = false;
            lblPlaceholder.Text = "📡\n\nTurn on Local Share or Online Share\nto generate QR Code and link.";
            lblPlaceholder.Font = new Font("Segoe UI", 9.5F);
            lblPlaceholder.ForeColor = Color.FromArgb(100, 116, 139);
            lblPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
            lblPlaceholder.Dock = DockStyle.Fill;
            pnlQrWrapper.Controls.Add(lblPlaceholder);

            // Dynamic centering of boxQr inside pnlQrWrapper
            pnlQrWrapper.Resize += (s, e) =>
            {
                int availW = pnlQrWrapper.Width - 16;
                int availH = pnlQrWrapper.Height - 16;
                int side = Math.Min(availW, availH);
                if (side > 200) side = 200;
                if (side < 110) side = 110;

                boxQr.Size = new Size(side, side);
                boxQr.Location = new Point((pnlQrWrapper.Width - side) / 2, Math.Max(2, (pnlQrWrapper.Height - side) / 2));
            };

            cardLocalToggle.Resize += (s, e) =>
            {
                switchLocal.Location = new Point((cardLocalToggle.Width - switchLocal.Width) / 2, 12);
                lblLocalTitle.Location = new Point((cardLocalToggle.Width - lblLocalTitle.Width) / 2, 52);
            };
            cardOnlineToggle.Resize += (s, e) =>
            {
                switchOnline.Location = new Point((cardOnlineToggle.Width - switchOnline.Width) / 2, 12);
                lblOnlineTitle.Location = new Point((cardOnlineToggle.Width - lblOnlineTitle.Width) / 2, 52);
            };

            // ADD CONTROLS TO MAIN CONTAINER IN PROPER DOCKING HIERARCHY
            mainContainer.Controls.Add(cardQrSection);    // Dock: Fill
            mainContainer.Controls.Add(pnlSpacerFooter);  // Dock: Bottom
            mainContainer.Controls.Add(pnlCreditFooter);  // Dock: Bottom
            mainContainer.Controls.Add(pnlSpacer2);       // Dock: Top
            mainContainer.Controls.Add(tableToggles);     // Dock: Top
            mainContainer.Controls.Add(pnlSpacer1);       // Dock: Top
            mainContainer.Controls.Add(cardFolder);       // Dock: Top
            mainContainer.Controls.Add(pnlHeader);         // Dock: Top

            this.ResumeLayout(false);
        }

        private Button CreateStyledButton(string text, Color backColor, Color borderColor)
        {
            Button btn = new Button();
            btn.UseMnemonic = false;
            btn.Text = text;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderColor = borderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.BackColor = backColor;
            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
            btn.UseVisualStyleBackColor = false;
            return btn;
        }

        private void SetupTunnelEvents()
        {
            tunnelManager.OnPublicUrlReceived += (url) =>
            {
                if (currentMode != ActiveShareMode.Online) return;

                lblQrHeader.Text = "🟢 Online Share Active (Internet Tunnel)";
                lblQrHeader.ForeColor = Color.FromArgb(16, 185, 129); // emerald-500

                txtActiveUrl.Text = url;
                txtActiveUrl.ForeColor = Color.FromArgb(56, 189, 248); // sky-400

                btnCopyUrl.Enabled = true;
                btnOpenBrowser.Enabled = true;

                if (picQr.Image != null) picQr.Image.Dispose();
                picQr.Image = GenerateQrBitmap(url);
                boxQr.Visible = true;
                lblPlaceholder.Visible = false;
            };

            tunnelManager.OnTunnelError += (errMsg) =>
            {
                if (currentMode != ActiveShareMode.Online) return;

                switchOnline.Checked = false;
                currentMode = ActiveShareMode.None;

                lblQrHeader.Text = "❌ Tunnel Error: " + errMsg;
                lblQrHeader.ForeColor = Color.FromArgb(239, 68, 68);

                txtActiveUrl.Text = "Failed to establish tunnel";
                txtActiveUrl.ForeColor = Color.FromArgb(239, 68, 68);

                btnCopyUrl.Enabled = false;
                btnOpenBrowser.Enabled = false;
                boxQr.Visible = false;
                lblPlaceholder.Visible = true;
                if (picQr.Image != null)
                {
                    picQr.Image.Dispose();
                    picQr.Image = null;
                }
            };

            tunnelManager.OnTunnelClosed += () =>
            {
                if (currentMode == ActiveShareMode.Online)
                {
                    StopAllShares();
                }
            };
        }

        private void LoadSavedFolder()
        {
            string savedFolder = ShareSettings.LoadLastFolder();
            if (!string.IsNullOrEmpty(savedFolder) && Directory.Exists(savedFolder))
            {
                SetSharedFolder(savedFolder);
            }
        }

        private void ChooseFolderDialog()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select a folder to share:";
                if (!string.IsNullOrEmpty(currentSharedFolder) && Directory.Exists(currentSharedFolder))
                {
                    dlg.SelectedPath = currentSharedFolder;
                }

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    SetSharedFolder(dlg.SelectedPath);
                    ShareSettings.SaveLastFolder(dlg.SelectedPath);

                    // If active share is running, reload server with new folder
                    if (currentMode == ActiveShareMode.Local)
                    {
                        lanServer.Start(currentSharedFolder, lanServer.Port);
                        UpdateActiveUrlAndQr(lanServer.GetShareUrl(), "🟢 Local Share Active (Wi-Fi)");
                    }
                    else if (currentMode == ActiveShareMode.Online)
                    {
                        lanServer.Start(currentSharedFolder, lanServer.Port);
                    }
                }
            }
        }

        private void SetSharedFolder(string folderPath)
        {
            currentSharedFolder = folderPath;
            txtFolderPath.Text = TruncateMiddlePath(folderPath, 75);
            txtFolderPath.ForeColor = Color.FromArgb(248, 250, 252);
            toolTip.SetToolTip(txtFolderPath, folderPath);
        }

        private static string TruncateMiddlePath(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
                return path;

            int sideLength = (maxLength - 3) / 2;
            if (sideLength < 6) sideLength = 6;
            return path.Substring(0, sideLength) + "..." + path.Substring(path.Length - sideLength);
        }

        // ====================== SINGLE ACTIVE SHARE MUTUAL EXCLUSION ======================

        private void HandleLocalToggleRequest()
        {
            if (string.IsNullOrEmpty(currentSharedFolder) || !Directory.Exists(currentSharedFolder))
            {
                MessageBox.Show("Please choose a folder to share first.", "No Folder Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (currentMode == ActiveShareMode.Local)
            {
                // Turn OFF Local Share
                StopAllShares();
            }
            else if (currentMode == ActiveShareMode.Online)
            {
                // Online Share is currently active -> Prompt warning before switching
                DialogResult dr = MessageBox.Show(
                    this,
                    "Online Share (Internet Tunnel) is currently active.\n\nEnabling Local Share will close the public tunnel and switch to local Wi-Fi sharing.\n\nDo you want to switch to Local Share?",
                    "Switch to Local Share?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Yes)
                {
                    StartLocalShare();
                }
            }
            else
            {
                // Start Local Share from idle
                StartLocalShare();
            }
        }

        private void HandleOnlineToggleRequest()
        {
            if (string.IsNullOrEmpty(currentSharedFolder) || !Directory.Exists(currentSharedFolder))
            {
                MessageBox.Show("Please choose a folder to share first.", "No Folder Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (currentMode == ActiveShareMode.Online)
            {
                // Turn OFF Online Share
                StopAllShares();
            }
            else if (currentMode == ActiveShareMode.Local)
            {
                // Local Share is currently active -> Prompt warning that this will switch to internet share QR code
                DialogResult dr = MessageBox.Show(
                    this,
                    "Local Share is currently active.\n\nEnabling Online Share will switch to an Internet Tunnel and generate a worldwide Internet Share QR Code.\n\nDo you want to switch to Online Share?",
                    "Switch to Online Share?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (dr == DialogResult.Yes)
                {
                    StartOnlineShare();
                }
            }
            else
            {
                // Start Online Share from idle
                StartOnlineShare();
            }
        }

        private void StartLocalShare()
        {
            try
            {
                tunnelManager.Stop();

                lanServer.Start(currentSharedFolder, 8090);
                string url = lanServer.GetShareUrl();

                currentMode = ActiveShareMode.Local;
                switchLocal.Checked = true;
                switchOnline.Checked = false;

                UpdateActiveUrlAndQr(url, "🟢 Local Share Active (Wi-Fi)");
            }
            catch (Exception ex)
            {
                StopAllShares();
                MessageBox.Show("Could not start local share: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartOnlineShare()
        {
            try
            {
                lanServer.Start(currentSharedFolder, 8090);

                currentMode = ActiveShareMode.Online;
                switchOnline.Checked = true;
                switchLocal.Checked = false;

                lblQrHeader.Text = "🟡 Connecting Internet Tunnel...";
                lblQrHeader.ForeColor = Color.FromArgb(245, 158, 11); // amber-500

                txtActiveUrl.Text = "Establishing secure tunnel...";
                txtActiveUrl.ForeColor = Color.FromArgb(245, 158, 11);

                btnCopyUrl.Enabled = false;
                btnOpenBrowser.Enabled = false;

                boxQr.Visible = false;
                lblPlaceholder.Visible = true;
                lblPlaceholder.Text = "🔄\n\nConnecting to tunnel server...";

                tunnelManager.Start(lanServer.Port);
            }
            catch (Exception ex)
            {
                StopAllShares();
                MessageBox.Show("Failed to launch internet tunnel:\n" + ex.Message, "Tunnel Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopAllShares()
        {
            currentMode = ActiveShareMode.None;
            switchLocal.Checked = false;
            switchOnline.Checked = false;

            tunnelManager.Stop();
            lanServer.Stop();

            lblQrHeader.Text = "⚪ No Active Share — Toggle Local or Online Share above";
            lblQrHeader.ForeColor = Color.FromArgb(148, 163, 184);

            txtActiveUrl.Text = "Share is idle";
            txtActiveUrl.ForeColor = Color.FromArgb(100, 116, 139);

            btnCopyUrl.Enabled = false;
            btnOpenBrowser.Enabled = false;

            boxQr.Visible = false;
            lblPlaceholder.Visible = true;
            lblPlaceholder.Text = "📡\n\nTurn on Local Share or Online Share\nto generate QR Code and link.";

            if (picQr.Image != null)
            {
                picQr.Image.Dispose();
                picQr.Image = null;
            }
        }

        private void UpdateActiveUrlAndQr(string url, string statusHeader)
        {
            lblQrHeader.Text = statusHeader;
            lblQrHeader.ForeColor = Color.FromArgb(16, 185, 129); // emerald-500

            txtActiveUrl.Text = url;
            txtActiveUrl.ForeColor = Color.FromArgb(16, 185, 129);

            btnCopyUrl.Enabled = true;
            btnOpenBrowser.Enabled = true;

            if (picQr.Image != null) picQr.Image.Dispose();
            picQr.Image = GenerateQrBitmap(url);
            boxQr.Visible = true;
            lblPlaceholder.Visible = false;
        }

        private Bitmap GenerateQrBitmap(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
                using (QRCode qrCode = new QRCode(qrCodeData))
                {
                    return qrCode.GetGraphic(6);
                }
            }
            catch
            {
                return null;
            }
        }

        private void CopyActiveUrl()
        {
            string url = txtActiveUrl.Text.Trim();
            if (string.IsNullOrEmpty(url) || url.StartsWith("Share is idle") || url.StartsWith("Failed")) return;

            try
            {
                Clipboard.SetText(url);
                MessageBox.Show("Link copied to clipboard!\n\n" + url, "Link Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not copy link: " + ex.Message, "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenInBrowser()
        {
            string url = txtActiveUrl.Text.Trim();
            if (string.IsNullOrEmpty(url) || url.StartsWith("Share is idle") || url.StartsWith("Failed")) return;

            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open browser: " + ex.Message, "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void Shutdown()
        {
            try
            {
                if (tunnelManager != null)
                {
                    tunnelManager.Stop();
                }
                if (lanServer != null && lanServer.IsRunning)
                {
                    lanServer.Stop();
                }
                if (picQr != null && picQr.Image != null)
                {
                    picQr.Image.Dispose();
                    picQr.Image = null;
                }
            }
            catch { }
        }
    }
}
