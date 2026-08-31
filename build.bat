@echo off
setlocal
echo Compiling OKZ Share Portable Executable...

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

set ICON_FLAG=
if exist app.ico set ICON_FLAG=/win32icon:app.ico

%CSC% /target:winexe /platform:anycpu /optimize+ %ICON_FLAG% /reference:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,QRCoder.dll /resource:QRCoder.dll,QRCoder.dll /resource:icon.png,icon.png /out:OKZShare.exe Program.cs LocalFileShare.cs

if errorlevel 1 (
    echo.
    echo ============================================
    echo FAILED: Compilation encountered errors.
    echo ============================================
    exit /b 1
) else (
    echo.
    echo ============================================
    echo SUCCESS: Built OKZShare.exe (Portable)
    echo ============================================
    copy /y OKZShare.exe LocalFileShare.exe >nul 2>&1
)
