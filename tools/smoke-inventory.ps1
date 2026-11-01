$ErrorActionPreference = 'Continue'

# Drives the purchase and inventory screens through UI Automation against the real application, in
# Arabic and then in English. The application is pointed at a throwaway data folder, so the shop's
# own data is never touched.
#
# Every control is found by its automation identifier rather than by its label, so the script does
# not depend on the wording of the interface in either language. The only words the script uses are
# the stock-keeping codes of the products it creates, which are the same in every language.
#
# The screens are captured by the application itself. The script asks the running application to
# draw one of its own windows over the capture pipe, and the application writes the picture. A
# picture taken from outside the process comes back mirrored for a right-to-left window and shows a
# frame that is not the one the owner sees, so it is not used.

$exe = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
$outDir = 'D:\Coding\UA Retail Store\docs\phases\phase2-screens'
$runRoot = Join-Path $env:TEMP ('storeflow-phase2-' + [Guid]::NewGuid().ToString('N'))
$pipeName = 'storeflow-phase2-capture-' + [Guid]::NewGuid().ToString('N')

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
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
Write-Output ('shots folder: ' + $outDir)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

# Arabic strings, built from code points so they survive this file being read as ANSI.
function A([int[]]$cp) { -join ($cp | ForEach-Object { [char]$_ }) }

function Find-ById($scope, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $scope.FindFirst($TS::Subtree, $cond)
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

function Get-NameById($scope, [string]$id) {
    $el = Find-ById $scope $id
    if (-not $el) { return $null }
    try { return $el.Current.Name } catch { return $null }
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
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Wait-Gone($scope, [string]$id, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $el = Find-ById $scope $id
        if (-not $el) { return $true }
        Start-Sleep -Milliseconds 300
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

# The shell is the one wide window of the application.
function Wait-Shell([int]$processId, [int]$seconds = 30) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) {
            try {
                if ($w.Current.BoundingRectangle.Width -gt 600) { return $w }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
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
            if (-not $e.Current.IsOffscreen -and $e.Current.BoundingRectangle.Height -gt 0) { $result += $e }
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
            if (-not $e.Current.IsOffscreen -and $e.Current.BoundingRectangle.Height -gt 0 -and
                $e.Current.ClassName -like '*PasswordBox*') { $result += $e }
        } catch { }
    }
    return $result
}

function Set-EditValue($edit, [string]$value) {
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
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

# Every name on the screen, whatever kind of element it belongs to.
function Get-TextNames($window) {
    $result = @()
    foreach ($e in $window.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            if ($e.Current.Name) { $result += $e.Current.Name }
        } catch { }
    }
    return $result
}

# Looks for a piece of text anywhere on the screen, and waits for it: the rows of a table are only
# there once the table has drawn them. When the text never turns up, the screen is written out, so
# that a failed check says what was on it.
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

# Reads back what a picker currently holds, so a choice can be checked rather than assumed.
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

# Chooses an entry in a picker. A WPF picker only takes a choice while its list is open: marking an
# entry of a closed list as chosen leaves the picker's own selection untouched, so the choice never
# reaches the screen behind it and the picker still reads as empty. The list is therefore opened
# first, the entry is chosen from the open list, and the choice is read back to be sure it took.
#
# The list of a dialog that counts stock is filled in the background, so an empty list is not an
# answer - it is a list that has not arrived yet. The picker is therefore kept open and watched
# until the entry turns up or the wait runs out.
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

        # The list is still being filled. The picker stays open, so the entry can be taken the
        # moment it arrives.
        Start-Sleep -Milliseconds 800
    }

    Write-Host ('    the picker has no entry that contains "' + $needle + '"')
    foreach ($item in $items) {
        try { Write-Host ('      ' + $item.Current.Name) } catch { }
    }
    return $false
}

# Writes into a field of a table. The fields of a table are plain boxes, so they are found by the
# identifier the view gives them rather than by their position.
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

# Draws the window with the given automation identifier and saves the picture under the given name.
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

# --- The application -----------------------------------------------------------
$p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 7

# --- The first-run wizard -------------------------------------------------------
$wizard = Wait-Window $p.Id (A @(0x0625,0x0639,0x062F,0x0627,0x062F,0x0020,0x0633,0x062A,0x0648,0x0631,0x0020,0x0641,0x0644,0x0648)) 30
if (-not $wizard) { Write-Output 'FAIL: the wizard never appeared'; Stop-Process -Id $p.Id -Force; exit 1 }
Write-Output ('wizard: "' + $wizard.Current.Name + '" is up')

