@echo off
rem Builds the release:
rem   dist\OpaGachi\            the app exactly as it is installed (OpaGachi.exe, uninstall.exe, assets\, README.txt)
rem   dist\OpaGachi Setup.exe   that folder as a per-user installer (installer\Setup.cs) that also updates in place
rem   dist\OpaGachi-<version>.zip   the download: the installer plus "How to install.txt"
rem User data never ships and never sits in the install folder: it lives in %LOCALAPPDATA%\OpaGachi.
rem For a new release, bump the version in src\AssemblyInfo.cs first (see README "Releasing an update").
rem "package.cmd --skip-build" packages bin\OpaGachi.exe as it is (handy while the app is running and locks it).
rem Code signing: set OPAGACHI_CERT to your code-signing certificate's thumbprint (Current User > Personal store)
rem and every exe is signed (installer\sign.ps1). Windows Smart App Control blocks unsigned builds.
setlocal
cd /d "%~dp0"
if /i not "%~1"=="--skip-build" call "%~dp0build.cmd" || exit /b 1
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "SETUP=/nologo /target:winexe /platform:anycpu /optimize+ /win32manifest:src\app.manifest /win32icon:assets\icon.ico /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll"
set "SETUPSRC=installer\Setup.cs src\AssemblyInfo.cs"
set "OUT=dist\OpaGachi"
if exist dist rmdir /s /q dist
mkdir "%OUT%\assets" || exit /b 1
copy /y bin\OpaGachi.exe "%OUT%\" >nul || exit /b 1
copy /y README-download.txt "%OUT%\README.txt" >nul || exit /b 1
rem Dev-only files stay out: legacy custom sprites, the template preview/manifest, the developer's own agents.json.
robocopy assets "%OUT%\assets" /e /xd custom /xf preview.png manifest.json agents.json *-custom.png /njh /njs /nfl /ndl >nul
if errorlevel 8 exit /b 1
"%CSC%" %SETUP% /out:"%OUT%\uninstall.exe" %SETUPSRC% || exit /b 1
rem The installed exes are signed before they go into the payload, Setup itself after it's built.
if defined OPAGACHI_CERT powershell -NoProfile -ExecutionPolicy Bypass -File installer\sign.ps1 "%OUT%\OpaGachi.exe" "%OUT%\uninstall.exe" || exit /b 1
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::CreateFromDirectory('%CD%\dist\OpaGachi', '%CD%\dist\payload.zip')" || exit /b 1
"%CSC%" %SETUP% /resource:dist\payload.zip,payload.zip /out:"dist\OpaGachi Setup.exe" %SETUPSRC% || exit /b 1
del dist\payload.zip
if defined OPAGACHI_CERT powershell -NoProfile -ExecutionPolicy Bypass -File installer\sign.ps1 "dist\OpaGachi Setup.exe" || exit /b 1
echo Built "dist\OpaGachi Setup.exe"
powershell -NoProfile -ExecutionPolicy Bypass -File installer\zip.ps1 || exit /b 1
if not defined OPAGACHI_CERT echo NOTE: unsigned build. Windows Smart App Control blocks it; see README "Code signing".
