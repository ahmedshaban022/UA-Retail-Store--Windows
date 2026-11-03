param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Signs in to an already-configured StoreFlow and captures the shell.
#
# The picture is taken by the application itself (--capture) rather than by a screen or window
# capture. A screen capture needs an unlocked desktop, and PrintWindow draws a right-to-left
# window mirrored - both of which make the result useless for checking the layout.

$exe = $ExePath
$outDir = 'C:\Users\ahmed\.minimax\v2\sessions\2026\10\25\15-42-28-262-session_bXZzX2RhYzY2YzI3Zjg3MjQ4N2E5YTMxZTNlZDYzYWVhZGJh\artifacts'
$capture = Join-Path $outDir 'phase0-shell.png'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Wait-FirstWindow([int]$processId, [int]$seconds = 25) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
        $all = $AE::RootElement.FindAll($TS::Children, $cond)
        if ($all.Count -gt 0) { return $all[0] }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Get-Edits($window, [bool]$password) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $all = $window.FindAll($TS::Descendants, $cond)
    $result = @()
    foreach ($e in $all) {
        try {
            if (-not $e.Current.IsOffscreen -and $e.Current.IsPassword -eq $password) { $result += $e }
        } catch { }
    }
    return $result
}

function Set-EditValue($element, [string]$value) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Invoke-AutomationId($window, [string]$automationId) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)))
    $b = $window.FindFirst($TS::Descendants, $cond)
    if (-not $b) { return $false }
    try { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { return $false }
}

Get-Process | Where-Object { $_.ProcessName -like 'StoreFlow*' } | ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Milliseconds 500

$p = Start-Process -FilePath $exe -ArgumentList @('--capture', $capture) -PassThru
Write-Host ('launched pid=' + $p.Id)

$login = Wait-FirstWindow $p.Id
if (-not $login) {
    Write-Host 'FAIL: no application window appeared'
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Host ('sign-in window is up: [' + $login.Current.Name + ']')

$edits = Get-Edits $login $false
$passwords = Get-Edits $login $true
Write-Host ('  text fields = ' + $edits.Count + ', password fields = ' + $passwords.Count)
if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' }
if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 400
Write-Host ('  clicked Sign in: ' + (Invoke-AutomationId $login 'Login.SignIn'))
Start-Sleep -Seconds 8

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$windows = $AE::RootElement.FindAll($TS::Children, $cond)
foreach ($w in $windows) {
    Write-Host ('  window=[' + $w.Current.Name + ']')
}

if (Test-Path $capture) {
    Write-Host ('  capture -> ' + $capture + ' (' + (Get-Item $capture).Length + ' bytes)')
} else {
    Write-Host '  FAIL: the application wrote no capture'
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Host 'stopped'
