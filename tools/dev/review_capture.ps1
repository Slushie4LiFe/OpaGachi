$taskRoot = (Resolve-Path "$PSScriptRoot\..\..").Path   # the repository root
Set-Location $taskRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpf = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF"
& $csc /nologo /target:exe /unsafe /main:OpaGachi.ReviewCapture /out:tools\dev\review_capture.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$wpf\UIAutomationClient.dll" "/r:$wpf\UIAutomationTypes.dll" "/r:$wpf\WindowsBase.dll" src\*.cs tools\dev\review_capture.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$taskRoot\tools\dev\review_capture.exe"
