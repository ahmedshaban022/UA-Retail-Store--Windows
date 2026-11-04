param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Signs in to an already-configured StoreFlow and captures the shell.

$exe = $ExePath
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot 'docs\phases\phase0-screens'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Capture {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Find-Window([int]$processId, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $AE::RootElement.FindFirst($TS::Children, $cond)
}

function Wait-Window([int]$processId, [string]$name, [int]$seconds = 25) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Find-Window $processId $name
        if ($w) { return $w }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

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

function Invoke-Button($window, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $b = $window.FindFirst($TS::Descendants, $cond)
    if (-not $b) { return $false }
    try { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { return $false }
}

function Invoke-AutomationId($window, [string]$automationId) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $automationId)))
    $b = $window.FindFirst($TS::Descendants, $cond)
    if (-not $b) { return $false }
    try { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { return $false }
}

function Capture-Window($window, [string]$fileName) {
    # A screen crop is used rather than PrintWindow: PrintWindow mirrors a right-to-left window,
    # which makes the capture useless for checking the layout.
    $handle = [IntPtr]$window.Current.NativeWindowHandle

    # The window has to be on top of everything else, or the crop shows whatever is behind it.
    [Capture]::ShowWindow($handle, 9) | Out-Null      # SW_RESTORE
    [Capture]::BringWindowToTop($handle) | Out-Null
    [Capture]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 800

    $rect = New-Object Capture+RECT
    [Capture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { return $false }

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
    $g.Dispose()
    $path = Join-Path $outDir $fileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("  window capture -> " + $fileName + " (" + $width + 'x' + $height + ')')
    return $true
}

$p = Start-Process -FilePath $exe -PassThru
Write-Output ('launched pid=' + $p.Id)

$login = Wait-FirstWindow $p.Id
if (-not $login) {
    Write-Output 'FAIL: no application window appeared'
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output ('sign-in window is up: [' + $login.Current.Name + ']')
Capture-Window $login 'phase0-04-login.png' | Out-Null

$edits = Get-Edits $login $false
$passwords = Get-Edits $login $true
Write-Output ('  text fields = ' + $edits.Count + ', password fields = ' + $passwords.Count)
if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' }
if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 400
Write-Output ('  clicked Sign in: ' + (Invoke-AutomationId $login 'Login.SignIn'))
Start-Sleep -Seconds 6

$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$windows = $AE::RootElement.FindAll($TS::Children, $cond)
Write-Output '--- application windows ---'
foreach ($w in $windows) {
    Write-Output ('  name=[' + $w.Current.Name + '] type=' + $w.Current.ControlType.ProgrammaticName)
    Capture-Window $w 'phase0-05-shell-window.png' | Out-Null

    Write-Output '  --- rendered text in this window ---'
    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $w.FindAll($TS::Descendants, $textCond)) {
        if ($t.Current.Name) { Write-Output ('    [' + $t.Current.Name + ']') }
    }
    $buttonCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $w.FindAll($TS::Descendants, $buttonCond)) {
        if ($b.Current.Name) { Write-Output ('    <button> [' + $b.Current.Name + ']') }
    }
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
