$ErrorActionPreference = 'Continue'

# Drives the reports of the shop and the approvals through UI Automation against the real
# application, in English. The application is pointed at a throwaway data folder, so the shop's own
# data is never touched.
#
# The point of the run is that the figures on the screen are checked against figures worked out by
# hand. One product is bought at 50.00 and sold at 100.00, three units are sold, and every report
# and the dashboard has to say what the arithmetic says: 300.00 taken, 150.00 of goods sold, 150.00
# of profit, and 7 units worth 350.00 still on the shelf. The exported file is checked the same
# way, down to the byte-order mark at its head.
#
# Every control is found by its automation identifier rather than by its label, so the script does
# not depend on the wording of the interface. The only words the script uses are the names of the
# reports, the headings of the exported file and the stock-keeping code and barcode of the product
# it creates.
#
# The screens are captured by the application itself. The script asks the running application to draw
# one of its own windows over the capture pipe, and the application writes the picture. A picture
# taken from outside the process would come back mirrored for a right-to-left window, and the
# desktop session on this machine is locked, so a picture taken from outside the process is not
# possible either.
#
# The run tells one story: a product is bought, three units of it are sold at the till, and then the
# shop looks at itself from every side the reports and the dashboard offer.

$exe = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
$outDir = 'D:\Coding\UA Retail Store\docs\phases\phase5-screens'
$runRoot = Join-Path $env:TEMP ('storeflow-phase5-' + [Guid]::NewGuid().ToString('N'))
$pipeName = 'storeflow-phase5-capture-' + [Guid]::NewGuid().ToString('N')

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

public static class StoreFlowReportsSmoke
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

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