$edits = Get-VisibleEdits $wizard
$values = @('Al-Masria Store', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000')
for ($i = 0; $i -lt [Math]::Min($edits.Count, $values.Count); $i++) { Set-EditValue $edits[$i] $values[$i] }
Start-Sleep -Milliseconds 700
Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
Start-Sleep -Seconds 2

$edits = Get-VisibleEdits $wizard
$passwords = Get-PasswordEdits $wizard
if ($edits.Count -ge 2) { Set-EditValue $edits[0] 'Owner'; Set-EditValue $edits[1] 'owner' }
if ($passwords.Count -ge 2) { Set-EditValue $passwords[0] 'StoreFlow!2026'; Set-EditValue $passwords[1] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 700
Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
Start-Sleep -Seconds 2
Write-Output ('  finish: ' + (Invoke-ById $wizard 'FirstRun.Finish'))
Start-Sleep -Seconds 6

# --- Sign in -------------------------------------------------------------------
$login = Wait-Window $p.Id (A @(0x062A,0x0633,0x062C,0x064A,0x0644,0x0020,0x0627,0x0644,0x062F,0x062E,0x0648,0x0644)) 60
if (-not $login) {
    Write-Output 'FAIL: the sign-in window never appeared'
    Dump-Windows $p.Id $null
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output ('login: "' + $login.Current.Name + '" is up')

$edits = Get-VisibleEdits $login
$passwords = Get-PasswordEdits $login
if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' }
if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 700
Write-Output ('  sign in: ' + (Invoke-ById $login 'Login.SignIn'))
Start-Sleep -Seconds 7

$shell = Wait-Shell $p.Id
if (-not $shell) { Write-Output 'FAIL: no shell window'; Stop-Process -Id $p.Id -Force; exit 1 }
Write-Output ('shell: "' + $shell.Current.Name + '"')
Save-Shot 'MainWindow' 'phase2-01-shell-arabic.png'

# --- Two products to buy -------------------------------------------------------
Write-Output ('  open products: ' + (Invoke-ById $shell 'Products'))
Start-Sleep -Seconds 3

$products = @(
    @('RICE-001', 'Rice 1kg', '450.00', '400.00', '5'),
    @('OIL-001', 'Olive Oil 1L', '320.00', '260.00', '10')
)

foreach ($product in $products) {
    Write-Output ('  new product ' + $product[0] + ': ' + (Invoke-ById $shell 'Products.New'))
    Start-Sleep -Seconds 3

    $editor = Wait-ById $shell 'ProductEditorWindow' 20
    if (-not $editor) { Fail 'the product editor never appeared'; break }

    Set-ById $editor 'ProductEditor.Sku' $product[0] | Out-Null
    Set-ById $editor 'ProductEditor.Name' $product[1] | Out-Null
    Set-ById $editor 'ProductEditor.SalePrice' $product[2] | Out-Null
    Set-ById $editor 'ProductEditor.CostPrice' $product[3] | Out-Null
    Set-ById $editor 'ProductEditor.ReorderLevel' $product[4] | Out-Null
    Start-Sleep -Milliseconds 700

    Invoke-ById $editor 'ProductEditor.Save' | Out-Null
    Start-Sleep -Seconds 4

    Check (Wait-Gone $shell 'ProductEditorWindow' 15) ('the editor closed after saving ' + $product[0])
}

Check (Find-Text $shell 'RICE-001') 'the products are in the catalogue'

# --- A purchase receipt --------------------------------------------------------
Write-Output ('  open purchases: ' + (Invoke-ById $shell 'Purchases'))
Start-Sleep -Seconds 3
Save-Shot 'MainWindow' 'phase2-02-purchases-empty-arabic.png'
Check ((Find-ById $shell 'Purchases.Grid') -ne $null) 'the purchase register is on the page'

Write-Output ('  new purchase: ' + (Invoke-ById $shell 'Purchases.New'))
Start-Sleep -Seconds 3

$editor = Wait-ById $shell 'PurchaseEditorWindow' 20
if (-not $editor) {
    Fail 'the purchase editor never appeared'
    Dump-Windows $p.Id $shell
} else {
    Write-Output '  the purchase editor is up'
    Save-Shot 'PurchaseEditorWindow' 'phase2-03-purchase-editor-arabic.png'

    Invoke-ById $editor 'PurchaseEditor.AddLine' | Out-Null
    Start-Sleep -Milliseconds 900

    Check (Select-ComboItem $editor 'PurchaseEditor.Line.Product' 'RICE-001') 'the product was chosen on the line'

    # The line is: product, quantity, unit cost, tax, line total, remove.
    $grid = Find-ById $editor 'PurchaseEditor.Lines'
    Check (Set-GridField $grid 'PurchaseEditor.Line.Quantity' '20') 'the quantity was written into the line'
    Check (Set-GridField $grid 'PurchaseEditor.Line.UnitCost' '380.00') 'the unit cost was written into the line'

    Save-Shot 'PurchaseEditorWindow' 'phase2-04-purchase-editor-filled-arabic.png'

    Write-Output ('  save and post: ' + (Invoke-ById $editor 'PurchaseEditor.SaveAndPost'))
    Start-Sleep -Seconds 5

    # A window that stays open says why on its own status line, and that line is the only
    # account of what went wrong, so it is read before anything else.
    Write-Output ('  the editor says: ' + (Get-TextById $editor 'PurchaseEditor.Status'))

    Check (Wait-Gone $shell 'PurchaseEditorWindow' 20) 'the purchase editor closed after posting'
    Save-Shot 'MainWindow' 'phase2-05-purchases-posted-arabic.png'
    Check (Find-Text $shell 'PUR-') 'the purchase is in the register'
}

# --- The inventory -------------------------------------------------------------
Write-Output ('  open inventory: ' + (Invoke-ById $shell 'Inventory'))
Start-Sleep -Seconds 4
Save-Shot 'MainWindow' 'phase2-06-inventory-balances-arabic.png'
Check ((Find-ById $shell 'Inventory.Balances') -ne $null) 'the stock on hand table is on the page'
Check (Find-Text $shell 'RICE-001') 'the received product is in the stock list'

# The ledger is the second tab of the screen.
$tabCond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
$tabs = $shell.FindAll($TS::Descendants, $tabCond)
Write-Output ('  the screen has ' + $tabs.Count + ' tabs')

if ($tabs.Count -ge 2) {
    try { $tabs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
    Start-Sleep -Seconds 3
    Save-Shot 'MainWindow' 'phase2-07-inventory-movements-arabic.png'
    Check (Find-Text $shell 'RICE-001') 'the ledger names the product that moved'
}

if ($tabs.Count -ge 3) {
    try { $tabs[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
    Start-Sleep -Seconds 2
    Save-Shot 'MainWindow' 'phase2-08-inventory-counts-arabic.png'
}

# --- A stock count -------------------------------------------------------------
if ($tabs.Count -ge 3) {
    try { $tabs[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
    Start-Sleep -Seconds 1
}

Write-Output ('  new stock count: ' + (Invoke-ById $shell 'Inventory.NewCount'))
Start-Sleep -Seconds 3

$counter = Wait-ById $shell 'StockCountWindow' 20
if (-not $counter) {
    Fail 'the stock count window never appeared'
} else {
    Write-Output '  the stock count window is up'
    Save-Shot 'StockCountWindow' 'phase2-09-stock-count-arabic.png'

    Invoke-ById $counter 'StockCount.AddLine' | Out-Null
    Start-Sleep -Milliseconds 900

    Check (Select-ComboItem $counter 'StockCount.Line.Product' 'RICE-001') 'the counted product was chosen'

    # The line is: product, counted quantity, remove.
    $grid = Find-ById $counter 'StockCount.Lines'
    Check (Set-GridField $grid 'StockCount.Line.Counted' '18') 'the counted quantity was written into the line'

    Save-Shot 'StockCountWindow' 'phase2-10-stock-count-filled-arabic.png'

    Write-Output ('  submit the count: ' + (Invoke-ById $counter 'StockCount.Submit'))
    Start-Sleep -Seconds 4
    Write-Output ('  the count says: ' + (Get-TextById $counter 'StockCount.Status'))
    Check (Wait-Gone $shell 'StockCountWindow' 20) 'the stock count window closed after submitting'
}

# The count waits for an approval before it touches posted stock.
Start-Sleep -Seconds 2
$approveButton = Find-ById $shell 'Inventory.ApproveCount'
if ($approveButton) {
    Write-Output ('  approve the count: ' + (Invoke-ById $shell 'Inventory.ApproveCount'))
    Start-Sleep -Seconds 3

    $prompt = Wait-ById $shell 'ReasonPromptWindow' 15
    if ($prompt) {
        Save-Shot 'ReasonPromptWindow' 'phase2-11-approve-count-arabic.png'
        Set-ById $prompt 'ReasonPrompt.Reason' 'Recount of the shelf' | Out-Null
        Start-Sleep -Milliseconds 500
        Invoke-ById $prompt 'ReasonPrompt.Accept' | Out-Null
        Start-Sleep -Seconds 4
        Check (Wait-Gone $shell 'ReasonPromptWindow' 15) 'the approval prompt closed'
    } else {
        Fail 'the approval prompt never appeared'
    }
} else {
    Fail 'the approve button is not on the counts tab'
}

Write-Output ('  post the count: ' + (Invoke-ById $shell 'Inventory.PostCount'))
Start-Sleep -Seconds 4
Save-Shot 'MainWindow' 'phase2-12-inventory-counts-posted-arabic.png'

# --- A stock adjustment --------------------------------------------------------
Write-Output ('  stock adjustment: ' + (Invoke-ById $shell 'Inventory.Adjustment'))
Start-Sleep -Seconds 3

$adjuster = Wait-ById $shell 'StockAdjustmentWindow' 20
if (-not $adjuster) {
    Fail 'the stock adjustment window never appeared'
} else {
    Save-Shot 'StockAdjustmentWindow' 'phase2-13-stock-adjustment-arabic.png'

    Check (Select-ComboItem $adjuster 'StockAdjustment.Product' 'RICE-001') 'the adjusted product was chosen'

    Set-ById $adjuster 'StockAdjustment.Quantity' '-1' | Out-Null
    Start-Sleep -Milliseconds 400
    Set-ById $adjuster 'StockAdjustment.Reason' 'One bag was damaged in the shop' | Out-Null
    Start-Sleep -Milliseconds 600

    Write-Output ('  post the adjustment: ' + (Invoke-ById $adjuster 'StockAdjustment.Submit'))
    Start-Sleep -Seconds 4
    Write-Output ('  the adjustment says: ' + (Get-TextById $adjuster 'StockAdjustment.Status'))

    Check (Wait-Gone $shell 'StockAdjustmentWindow' 20) 'the stock adjustment window closed'
}

Start-Sleep -Seconds 2
if ($tabs.Count -ge 1) {
    try { $tabs[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
    Start-Sleep -Seconds 3
    Save-Shot 'MainWindow' 'phase2-14-inventory-balances-after-arabic.png'
}

# --- The same screens in English -----------------------------------------------
# The language is chosen from the picker in the shell. When the picker cannot be pressed — the
# desktop is locked and the popup of a combo box is a window of its own that the automation cannot
# reach — the language is written into the theme the application reads when it starts, and the
# application is started again. The screens are then read in English exactly as the shopkeeper
# would read them.
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

        $login = Wait-Window $p.Id 'Sign in' 30
        if (-not $login) {
            Fail 'the sign-in window never appeared in English'
        } else {
            $edits = Get-VisibleEdits $login
            $passwords = Get-PasswordEdits $login
            if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' }
            if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' }
            Start-Sleep -Milliseconds 700
            Invoke-ById $login 'Login.SignIn' | Out-Null
            Start-Sleep -Seconds 7
        }

        $shell = Wait-Shell $p.Id
        if (-not $shell) {
            Fail 'the shell never appeared in English'
        } else {
            Check ((Get-TextsContaining $shell 'Products').Count -gt 0) 'the navigation rail reads in English'
        }
    }
}

Write-Output ('  open purchases in English: ' + (Invoke-ById $shell 'Purchases'))
Start-Sleep -Seconds 4
Save-Shot 'MainWindow' 'phase2-15-purchases-english.png'
Check ((Get-TextsContaining $shell 'Number').Count -gt 0) 'the purchase register reads in English'
Check (Find-Text $shell 'PUR-') 'the purchase is still there in English'

Write-Output ('  open inventory in English: ' + (Invoke-ById $shell 'Inventory'))
Start-Sleep -Seconds 4
Save-Shot 'MainWindow' 'phase2-16-inventory-balances-english.png'
Check ((Get-TextsContaining $shell 'On hand').Count -gt 0) 'the stock on hand table reads in English'
Check (Find-Text $shell 'RICE-001') 'the received product is still there in English'

$tabs = $shell.FindAll($TS::Descendants, $tabCond)
if ($tabs.Count -ge 2) {
    try { $tabs[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() } catch { }
    Start-Sleep -Seconds 3
    Save-Shot 'MainWindow' 'phase2-17-inventory-movements-english.png'
    Check ((Get-TextsContaining $shell 'Balance').Count -gt 0) 'the ledger reads in English'
}

# --- Report --------------------------------------------------------------------
Dump-Windows $p.Id $shell

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
Write-Output ('run folder kept at: ' + $runRoot)
Write-Output ('failures: ' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
