param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Drives the StoreFlow first-run wizard and the sign-in screen through UI Automation, so the whole
# start-up path is exercised against the real application rather than mocked.

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
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@

function Capture-Window($window, [string]$fileName) {
    $handle = [IntPtr]$window.Current.NativeWindowHandle
    $rect = New-Object Capture+RECT
    [Capture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { return $false }

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Capture]::PrintWindow($handle, $hdc, 2) | Out-Null
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $path = Join-Path $outDir $fileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("  window capture -> " + $fileName + " (" + $width + 'x' + $height + ')')
    return $true
}

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CP = [System.Windows.Automation.Condition]

function Find-Window([int]$processId, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $AE::RootElement.FindFirst($TS::Children, $cond)
}

function Wait-Window([int]$processId, [string]$name, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Find-Window $processId $name
        if ($w) { return $w }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Get-VisibleEdits($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $all = $window.FindAll($TS::Descendants, $cond)
    $result = @()
    foreach ($e in $all) {
        try {
            if (-not $e.Current.IsOffscreen -and -not $e.Current.IsPassword) { $result += $e }
        } catch { }
    }
    return $result
}

function Get-PasswordEdits($window) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $all = $window.FindAll($TS::Descendants, $cond)
    $result = @()
    foreach ($e in $all) {
        try {
            if (-not $e.Current.IsOffscreen -and $e.Current.IsPassword) { $result += $e }
        } catch { }
    }
    return $result
}

function Set-EditValue($element, [string]$value) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}

function Invoke-Button($window, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $b = $window.FindFirst($TS::Descendants, $cond)
    if (-not $b) { return $false }
    try {
        $inv = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $inv.Invoke()
        return $true
    } catch {
        return $false
    }
}

function Save-Screen([string]$fileName) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size)
    $g.Dispose()
    $path = Join-Path $outDir $fileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("  screenshot -> " + $fileName)
}

$p = Start-Process -FilePath $exe -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 6

# --- Step 1: store details -------------------------------------------------
$wizard = Wait-Window $p.Id 'Set up StoreFlow'
if (-not $wizard) { Write-Output 'FAIL: wizard window never appeared'; Stop-Process -Id $p.Id -Force; exit 1 }
Write-Output 'wizard: "Set up StoreFlow" is up'
Save-Screen 'phase0-01-wizard-step1.png'

$edits = Get-VisibleEdits $wizard
Write-Output ('  step 1 visible text fields = ' + $edits.Count)

# Built from code points so the Arabic survives PowerShell 5.1's ANSI reading of this file.
$arabicStoreName = -join (@(0x0645, 0x062A, 0x062C, 0x0631, 0x0020, 0x0645, 0x0635, 0x0631, 0x064A, 0x0629) | ForEach-Object { [char]$_ })

$values = @('Al-Masria Store', $arabicStoreName, '12 Tahrir Street, Cairo', '+20 100 000 0000')
for ($i = 0; $i -lt [Math]::Min($edits.Count, $values.Count); $i++) {
    Set-EditValue $edits[$i] $values[$i]
    Write-Output ('    field ' + $i + ' = ' + $values[$i])
}
Start-Sleep -Milliseconds 400
Write-Output ('  clicked Next: ' + (Invoke-Button $wizard 'Next'))
Start-Sleep -Seconds 2

# --- Step 2: administrator account ----------------------------------------
$edits = Get-VisibleEdits $wizard
Write-Output ('  step 2 visible text fields = ' + $edits.Count)
$passwords = Get-PasswordEdits $wizard
Write-Output ('  step 2 password fields = ' + $passwords.Count)
if ($edits.Count -ge 2) {
    Set-EditValue $edits[0] 'Owner'
    Set-EditValue $edits[1] 'owner'
    Write-Output '    display name = Owner ; user name = owner'
}
if ($passwords.Count -ge 2) {
    Set-EditValue $passwords[0] 'StoreFlow!2026'
    Set-EditValue $passwords[1] 'StoreFlow!2026'
    Write-Output '    password + confirmation filled'
}
Start-Sleep -Milliseconds 400
Save-Screen 'phase0-02-wizard-step2.png'
Write-Output ('  clicked Next: ' + (Invoke-Button $wizard 'Next'))
Start-Sleep -Seconds 2

# --- Step 3: language and appearance ---------------------------------------
Save-Screen 'phase0-03-wizard-step3.png'
Write-Output ('  clicked Finish: ' + (Invoke-Button $wizard 'Finish'))
Start-Sleep -Seconds 4

# --- Sign in ---------------------------------------------------------------
$login = Wait-Window $p.Id 'Sign in'
if (-not $login) {
    Write-Output 'FAIL: sign-in window never appeared — dumping what the wizard is showing'
    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($t in $wizard.FindAll($TS::Descendants, $textCond)) {
        if ($t.Current.Name) { Write-Output ('    text: ' + $t.Current.Name) }
    }
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output 'login: "Sign in" is up'
Save-Screen 'phase0-04-login.png'

$edits = Get-VisibleEdits $login
$passwords = Get-PasswordEdits $login
Write-Output ('  login text fields = ' + $edits.Count + ', password fields = ' + $passwords.Count)
if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' }
if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 400
Write-Output ('  clicked Sign in: ' + (Invoke-Button $login 'Sign in'))
Start-Sleep -Seconds 5

Save-Screen 'phase0-05-shell.png'

# Report every top-level window the app owns right now.
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$windows = $AE::RootElement.FindAll($TS::Children, $cond)
Write-Output '--- application windows ---'
foreach ($w in $windows) {
    Write-Output ('  name=[' + $w.Current.Name + '] type=' + $w.Current.ControlType.ProgrammaticName + ' offscreen=' + $w.Current.IsOffscreen)
    Capture-Window $w ('phase0-05-shell-window.png') | Out-Null
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
