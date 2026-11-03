param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Measures how long the till takes to answer a scan.
#
# A scanner puts a barcode into the field and presses Enter. The cashier must not wait: a till that
# takes a second to put the line on the screen is a queue that grows at the counter. This script
# measures that wait, over and over, on the real application, and writes down what it found.
#
# The measurement starts when the Enter key goes down and stops when the line is on the cart. The
# cart is emptied between the scans, so every scan is the first one of its kind and none of them
# is helped by the one before it.

# Preserve the original positional scan count, such as scan-latency.ps1 20.
if ($ExePath -match '^\d+$' -and $args.Count -eq 0) { $args = @($ExePath); $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe' }
$exe = $ExePath
$runRoot = Join-Path $env:TEMP ('storeflow-latency-' + [Guid]::NewGuid().ToString('N'))
$pipeName = 'storeflow-latency-capture-' + [Guid]::NewGuid().ToString('N')

$scans = 20
if ($args.Count -ge 1 -and [int]::TryParse($args[0], [ref]$null)) { $scans = [int]$args[0] }

New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$env:STOREFLOW_HOME = $runRoot

$failures = 0
function Fail([string]$message) {
    $script:failures++
    Write-Output ('FAIL: ' + $message)
}
function Check([bool]$condition, [string]$message) {
    if ($condition) { Write-Output ('  ok   ' + $message) } else { Fail $message }
}

Write-Output ('run folder: ' + $runRoot)
Write-Output ('scans to measure: ' + $scans)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class StoreFlowLatency
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Find-ById($scope, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $scope.FindFirst($TS::Subtree, $cond)
}

function Find-WindowById([string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $AE::RootElement.FindFirst($TS::Subtree, $cond)
}

function Invoke-ById($scope, [string]$id) {
    $el = Find-ById $scope $id
    if (-not $el) { return $false }
    try {
        $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        return $true
    } catch { return $false }
}

function Set-ById($scope, [string]$id, [string]$value) {
    $el = Find-ById $scope $id
    if (-not $el) { return $false }
    try {
        $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
        return $true
    } catch { return $false }
}

function Wait-WindowById([string]$id, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Find-WindowById $id
        if ($w) { return $w }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

function Wait-Shell([int]$processId, [int]$seconds = 40) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $deadline = (Get-Date).AddSeconds($seconds)

    while ((Get-Date) -lt $deadline) {
        $named = Find-WindowById 'MainWindow'
        if ($named) { return $named }

        foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) {
            try {
                if ($w.Current.BoundingRectangle.Width -gt 600) { return $w }
            } catch { }
        }

        Start-Sleep -Milliseconds 500
    }

    return $null
}

function Get-EditBoxes($window, [bool]$anywhere = $false) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $all = $window.FindAll($TS::Descendants, $cond)
    $result = @()
    foreach ($e in $all) {
        try {
            if ($e.Current.BoundingRectangle.Height -le 0) { continue }
            if (-not $anywhere -and $e.Current.IsOffscreen) { continue }
            $result += $e
        } catch { }
    }
    return $result
}

function Get-PasswordBoxes($window, [bool]$anywhere = $false) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    $all = $window.FindAll($TS::Descendants, $cond)
    $result = @()
    foreach ($e in $all) {
        try {
            if ($e.Current.BoundingRectangle.Height -le 0) { continue }
            if (-not $anywhere -and $e.Current.IsOffscreen) { continue }
            if ($e.Current.IsPassword) { $result += $e }
        } catch { }
    }
    return $result
}

function Set-EditValue($edit, [string]$value) {
    try {
        $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
        return $true
    } catch { return $false }
}

function Fill-Fields($window, [string[]]$values, [bool]$anywhere = $false) {
    $deadline = (Get-Date).AddSeconds(25)

    while ((Get-Date) -lt $deadline) {
        $edits = Get-EditBoxes $window $anywhere

        if ($edits.Count -ge $values.Count) {
            for ($i = 0; $i -lt $values.Count; $i++) { Set-EditValue $edits[$i] $values[$i] | Out-Null }
            Start-Sleep -Milliseconds 600
            return $true
        }

        Start-Sleep -Milliseconds 600
    }

    return $false
}

function Select-ComboItem($scope, [string]$id, [string]$needle) {
    $combo = Find-ById $scope $id
    if (-not $combo) { return $false }

    $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

    $deadline = (Get-Date).AddSeconds(25)

    while ((Get-Date) -lt $deadline) {
        try {
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        } catch { }
        Start-Sleep -Milliseconds 800

        foreach ($item in $combo.FindAll($TS::Subtree, $itemCond)) {
            try {
                if ($item.Current.Name -and $item.Current.Name.Contains($needle)) {
                    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                    Start-Sleep -Milliseconds 700

                    try {
                        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
                    } catch { }

                    Start-Sleep -Milliseconds 400

                    # The choice has to have been taken. A picker that shows the choice without
                    # keeping it saves a line with no product on it.
                    $chosen = $null
                    try {
                        $pattern = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
                        $selected = $pattern.Current.GetSelection()
                        if ($selected.Count -gt 0) { $chosen = $selected[0].Current.Name }
                    } catch { }

                    if (-not $chosen) {
                        try {
                            $chosen = $combo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
                        } catch { }
                    }

                    if ($chosen -and $chosen.Contains($needle)) { return $true }

                    Write-Output ('    the picker did not take the choice. It reads "' + $chosen + '"')
                    return $false
                }
            } catch { }
        }

        Start-Sleep -Milliseconds 800
    }

    return $false
}