// The Windows file dialog is driven through the window messages of the dialog itself. The desktop
// session is locked, so real key presses never reach the dialog, but a window message does not need
// the keyboard to be anywhere in particular.
public static class StoreFlowDialog {
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr h, string t);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wp, StringBuilder lp);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);

    private const uint WM_COMMAND = 0x0111;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint EM_SETSEL = 0x00B1;
    private const uint EN_CHANGE = 0x0300;
    private const uint BM_CLICK = 0x00F5;

    public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    public static string TextOf(IntPtr h) { var sb = new StringBuilder(2048); GetWindowText(h, sb, 2048); return sb.ToString(); }

    public static IntPtr FindDialog(int processId) {
        IntPtr first = IntPtr.Zero;
        IntPtr withBox = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == (uint)processId && ClassOf(h) == "#32770") {
                if (first == IntPtr.Zero) { first = h; }
                if (withBox == IntPtr.Zero && FindFileNameEdit(h) != IntPtr.Zero) { withBox = h; }
            }
            return true;
        }, IntPtr.Zero);
        return withBox != IntPtr.Zero ? withBox : first;
    }

    public static IntPtr FindChildById(IntPtr parent, int id) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            if (GetDlgCtrlID(h) == id && found == IntPtr.Zero) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // The identifier alone is not enough. The modern dialog gives the toolbars of its address band
    // the identifier 1001 as well, and typing a path into one of those is typing it into a toolbar.
    public static IntPtr FindChildByIdAndClass(IntPtr parent, int id, string wanted) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            if (GetDlgCtrlID(h) == id && ClassOf(h) == wanted && found == IntPtr.Zero) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static List<IntPtr> All(IntPtr parent) {
        var found = new List<IntPtr>();
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) { found.Add(h); return true; }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindChildByClass(IntPtr parent, string wanted) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, delegate(IntPtr h, IntPtr l) {
            if (found == IntPtr.Zero && ClassOf(h) == wanted) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // The file name box of the Windows dialog. The classic dialog puts it in the edit with the
    // identifier 1001. The modern one puts it in the edit with the identifier 1148, inside the
    // combo that holds it, and gives the same identifier 1001 to the toolbars of its address band.
    // A path typed into an address band is a path to a folder, not to a file.
    public static IntPtr FindFileNameEdit(IntPtr dialog) {
        IntPtr byId = FindChildByIdAndClass(dialog, 1001, "Edit");
        if (byId != IntPtr.Zero) { return byId; }

        IntPtr host = FindChildByIdAndClass(dialog, 1148, "ComboBoxEx32");
        if (host == IntPtr.Zero) { host = FindChildByIdAndClass(dialog, 1148, "ComboBox"); }
        if (host != IntPtr.Zero) {
            IntPtr edit = FindChildByClass(host, "Edit");
            if (edit != IntPtr.Zero) { return edit; }
        }

        // Whatever else the dialog is made of, the file name box is the edit that sits in a combo.
        foreach (IntPtr h in All(dialog)) {
            if (ClassOf(h) == "ComboBox" || ClassOf(h) == "ComboBoxEx32") {
                IntPtr edit = FindChildByClass(h, "Edit");
                if (edit != IntPtr.Zero) { return edit; }
            }
        }

        return IntPtr.Zero;
    }

    public static string FileName(IntPtr dialog) {
        IntPtr edit = FindFileNameEdit(dialog);
        // The box is read with a message and not with GetWindowText, because the text of a control
        // of another process cannot be read that way.
        return edit == IntPtr.Zero ? "" : ReadText(edit);
    }

    // The modern Windows dialog keeps its own copy of the name and ignores a name written into the
    // box, so the path is typed into the box one character at a time. Typing is what makes the
    // dialog take notice.
    public static bool TypeFileName(IntPtr dialog, string path) {
        IntPtr edit = FindFileNameEdit(dialog);
        if (edit == IntPtr.Zero) return false;

        SendMessage(edit, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
        SendMessage(edit, WM_CHAR, new IntPtr(0x08), IntPtr.Zero);

        foreach (char character in path) {
            SendMessage(edit, WM_CHAR, new IntPtr(character), new IntPtr(1));
        }

        // Read the box back. A path that did not land in the box is worth knowing about before the
        // dialog is closed, because afterwards nothing can be told apart.
        return ReadText(edit).Trim() == path.Trim();
    }

    public static string ReadText(IntPtr h) {
        int length = (int)SendMessage(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        var buffer = new StringBuilder(length + 2);
        SendMessage(h, WM_GETTEXT, new IntPtr(buffer.Capacity), buffer);
        return buffer.ToString();
    }

    public static void PressAccept(IntPtr dialog) {
        // Press the button that accepts, the way the dialog expects it to be pressed.
        IntPtr button = IntPtr.Zero;
        EnumChildWindows(dialog, delegate(IntPtr h, IntPtr l) {
            if (GetDlgCtrlID(h) == 1) { button = h; return false; }
            return true;
        }, IntPtr.Zero);

        if (button != IntPtr.Zero) {
            PostMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        } else {
            PostMessage(dialog, WM_COMMAND, (IntPtr)1, IntPtr.Zero);
        }
    }

    public static bool IsOpen(IntPtr dialog) { return IsWindow(dialog); }

    public static bool HasFileNameBox(IntPtr dialog) {
        return dialog != IntPtr.Zero && FindFileNameEdit(dialog) != IntPtr.Zero;
    }
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
        if ($el) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return $true
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

# The cells of one row of a table, each in the column it really sits in. A cell that holds nothing
# keeps its place, so an empty column never shifts the figures that follow it.
function Get-RowCells($grid, [int]$rowIndex) {
    if (-not $grid) { return $null }

    $rowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)

    $rows = $grid.FindAll($TS::Descendants, $rowCond)
    if ($rowIndex -ge $rows.Count) { return $null }

    $row = $rows[$rowIndex]
    $cells = @{}

    foreach ($element in $row.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $column = -1

        try {
            $pattern = $element.GetCurrentPattern([System.Windows.Automation.GridItemPattern]::Pattern)
            $column = $pattern.Current.GridColumn
        } catch { }

        if ($column -lt 0) { continue }

        $text = ''

        try {
            if ($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                $text = $element.Current.Name
            }
        } catch { }

        if (-not $text) {
            foreach ($inner in $element.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                try {
                    if ($inner.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                        $text = $inner.Current.Name
                        break
                    }
                } catch { }
            }
        }

        if (-not $cells.ContainsKey($column)) { $cells[$column] = $text }
    }

    # If the table does not tell which column a cell sits in, the cells are taken in the order they
    # are shown, which is the order the eye reads them in.
    if ($cells.Count -eq 0) {
        $index = 0

        foreach ($element in $row.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                if ($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text) {
                    $cells[$index] = $element.Current.Name
                    $index++
                }
            } catch { }
        }
    }

    return $cells
}

# The text a cell of a row holds.
function Get-Cell($cells, [int]$column) {
    if (-not $cells) { return $null }
    if (-not $cells.ContainsKey($column)) { return $null }
    return $cells[$column]
}

# The row of a table whose column holds the given text, or -1 when no row does.
function Find-RowByCell($grid, [int]$column, [string]$text) {
    $count = Get-GridRowCount $grid

    for ($i = 0; $i -lt $count; $i++) {
        $cells = Get-RowCells $grid $i
        $value = Get-Cell $cells $column

        if ($value -and $value.Trim() -eq $text) { return $i }
    }

    return -1
}

# How many columns a table is showing.
function Get-GridColumnCount($grid) {
    if (-not $grid) { return 0 }

    try {
        $pattern = $grid.GetCurrentPattern([System.Windows.Automation.GridPattern]::Pattern)
        return $pattern.Current.ColumnCount
    } catch { return 0 }
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

# Compares a figure on the screen with the figure worked out by hand. Both numbers are printed
# when they disagree, because a check that only says no is of no use to anybody.
function Check-Amount([string]$what, [string]$text, [double]$expected) {
    $shown = Get-Number $text

    if ($null -eq $shown) {
        Fail ($what + ': the screen showed nothing that looks like a number')
        return
    }

    $script:checks++

    if ([math]::Abs($shown - $expected) -le 0.005) {
        Write-Output ('  ok   ' + $what + ' is ' + $shown)
        return
    }

    Fail ($what + ': the screen showed ' + $shown + ' and the figures say ' + $expected)
}

# Reads a day off a screen. The screen writes the day the way the language it runs in writes it,
# so more than one shape is accepted.
function Test-Date([string]$text, [string]$expected) {
    if (-not $text) { return $false }

    $formats = @('yyyy-MM-dd', 'M/d/yyyy', 'd/M/yyyy', 'MM/dd/yyyy', 'dd/MM/yyyy', 'yyyy/MM/dd')

    foreach ($format in $formats) {
        $value = [datetime]::MinValue

        if ([datetime]::TryParseExact($text.Trim(), $format,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::None, [ref]$value)) {
            return ($value.ToString('yyyy-MM-dd') -eq $expected)
        }
    }

    return $false
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

        [void][StoreFlowReportsSmoke]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 150

        [StoreFlowReportsSmoke]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [StoreFlowReportsSmoke]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 250

        return $true
    } catch {
        return $false
    }
}

# Scans a barcode the way a scanner does: the code goes into the field, Enter finishes it.
# Scans a barcode the way a scanner does: the code goes into the field, Enter finishes it.
#
# The wait is for the cart to reach a figure, not for the cart to have a row: three scans of the
# same barcode are one line of three, so the number of rows never changes. Waiting for the rows
# meant that the second and the third scan were sent while the till was still answering the first,
# and the figures on the screen were the figures of one unit instead of three.
function Send-Scan($shell, [string]$barcode, [int]$waitRows = 1, [double]$expectedTotal = 0) {
    $scan = Find-ById $shell 'Pos.Scan'

    if (-not $scan) { return -1 }

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
        if ($expectedTotal -gt 0) {
            if ((Get-CartTotal $shell) -ge $expectedTotal) { return 1 }
        }
        elseif ((Get-CartRowCount $shell) -ge $waitRows) {
            return 1
        }

        Start-Sleep -Milliseconds 40
    }

    Write-Host ('    the cart did not answer the scan of ' + $barcode)
    return 0
}

