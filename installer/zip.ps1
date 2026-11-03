# Wraps the installer for download: dist\OpaGachi-<version>.zip holding OpaGachi Setup.exe and "How to install.txt".
# Unzip and run the installer (it's self-contained, so it also runs straight from the zip). package.cmd calls this
# last, after signing; zipping doesn't change what Smart App Control checks, so the exe inside must be signed.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$setup = 'dist\OpaGachi Setup.exe'
$zip = 'dist\OpaGachi-' + (Get-Item $setup).VersionInfo.ProductVersion + '.zip'
if (Test-Path $zip) { Remove-Item $zip }
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $setup, 'OpaGachi Setup.exe')
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, 'installer\How to install.txt', 'How to install.txt')
}
finally { $archive.Dispose() }
"Built $zip"
