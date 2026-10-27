$ErrorActionPreference = 'Continue'

# Opens the appearance dropdown on the first-run wizard and captures the result, so the select can
# be reviewed as a rendered control rather than as source.

$exe = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
$outDir = 'C:\Users\ahmed\.minimax\v2\sessions\2026\10\25\15-42-28-262-session_bXZzX2RhYzY2YzI3Zjg3MjQ4N2E5YTMxZTNlZDYzYWVhZGJh\artifacts'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

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

function Invoke-Button($window, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $b = $window.FindFirst($TS::Descendants, $cond)
    if (-not $b) { return $false }
    try { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true } catch { return $false }
}

function Save-Screen([string]$fileName, [int]$x = 0, [int]$y = 0, [int]$w = 0, [int]$h = 0) {
    if ($w -le 0) {
        $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $x = $bounds.Left; $y = $bounds.Top; $w = $bounds.Width; $h = $bounds.Height
    }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $g.Dispose()
    $path = Join-Path $outDir $fileName
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("  screenshot -> " + $fileName)
}

$p = Start-Process -FilePath $exe -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 6

$wizard = Wait-Window $p.Id 'Set up StoreFlow'
if (-not $wizard) { Write-Output 'FAIL: no wizard'; Stop-Process -Id $p.Id -Force; exit 1 }
Write-Output 'wizard up'

# Step 1 -> 2 -> 3, so the language and appearance selects are on screen.
Invoke-Button $wizard 'Next' | Out-Null
Start-Sleep -Seconds 1
Invoke-Button $wizard 'Next' | Out-Null
Start-Sleep -Seconds 2

$rect = $wizard.Current.BoundingRectangle
Write-Output ('wizard rect: ' + $rect.X + ',' + $rect.Y + ' ' + $rect.Width + 'x' + $rect.Height)

# Expand the appearance select.
$comboCond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
$combos = $wizard.FindAll($TS::Descendants, $comboCond)
Write-Output ('combo boxes on this step = ' + $combos.Count)

if ($combos.Count -ge 2) {
    $appearance = $combos[1]
    Write-Output ('expanding: ' + $appearance.Current.Name)
    $appearance.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Seconds 2

    # Capture just the wizard area so the dropdown is legible.
    Save-Screen 'phase0-06-dropdown-open.png' ([int]$rect.X) ([int]$rect.Y) ([int]($rect.Width + 40)) ([int]($rect.Height + 260))
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