function Get-CartRowCount($shell) {
    $grid = Find-ById $shell 'Pos.Cart'
    if (-not $grid) { return 0 }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    return $grid.FindAll($TS::Descendants, $rowCond).Count
}

function Focus-App($window) {
    if (-not $window) { return $false }

    try {
        $handle = [IntPtr]$window.Current.NativeWindowHandle
        if ($handle -eq [IntPtr]::Zero) { return $false }

        [void][StoreFlowLatency]::ShowWindow($handle, 9)
        [void][StoreFlowLatency]::BringWindowToTop($handle)
        [void][StoreFlowLatency]::SetForegroundWindow($handle)
        return $true
    } catch { return $false }
}

$sku = 'LAT-001'
$barcode = '6221007654321'

# --- The application -----------------------------------------------------------
$p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 7

$wizard = Wait-WindowById 'FirstRunWindow' 30
if (-not $wizard) { Fail 'the wizard never appeared'; Stop-Process -Id $p.Id -Force; exit 1 }

Fill-Fields $wizard @('Al-Masria Store', 'Al-Masria', '12 Tahrir Street, Cairo', '+20 100 000 0000') $true | Out-Null
Start-Sleep -Milliseconds 700
Invoke-ById $wizard 'FirstRun.Next' | Out-Null
Start-Sleep -Seconds 2

Fill-Fields $wizard @('Owner', 'owner', 'StoreFlow!2026', 'StoreFlow!2026') $true | Out-Null
Start-Sleep -Milliseconds 700
Invoke-ById $wizard 'FirstRun.Next' | Out-Null
Start-Sleep -Seconds 2
Invoke-ById $wizard 'FirstRun.Finish' | Out-Null
Start-Sleep -Seconds 6

$login = Wait-WindowById 'LoginWindow' 60
if (-not $login) { Fail 'the sign-in window never appeared'; Stop-Process -Id $p.Id -Force; exit 1 }

Fill-Fields $login @('owner') $false | Out-Null

$passwords = Get-PasswordBoxes $login
if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' | Out-Null }

Start-Sleep -Milliseconds 700
Invoke-ById $login 'Login.SignIn' | Out-Null
Start-Sleep -Seconds 7

$shell = Wait-Shell $p.Id
if (-not $shell) { Fail 'the shell never appeared'; Stop-Process -Id $p.Id -Force; exit 1 }

# --- A product on the shelf ----------------------------------------------------
Invoke-ById $shell 'Products' | Out-Null
Start-Sleep -Seconds 3
Invoke-ById $shell 'Products.New' | Out-Null
Start-Sleep -Seconds 3

$editor = Wait-WindowById 'ProductEditorWindow'
if (-not $editor) {
    Fail 'the product editor never appeared'
    Stop-Process -Id $p.Id -Force
    exit 1
}

Set-ById $editor 'ProductEditor.Sku' $sku | Out-Null
Set-ById $editor 'ProductEditor.Name' 'Latency product' | Out-Null
Set-ById $editor 'ProductEditor.SalePrice' '10.00' | Out-Null
Set-ById $editor 'ProductEditor.CostPrice' '8.00' | Out-Null
Set-ById $editor 'ProductEditor.Barcode' $barcode | Out-Null
Start-Sleep -Milliseconds 700
Invoke-ById $editor 'ProductEditor.Save' | Out-Null
Start-Sleep -Seconds 4

Invoke-ById $shell 'Purchases' | Out-Null
Start-Sleep -Seconds 3
Invoke-ById $shell 'Purchases.New' | Out-Null
Start-Sleep -Seconds 3

$purchase = Wait-WindowById 'PurchaseEditorWindow'
if (-not $purchase) {
    Fail 'the purchase editor never appeared'
    Stop-Process -Id $p.Id -Force
    exit 1
}

Invoke-ById $purchase 'PurchaseEditor.AddLine' | Out-Null
Start-Sleep -Milliseconds 900
Check (Select-ComboItem $purchase 'PurchaseEditor.Line.Product' $sku) 'the product was chosen on the line'

