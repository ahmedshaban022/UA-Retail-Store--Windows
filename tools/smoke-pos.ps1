param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Drives the point of sale through UI Automation against the real application, in Arabic and then
# in English. The application is pointed at a throwaway data folder, so the shop's own data is never
# touched.
#
# Every control is found by its automation identifier rather than by its label, so the script does
# not depend on the wording of the interface in either language. The only words the script uses are
# the stock-keeping codes and the barcodes of the products it creates, which are the same in every
# language.
#
# The screens are captured by the application itself. The script asks the running application to draw
# one of its own windows over the capture pipe, and the application writes the picture. A picture
# taken from outside the process comes back mirrored for a right-to-left window and shows a frame
# that is not the one the owner sees, so it is not used.
#
# The scan is done the way a scanner does it: the code goes into the field and Enter finishes it.
# The time between the Enter and the line appearing on the cart is measured, because a till that
# takes a second to answer is a queue that grows.

$exe = $ExePath
$outDir = 'D:\Coding\UA Retail Store\docs\phases\phase3-screens'
$runRoot = Join-Path $env:TEMP ('storeflow-phase3-' + [Guid]::NewGuid().ToString('N'))
$pipeName = 'storeflow-phase3-capture-' + [Guid]::NewGuid().ToString('N')

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$env:STOREFLOW_HOME = $runRoot

$failures = 0
$checks = 0
function Fail([string]$message) {
    $script:failures++
    Write-Output ('FAIL: ' + $message)
}
function Check([bool]$condition, [string]$message) {
    $script:checks++
    if ($condition) { Write-Output ('  ok   ' + $message) } else { Fail $message }
}

Write-Output ('run folder: ' + $runRoot)
Write-Output ('shots folder: ' + $outDir)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class StoreFlowSmoke
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

# Arabic strings, built from code points so they survive this file being read as ANSI.
function A([int[]]$cp) { -join ($cp | ForEach-Object { [char]$_ }) }

function Find-ById($scope, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $scope.FindFirst($TS::Subtree, $cond)
}

# A window that is its own top-level window is looked for on the desktop as well, because a dialog
# that is owned by the shell is not always inside the shell's own tree.
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

function Get-TextById($scope, [string]$id) {
    $el = Find-ById $scope $id
    if (-not $el) { return $null }
    try {
        return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    } catch { }
    try { return $el.Current.Name } catch { return $null }
}

function Wait-ById($scope, [string]$id, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $el = Find-ById $scope $id
        if ($el) { return $el }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Wait-Gone($scope, [string]$id, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $el = Find-ById $scope $id
        if (-not $el) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Find-Window([int]$processId, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    return $AE::RootElement.FindFirst($TS::Children, $cond)
}

function Wait-Window([int]$processId, [string]$name, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Find-Window $processId $name
        if ($w) { return $w }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

# A window is waited for by its automation identifier rather than by its title, because a title is
# a piece of wording that can be changed and an identifier cannot.
function Wait-WindowById([string]$id, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $w = Find-WindowById $id
        if ($w) { return $w }
        Start-Sleep -Milliseconds 400
    }
    return $null
}

# The shell is the one wide window of the application. It is found by its identifier when it can
# be, and by its width when it cannot.
function Wait-Shell([int]$processId, [int]$seconds = 30) {
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
    } catch {
        return $false
    }
}

function Get-EditValue($edit) {
    try {
        return $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    } catch { return $null }
}

# Fills a row of boxes and waits until they really hold what was asked for. A box that is not
# there yet is waited for rather than skipped, and a box that is scrolled out of sight is filled
# all the same: leaving a box empty is how a shop ends up with a password that does not match.
function Fill-Fields($window, [string[]]$values, [bool]$anywhere = $false) {
    $deadline = (Get-Date).AddSeconds(25)

    while ((Get-Date) -lt $deadline) {
        $edits = Get-EditBoxes $window $anywhere

        if ($edits.Count -ge $values.Count) {
            for ($i = 0; $i -lt $values.Count; $i++) {
                Set-EditValue $edits[$i] $values[$i] | Out-Null
            }

            Start-Sleep -Milliseconds 600

            $allSet = $true

            for ($i = 0; $i -lt $values.Count; $i++) {
                # A secret cannot be read back, so it is taken on trust.
                try {
                    if ($edits[$i].Current.IsPassword) { continue }
                } catch { }

                $read = Get-EditValue $edits[$i]
                if ($null -ne $read -and $read -ne $values[$i]) { $allSet = $false }
            }

            if ($allSet) { return $true }
        }

        Start-Sleep -Milliseconds 600
    }

    Write-Host ('    the boxes did not take the values. The window had ' + (Get-EditBoxes $window $anywhere).Count + ' boxes')
    return $false
}

# Fills the secret box of a window. A secret cannot be read back — the automation is not allowed
# to see it — so the box is taken on trust once it has been written to.
function Fill-Password($window, [string]$value, [bool]$anywhere = $false) {
    $deadline = (Get-Date).AddSeconds(20)

    while ((Get-Date) -lt $deadline) {
        $passwords = Get-PasswordBoxes $window $anywhere

        if ($passwords.Count -ge 1) {
            Set-EditValue $passwords[0] $value | Out-Null
            Start-Sleep -Milliseconds 600
            return $true
        }

        Start-Sleep -Milliseconds 600
    }

    Write-Host '    the secret box never appeared'
    return $false
}

function Get-TextNames($window) {
    $result = @()
    foreach ($e in $window.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            if ($e.Current.Name) { $result += $e.Current.Name }
        } catch { }
    }
    return $result
}

