$ErrorActionPreference = 'Continue'

# Drives the sales register and the returns through UI Automation against the real application, in
# Arabic and then in English. The application is pointed at a throwaway data folder, so the shop's
# own data is never touched.
#
# Every control is found by its automation identifier rather than by its label, so the script does
# not depend on the wording of the interface in either language. The only words the script uses are
# the stock-keeping code and the barcode of the product it creates, which are the same in every
# language.
#
# The screens are captured by the application itself. The script asks the running application to draw
# one of its own windows over the capture pipe, and the application writes the picture. A picture
# taken from outside the process comes back mirrored for a right-to-left window and shows a frame
# that is not the one the owner sees, so it is not used.
#
# The run tells one story: a sale is made at the till, one of the three units comes back, the stock
# goes back on the shelf, and then a second sale is reversed. The register has to show all of it.

$exe = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
$outDir = 'D:\Coding\UA Retail Store\docs\phases\phase4-screens'
$runRoot = Join-Path $env:TEMP ('storeflow-phase4-' + [Guid]::NewGuid().ToString('N'))
$pipeName = 'storeflow-phase4-capture-' + [Guid]::NewGuid().ToString('N')

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

public static class StoreFlowSalesSmoke
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
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

# The shell is the one wide window of the application.
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
# all the same.
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

# Fills the secret box of a window. A secret cannot be read back, so it is taken on trust once it
# has been written to.
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

# Picks an entry of a picker by the text it carries, so the script does not depend on the order the
# entries happen to be in.
function Select-ComboItem($scope, [string]$id, [string]$needle) {
    $combo = Find-ById $scope $id
    if (-not $combo) {
        Write-Host ('    the picker [' + $id + '] was not found')
        return $false
    }

    $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

    $deadline = (Get-Date).AddSeconds(25)

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

                    return $true
                }
            } catch { }
        }

        Start-Sleep -Milliseconds 500
    }

    Write-Host ('    the entry "' + $needle + '" was not in the picker [' + $id + ']')
    return $false
}

# Writes into the first cell of a table that carries the given identifier.
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

# How many rows a table is holding.
function Get-GridRowCount($grid) {
    if (-not $grid) { return 0 }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    return $grid.FindAll($TS::Descendants, $rowCond).Count
}

# The text of every row of a table, one string per row.
function Get-GridRows($grid) {
    $result = @()
    if (-not $grid) { return $result }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    foreach ($row in $grid.FindAll($TS::Descendants, $rowCond)) {
        $parts = @()

        foreach ($cell in $row.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                if ($cell.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                    if ($cell.Current.Name) { $parts += $cell.Current.Name }
                }
            } catch { }
        }

        $result += ($parts -join ' | ')
    }

    return $result
}

# Picks the row of a table that carries the given text.
function Select-GridRow($grid, [string]$needle) {
    if (-not $grid) { return $false }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    foreach ($row in $grid.FindAll($TS::Descendants, $rowCond)) {
        $text = @()

        foreach ($cell in $row.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try { if ($cell.Current.Name) { $text += $cell.Current.Name } } catch { }
        }

        if (($text -join ' ') -like ('*' + $needle + '*')) {
            try {
                $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Milliseconds 700
                return $true
            } catch { }
        }
    }

    Write-Host ('    no row of the table carries "' + $needle + '"')
    return $false
}

# Picks the row of a table whose own number is the given one. A row can carry a number in another
# column as well: a return carries the number of the invoice it undoes. A search that looks for
# the number anywhere in the row picks the wrong row, so the first cell is the one that decides.
function Select-GridRowByNumber($grid, [string]$number) {
    if (-not $grid) { return $false }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    foreach ($row in $grid.FindAll($TS::Subtree, $rowCond)) {
        $own = $null

        foreach ($cell in $row.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                if ($cell.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                    $own = $cell.Current.Name
                    break
                }
            } catch { }
        }

        if ($own -and $own.Trim() -eq $number.Trim()) {
            try {
                $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                Start-Sleep -Milliseconds 700
                return $true
            } catch { }
        }
    }

    Write-Host ('    no row of the table is numbered "' + $number + '"')
    return $false
}

