# Builds wizard_test.exe (the real installer\Setup.cs plus the current dist\OpaGachi payload) and drives the setup
# wizard offscreen: Install, Close, Update, Downgrade label, Cancel. Hooks and data are isolated with OPAGACHI_HOME;
# the test install is uninstalled at the end. Run package.cmd first so dist\OpaGachi is current.
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path "$PSScriptRoot\..\..").Path   # the repository root (this script lives in tools\dev)
$t = Join-Path $env:TEMP 'opagachi-wizard-test'
if (Test-Path $t) { Remove-Item -Recurse -Force $t }
New-Item -ItemType Directory -Force "$t\home" | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory("$app\dist\OpaGachi", "$t\payload.zip")
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /main:OpaGachi.Setup.WizardTest "/out:$t\wizard_test.exe" "/win32manifest:$app\src\app.manifest" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/resource:$t\payload.zip,payload.zip" "$app\installer\Setup.cs" "$app\src\AssemblyInfo.cs" "$app\tools\dev\wizard_test.cs"
if ($LASTEXITCODE -ne 0) { throw 'wizard_test build failed' }
$env:OPAGACHI_HOME = "$t\home"; $env:CODEX_HOME = $null
$dir = "$t\install\OpaGachi"
# winexe: run it and collect its console output through a redirect
$p = Start-Process "$t\wizard_test.exe" -ArgumentList "`"$dir`"", "`"$app\tools\dev\captures`"" -Wait -PassThru -RedirectStandardOutput "$t\out.txt"
Get-Content "$t\out.txt"
if (Test-Path "$dir\uninstall.exe") {
    Start-Process "$dir\uninstall.exe" -ArgumentList '/silent' | Out-Null
    for ($i = 0; $i -lt 60 -and (Test-Path $dir); $i++) { Start-Sleep -Milliseconds 500 }
}
"test install removed: " + (-not (Test-Path $dir) -and -not (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpaGachi'))
if ($p.ExitCode -ne 0) { throw "wizard test failed (exit $($p.ExitCode))" }