function Get-TextsContaining($window, [string]$needle) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $result = @()
    foreach ($t in $window.FindAll($TS::Descendants, $cond)) {
        try {
            if ($t.Current.Name -and $t.Current.Name.Contains($needle)) { $result += $t.Current.Name }
        } catch { }
    }
    return $result
}

function Find-Text($window, [string]$needle) {
    $deadline = (Get-Date).AddSeconds(10)

    while ((Get-Date) -lt $deadline) {
        foreach ($name in (Get-TextNames $window)) {
            if ($name -and $name.Contains($needle)) { return $true }
        }
        Start-Sleep -Milliseconds 500
    }

    Write-Host ('    the text "' + $needle + '" never turned up. The screen says:')
    foreach ($name in (Get-TextNames $window)) {
        if ($name) { Write-Host ('      ' + $name) }
    }
    return $false
}

function Select-ComboItem($scope, [string]$id, [string]$needle) {
    $combo = Find-ById $scope $id
    if (-not $combo) {
        Write-Host ('    the picker [' + $id + '] was not found')
        return $false
    }

    $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

    $deadline = (Get-Date).AddSeconds(25)
    $items = @()

    while ((Get-Date) -lt $deadline) {
        try {
            $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        } catch { }
        Start-Sleep -Milliseconds 800

        $items = $combo.FindAll($TS::Subtree, $itemCond)

        foreach ($item in $items) {
            try {
                if ($item.Current.Name -and $item.Current.Name.Contains($needle)) {
                    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                    Start-Sleep -Milliseconds 700

                    try {
                        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
                    } catch { }
                    Start-Sleep -Milliseconds 400

                    $chosen = Get-ComboSelection $combo

                    if ($chosen -and $chosen.Contains($needle)) {
                        return $true
                    }

                    Write-Host ('    the picker did not take the choice. It reads "' + $chosen + '"')
                    return $false
                }
            } catch { }
        }

        Start-Sleep -Milliseconds 800
    }

    Write-Host ('    the picker has no entry that contains "' + $needle + '"')
    foreach ($item in $items) {
        try { Write-Host ('      ' + $item.Current.Name) } catch { }
    }
    return $false
}

function Get-ComboSelection($combo) {
    try {
        $pattern = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
        $selected = $pattern.Current.GetSelection()
        if ($selected.Count -gt 0) { return $selected[0].Current.Name }
    } catch { }

    try {
        return $combo.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    } catch { }

    return $null
}

