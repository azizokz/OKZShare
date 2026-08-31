# OKZ Share — Local & LAN File Sharing 🚀

> **A fast, lightweight, and zero-dependency Windows desktop application to share files and folders across your local network (Wi-Fi / LAN) with instant QR code generation.**

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%207%20%7C%208%20%7C%2010%20%7C%2011-0078d7.svg)
![Runtime](https://img.shields.io/badge/.NET%20Framework-4.0%2B%20%7C%20.NET%206%2B-512bd4.svg)
![Build](https://img.shields.io/badge/build-standalone%20csc-success.svg)

---

## 🌟 Key Features

- **📁 One-Click Folder Sharing**: Select any folder on your computer and make its contents browsable and downloadable instantly.
- **⚡ Local Wi-Fi / LAN Speeds**: Streams files directly peer-to-peer over your local network using .NET's built-in `HttpListener` (no internet required).
- **📱 Instant QR Code Display**: Generates a high-contrast QR code — scan with any smartphone or tablet camera to open the web file browser immediately.
- **🎨 Modern Dark UI**: Sleek Slate dark-theme interface (`#0f172a`), real-time status indicator, and folder persistence.
- **📋 Convenient Action Buttons**: Includes **"Copy Link"** and **"Open in Browser"** buttons for immediate access.
- **🔍 Web Search & File Filtering**: Built-in responsive HTML5 web browser with real-time client-side search and file-type icons.
- **🔒 Path Traversal Protection**: Full path canonicalization and verification to ensure connected clients cannot access files outside the selected directory.
- **📦 100% Single-File Portable**: All dependencies (QR code generator, icons) are embedded directly into `OKZShare.exe` at build time.

---

## 📂 Source Code Structure

| File | Type | Description |
| :--- | :--- | :--- |
| **`LocalFileShare.cs`** | C# Source | Core server engine (`LocalFileShareServer`) and interactive UI control (`LocalShareControl`). |
| **`Program.cs`** | C# Source | Application entry point (`Main`), `MainForm` window wrapper, and runtime embedded assembly resolver. |
| **`Installer.cs`** | C# Source | Self-extracting setup installer source code with automatic Desktop & Start Menu shortcut creation. |
| **`build.bat`** | Batch Script | Compiles the standalone portable executable (`OKZShare.exe`). |
| **`build_installer.bat`** | Batch Script | Packages payload and compiles the setup installer (`OKZShare_Setup.exe`). |
| **`QRCoder.dll`** | Library | Lightweight QR code generation library referenced during compilation and embedded as a resource. |
| **`app.ico`** & **`icon.png`** | Assets | Application and window icons. |
| **`.gitignore`** | Git Config | Ignores temporary build outputs, object files, and local settings. |

---

## 💻 How to Use the Source Code

### Option 1: 1-Click Build (Easiest)

You do **not** need Visual Studio or external package managers installed. OKZ Share compiles using the standard C# compiler (`csc.exe`) built into Windows.

#### 1. Compile the Portable App
Double-click or run [build.bat](build.bat) in your terminal:
```cmd
build.bat
```
* **Output**: Generates `OKZShare.exe` (~350 KB).
* **Usage**: Double-click `OKZShare.exe` to run immediately without installation.

#### 2. Compile the Setup Installer
Double-click or run [build_installer.bat](build_installer.bat):
```cmd
build_installer.bat
```
* **Output**: Generates `OKZShare_Setup.exe`.
* **Usage**: Runs a step-by-step setup wizard that installs the app to `%LocalAppData%\OKZShare` and adds Desktop and Start Menu shortcuts.

---

### Option 2: Manual Command-Line Compilation

You can compile directly using the .NET Framework compiler included in Windows:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:anycpu /optimize+ /win32icon:app.ico /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,QRCoder.dll /resource:QRCoder.dll,QRCoder.dll /resource:icon.png,icon.png /out:OKZShare.exe Program.cs LocalFileShare.cs
```

#### Compiler Flags Explained:
* `/target:winexe` — Builds a Windows GUI application (prevents a background command prompt window from appearing).
* `/platform:anycpu` — Compatible with both 32-bit and 64-bit Windows.
* `/resource:QRCoder.dll,QRCoder.dll` — Embeds the QR library inside the `.exe` so the output is 100% single-file portable.
* `/win32icon:app.ico` — Embeds the icon into the executable binary.

---

### Option 3: Integrate into Your Own C# / WinForms Project

You can reuse the file sharing components directly in other C# applications:

#### 1. Embed the UI Control in a Form
```csharp
using PosInstaller;

public class MyCustomForm : Form
{
    public MyCustomForm()
    {
        InitializeComponent();

        // Add the OKZ Share panel to your form
        LocalShareControl shareControl = new LocalShareControl();
        shareControl.Dock = DockStyle.Fill;
        this.Controls.Add(shareControl);
        
        // Ensure graceful shutdown on window close
        this.FormClosing += (s, e) => shareControl.Shutdown();
    }
}
```

#### 2. Use the Headless HTTP Server in Code
```csharp
using PosInstaller;

// Create and start background server
LocalFileShareServer server = new LocalFileShareServer();
server.Start(@"C:\MyFolderToShare", 8090);

string shareUrl = server.GetShareUrl(); // e.g. "http://192.168.1.50:8090/"
Console.WriteLine("Sharing active at: " + shareUrl);

// Stop when done
server.Stop();
```

---

## 🛠️ How It Works Internally

```
┌─────────────────────────────────────────────────────────────┐
│                    OKZ Share Architecture                   │
└──────────────────────────────┬──────────────────────────────┘
                               │
            ┌──────────────────┴──────────────────┐
            │                                     │
   [ WinForms Frontend ]                 [ HttpListener Server ]
   • LocalShareControl                   • Binds to LAN IP
   • Dynamic QR Code Generation          • Port 8090 (Configurable)
   • Folder Browser Dialog               • Path Traversal Security
   • Clipboard & Browser Launcher        • MIME Detection & Streaming
            │                                     │
            └──────────────────┬──────────────────┘
                               │
               http://192.168.X.X:8090/
                               │
       ┌───────────────────────┴───────────────────────┐
       │                                               │
 📱 Smartphone / Tablet                         💻 Laptop / PC
 (Scan QR Code with Camera)                    (Open in Browser)
```

1. **LAN IP Detection**: Uses UDP socket endpoint binding (`GetLocalIPAddress()`) to determine the host's actual network adapter IP address without sending external packets.
2. **HTTP Server**: Initializes `HttpListener` with prefix `http://+:<port>/` (with direct IP fallback if URL ACLs require standard permissions).
3. **Web Browser Interface**: Dynamically generates an HTML5 directory listing with file icons, folder navigation, instant search filtering, and file download attachments.
4. **Embedded Assembly Loading**: `Program.cs` intercepts `AppDomain.CurrentDomain.AssemblyResolve` to stream `QRCoder.dll` from binary resources into memory at runtime.

---

## ⚙️ Customization & Settings

* **Default Port**: Set to `8090` by default. Can be modified via the UI text box or in code (`server.Start(folderPath, port)`).
* **Last Folder Persistence**: The selected folder path is automatically saved to `share_settings.txt` in the application directory.
* **Theme Styling**: Colors and typography are configured via `Color.FromArgb(15, 23, 42)` (slate-900) in `LocalFileShare.cs` and can be customized to match your branding.

---

## 👤 Author & Credits

* **Developer**: azizokz
* **GitHub**: [@azizokz](https://github.com/azizokz)
* **Email**: [azizokz@gmail.com](mailto:azizokz@gmail.com)

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
