# Silent install -> update -> uninstall of dist\OpaGachi Setup.exe, isolated with OPAGACHI_HOME (fixture hooks + data).
$ErrorActionPreference = 'Stop'
$app = (Resolve-Path "$PSScriptRoot\..\..").Path   # the repository root (this script lives in tools\dev)
$t = Join-Path $env:TEMP 'opagachi-setup-test'
if (Test-Path $t) { Remove-Item -Recurse -Force $t }
$home2 = Join-Path $t 'home'; $dir = Join-Path $t 'install\OpaGachi'; $data = Join-Path $home2 'AppData\Local\OpaGachi'
New-Item -ItemType Directory -Force $home2 | Out-Null
$env:OPAGACHI_HOME = $home2; $env:CODEX_HOME = $null
$setup = Join-Path $app 'dist\OpaGachi Setup.exe'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpaGachi'
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'OpaGachi.lnk'
$realPath = "$env:USERPROFILE\.claude\settings.json"
$realClaude = if (Test-Path $realPath) { (Get-FileHash $realPath).Hash } else { 'none' }
function Check($ok, $label) { if (-not $ok) { throw "FAIL $label" }; "PASS $label" }
if (Test-Path $key) { throw 'An OpaGachi install is already registered; not testing over it.' }
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty $run -Name OpaGachi -ErrorAction SilentlyContinue) { throw 'Run at startup is on; not testing over it.' }

$p = Start-Process $setup -ArgumentList '/silent', "`"/dir=$dir`"" -Wait -PassThru
Check ($p.ExitCode -eq 0 -and (Test-Path "$dir\OpaGachi.exe") -and (Test-Path "$dir\uninstall.exe") -and (Test-Path "$dir\assets\sprites")) 'Fresh install copies the app'
$reg = Get-ItemProperty $key
Check ($reg.DisplayVersion -eq '1.0.0' -and $reg.InstallLocation -eq $dir -and $reg.UninstallString -like '*uninstall.exe*') 'Apps entry registered with version and uninstaller'
Check (Test-Path $menu) 'Start menu shortcut created'
Check (-not (Test-Path (Join-Path $dir 'settings.json'))) 'No user data in the install folder'

# User data and hooks made by the installed copy (fixture home), plus a stale file an update must clear.
Start-Process "$dir\OpaGachi.exe" -ArgumentList '--install-statusline' -Wait; Start-Process "$dir\OpaGachi.exe" -ArgumentList '--install-grok-hook' -Wait
Check ((Get-Content "$home2\.claude\settings.json" -Raw) -match 'statusline' -and (Test-Path "$home2\.grok\hooks\opagachi.json")) 'Installed exe adds fixture hooks'
New-Item -ItemType Directory -Force "$data\sprites\custom\idle" | Out-Null; Set-Content "$data\sprites\custom\idle\IDLE_1.opaclip" 'mine'; Set-Content "$data\settings.json" '{"textSize":2}'
Set-Content "$dir\assets\stale-from-old-release.txt" 'old'

$p = Start-Process $setup -ArgumentList '/silent' -Wait -PassThru
Check ($p.ExitCode -eq 0 -and (Test-Path "$dir\OpaGachi.exe") -and -not (Test-Path "$dir\assets\stale-from-old-release.txt")) 'Update without /dir finds the install and replaces program files'
Check ((Test-Path "$data\sprites\custom\idle\IDLE_1.opaclip") -and (Get-Content "$data\settings.json" -Raw) -match '"textSize":2') 'Update keeps user data'

Set-ItemProperty $key -Name DisplayVersion -Value '9.9.9'; Set-Content "$dir\assets\marker.txt" 'newer build'   # pretend a newer release is installed
$p = Start-Process $setup -ArgumentList '/silent' -Wait -PassThru
Check ($p.ExitCode -eq 2 -and (Test-Path "$dir\assets\marker.txt")) 'Silent setup refuses to downgrade a newer install'
$p = Start-Process $setup -ArgumentList '/silent', '/force' -Wait -PassThru
Check ($p.ExitCode -eq 0 -and (Get-ItemProperty $key).DisplayVersion -eq '1.0.0' -and -not (Test-Path "$dir\assets\marker.txt")) '/force replaces it'

$foreign = Join-Path $t 'foreign'; New-Item -ItemType Directory -Force $foreign | Out-Null; Set-Content "$foreign\mine.txt" 'x'
$p = Start-Process $setup -ArgumentList '/silent', "`"/dir=$foreign`"" -Wait -PassThru
Check ($p.ExitCode -eq 1 -and -not (Test-Path "$foreign\OpaGachi.exe")) 'Refuses a non-empty folder that is not an OpaGachi install'
Check ((Get-ItemProperty $key).InstallLocation -eq $dir) 'Refused install leaves the registration alone'

Set-ItemProperty $run -Name OpaGachi -Value "`"$dir\OpaGachi.exe`" --background"   # as Settings > Startup writes it
Start-Process "$dir\uninstall.exe" -ArgumentList '/silent' -Wait | Out-Null
for ($i = 0; $i -lt 60 -and (Test-Path $dir); $i++) { Start-Sleep -Milliseconds 500 }
Check (-not (Test-Path $dir)) 'Uninstall removes the program folder'
Check (-not (Test-Path $key) -and -not (Test-Path $menu)) 'Uninstall removes the Apps entry and Start menu shortcut'
Check (-not (Get-ItemProperty $run -Name OpaGachi -ErrorAction SilentlyContinue)) 'Uninstall removes the run-at-startup entry'
Check ((Get-Content "$home2\.claude\settings.json" -Raw) -notmatch 'statusline' -and -not (Test-Path "$home2\.grok\hooks\opagachi.json")) 'Uninstall takes the hooks out first'
Check (Test-Path "$data\sprites\custom\idle\IDLE_1.opaclip") 'Silent uninstall keeps user data unless /purge'
Check ($(if (Test-Path $realPath) { (Get-FileHash $realPath).Hash } else { 'none' }) -eq $realClaude -and -not (Test-Path "$env:LOCALAPPDATA\OpaGachi\sprites")) 'Real Claude settings and real data untouched'
'ALL PASSED'
