@echo off
setlocal
echo =======================================================
echo Building OKZ Share Standalone Setup Installer...
echo =======================================================

REM 1. Build Main Application
call build.bat
if errorlevel 1 (
    echo [ERROR] Main application build failed.
    exit /b 1
)

REM 2. Package App Payload into Zip
echo Compressing application payload into package...
if exist payload.zip del /f /q payload.zip
powershell -NoProfile -Command "Compress-Archive -Path 'OKZShare.exe', 'icon.png', 'app.ico' -DestinationPath 'payload.zip' -Force"

if not exist payload.zip (
    echo [ERROR] Failed to create payload.zip.
    exit /b 1
)

REM 3. Compile Installer Executable
echo Compiling OKZShare_Setup.exe...
set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

set ICON_FLAG=
if exist app.ico set ICON_FLAG=/win32icon:app.ico

%CSC% /target:winexe /platform:anycpu /optimize+ %ICON_FLAG% /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll /resource:payload.zip,payload.zip /resource:icon.png,icon.png /out:OKZShare_Setup.exe Installer.cs

REM 4. Cleanup temporary payload
if exist payload.zip del /f /q payload.zip

if errorlevel 1 (
    echo.
    echo ============================================
    echo FAILED: Setup Installer build failed.
    echo ============================================
    exit /b 1
) else (
    echo.
    echo =======================================================
    echo SUCCESS: Built Single-File Installer: OKZShare_Setup.exe
    echo =======================================================
    copy /y OKZShare_Setup.exe LocalFileShare_Setup.exe >nul 2>&1
)
