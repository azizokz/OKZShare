# OKZ Share — Local & Internet 🚀

> **Instant, secure folder sharing over your local Wi-Fi network and worldwide internet tunneling with QR code generation.**

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%207%20%7C%208%20%7C%2010%20%7C%2011-0078d7.svg)
![Runtime](https://img.shields.io/badge/.NET%20Framework-4.0%2B%20%7C%20.NET%206%2B-512bd4.svg)

---

## 🌟 Features

- **📁 One-Click Folder Sharing**: Pick any folder on your computer and share its contents instantly.
- **⚡ Local Wi-Fi / LAN Sharing**: Direct peer-to-peer sharing over your local network using a built-in lightweight HTTP engine (no internet connection required).
- **🌐 Secure Internet Tunneling**: Share with anyone across the globe via a secure encrypted tunnel with zero network configuration or router port-forwarding needed.
- **📱 Instant QR Codes**: Generates high-contrast QR codes for both Local and Online modes — scan with any smartphone camera to open and download files immediately.
- **🎨 Modern Dark Developer UI**: Beautiful Slate dark-theme interface (`#0f172a`), responsive layout, and real-time status indicators.
- **🔒 Directory Traversal Protection**: Full path canonicalization and security checks to guarantee access remains strictly within the chosen shared root directory.
- **💾 Auto-Persistence**: Remembers your last shared folder across launches so you never have to re-select it.
- **📦 Zero-Dependency Setup**: Includes a standalone single-file installer (`OKZShare_Setup.exe`) that sets up the app and creates a Desktop shortcut with 1 click.

---

## 📥 Download & Installation

### Option 1: Standalone Windows Setup (Recommended)
Download **`OKZShare_Setup.exe`** from the [Releases](https://github.com/) section:
1. Run `OKZShare_Setup.exe`.
2. Click **Install OKZ Share**.
3. It installs to `%LocalAppData%\OKZShare` (no administrator privileges needed) and adds a **OKZ Share** shortcut to your Desktop and Start Menu.

### Option 2: Portable Version (.zip)
Download **`OKZShare_v1.0.0_Portable.zip`** from the [Releases](https://github.com/) section:
1. Extract the `.zip` to any folder.
2. Double-click **`OKZShare.exe`** to run immediately.

---

## 🛠️ How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                       OKZ Share                             │
└──────────────┬───────────────────────────────┬──────────────┘
               │                               │
       [ Local Share ]                 [ Online Share ]
               │                               │
        HttpListener                     Tunnel Engine
    (Local Port: 8090)                 (Bundled Runtime)
               │                               │
      http://<lan-ip>:8090/            https://<subdomain>.loca.lt
               │                               │
        📱 Local Phone / Laptop          🌍 Remote User Anywhere
```

1. **Local Share Mode**: Starts an internal `HttpListener` bound to port `8090`, detects the local LAN IP (e.g. `192.168.x.x`), and renders a responsive file manager for connected devices.
2. **Online Share Mode**: Starts the local server and connects a secure tunnel that exposes a public HTTPS URL (`https://*.loca.lt`).
3. **Mutual Exclusion**: Toggle between Local and Online mode with safety confirmation prompts.

---

## 💻 Building from Source

### Prerequisites
- Windows 7, 8, 10, or 11
- .NET Framework 4.0+ (Included by default on Windows)
- Node.js (Only required if re-bundling tunnel packages)

### Compile Portable Application
Run [build.bat](file:///c:/Users/Admin/Downloads/local%20share/build.bat):
```cmd
build.bat
```

### Compile Single-File Setup Installer
Run [build_installer.bat](file:///c:/Users/Admin/Downloads/local%20share/build_installer.bat):
```cmd
build_installer.bat
```

---

## 👤 Author & Credits

- **Developer**: azizokz
- **Email**: [azizokz@gmail.com](mailto:azizokz@gmail.com)
- Made with ❤️

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.