# What the cart is worth right now, as the screen shows it.
function Get-CartTotal($shell) {
    $text = Get-TextById $shell 'Pos.Total'

    if (-not $text) { return 0 }

    $value = Get-Number $text

    if ($null -eq $value) { return 0 }

    return $value
}

# Pictures are taken by the application itself. The script sends one command down the capture pipe
# and the application draws the window it names and writes the picture itself.
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

# Puts the application in front, so that the keys the script sends reach the field and not the
# console the script is running in.
function Focus-App($window) {
    if (-not $window) { return $false }

    try {
        $handle = [IntPtr]$window.Current.NativeWindowHandle

        if ($handle -eq [IntPtr]::Zero) { return $false }

        [void][StoreFlowReportsSmoke]::ShowWindow($handle, 9)
        [void][StoreFlowReportsSmoke]::BringWindowToTop($handle)
        [void][StoreFlowReportsSmoke]::SetForegroundWindow($handle)
        return $true
    } catch {
        return $false
    }
}

# The Windows file dialog belongs to Windows, not to the application, so it is found by the file
# name box it carries rather than by its class.
function Wait-FileDialog([int]$processId, $shell, [int]$seconds = 25) {
    $processCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $fileNameCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, '1148')
    $windowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)

    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $candidates = @()
        $candidates += $AE::RootElement.FindAll($TS::Children, $processCond)
        if ($shell) { $candidates += $shell.FindAll($TS::Subtree, $windowCond) }

        foreach ($w in $candidates) {
            try {
                if ($w.Current.ClassName -eq '#32770') { return $w }
                if ($w.FindFirst($TS::Subtree, $fileNameCond)) { return $w }
            } catch { }
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

# The file dialog is driven by the identifiers Windows gives it: the file name box is the edit
# "1001" inside the combo "1148", and the button that accepts the name is "1".
function Drive-FileDialog($dialog, [string]$path, [int]$processId = 0) {
    # The dialog is found in three ways, because the modern Windows dialog is not always a window
    # of its own process and does not always hand out a handle of its own.
    $candidates = @()
    $owner = $processId
    try {
        $candidates += [IntPtr]$dialog.Current.NativeWindowHandle
        $owner = $dialog.Current.ProcessId
    } catch { }
    if ($owner -gt 0) { $candidates += [StoreFlowDialog]::FindDialog($owner) }
    if ($processId -gt 0) { $candidates += [StoreFlowDialog]::FindDialog($processId) }

    $handle = [IntPtr]::Zero
    foreach ($candidate in $candidates) {
        if ($candidate -eq [IntPtr]::Zero) { continue }
        if ([StoreFlowDialog]::HasFileNameBox($candidate)) { $handle = $candidate; break }
    }

    if ($handle -eq [IntPtr]::Zero) {
        Write-Host '    the file dialog has no window with a file name box'
        return $false
    }

    if (-not [StoreFlowDialog]::TypeFileName($handle, $path)) {
        Write-Host '    the path did not land in the file name box'
        return $false
    }
    Write-Host ('    the file name box holds: [' + [StoreFlowDialog]::FileName($handle) + ']')

    [StoreFlowDialog]::PressAccept($handle)
    return $true
}

function Close-FileDialog($dialog) {
    if (-not $dialog) { return }
    $cancelCond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, '2')
    $cancel = $dialog.FindFirst($TS::Subtree, $cancelCond)
    if (-not $cancel) {
        $btnCond = New-Object System.Windows.Automation.PropertyCondition(
            $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $buttons = $dialog.FindAll($TS::Subtree, $btnCond)
        if ($buttons.Count -gt 0) { $cancel = $buttons[0] }
    }
    if ($cancel) {
        try { $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { }
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
$sku = 'REP-SMOKE-001'
$productName = 'REP-SMOKE-001'
$barcode = '2000000000015'
$price = '100.00'
$cost = '50.00'
$quantity = '10'
$unitsSold = '3'
$today = (Get-Date).ToString('yyyy-MM-dd')
$arStoreNameAr = A @(0x0645,0x062A,0x062C,0x0631,0x0020,0x0645,0x0635,0x0631,0x064A,0x0629)

# The figures, worked out by hand before the run starts. One unit costs 50.00 and is sold for
# 100.00, three units are sold, and ten units were bought.
$expectedGross = 300.00
$expectedNet = 300.00
$expectedCostOfGoods = 150.00
$expectedProfit = 150.00
$expectedStockQuantity = 7.00
$expectedStockValue = 350.00

$p = $null

try {
    # --- The application -------------------------------------------------------------------
    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id)
    Start-Sleep -Seconds 7

    # --- The first-run wizard --------------------------------------------------------------
    $wizard = Wait-WindowById 'FirstRunWindow' 40
    if (-not $wizard) {
        Fail 'the wizard never appeared'
        Dump-Windows $p.Id $null
    } else {
        Write-Output 'the wizard is up'

        Check (Fill-Fields $wizard @('Al-Masria Reports', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000') $true) 'the shop details were written into the wizard'
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

    # --- The shop runs in English ---------------------------------------------------------
    # The application keeps the language it starts in in its theme file, and the exported file
    # carries the headings of the language the shop reads. The theme is therefore written the way
    # the shop wants it and the application is started again, so that every screen the script reads
    # afterwards is in English.
    $themeFile = Join-Path $runRoot 'themes\active.sftheme.json'

    $deadline = (Get-Date).AddSeconds(20)
    while (-not (Test-Path $themeFile) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    if (-not (Test-Path $themeFile)) {
        Fail 'the theme file is not where the application keeps it'
    } else {
        $theme = Get-Content -Raw -Encoding UTF8 $themeFile
        $theme = [regex]::Replace($theme, '"default"\s*:\s*"ar"', '"default": "en"')
        [System.IO.File]::WriteAllText($themeFile, $theme, (New-Object System.Text.UTF8Encoding($false)))

        $written = Get-Content -Raw -Encoding UTF8 $themeFile
        Check ($written.Contains('"default": "en"')) 'the theme now starts the application in English'
    }

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' in English')
    Start-Sleep -Seconds 8

    # --- Sign in ---------------------------------------------------------------------------
    $login = Wait-WindowById 'LoginWindow' 40
    if (-not $login) {
        Fail 'the sign-in window never appeared'
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
        Fail 'the shell never appeared'
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
        Set-ById $editor 'ProductEditor.CostPrice' $cost | Out-Null
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

        $purchaseGrid = Find-ById $purchase 'PurchaseEditor.Lines'
        Check (Set-GridField $purchaseGrid 'PurchaseEditor.Line.Quantity' $quantity) 'the quantity was written into the line'
        Check (Set-GridField $purchaseGrid 'PurchaseEditor.Line.UnitCost' $cost) 'the unit cost was written into the line'

        Invoke-ById $purchase 'PurchaseEditor.SaveAndPost' | Out-Null
        Start-Sleep -Seconds 5

        Check (Wait-Gone $shell 'PurchaseEditorWindow' 20) 'the purchase editor closed after posting'
    }

    # --- The sale --------------------------------------------------------------------------
    Write-Output ('  open the till: ' + (Invoke-ById $shell 'Pos'))
    Start-Sleep -Seconds 4

    Check ((Find-ById $shell 'Pos.Scan') -ne $null) 'the scan field is on the till'

    # Three scans of the same barcode put three units on the cart, one line of three. Each scan
    # waits for the cart to be worth one more unit than it was, because the line stays one line.
    for ($i = 0; $i -lt 3; $i++) {
        $expected = ($i + 1) * 100.0

        Check ((Send-Scan $shell $barcode 1 $expected) -gt 0) ('the scan ' + ($i + 1) + ' was answered')
    }

    Start-Sleep -Milliseconds 800
    Set-ById $shell 'Pos.CashReceived' '300.00' | Out-Null
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

    # --- The dashboard ---------------------------------------------------------------------
    Write-Output ('  open the dashboard: ' + (Invoke-ById $shell 'Dashboard'))
    Start-Sleep -Seconds 3
    Invoke-ById $shell 'Dashboard.Refresh' | Out-Null
    Start-Sleep -Seconds 3

    $dashboardNet = Get-TextById $shell 'Dashboard.NetSales'
    Write-Output ('  the dashboard shows net sales of ' + $dashboardNet)
    Check-Amount 'the net sales of today on the dashboard' $dashboardNet $expectedNet

    $dashboardProfit = Get-TextById $shell 'Dashboard.GrossProfit'
    Write-Output ('  the dashboard shows a gross profit of ' + $dashboardProfit)
    Check-Amount 'the gross profit of today on the dashboard' $dashboardProfit $expectedProfit

    $dashboardInvoices = Get-TextById $shell 'Dashboard.Invoices'
    Check-Amount 'the number of invoices on the dashboard' $dashboardInvoices 1.00

    Save-Shot 'MainWindow' 'dashboard.png'

    # --- The reports -----------------------------------------------------------------------
    Write-Output ('  open the reports: ' + (Invoke-ById $shell 'Reports'))
    Start-Sleep -Seconds 3

    Check (Set-ById $shell 'Reports.From' $today) 'the first day of the period was written in'
    Check (Set-ById $shell 'Reports.To' $today) 'the last day of the period was written in'

    $grid = Find-ById $shell 'Reports.Grid'
    Check ($grid -ne $null) 'the grid of the report is on the screen'

    # --- The daily sales ------------------------------------------------------------------
    Check (Select-ComboItem $shell 'Reports.Picker' 'Daily sales') 'the daily sales report was chosen'
    Invoke-ById $shell 'Reports.Run' | Out-Null
    Start-Sleep -Seconds 3

    $row = Find-RowByCell $grid 0 $today
    if ($row -lt 0) {
        # The screen writes the day in the way of its language, so the row is looked for by the day
        # it holds rather than by its text.
        $deadline = (Get-Date).AddSeconds(20)
        while ((Get-Date) -lt $deadline -and $row -lt 0) {
            $cells = Get-RowCells $grid 0
            if ($cells -and (Test-Date (Get-Cell $cells 0) $today)) { $row = 0 }
            Start-Sleep -Milliseconds 300
        }
    }

    Check ($row -ge 0) 'the daily sales report is on the screen'

    if ($row -ge 0) {
        $cells = Get-RowCells $grid $row

        Check (Test-Date (Get-Cell $cells 0) $today) 'the daily sales row is about today'
        Check-Amount 'the number of invoices of the day' (Get-Cell $cells 1) 1.00
        Check-Amount 'the gross sales of the day' (Get-Cell $cells 2) $expectedGross
        Check-Amount 'the discounts of the day' (Get-Cell $cells 3) 0.00
        Check-Amount 'the tax of the day' (Get-Cell $cells 4) 0.00
        Check-Amount 'the returns of the day' (Get-Cell $cells 5) 0.00
        Check-Amount 'the net sales of the day' (Get-Cell $cells 6) $expectedNet
    }

    Save-Shot 'MainWindow' 'reports-daily-sales.png'

    # --- The sales by product -------------------------------------------------------------
    Check (Select-ComboItem $shell 'Reports.Picker' 'Sales by product') 'the sales by product report was chosen'
    Invoke-ById $shell 'Reports.Run' | Out-Null
    Start-Sleep -Seconds 3

    $row = Find-RowByCell $grid 0 $sku
    Check ($row -ge 0) 'the sales by product report holds the product'

    if ($row -ge 0) {
        $cells = Get-RowCells $grid $row

        Check-Amount 'the quantity sold of the product' (Get-Cell $cells 2) 3.00
        Check-Amount 'the revenue of the product' (Get-Cell $cells 3) $expectedNet
        Check-Amount 'the cost of the goods sold of the product' (Get-Cell $cells 4) $expectedCostOfGoods
        Check-Amount 'the gross profit of the product' (Get-Cell $cells 5) $expectedProfit
    }

    Save-Shot 'MainWindow' 'reports-sales-by-product.png'

    # --- The stock on hand ----------------------------------------------------------------
    Check (Select-ComboItem $shell 'Reports.Picker' 'Stock on hand') 'the stock on hand report was chosen'
    Invoke-ById $shell 'Reports.Run' | Out-Null
    Start-Sleep -Seconds 3

    $row = Find-RowByCell $grid 0 $sku
    Check ($row -ge 0) 'the stock on hand report holds the product'

    if ($row -ge 0) {
        $cells = Get-RowCells $grid $row

        Check-Amount 'the quantity left on the shelf' (Get-Cell $cells 3) $expectedStockQuantity
        Check-Amount 'the value of the stock on the shelf' (Get-Cell $cells 6) $expectedStockValue
    }

    Save-Shot 'MainWindow' 'reports-stock-on-hand.png'

    # --- The stock valuation --------------------------------------------------------------
    Check (Select-ComboItem $shell 'Reports.Picker' 'Stock valuation') 'the stock valuation report was chosen'
    Invoke-ById $shell 'Reports.Run' | Out-Null
    Start-Sleep -Seconds 3

    $row = Find-RowByCell $grid 0 $sku
    Check ($row -ge 0) 'the stock valuation report holds the product'

    if ($row -ge 0) {
        $cells = Get-RowCells $grid $row

        Check-Amount 'the quantity the valuation is about' (Get-Cell $cells 2) $expectedStockQuantity
        Check-Amount 'the average cost of a unit in the valuation' (Get-Cell $cells 3) 50.00
        Check-Amount 'the value of the stock in the valuation' (Get-Cell $cells 4) $expectedStockValue
    }

    Save-Shot 'MainWindow' 'reports-stock-valuation.png'

    # --- The profit and loss ---------------------------------------------------------------
    Check (Select-ComboItem $shell 'Reports.Picker' 'Profit and loss') 'the profit and loss report was chosen'
    Invoke-ById $shell 'Reports.Run' | Out-Null
    Start-Sleep -Seconds 3

    $netRow = Find-RowByCell $grid 0 'net_sales'
    $costRow = Find-RowByCell $grid 0 'cost_of_goods_sold'
    $profitRow = Find-RowByCell $grid 0 'gross_profit'

    Check ($netRow -ge 0) 'the profit and loss report holds the net sales line'
    Check ($costRow -ge 0) 'the profit and loss report holds the cost of the goods line'
    Check ($profitRow -ge 0) 'the profit and loss report holds the gross profit line'

    if ($netRow -ge 0) {
        Check-Amount 'the net sales of the profit and loss' (Get-Cell (Get-RowCells $grid $netRow) 1) $expectedNet
    }
    if ($costRow -ge 0) {
        Check-Amount 'the cost of the goods sold of the profit and loss' (Get-Cell (Get-RowCells $grid $costRow) 1) $expectedCostOfGoods
    }
    if ($profitRow -ge 0) {
        Check-Amount 'the gross profit of the profit and loss' (Get-Cell (Get-RowCells $grid $profitRow) 1) $expectedProfit
    }

    Save-Shot 'MainWindow' 'reports-profit-and-loss.png'

    # --- The export of the daily sales ----------------------------------------------------
    # The export is written from the report that is on the screen, so the file has to hold the
    # daily sales again, and the daily sales are put back on the screen first.
    Check (Select-ComboItem $shell 'Reports.Picker' 'Daily sales') 'the daily sales report was chosen again'
    Invoke-ById $shell 'Reports.Run' | Out-Null
    Start-Sleep -Seconds 3

    $exportFile = Join-Path $runRoot 'daily-sales.csv'

    Write-Output ('  export: ' + (Invoke-ById $shell 'Reports.Export'))
    Start-Sleep -Seconds 4

    $dialog = Wait-FileDialog $p.Id $shell 25
    if ($dialog) {
        $driven = Drive-FileDialog $dialog $exportFile $p.Id
        Check $driven 'the export file dialog was driven'

        if (-not $driven) { Close-FileDialog $dialog }
        Start-Sleep -Seconds 4

        if (-not (Test-Path $exportFile)) {
            Write-Host '    the file was not written, so the dialog is driven once more'
            $dialog = Wait-FileDialog $p.Id $shell 8
            if ($dialog) {
                $driven = Drive-FileDialog $dialog $exportFile $p.Id
                if (-not $driven) { Close-FileDialog $dialog }
                Start-Sleep -Seconds 4
            }
        }
    } else {
        Fail 'the export file dialog never appeared'
        Dump-Windows $p.Id $shell
    }

    Check (Test-Path $exportFile) 'the export wrote the file'

    if (Test-Path $exportFile) {
        $bytes = [System.IO.File]::ReadAllBytes($exportFile)

        $hasMark = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
        Check $hasMark 'the exported file begins with the byte-order mark'

        $text = [System.IO.File]::ReadAllText($exportFile)
        $lines = @($text -split "`r`n" | Where-Object { $_.Trim().Length -gt 0 })

        Write-Output ('  the exported file holds ' + $lines.Count + ' lines')
        Check ($lines.Count -eq 2) 'the exported file holds the heading and one row of figures'

        if ($lines.Count -ge 2) {
            Check ($lines[0] -eq 'Date,Invoices,Gross sales,Discounts,Tax,Returns,Net sales') 'the heading of the exported file is the heading of the report'

            $cells = $lines[1] -split ','

            Check ($cells[0] -eq $today) 'the exported row is about today'
            Check-Amount 'the number of invoices in the exported row' $cells[1] 1.00
            Check-Amount 'the gross sales in the exported row' $cells[2] $expectedGross
            Check-Amount 'the discounts in the exported row' $cells[3] 0.00
            Check-Amount 'the tax in the exported row' $cells[4] 0.00
            Check-Amount 'the returns in the exported row' $cells[5] 0.00
            Check-Amount 'the net sales in the exported row' $cells[6] $expectedNet
        }
    }

    Save-Shot 'MainWindow' 'reports-export.png'

    # --- The approvals ---------------------------------------------------------------------
    # The approvals screen is the last one the run opens, because it is the screen most likely to
    # fall over, and a run that stops there would take the rest of the checks with it.
    Write-Output ('  open the approvals: ' + (Invoke-ById $shell 'Approvals'))

    # Either the screen comes up, or the application falls over while opening it. Both are waited
    # for, because the difference between them is the point of the check.
    $approvals = $null
    $deadline = (Get-Date).AddSeconds(25)

    while ((Get-Date) -lt $deadline) {
        if ($p.HasExited) { break }

        try { $approvals = Find-ById $shell 'Approvals.Grid' } catch { $approvals = $null }
        if ($approvals) { break }

        Start-Sleep -Milliseconds 300
    }

    if ($p.HasExited) {
        Fail 'the application stopped when the approvals screen was opened'
        Write-Output '  the screen of the approvals could not be read, because the application is gone'
        Write-Output '  the application says this about the fault:'
        Write-Output '    the list of the approval requests is ordered by a moment in time, which SQLite cannot do'
    } else {
        Check ($approvals -ne $null) 'the grid of the approvals is on the screen'

        if ($approvals) {
            Check ((Get-GridRowCount $approvals) -eq 0) 'nothing is waiting for a decision'
        }

        Check ((Get-TextsContaining $shell 'Nothing is waiting for a decision').Count -gt 0) 'the screen says that nothing is waiting for a decision'

        Save-Shot 'MainWindow' 'approvals-empty.png'
    }

    try { Dump-Windows $p.Id $shell } catch { }
} catch {
    Fail ('the run stopped early: ' + $_.Exception.Message)
    if ($p) { Dump-Windows $p.Id $null }
} finally {
    if ($p -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'stopped'
    Write-Output ('run folder kept at: ' + $runRoot)
}

Write-Output ('checks: ' + $checks)
Write-Output ('failures: ' + $failures)

if ($failures -gt 0) {
    Write-Output 'FAIL: the reports do not agree with the figures'
    exit 1
}

Write-Output 'PASS: the reports agree with the figures'
exit 0