function Set-GridField($grid, [string]$id, [string]$value) {
    if (-not $grid) { return $false }

    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $fields = $grid.FindAll($TS::Descendants, $cond)

    if ($fields.Count -eq 0) {
        Write-Host ('    the table has no field [' + $id + ']')
        return $false
    }

    try {
        $fields[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
        Start-Sleep -Milliseconds 400
        return $true
    } catch {
        Write-Host ('    the field refused the value: ' + $_.Exception.Message)
        return $false
    }
}

# Pictures are taken by the application itself. The script sends one command down the capture pipe
# and the application draws the window it names and writes the picture itself, so a right-to-left
# window comes out the way the owner sees it rather than mirrored.
function Send-Capture([string]$command) {
    if ($p -and $p.HasExited) {
        return 'ERR the application has stopped'
    }

    $writer = $null
    $reader = $null
    $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(
        '.', $pipeName, [System.IO.Pipes.PipeDirection]::InOut)

    try {
        $pipe.Connect(20000)

        $writer = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
        $writer.AutoFlush = $true
        $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8, $false, 1024, $true)

        $writer.WriteLine($command)

        # A named pipe cannot be given a read timeout of its own, so the answer is waited for
        # here instead. The application always answers, even when the capture failed.
        $answer = $reader.ReadLineAsync()
        if (-not $answer.Wait(30000)) {
            return 'ERR the application did not answer in time'
        }

        return $answer.Result
    } catch {
        return ('ERR ' + $_.Exception.Message)
    } finally {
        if ($reader) { $reader.Dispose() }
        if ($writer) { $writer.Dispose() }
        $pipe.Dispose()
    }
}

function Save-Shot([string]$windowId, [string]$fileName) {
    $path = Join-Path $outDir $fileName
    $reply = Send-Capture ('CAPTURE ' + $windowId + ' ' + $path)

    if ($reply -and $reply.StartsWith('OK')) {
        Write-Output ('  screenshot -> ' + $fileName + ' (' + $windowId + ')')
        return
    }

    Write-Output ('  no screenshot for ' + $fileName + ': ' + $reply)
    Fail ('the picture of ' + $fileName + ' could not be taken: ' + $reply)
}

# Puts the application in front, so that the keys the script sends reach the field and not the
# console the script is running in.
function Focus-App($window) {
    if (-not $window) { return $false }

    try {
        $handle = [IntPtr]$window.Current.NativeWindowHandle

        if ($handle -eq [IntPtr]::Zero) { return $false }

        [void][StoreFlowSmoke]::ShowWindow($handle, 9)
        [void][StoreFlowSmoke]::BringWindowToTop($handle)
        [void][StoreFlowSmoke]::SetForegroundWindow($handle)
        return $true
    } catch {
        return $false
    }
}

# Reads the number out of an amount the screen shows, whatever currency mark is written next to it.
function Get-MoneyValue([string]$text) {
    if (-not $text) { return $null }

    $match = [regex]::Match($text, '[-]?[\d,]+(\.\d+)?')

    if (-not $match.Success) { return $null }

    $value = 0.0
    $clean = $match.Value.Replace(',', '')

    if ([double]::TryParse($clean, [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
        return $value
    }

    return $null
}

# How many lines the cart is holding.
function Get-CartRowCount($shell) {
    $grid = Find-ById $shell 'Pos.Cart'

    if (-not $grid) { return 0 }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    return $grid.FindAll($TS::Descendants, $rowCond).Count
}

# Scans a barcode the way a scanner does: the code goes into the field, Enter finishes it. The
# time until the cart answers is measured and given back in milliseconds.
function Send-Scan($shell, [string]$barcode, [int]$waitRows = 1) {
    $scan = Find-ById $shell 'Pos.Scan'

    if (-not $scan) { return -1 }

    $before = Get-CartRowCount $shell

    Set-ById $shell 'Pos.Scan' $barcode | Out-Null
    Start-Sleep -Milliseconds 120

    Focus-App $shell | Out-Null

    try { $scan.SetFocus() } catch { }
    Start-Sleep -Milliseconds 120

    $watch = [System.Diagnostics.Stopwatch]::StartNew()

    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

    $deadline = (Get-Date).AddSeconds(15)

    while ((Get-Date) -lt $deadline) {
        if ((Get-CartRowCount $shell) -ge $waitRows) {
            $watch.Stop()
            return [int]$watch.ElapsedMilliseconds
        }

        Start-Sleep -Milliseconds 20
    }

    $watch.Stop()
    return -1
}

function Dump-Windows([int]$processId, $shell) {
    $processCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    Write-Output '  --- the windows of the application right now ---'
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $processCond)) {
        Write-Output ('    root: [' + $w.Current.Name + '] class=' + $w.Current.ClassName + ' id=' + $w.Current.AutomationId)
    }
    if ($shell) {
        foreach ($w in $shell.FindAll($TS::Subtree, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                if ($w.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
                    Write-Output ('    inside the shell: [' + $w.Current.Name + '] id=' + $w.Current.AutomationId)
                }
            } catch { }
        }
    }
}