$grid = Find-ById $purchase 'PurchaseEditor.Lines'
$fieldCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'PurchaseEditor.Line.Quantity')
$fields = $grid.FindAll($TS::Descendants, $fieldCond)
if ($fields.Count -gt 0) {
    $fields[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('100')
}

Start-Sleep -Milliseconds 700
Invoke-ById $purchase 'PurchaseEditor.SaveAndPost' | Out-Null
Start-Sleep -Seconds 5

# The purchase editor closing is the proof that the goods were bought in. Without them on the
# shelf the scan finds nothing, and the measurement would be of an empty till.
$editorGone = $true
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline) {
    if (-not (Find-WindowById 'PurchaseEditorWindow')) { $editorGone = $true; break }
    $editorGone = $false
    Start-Sleep -Milliseconds 500
}

Check $editorGone 'the purchase was posted and the goods are on the shelf'

# --- The till ------------------------------------------------------------------
Invoke-ById $shell 'Pos' | Out-Null
Start-Sleep -Seconds 4

Check ((Get-CartRowCount $shell) -eq 0) 'the cart starts empty'

# --- The measurement -----------------------------------------------------------
$times = New-Object System.Collections.Generic.List[int]
$scan = Find-ById $shell 'Pos.Scan'

if (-not $scan) {
    Fail 'the scan field is not on the till'
} else {
    for ($i = 1; $i -le $scans; $i++) {
        Invoke-ById $shell 'Pos.Clear' | Out-Null
        Start-Sleep -Milliseconds 250

        Set-ById $shell 'Pos.Scan' $barcode | Out-Null
        Start-Sleep -Milliseconds 80

        Focus-App $shell | Out-Null
        try { $scan.SetFocus() } catch { }
        Start-Sleep -Milliseconds 80

        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

        $answered = $false
        $deadline = (Get-Date).AddSeconds(15)

        while ((Get-Date) -lt $deadline) {
            if ((Get-CartRowCount $shell) -ge 1) {
                $watch.Stop()
                $times.Add([int]$watch.ElapsedMilliseconds)
                $answered = $true
                break
            }

            Start-Sleep -Milliseconds 10
        }

        if (-not $answered) {
            $watch.Stop()
            Fail ('the scan number ' + $i + ' was never answered')
            break
        }
    }
}

# --- The numbers ---------------------------------------------------------------
if ($times.Count -gt 0) {
    $sorted = $times | Sort-Object
    $minimum = $sorted[0]
    $maximum = $sorted[$sorted.Count - 1]
    $median = $sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)]
    $p95 = $sorted[[int][Math]::Min($sorted.Count - 1, [Math]::Ceiling($sorted.Count * 0.95) - 1)]

    Write-Output ''
    Write-Output 'The wait the cashier feels, from the key going down to the line appearing:'
    Write-Output ('scans answered: ' + $times.Count)
    Write-Output ('fastest: ' + $minimum + ' ms')
    Write-Output ('median: ' + $median + ' ms')
    Write-Output ('95th percentile: ' + $p95 + ' ms')
    Write-Output ('slowest: ' + $maximum + ' ms')
    Write-Output ('all: ' + ($times -join ', '))

    # A till that takes more than half a second to answer a scan makes a queue at the counter.
    Check ($p95 -le 500) 'the till answers a scan inside half a second'
}

# The wait the till itself took, from the till's own log. The figure above also carries the cost
# of the automation watching the screen, which a cashier does not pay.
$appTimes = @()
$logFile = Get-ChildItem (Join-Path $runRoot 'logs') -Filter 'storeflow-*.log' -ErrorAction SilentlyContinue |
    Select-Object -First 1

if ($logFile) {
    foreach ($line in (Get-Content $logFile.FullName -ErrorAction SilentlyContinue)) {
        $match = [regex]::Match($line, 'was answered in (\d+) ms')
        if ($match.Success) { $appTimes += [int]$match.Groups[1].Value }
    }
}

if ($appTimes.Count -gt 0) {
    $sorted = $appTimes | Sort-Object
    $minimum = $sorted[0]
    $maximum = $sorted[$sorted.Count - 1]
    $median = $sorted[[int][Math]::Floor(($sorted.Count - 1) / 2)]
    $p95 = $sorted[[int][Math]::Min($sorted.Count - 1, [Math]::Ceiling($sorted.Count * 0.95) - 1)]

    Write-Output ''
    Write-Output 'The wait the till itself took, from the till''s own log:'
    Write-Output ('scans measured: ' + $appTimes.Count)
    Write-Output ('fastest: ' + $minimum + ' ms')
    Write-Output ('median: ' + $median + ' ms')
    Write-Output ('95th percentile: ' + $p95 + ' ms')
    Write-Output ('slowest: ' + $maximum + ' ms')
    Write-Output ('all: ' + ($appTimes -join ', '))

    # The plan's gate for the till is a scan answered inside fifty milliseconds.
    Check ($p95 -le 50) 'the till answers a scan inside fifty milliseconds'
} else {
    Fail 'the till wrote no scan timings down'
}

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output ('failures: ' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
