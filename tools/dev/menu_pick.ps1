param([string]$name)
# Invokes the open context-menu item called $name (UI Automation; the real mouse isn't touched).
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)),
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)))
for ($i = 0; $i -lt 20; $i++) {
    $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($item) { break }
    Start-Sleep -Milliseconds 150
}
if (-not $item) { "no menu item $name"; exit 1 }
$p = $null
if ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); "invoked $name" }
else { "no invoke pattern" }