$arStoreNameAr = A @(0x0645,0x062A,0x062C,0x0631,0x0020,0x0645,0x0635,0x0631,0x064A,0x0629)

$sku = 'POS-001'
$barcode = '6221001234567'
$productName = 'Rice 1kg POS'
$price = '45.00'

# --- The application -----------------------------------------------------------
$p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 7

# --- The first-run wizard -------------------------------------------------------
$wizard = Wait-WindowById 'FirstRunWindow' 30
if (-not $wizard) {
    Write-Output 'FAIL: the wizard never appeared'
    Dump-Windows $p.Id $null
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output ('wizard: "' + $wizard.Current.Name + '" is up')

$edits = Get-EditBoxes $wizard $true
$values = @('Al-Masria Store', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000')
Check (Fill-Fields $wizard $values $true) 'the shop details were written into the wizard'
Start-Sleep -Milliseconds 700
Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
Start-Sleep -Seconds 2

# The administrator page holds two boxes and two secret boxes. The boxes are filled by their order
# on the page, and the last one is off the bottom of the window, so it is filled anyway.
Check (Fill-Fields $wizard @('Owner', 'owner', 'StoreFlow!2026', 'StoreFlow!2026') $true) 'the owner account was written into the wizard'
Start-Sleep -Milliseconds 700
Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
Start-Sleep -Seconds 2
Write-Output ('  finish: ' + (Invoke-ById $wizard 'FirstRun.Finish'))
Start-Sleep -Seconds 6

$wizardError = Get-TextById $wizard 'FirstRun.Error'
if ($wizardError) { Write-Output ('  the wizard says: ' + $wizardError) }

# --- Sign in -------------------------------------------------------------------
$login = Wait-WindowById 'LoginWindow' 60
if (-not $login) {
    Write-Output 'FAIL: the sign-in window never appeared'
    Dump-Windows $p.Id $null
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output ('login: "' + $login.Current.Name + '" is up')

Check (Fill-Fields $login @('owner') $false) 'the owner name was written into the sign-in window'
Check (Fill-Password $login 'StoreFlow!2026' $false) 'the password was written into the sign-in window'
Start-Sleep -Milliseconds 700
Write-Output ('  sign in: ' + (Invoke-ById $login 'Login.SignIn'))
Start-Sleep -Seconds 7

$shell = Wait-Shell $p.Id
if (-not $shell) {
    Write-Output 'FAIL: no shell window'
    Write-Output '  the sign-in window says:'
    foreach ($name in (Get-TextNames $login)) {
        if ($name) { Write-Output ('    ' + $name) }
    }
    Dump-Windows $p.Id $null
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output ('shell: "' + $shell.Current.Name + '"')

# --- A product with a barcode --------------------------------------------------
Write-Output ('  open products: ' + (Invoke-ById $shell 'Products'))
Start-Sleep -Seconds 3

Write-Output ('  new product: ' + (Invoke-ById $shell 'Products.New'))
Start-Sleep -Seconds 3

$editor = Wait-ById $shell 'ProductEditorWindow' 20
if (-not $editor) {
    Fail 'the product editor never appeared'
    Dump-Windows $p.Id $shell
} else {
    Set-ById $editor 'ProductEditor.Sku' $sku | Out-Null
    Set-ById $editor 'ProductEditor.Name' $productName | Out-Null
    Set-ById $editor 'ProductEditor.SalePrice' $price | Out-Null
    Set-ById $editor 'ProductEditor.CostPrice' '38.00' | Out-Null
    Set-ById $editor 'ProductEditor.Barcode' $barcode | Out-Null
    Start-Sleep -Milliseconds 700

    Invoke-ById $editor 'ProductEditor.Save' | Out-Null
    Start-Sleep -Seconds 4

    Check (Wait-Gone $shell 'ProductEditorWindow' 15) ('the editor closed after saving ' + $sku)
}

Check (Find-Text $shell $sku) 'the product is in the catalogue'

# --- Stock on the shelf --------------------------------------------------------
# The till cannot sell what is not on the shelf, so the goods are bought in first.
Write-Output ('  open purchases: ' + (Invoke-ById $shell 'Purchases'))
Start-Sleep -Seconds 3

Write-Output ('  new purchase: ' + (Invoke-ById $shell 'Purchases.New'))
Start-Sleep -Seconds 3

$purchase = Wait-ById $shell 'PurchaseEditorWindow' 20
if (-not $purchase) {
    Fail 'the purchase editor never appeared'
    Dump-Windows $p.Id $shell
} else {
    Invoke-ById $purchase 'PurchaseEditor.AddLine' | Out-Null
    Start-Sleep -Milliseconds 900

    Check (Select-ComboItem $purchase 'PurchaseEditor.Line.Product' $sku) 'the product was chosen on the line'

    $grid = Find-ById $purchase 'PurchaseEditor.Lines'
    Check (Set-GridField $grid 'PurchaseEditor.Line.Quantity' '20') 'the quantity was written into the line'
    Check (Set-GridField $grid 'PurchaseEditor.Line.UnitCost' '38.00') 'the unit cost was written into the line'

    Invoke-ById $purchase 'PurchaseEditor.SaveAndPost' | Out-Null
    Start-Sleep -Seconds 5

    Check (Wait-Gone $shell 'PurchaseEditorWindow' 20) 'the purchase editor closed after posting'
}

# --- The till ------------------------------------------------------------------
Write-Output ('  open the till: ' + (Invoke-ById $shell 'Pos'))
Start-Sleep -Seconds 4

if ($p.HasExited) {
    Write-Output ('FAIL: the application stopped when the till was opened. The log says:')
    $logFile = Join-Path $runRoot 'logs\startup.log'
    if (Test-Path $logFile) { Get-Content $logFile -Tail 30 | ForEach-Object { Write-Output ('    ' + $_) } }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

Save-Shot 'MainWindow' 'phase3-01-till-empty-arabic.png'
Check ((Find-ById $shell 'Pos.Scan') -ne $null) 'the scan field is on the till'
Check ((Get-CartRowCount $shell) -eq 0) 'the cart starts empty'

# --- A scan --------------------------------------------------------------------
$scanMs = Send-Scan $shell $barcode 1
Write-Output ('  the scan was answered in ' + $scanMs + ' ms')
Check ($scanMs -gt 0) 'the barcode was scanned onto the cart'
Check ((Get-CartRowCount $shell) -eq 1) 'the cart holds one line'
Check (Find-Text $shell $productName) 'the cart names the product that was scanned'

Save-Shot 'MainWindow' 'phase3-02-till-one-line-arabic.png'

$totalText = Get-TextById $shell 'Pos.Total'
$total = Get-MoneyValue $totalText
Write-Output ('  the total reads: ' + $totalText)
Check ($total -ne $null -and [math]::Abs($total - 45.0) -lt 0.001) 'the total is the price of the one line'

# --- The money the customer hands over ------------------------------------------
Set-ById $shell 'Pos.CashReceived' '100.00' | Out-Null
Start-Sleep -Milliseconds 700

$changeText = Get-TextById $shell 'Pos.Change'
$change = Get-MoneyValue $changeText
Write-Output ('  the change reads: ' + $changeText)
Check ($change -ne $null -and [math]::Abs($change - 55.0) -lt 0.001) 'the change is what is handed back'

Save-Shot 'MainWindow' 'phase3-03-till-cash-arabic.png'

# --- A whole-sale discount -----------------------------------------------------
Set-ById $shell 'Pos.InvoiceDiscount' '5' | Out-Null
Start-Sleep -Milliseconds 900

$discounted = Get-MoneyValue (Get-TextById $shell 'Pos.Total')
Write-Output ('  the total after the discount reads: ' + $discounted)
Check ($discounted -ne $null -and [math]::Abs($discounted - 40.0) -lt 0.001) 'the discount came off the total'

Save-Shot 'MainWindow' 'phase3-04-till-discounted-arabic.png'

# The tax and the taxable amount have to add up to the total, whatever the tax is.
$taxText = Get-TextById $shell 'Pos.Tax'
$tax = Get-MoneyValue $taxText
$taxable = Get-MoneyValue (Get-TextById $shell 'Pos.Taxable')
Write-Output ('  the tax reads: ' + $taxText + ' and the taxable amount: ' + (Get-TextById $shell 'Pos.Taxable'))

if ($taxable -ne $null -and $tax -ne $null) {
    $now = Get-MoneyValue (Get-TextById $shell 'Pos.Total')
    Check ($now -ne $null -and [math]::Abs(($taxable + $tax) - $now) -lt 0.001) 'the taxable amount and the tax add up to the total'
}

# --- Putting the cart aside ----------------------------------------------------
Write-Output ('  hold the cart: ' + (Invoke-ById $shell 'Pos.Hold'))
Start-Sleep -Seconds 2

$prompt = Wait-ById $shell 'ReasonPromptWindow' 15
if (-not $prompt) {
    Fail 'the label prompt never appeared'
} else {
    Set-ById $prompt 'ReasonPrompt.Reason' 'customer waiting' | Out-Null
    Start-Sleep -Milliseconds 500
    Invoke-ById $prompt 'ReasonPrompt.Accept' | Out-Null
    Start-Sleep -Seconds 3
}

Check ((Get-CartRowCount $shell) -eq 0) 'the cart is empty after the cart was put aside'
Check (Find-Text $shell 'customer waiting') 'the cart that is waiting is named on the screen'

Save-Shot 'MainWindow' 'phase3-05-till-held-cart-arabic.png'

# --- Taking the cart back ------------------------------------------------------
Write-Output ('  take the cart back: ' + (Invoke-ById $shell 'Pos.ResumeOne'))
Start-Sleep -Seconds 3

Check ((Get-CartRowCount $shell) -eq 1) 'the cart is back on the till'
$resumed = Get-MoneyValue (Get-TextById $shell 'Pos.Total')
Check ($resumed -ne $null -and [math]::Abs($resumed - 40.0) -lt 0.001) 'the cart came back with its discount'

Save-Shot 'MainWindow' 'phase3-06-till-resumed-arabic.png'

# The money the customer hands over is entered after the cart is back, because the cash belongs to
# this customer and not to the one who left the cart aside.
Set-ById $shell 'Pos.CashReceived' '100.00' | Out-Null
Start-Sleep -Milliseconds 700

# --- The sale ------------------------------------------------------------------
Write-Output ('  confirm the sale: ' + (Invoke-ById $shell 'Pos.Confirm'))
Start-Sleep -Seconds 5

$receipt = Wait-ById $shell 'Pos.ReceiptWindow' 20
if (-not $receipt) { $receipt = Find-WindowById 'Pos.ReceiptWindow' }

if (-not $receipt) {
    Fail 'the receipt never appeared'
    Write-Output ('  the till says: ' + (Get-TextById $shell 'Pos.Message'))
    Dump-Windows $p.Id $shell
} else {
    Write-Output '  the receipt is up'
    Save-Shot 'Pos.ReceiptWindow' 'phase3-07-receipt-arabic.png'

    Invoke-ById $receipt 'Pos.ReceiptClose' | Out-Null
    Start-Sleep -Seconds 3
}

Check (Wait-Gone $shell 'Pos.ReceiptWindow' 15) 'the receipt closed'
Check ((Get-CartRowCount $shell) -eq 0) 'the cart is empty after the sale'

# The receipt is drawn on a canvas, so the number on it is read from the till, which was told
# which number was posted.
$saleMessage = Get-TextById $shell 'Pos.Message'
Write-Output ('  the till says: ' + $saleMessage)
Check ($saleMessage -and $saleMessage.Contains('INV-')) 'the sale was given an invoice number'

# --- The stock came off the shelf ----------------------------------------------
Write-Output ('  open inventory: ' + (Invoke-ById $shell 'Inventory'))
Start-Sleep -Seconds 4

Save-Shot 'MainWindow' 'phase3-08-stock-after-the-sale-arabic.png'
Check (Find-Text $shell $sku) 'the product is still in the stock list'

# --- The same till in English --------------------------------------------------
Write-Output '  switch to English'
$switched = $false

$languageCombo = Find-ById $shell 'LanguagePicker'
if ($languageCombo) {
    try {
        $languageCombo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    } catch { }
    Start-Sleep -Milliseconds 1200

    $itemCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'English')),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
    $item = $shell.FindFirst($TS::Subtree, $itemCond)
    if (-not $item) { $item = $languageCombo.FindFirst($TS::Subtree, $itemCond) }
    if (-not $item) { $item = $AE::RootElement.FindFirst($TS::Subtree, $itemCond) }

    if ($item) {
        $chosen = $false

        try {
            $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            $chosen = $true
        } catch { }

        if (-not $chosen) {
            try {
                $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                $chosen = $true
            } catch { }
        }

        if ($chosen) {
            Start-Sleep -Seconds 4
            $switched = $true
            Write-Output '  the language is now English'
        }
    } else {
        Write-Output '  the English entry was not found in the language picker'
    }
} else {
    Write-Output '  the language picker was not found'
}

if (-not $switched) {
    Write-Output '  the language is written into the theme and the application is started again'
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $themeFile = Join-Path $runRoot 'themes\active.sftheme.json'
    if (-not (Test-Path $themeFile)) {
        Fail 'the theme file is not where the application keeps it'
    } else {
        $theme = Get-Content -Raw -Encoding UTF8 $themeFile
        $theme = [regex]::Replace($theme, '"default"\s*:\s*"ar"', '"default": "en"')
        [System.IO.File]::WriteAllText($themeFile, $theme, (New-Object System.Text.UTF8Encoding($false)))
        Write-Output '  the theme now asks for English'

        $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
        Start-Sleep -Seconds 8

        $login = Wait-WindowById 'LoginWindow' 30
        if (-not $login) {
            Fail 'the sign-in window never appeared in English'
        } else {
            $edits = Get-EditBoxes $login
            if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
            Fill-Password $login 'StoreFlow!2026' $false | Out-Null
            Start-Sleep -Milliseconds 700
            Invoke-ById $login 'Login.SignIn' | Out-Null
            Start-Sleep -Seconds 7
        }

        $shell = Wait-Shell $p.Id
        if (-not $shell) {
            Fail 'the shell never appeared in English'
        }
    }
}

Write-Output ('  open the till in English: ' + (Invoke-ById $shell 'Pos'))
Start-Sleep -Seconds 4

Save-Shot 'MainWindow' 'phase3-09-till-english.png'
Check ((Get-TextsContaining $shell 'Total').Count -gt 0) 'the till reads in English'

$englishScan = Send-Scan $shell $barcode 1
Write-Output ('  the scan was answered in ' + $englishScan + ' ms in English')
Check ($englishScan -gt 0) 'the till scans in English as well'

Save-Shot 'MainWindow' 'phase3-10-till-scanned-english.png'

# --- Report --------------------------------------------------------------------
Dump-Windows $p.Id $shell

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
Write-Output ('run folder kept at: ' + $runRoot)
Write-Output ('checks: ' + $checks)
Write-Output ('failures: ' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
