@echo off
rem Builds bin\OpaGachi.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
rem No SDK or install needed. The code sticks to C# 5 for that reason.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework 4.x C# compiler.
  exit /b 1
)
rem UI Automation (reading desktop AI apps) ships with .NET Framework as WPF assemblies.
for %%I in ("%CSC%") do set "WPF=%%~dpIWPF"
if not exist bin mkdir bin
set "ICON="
if exist assets\icon.ico set "ICON=/win32icon:assets\icon.ico"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /unsafe /warn:4 ^
  /out:bin\OpaGachi.exe /win32manifest:src\app.manifest %ICON% ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  /r:"%WPF%\UIAutomationClient.dll" /r:"%WPF%\UIAutomationTypes.dll" /r:"%WPF%\WindowsBase.dll" ^
  src\*.cs || exit /b 1
echo Built bin\OpaGachi.exe