# Reads the number out of a piece of text, so the check does not depend on how the money is written.
function Get-Number([string]$text) {
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

# How many lines the till's cart is holding.
function Get-CartRowCount($shell) {
    $grid = Find-ById $shell 'Pos.Cart'

    if (-not $grid) { return 0 }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    return $grid.FindAll($TS::Descendants, $rowCond).Count
}

# Puts the caret in a field the way a person does: with a click of the mouse. A field that only
# the automation can address does not always take the keys a scanner sends.
function Click-Element($element) {
    if (-not $element) { return $false }

    try {
        $rect = $element.Current.BoundingRectangle
        $x = [int]($rect.Left + ($rect.Width / 2))
        $y = [int]($rect.Top + ($rect.Height / 2))

        [void][StoreFlowSalesSmoke]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 150

        [StoreFlowSalesSmoke]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [StoreFlowSalesSmoke]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 250

        return $true
    } catch {
        return $false
    }
}

# Scans a barcode the way a scanner does: the code goes into the field, Enter finishes it.
function Send-Scan($shell, [string]$barcode, [int]$waitRows = 1) {
    $scan = Find-ById $shell 'Pos.Scan'

    if (-not $scan) { return -1 }

    $before = Get-CartRowCount $shell

    Set-ById $shell 'Pos.Scan' $barcode | Out-Null
    Start-Sleep -Milliseconds 120

    Focus-App $shell | Out-Null

    # The field is clicked as well as addressed, because the keys a scanner sends only reach it
    # when the caret is really in it.
    Click-Element $scan | Out-Null

    try { $scan.SetFocus() } catch { }
    Start-Sleep -Milliseconds 150

    $focused = $false
    try { $focused = $scan.Current.HasKeyboardFocus } catch { }

    if (-not $focused) {
        Write-Host ('    the scan field did not take the keyboard focus')
    }

    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')

    $deadline = (Get-Date).AddSeconds(15)

    while ((Get-Date) -lt $deadline) {
        if ((Get-CartRowCount $shell) -ge $waitRows) { return 1 }
        Start-Sleep -Milliseconds 40
    }

    Write-Host ('    the cart did not answer the scan of ' + $barcode)
    return 0
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

# Cuts a part out of a picture the application drew. The picture is the application's own drawing,
# so a cut out of it is still what the owner sees; it is only closer.
function Crop-Shot([string]$fileName, [int]$x, [int]$y, [int]$width, [int]$height) {
    $path = Join-Path $outDir $fileName

    if (-not (Test-Path $path)) {
        Write-Output ('  no picture to cut: ' + $fileName)
        Fail ('the picture of ' + $fileName + ' was not there to cut')
        return
    }

    try {
        Add-Type -AssemblyName System.Drawing

        $picture = [System.Drawing.Image]::FromFile($path)
        $cut = New-Object System.Drawing.Bitmap($width, $height)
        $brush = [System.Drawing.Graphics]::FromImage($cut)
        $brush.DrawImage(
            $picture,
            (New-Object System.Drawing.Rectangle(0, 0, $width, $height)),
            (New-Object System.Drawing.Rectangle($x, $y, $width, $height)),
            [System.Drawing.GraphicsUnit]::Pixel)
        $brush.Dispose()

        # The cut is written beside the picture and then takes its place, because the picture is
        # still open while it is being cut.
        $staging = $path + '.cut.png'
        $cut.Save($staging, [System.Drawing.Imaging.ImageFormat]::Png)
        $cut.Dispose()
        $picture.Dispose()

        Move-Item -Force $staging $path

        Write-Output ('  cut to the detail panel -> ' + $fileName)
    } catch {
        Write-Output ('  the picture of ' + $fileName + ' could not be cut: ' + $_.Exception.Message)
        Fail ('the picture of ' + $fileName + ' could not be cut')
    }
}

# Puts the application in front, so that the keys the script sends reach the field and not the
# console the script is running in.
function Focus-App($window) {
    if (-not $window) { return $false }

    try {
        $handle = [IntPtr]$window.Current.NativeWindowHandle

        if ($handle -eq [IntPtr]::Zero) { return $false }

        [void][StoreFlowSalesSmoke]::ShowWindow($handle, 9)
        [void][StoreFlowSalesSmoke]::BringWindowToTop($handle)
        [void][StoreFlowSalesSmoke]::SetForegroundWindow($handle)
        return $true
    } catch {
        return $false
    }
}

function Dump-Windows([int]$processId, $shell) {
    Write-Output '  --- the windows of the application right now ---'

    foreach ($w in $AE::RootElement.FindAll($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)))) {
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

# --- The things the run needs ----------------------------------------------------------
$sku = 'RICE-SALES-4'
$productName = 'Rice 1kg Sales'
$barcode = '6291041500224'
$price = '45.00'
$arStoreNameAr = A @(0x0645,0x062A,0x062C,0x0631,0x0020,0x0645,0x0635,0x0631,0x064A,0x0629)

# --- The application -------------------------------------------------------------------
$p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 7

# --- The first-run wizard --------------------------------------------------------------
$wizard = Wait-WindowById 'FirstRunWindow' 30
if (-not $wizard) {
    Write-Output 'FAIL: the wizard never appeared'
    Dump-Windows $p.Id $null
} else {
    Write-Output 'the wizard is up'

    Check (Fill-Fields $wizard @('Al-Masria Store', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000') $true) 'the shop details were written into the wizard'
    Start-Sleep -Milliseconds 700
    Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
    Start-Sleep -Seconds 2

    Check (Fill-Fields $wizard @('Owner', 'owner', 'StoreFlow!2026', 'StoreFlow!2026') $true) 'the owner account was written into the wizard'
    Start-Sleep -Milliseconds 700
    Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
    Start-Sleep -Seconds 2
    Write-Output ('  finish: ' + (Invoke-ById $wizard 'FirstRun.Finish'))
    Start-Sleep -Seconds 6
}

# --- Sign in ---------------------------------------------------------------------------
$login = Wait-WindowById 'LoginWindow' 60
if (-not $login) {
    Write-Output 'FAIL: the sign-in window never appeared'
    Dump-Windows $p.Id $null
} else {
    Write-Output 'the sign-in window is up'

    $edits = Get-EditBoxes $login
    if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
    Fill-Password $login 'StoreFlow!2026' $false | Out-Null
    Start-Sleep -Milliseconds 700

    Invoke-ById $login 'Login.SignIn' | Out-Null
    Start-Sleep -Seconds 7
}

$shell = Wait-Shell $p.Id
if (-not $shell) {
    Write-Output 'FAIL: the shell never appeared'
    Dump-Windows $p.Id $null
} else {
    Write-Output ('shell: "' + $shell.Current.Name + '"')
}

# --- A product with a barcode ----------------------------------------------------------
Write-Output ('  open products: ' + (Invoke-ById $shell 'Products'))
Start-Sleep -Seconds 3
Write-Output ('  new product: ' + (Invoke-ById $shell 'Products.New'))
Start-Sleep -Seconds 3

$editor = Wait-ById $shell 'ProductEditorWindow' 20
if (-not $editor) {
    Fail 'the product editor never appeared'
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

# --- Stock on the shelf ----------------------------------------------------------------
Write-Output ('  open purchases: ' + (Invoke-ById $shell 'Purchases'))
Start-Sleep -Seconds 3
Write-Output ('  new purchase: ' + (Invoke-ById $shell 'Purchases.New'))
Start-Sleep -Seconds 3

$purchase = Wait-ById $shell 'PurchaseEditorWindow' 20
if (-not $purchase) {
    Fail 'the purchase editor never appeared'
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

# --- The first sale --------------------------------------------------------------------
Write-Output ('  open the till: ' + (Invoke-ById $shell 'Pos'))
Start-Sleep -Seconds 4

Check ((Find-ById $shell 'Pos.Scan') -ne $null) 'the scan field is on the till'

# Three units are scanned, so that a return of one of them is a part of the sale rather than the
# whole of it.
for ($i = 0; $i -lt 3; $i++) {
    Check ((Send-Scan $shell $barcode 1) -gt 0) ('the scan ' + ($i + 1) + ' was answered')
}

Start-Sleep -Milliseconds 800
Set-ById $shell 'Pos.CashReceived' '200.00' | Out-Null
Start-Sleep -Milliseconds 700

Write-Output ('  confirm the sale: ' + (Invoke-ById $shell 'Pos.Confirm'))
Start-Sleep -Seconds 5

$receipt = Wait-ById $shell 'Pos.ReceiptWindow' 20
if (-not $receipt) { $receipt = Find-WindowById 'Pos.ReceiptWindow' }

if (-not $receipt) {
    Fail 'the receipt never appeared'
    Write-Output ('  the till says: ' + (Get-TextById $shell 'Pos.Message'))
} else {
    Invoke-ById $receipt 'Pos.ReceiptClose' | Out-Null
    Start-Sleep -Seconds 3
}

Check (Wait-Gone $shell 'Pos.ReceiptWindow' 15) 'the receipt closed'

$saleMessage = Get-TextById $shell 'Pos.Message'
Write-Output ('  the till says: ' + $saleMessage)
Check ($saleMessage -and $saleMessage.Contains('INV-')) 'the sale was given an invoice number'

# --- The sales register ----------------------------------------------------------------
Write-Output ('  open sales: ' + (Invoke-ById $shell 'Sales'))
Start-Sleep -Seconds 4

$register = Wait-ById $shell 'SalesRegister' 20
if (-not $register) {
    Fail 'the sales register never appeared'
    Dump-Windows $p.Id $shell
} else {
    Check ((Get-GridRowCount $register) -ge 1) 'the register is holding the sale'
    Check (Find-Text $shell 'INV-') 'the invoice is in the register'

    Save-Shot 'MainWindow' 'phase4-01-sales-register-arabic.png'

    # The invoice is picked, so the detail beside the register can be read.
    Check (Select-GridRow $register 'INV-') 'the invoice row was picked'

    Start-Sleep -Seconds 2
    Save-Shot 'MainWindow' 'phase4-02-sales-detail-arabic.png'

    # The register picks the newest document by itself, so the whole window would be the same
    # picture twice over. This one is the detail panel on its own.
    Crop-Shot 'phase4-02-sales-detail-arabic.png' 20 178 340 580

    $detail = Get-TextsContaining $shell '135'
    Check ($detail.Count -gt 0) 'the detail shows the total of three units'

    # --- The return -------------------------------------------------------------------
    Write-Output ('  open the return: ' + (Invoke-ById $shell 'SalesReturn'))
    Start-Sleep -Seconds 3

    $returnDialog = Wait-WindowById 'ReturnDialog' 20
    if (-not $returnDialog) {
        Fail 'the return dialog never appeared'
        Dump-Windows $p.Id $shell
    } else {
        Write-Output '  the return dialog is up'

        $returnGrid = Find-ById $returnDialog 'ReturnDialogLines'
        Check ($returnGrid -ne $null) 'the return dialog is holding the lines of the invoice'

        Check (Set-GridField $returnGrid 'ReturnDialogQuantity' '1') 'the quantity to return was written in'

        Check (Set-ById $returnDialog 'ReturnDialogReason' 'Wrong item') 'the reason was written in'

        Start-Sleep -Milliseconds 700

        $refund = Get-Number (Get-TextById $returnDialog 'ReturnDialogTotal')
        Write-Output ('  the dialog says the refund is ' + $refund)
        Check ($refund -ne $null -and [math]::Abs($refund - 45.0) -lt 0.001) 'the refund is the price of the one unit'

        # The picture is taken with the return filled in, so it shows the money that goes back.
        Save-Shot 'ReturnDialog' 'phase4-03-return-dialog-arabic.png'

        Invoke-ById $returnDialog 'ReturnDialogPost' | Out-Null
        Start-Sleep -Seconds 4

        Check (Wait-Gone $shell 'ReturnDialog' 20) 'the return dialog closed after posting'
    }

    Start-Sleep -Seconds 2
    Check (Find-Text $shell 'RET-') 'the return is in the register'

    Save-Shot 'MainWindow' 'phase4-04-sales-after-the-return-arabic.png'

    $rows = Get-GridRows $register
    foreach ($row in $rows) { Write-Output ('    row: ' + $row) }

    $returnRow = $rows | Where-Object { $_ -like '*RET-*' } | Select-Object -First 1
    Check ($returnRow -ne $null) 'the register holds a row for the return'

    if ($returnRow) {
        $parts = $returnRow -split '\|'
        $found = $false
        foreach ($part in $parts) {
            $value = Get-Number $part
            if ($null -ne $value -and [math]::Abs($value - (-45.0)) -lt 0.001) { $found = $true }
        }
        Check $found 'the return is shown with a negative amount, because the money went back'
    }
}

# --- The stock came back ---------------------------------------------------------------
Write-Output ('  open inventory: ' + (Invoke-ById $shell 'Inventory'))
Start-Sleep -Seconds 4

$balances = Find-ById $shell 'Inventory.Balances'
$balanceRows = Get-GridRows $balances
foreach ($row in $balanceRows) { Write-Output ('    stock row: ' + $row) }

$stockRow = $balanceRows | Where-Object { $_ -like ('*' + $sku + '*') } | Select-Object -First 1
Check ($stockRow -ne $null) 'the product is in the stock list'

# Twenty bought, three sold and one back on the shelf: eighteen.
Check ($stockRow -ne $null -and $stockRow -match '\b18\b') 'the shelf has the returned unit back on it: 20 bought, 3 sold, 1 back'

Save-Shot 'MainWindow' 'phase4-05-stock-after-the-return-arabic.png'

# --- The reversal ----------------------------------------------------------------------
Write-Output ('  open the till again: ' + (Invoke-ById $shell 'Pos'))
Start-Sleep -Seconds 4

Check ((Send-Scan $shell $barcode 1) -gt 0) 'the second sale was scanned'
Start-Sleep -Milliseconds 800
Set-ById $shell 'Pos.CashReceived' '100.00' | Out-Null
Start-Sleep -Milliseconds 700

Write-Output ('  confirm the second sale: ' + (Invoke-ById $shell 'Pos.Confirm'))
Start-Sleep -Seconds 5

$receipt = Wait-ById $shell 'Pos.ReceiptWindow' 20
if (-not $receipt) { $receipt = Find-WindowById 'Pos.ReceiptWindow' }
if ($receipt) {
    Invoke-ById $receipt 'Pos.ReceiptClose' | Out-Null
    Start-Sleep -Seconds 3
}

Write-Output ('  open sales again: ' + (Invoke-ById $shell 'Sales'))
Start-Sleep -Seconds 4

$register = Wait-ById $shell 'SalesRegister' 20
if ($register) {
    $rows = Get-GridRows $register
    $secondInvoice = $null
    foreach ($row in $rows) {
        # The register is newest first, so the first sale row is the second sale.
        if ($null -eq $secondInvoice -and $row -like '*INV-*' -and $row -notlike '*RET-*') {
            $secondInvoice = $row
        }
    }

    Write-Output ('  the rows are: ' + ($rows -join ' / '))

    if ($secondInvoice) {
        $number = ($secondInvoice -split '\|' | Where-Object { $_.Trim() -like 'INV-*' } | Select-Object -First 1)
        Write-Output ('  reversing ' + $number)

        Check (Select-GridRowByNumber $register $number.Trim()) 'the second invoice was picked'
        Start-Sleep -Seconds 2

        # The panel beside the register has to show the invoice itself. The return carries the same
        # number in its last column, so the number the panel shows is what proves which document
        # was picked.
        $shown = Get-TextById $shell 'SalesDetailInvoiceNumber'
        Check ($shown -and $shown.Trim() -eq $number.Trim()) 'the invoice is the document that is picked'

        Write-Output ('  open the reversal: ' + (Invoke-ById $shell 'SalesReverse'))
        Start-Sleep -Seconds 3

        $reverseDialog = Wait-WindowById 'ReverseDialog' 20
        if (-not $reverseDialog) {
            Fail 'the reversal dialog never appeared'
            Dump-Windows $p.Id $shell
        } else {
            Check (Set-ById $reverseDialog 'ReverseDialogReason' 'Keyed by mistake') 'the reason was written in'

            Start-Sleep -Milliseconds 700

            # The picture is taken with the reason written in, so it shows the document as it is
            # posted.
            Save-Shot 'ReverseDialog' 'phase4-06-reversal-dialog-arabic.png'

            Invoke-ById $reverseDialog 'ReverseDialogPost' | Out-Null
            Start-Sleep -Seconds 4

            Check (Wait-Gone $shell 'ReverseDialog' 20) 'the reversal dialog closed after posting'
        }

        Start-Sleep -Seconds 2
        Check (Find-Text $shell 'REV-') 'the reversal is in the register'

        Save-Shot 'MainWindow' 'phase4-07-sales-after-the-reversal-arabic.png'
    } else {
        Fail 'the second invoice was not in the register'
    }
}

# --- The same register in English ------------------------------------------------------
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

Write-Output ('  open sales in English: ' + (Invoke-ById $shell 'Sales'))
Start-Sleep -Seconds 4

Save-Shot 'MainWindow' 'phase4-08-sales-register-english.png'
Check ((Get-TextsContaining $shell 'Total').Count -gt 0) 'the register reads in English'
Check (Find-Text $shell 'INV-') 'the register still holds the sale in English'

# --- Report ----------------------------------------------------------------------------
Dump-Windows $p.Id $shell

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
Write-Output ('run folder kept at: ' + $runRoot)
Write-Output ('checks: ' + $checks)
Write-Output ('failures: ' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
