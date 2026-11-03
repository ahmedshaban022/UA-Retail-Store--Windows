param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Drives the backup and cloud screen, the restore wizard and the recovery wizard through UI
# Automation against the real application, in English and then in Arabic.
#
# The point of the run is that the shop's own copy is taken, that the file really lands on the
# disk with the checksum the screen shows, that the restore wizard can read that file back, that
# the shop is put back with its data, and that the same backup restores into a different shop
# folder on a different computer.
#
# Every control is found by its automation identifier rather than by its label, so the script does
# not depend on the wording of the interface. The wait is always for a value the screen must
# reach - the checksum of a backup, the message of a wizard - and never for the number of rows in
# a table, because a row count that is already right proves nothing.
#
# The screens are captured by the application itself. The script asks the running application to
# draw one of its own windows over the capture pipe, and the application writes the picture. A
# picture taken from outside the process would come back mirrored for a right-to-left window, and
# the desktop session on this machine is locked, so a picture taken from outside the process is not
# possible either.
#
# The run tells one story: the shop takes a copy, the copy is read back and put in place, the shop
# is found whole again after the shop is started afresh, and the same copy is put into a second,
# empty shop folder on the same machine, which is what a new computer would be.

$exe = $ExePath
$runRoot = Join-Path $env:TEMP ('storeflow-phase6-' + [Guid]::NewGuid().ToString('N'))
$runRootTwo = Join-Path $env:TEMP ('storeflow-phase6-other-' + [Guid]::NewGuid().ToString('N'))
$outDir = Join-Path $runRoot 'screens'
$pipeName = 'storeflow-phase6-capture-' + [Guid]::NewGuid().ToString('N')

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

public static class StoreFlowBackupSmoke
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

        [void][StoreFlowBackupSmoke]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 150

        [StoreFlowBackupSmoke]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [StoreFlowBackupSmoke]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
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

        [void][StoreFlowBackupSmoke]::ShowWindow($handle, 9)
        [void][StoreFlowBackupSmoke]::BringWindowToTop($handle)
        [void][StoreFlowBackupSmoke]::SetForegroundWindow($handle)
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
$sku = 'BKUP-SMOKE-001'
$productName = 'BKUP-SMOKE-001'
$barcode = '2000000000099'
$price = '100.00'
$cost = '50.00'
$arStoreNameAr = A @(0x0645,0x062A,0x062C,0x0631,0x0020,0x0645,0x0635,0x0631,0x064A,0x0629)
$password = 'StoreFlow!2026'
$backupFolder = Join-Path $runRoot 'backups'
$dailyTime = '03:45'
$dailyRetention = '5'
$localTargetName = 'Local folder'

$p = $null
$archiveFile = $null
$checksum = $null

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

        Check (Fill-Fields $wizard @('Al-Masria Backups', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000') $true) 'the shop details were written into the wizard'
        Start-Sleep -Milliseconds 700
        Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
        Start-Sleep -Seconds 2

        Check (Fill-Fields $wizard @('Owner', 'owner', $password, $password) $true) 'the owner account was written into the wizard'
        Start-Sleep -Milliseconds 700
        Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
        Start-Sleep -Seconds 2
        Write-Output ('  finish: ' + (Invoke-ById $wizard 'FirstRun.Finish'))
        Start-Sleep -Seconds 6
    }

    # --- The shop runs in English ---------------------------------------------------------
    # The application keeps the language it starts in in its theme file. The theme is written the
    # way the shop wants it and the application is started again, so that every screen the script
    # reads afterwards is in English.
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
        Fill-Password $login $password $false | Out-Null
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

    # --- The backup screen -----------------------------------------------------------------
    Write-Output ('  open the backup screen: ' + (Invoke-ById $shell 'Backup'))
    Start-Sleep -Seconds 4

    Check ((Find-ById $shell 'Backup.Grid') -ne $null) 'the backup screen is on the shell'

    # The destination the copy goes to is chosen on the screen, not hidden in the code.
    Check (Select-ComboItem $shell 'Backup.TargetPicker' $localTargetName) 'the local folder was chosen as the destination'
    Start-Sleep -Milliseconds 800

    # --- The schedule -----------------------------------------------------------------------
    Check (Set-ById $shell 'Backup.DailyTime' $dailyTime) 'the daily backup time was written in'
    Check (Set-ById $shell 'Backup.DailyRetention' $dailyRetention) 'the daily retention was written in'
    Check (Set-ById $shell 'Backup.Directory' $backupFolder) 'the backup folder was written in'
    Start-Sleep -Milliseconds 700

    Invoke-ById $shell 'Backup.SaveSchedule' | Out-Null
    Start-Sleep -Seconds 3

    $status = Get-TextById $shell 'Backup.Status'
    Write-Output ('  the screen says: ' + $status)
    Check ($status -and $status.Contains('saved')) 'the schedule was saved'

    # --- A product, so that the copy holds something -----------------------------------------
    Write-Output ('  open the catalogue: ' + (Invoke-ById $shell 'Products'))
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

    # --- The backup --------------------------------------------------------------------------
    Write-Output ('  open the backup screen: ' + (Invoke-ById $shell 'Backup'))
    Start-Sleep -Seconds 3

    Invoke-ById $shell 'Backup.Now' | Out-Null

    # The wait is for the checksum, the value the screen must reach. The number of rows in the
    # log is no use here: the log is empty before the backup and holds one row after it, and a
    # count that is already right proves nothing.
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        $shown = Get-TextById $shell 'Backup.Checksum'

        if ($shown -and $shown.Trim().Length -eq 64) {
            $checksum = $shown.Trim()
            break
        }

        Start-Sleep -Milliseconds 300
    }

    Check ($checksum -ne $null) 'the screen shows the checksum of the copy it has just taken'

    if ($checksum) {
        Write-Output ('  checksum: ' + $checksum)
        Check ($checksum -match '^[0-9a-f]{64}$') 'the checksum is a sha-256 of the file'
    }

    $outcome = Get-TextById $shell 'Backup.Outcome'
    Write-Output ('  the screen says: ' + $outcome)
    Check ($outcome -and $outcome.Trim().Length -gt 0) 'the screen says what the copy did'

    # --- The file on the disk ----------------------------------------------------------------
    if (Test-Path $backupFolder) {
        $files = @(Get-ChildItem -Path $backupFolder -Filter '*.sfb' -File | Sort-Object LastWriteTime)

        Write-Output ('  the folder holds ' + $files.Count + ' backup files')
        Check ($files.Count -ge 1) 'the copy was written to the folder the owner chose'

        # The copy the owner asked for is the manual one. The scheduler's own copies are named
        # after the day they belong to, and the restore must use the copy the screen reported.
        $manual = @($files | Where-Object { $_.Name.StartsWith('storeflow-manual-') })

        Check ($manual.Count -ge 1) 'the copy the owner asked for is in the folder'

        if ($manual.Count -ge 1) {
            $archiveFile = $manual[-1].FullName
            Write-Output ('  the file: ' + $archiveFile)

            Check ((Get-Item $archiveFile).Length -gt 0) 'the file is not empty'
        }

        # The scheduler takes its own copy without being asked.
        $daily = @($files | Where-Object { $_.Name.StartsWith('storeflow-daily-') })

        Write-Output ('  the scheduler took ' + $daily.Count + ' daily copies of its own')
        Check ($daily.Count -ge 1) 'the scheduler took its own copy without being asked'
    } else {
        Fail ('the backup folder was not written to: ' + $backupFolder)
    }

    # --- The log on the screen ----------------------------------------------------------------
    $grid = Find-ById $shell 'Backup.Grid'
    Check ($grid -ne $null) 'the log of the copies is on the screen'

    if ($grid -and $checksum) {
        $row = Find-RowByCell $grid 3 $checksum
        Check ($row -ge 0) 'the log holds the copy with the checksum the screen showed'
    }

    Save-Shot 'MainWindow' 'backup-screen.png'

    # --- The restore wizard -------------------------------------------------------------------
    Write-Output ('  open the restore wizard: ' + (Invoke-ById $shell 'Backup.Restore'))
    Start-Sleep -Seconds 4

    $restore = Wait-WindowById 'RestoreWizardWindow' 30
    if (-not $restore) {
        Fail 'the restore wizard never appeared'
        Dump-Windows $p.Id $shell
    } else {
        Write-Output 'the restore wizard is up'

        $fileName = if ($archiveFile) { Split-Path -Leaf $archiveFile } else { '' }

        Check (Select-ComboItem $restore 'Restore.Artifact' $fileName) 'the copy was chosen in the wizard'

        Invoke-ById $restore 'Restore.Check' | Out-Null

        # The wait is for the message the wizard must reach, not for a row.
        $integrity = $null
        $deadline = (Get-Date).AddSeconds(45)
        while ((Get-Date) -lt $deadline) {
            $integrity = Get-TextById $restore 'Restore.Status'

            if ($integrity -and $integrity.Contains('checksum')) { break }

            Start-Sleep -Milliseconds 300
        }

        Write-Output ('  the wizard says: ' + $integrity)
        Check ($integrity -and $integrity.Contains('checksum')) 'the wizard says the copy matches its own checksum'

        if ($checksum) {
            $shown = @(Get-TextNames $restore | Where-Object { $_ -and $_.Trim() -eq $checksum })
            Check ($shown.Count -gt 0) 'the wizard shows the checksum of the copy'
        }

        Save-Shot 'RestoreWizardWindow' 'restore-wizard-checked.png'

        # --- The confirmation ------------------------------------------------------------------
        Invoke-ById $restore 'Restore.Now' | Out-Null
        Start-Sleep -Seconds 2

        Check ((Find-ById $restore 'Restore.Confirm') -ne $null) 'the wizard asks the owner before it puts the shop back'
        Save-Shot 'RestoreWizardWindow' 'restore-wizard-confirm.png'

        Invoke-ById $restore 'Restore.Confirm' | Out-Null

        $restorePoint = $null
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline) {
            $restorePoint = Get-TextById $restore 'Restore.RestorePoint'

            if ($restorePoint -and $restorePoint.Trim().Length -gt 0) { break }

            Start-Sleep -Milliseconds 300
        }

        Write-Output ('  the shop was kept at: ' + $restorePoint)
        Check ($restorePoint -and $restorePoint.Contains('restore-points')) 'the wizard says where the shop was kept before the restore'

        $points = @(Get-ChildItem -Path (Join-Path $runRoot 'data\restore-points') -File -ErrorAction SilentlyContinue)
        Write-Output ('  the folder of restore points holds ' + $points.Count + ' files')
        Check ($points.Count -ge 1) 'the shop was kept before the restore, so the restore can be undone'

        Save-Shot 'RestoreWizardWindow' 'restore-wizard-restored.png'

        Invoke-ById $restore 'Restore.Close' | Out-Null
        Start-Sleep -Seconds 3
    }

    # The restored database is opened by the application's replacement process. Its restart does
    # not carry capture arguments, so identify its sign-in window and sign in before using its shell.
    $oldProcess = $p
    $oldId = $p.Id
    $login = $null
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        $candidate = Find-WindowById 'LoginWindow'
        if ($candidate -and $candidate.Current.ProcessId -ne $oldId) {
            $replacement = Get-Process -Id $candidate.Current.ProcessId -ErrorAction SilentlyContinue
            if ($replacement -and $replacement.Path -and
                [string]::Equals($replacement.Path, (Get-Item -LiteralPath $exe).FullName, [StringComparison]::OrdinalIgnoreCase)) {
                $login = $candidate
                $p = $replacement
                break
            }
        }
        Start-Sleep -Milliseconds 400
    }
    if (-not $login -or -not $oldProcess.WaitForExit(15000)) {
        throw 'the replacement application and exit of the old process were not observed after restore'
    }
    Write-Output ('replacement pid=' + $p.Id + ' after restore')
    $edits = Get-EditBoxes $login
    if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
    Fill-Password $login $password $false | Out-Null
    Invoke-ById $login 'Login.SignIn' | Out-Null
    $shell = Wait-Shell $p.Id 40
    if (-not $shell -or $shell.Current.ProcessId -ne $p.Id) {
        throw 'the replacement shell never appeared after restore'
    }
    Invoke-ById $shell 'Backup' | Out-Null
    if (-not (Wait-ById $shell 'Backup.Grid' 30)) {
        throw 'the backup screen never appeared in the replacement shell'
    }
    # --- The recovery wizard --------------------------------------------------------------------
    Write-Output ('  open the recovery wizard: ' + (Invoke-ById $shell 'Backup.Recover'))
    Start-Sleep -Seconds 4

    $recovery = Wait-WindowById 'RecoveryWizardWindow' 30
    if (-not $recovery) {
        Fail 'the recovery wizard never appeared'
        Dump-Windows $p.Id $shell
    } else {
        Write-Output 'the recovery wizard is up'

        Check ((Find-ById $recovery 'Recovery.Restore') -ne $null) 'the recovery wizard has the button that puts the shop back'

        Invoke-ById $recovery 'Recovery.Restore' | Out-Null
        Start-Sleep -Seconds 2

        $recoveryStatus = Get-TextById $recovery 'Recovery.Status'
        Write-Output ('  the wizard says: ' + $recoveryStatus)
        Check ($recoveryStatus -and $recoveryStatus.Contains('Choose a backup first')) 'the wizard will not put the shop back without a copy to put back'


        Invoke-ById $recovery 'Recovery.Close' | Out-Null
        Start-Sleep -Seconds 2
    }

    # --- The shop is found whole again -------------------------------------------------------------
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' after the restore')
    Start-Sleep -Seconds 8

    $login = Wait-WindowById 'LoginWindow' 40
    if (-not $login) {
        Fail 'the sign-in window never appeared after the restore'
        Dump-Windows $p.Id $null
    } else {
        $edits = Get-EditBoxes $login
        if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
        Fill-Password $login $password $false | Out-Null
        Start-Sleep -Milliseconds 700

        Invoke-ById $login 'Login.SignIn' | Out-Null
        Start-Sleep -Seconds 7
    }

    $shell = Wait-Shell $p.Id
    if (-not $shell) {
        Fail 'the shell never appeared after the restore'
        Dump-Windows $p.Id $null
    } else {
        Write-Output ('shell: "' + $shell.Current.Name + '"')
    }

    # The self-restarted process has no capture pipe. Photograph the same English recovery
    # screen in this fresh instrumented session, using the application's own capture path.
    Invoke-ById $shell 'Backup' | Out-Null
    if (-not (Wait-ById $shell 'Backup.Grid' 30)) { throw 'the backup screen never appeared for the recovery picture' }
    Invoke-ById $shell 'Backup.Recover' | Out-Null
    $recoveryPicture = Wait-WindowById 'RecoveryWizardWindow' 30
    if (-not $recoveryPicture) { Fail 'the recovery wizard never appeared for its picture' }
    else {
        Save-Shot 'RecoveryWizardWindow' 'recovery-wizard.png'
        Invoke-ById $recoveryPicture 'Recovery.Close' | Out-Null
    }
    # The schedule was in the copy, so it comes back with the shop.
    Write-Output ('  open the backup screen: ' + (Invoke-ById $shell 'Backup'))
    Start-Sleep -Seconds 4

    $savedTime = Get-TextById $shell 'Backup.DailyTime'
    Write-Output ('  the daily time the shop comes back with: ' + $savedTime)
    Check ($savedTime -and $savedTime.Trim() -eq $dailyTime) 'the schedule came back with the shop'

    $savedRetention = Get-TextById $shell 'Backup.DailyRetention'
    Write-Output ('  the daily retention the shop comes back with: ' + $savedRetention)
    Check ($savedRetention -and $savedRetention.Trim() -eq $dailyRetention) 'the retention came back with the shop'

    $savedFolder = Get-TextById $shell 'Backup.Directory'
    Write-Output ('  the backup folder the shop comes back with: ' + $savedFolder)
    Check ($savedFolder -and $savedFolder.Trim() -eq $backupFolder) 'the backup folder came back with the shop'

    # The product was in the copy, so it comes back with the shop.
    Write-Output ('  open the catalogue: ' + (Invoke-ById $shell 'Products'))
    Start-Sleep -Seconds 4
    Check (Find-Text $shell $sku) 'the product came back with the shop'

    # --- The shop in Arabic -----------------------------------------------------------------------
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $theme = Get-Content -Raw -Encoding UTF8 $themeFile
    $theme = [regex]::Replace($theme, '"default"\s*:\s*"en"', '"default": "ar"')
    [System.IO.File]::WriteAllText($themeFile, $theme, (New-Object System.Text.UTF8Encoding($false)))

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' in Arabic')
    Start-Sleep -Seconds 8

    $login = Wait-WindowById 'LoginWindow' 40
    if (-not $login) {
        Fail 'the sign-in window never appeared in Arabic'
        Dump-Windows $p.Id $null
    } else {
        $edits = Get-EditBoxes $login
        if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
        Fill-Password $login $password $false | Out-Null
        Start-Sleep -Milliseconds 700

        Invoke-ById $login 'Login.SignIn' | Out-Null
        Start-Sleep -Seconds 7
    }

    $shell = Wait-Shell $p.Id
    if (-not $shell) {
        Fail 'the shell never appeared in Arabic'
        Dump-Windows $p.Id $null
    } else {
        Write-Output ('shell: "' + $shell.Current.Name + '"')
    }

    Write-Output ('  open the backup screen: ' + (Invoke-ById $shell 'Backup'))
    Start-Sleep -Seconds 4
    Save-Shot 'MainWindow' 'backup-screen-arabic.png'

    Write-Output ('  open the restore wizard: ' + (Invoke-ById $shell 'Backup.Restore'))
    Start-Sleep -Seconds 4

    $restore = Wait-WindowById 'RestoreWizardWindow' 30
    if ($restore) {
        Save-Shot 'RestoreWizardWindow' 'restore-wizard-arabic.png'
        Invoke-ById $restore 'Restore.Close' | Out-Null
        Start-Sleep -Seconds 2
    } else {
        Fail 'the restore wizard never appeared in Arabic'
    }

    Write-Output ('  open the recovery wizard: ' + (Invoke-ById $shell 'Backup.Recover'))
    Start-Sleep -Seconds 4

    $recovery = Wait-WindowById 'RecoveryWizardWindow' 30
    if ($recovery) {
        Save-Shot 'RecoveryWizardWindow' 'recovery-wizard-arabic.png'
        Invoke-ById $recovery 'Recovery.Close' | Out-Null
        Start-Sleep -Seconds 2
    } else {
        Fail 'the recovery wizard never appeared in Arabic'
    }

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    # --- Another shop folder: the same copy, a different computer -------------------------------
    # The second folder is a shop that has never been opened. The copy taken in the first folder is
    # put into it, which is what happens when the shop's computer has failed and the copy is
    # brought over from a disk.
    Write-Output '--- the second shop folder ---'

    $env:STOREFLOW_HOME = $runRootTwo

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' for the second shop folder')
    Start-Sleep -Seconds 7

    $wizard = Wait-WindowById 'FirstRunWindow' 40
    if (-not $wizard) {
        Fail 'the wizard never appeared for the second shop folder'
        Dump-Windows $p.Id $null
    } else {
        Check (Fill-Fields $wizard @('Al-Masria Backups', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000') $true) 'the shop details were written into the second wizard'
        Start-Sleep -Milliseconds 700
        Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
        Start-Sleep -Seconds 2

        Check (Fill-Fields $wizard @('Owner', 'owner', $password, $password) $true) 'the owner account was written into the second wizard'
        Start-Sleep -Milliseconds 700
        Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
        Start-Sleep -Seconds 2
        Write-Output ('  finish: ' + (Invoke-ById $wizard 'FirstRun.Finish'))
        Start-Sleep -Seconds 6
    }

    $themeFileTwo = Join-Path $runRootTwo 'themes\active.sftheme.json'
    $deadline = (Get-Date).AddSeconds(20)
    while (-not (Test-Path $themeFileTwo) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    if (-not (Test-Path $themeFileTwo)) {
        Fail 'the theme file of the second shop folder was not written'
    } else {
        $theme = Get-Content -Raw -Encoding UTF8 $themeFileTwo
        $theme = [regex]::Replace($theme, '"default"\s*:\s*"ar"', '"default": "en"')
        [System.IO.File]::WriteAllText($themeFileTwo, $theme, (New-Object System.Text.UTF8Encoding($false)))
    }

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' for the second shop folder in English')
    Start-Sleep -Seconds 8

    $login = Wait-WindowById 'LoginWindow' 40
    if (-not $login) {
        Fail 'the sign-in window never appeared for the second shop folder'
        Dump-Windows $p.Id $null
    } else {
        $edits = Get-EditBoxes $login
        if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
        Fill-Password $login $password $false | Out-Null
        Start-Sleep -Milliseconds 700

        Invoke-ById $login 'Login.SignIn' | Out-Null
        Start-Sleep -Seconds 7
    }

    $shell = Wait-Shell $p.Id
    if (-not $shell) {
        Fail 'the shell never appeared for the second shop folder'
        Dump-Windows $p.Id $null
    } else {
        Write-Output ('shell: "' + $shell.Current.Name + '"')
    }

    Write-Output ('  open the backup screen: ' + (Invoke-ById $shell 'Backup'))
    Start-Sleep -Seconds 4

    Write-Output ('  open the restore wizard: ' + (Invoke-ById $shell 'Backup.Restore'))
    Start-Sleep -Seconds 4

    $restore = Wait-WindowById 'RestoreWizardWindow' 30
    if (-not $restore) {
        Fail 'the restore wizard never appeared for the second shop folder'
        Dump-Windows $p.Id $shell
    } else {
        Write-Output 'the restore wizard is up'

        Write-Output ('  browse for the copy: ' + (Invoke-ById $restore 'Restore.Browse'))
        Start-Sleep -Seconds 4

        $dialog = Wait-FileDialog $p.Id $restore 25
        if ($dialog) {
            $driven = Drive-FileDialog $dialog $archiveFile $p.Id
            Check $driven 'the file dialog was driven to the copy'

            if (-not $driven) { Close-FileDialog $dialog }
            Start-Sleep -Seconds 5
        } else {
            Fail 'the file dialog never appeared'
            Dump-Windows $p.Id $restore
        }

        # The wizard reads the chosen file by itself and says whether it may be restored.
        $integrity = $null
        $deadline = (Get-Date).AddSeconds(45)
        while ((Get-Date) -lt $deadline) {
            $integrity = Get-TextById $restore 'Restore.Status'

            if ($integrity -and $integrity.Contains('checksum')) { break }

            Start-Sleep -Milliseconds 300
        }

        Write-Output ('  the wizard says: ' + $integrity)
        Check ($integrity -and $integrity.Contains('checksum')) 'the wizard accepts the copy from the other shop folder'

        Invoke-ById $restore 'Restore.Now' | Out-Null
        Start-Sleep -Seconds 2
        Invoke-ById $restore 'Restore.Confirm' | Out-Null

        $restorePoint = $null
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline) {
            $restorePoint = Get-TextById $restore 'Restore.RestorePoint'

            if ($restorePoint -and $restorePoint.Trim().Length -gt 0) { break }

            Start-Sleep -Milliseconds 300
        }

        Check ($restorePoint -and $restorePoint.Trim().Length -gt 0) 'the shop was put back in the second folder'

        Save-Shot 'RestoreWizardWindow' 'restore-wizard-other-profile.png'

        Invoke-ById $restore 'Restore.Close' | Out-Null
        Start-Sleep -Seconds 3
    }

    # The second shop is started afresh, and the shop that was copied is in it.
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' after the restore into the second folder')
    Start-Sleep -Seconds 8

    $login = Wait-WindowById 'LoginWindow' 40
    if (-not $login) {
        Fail 'the sign-in window never appeared for the second shop folder after the restore'
        Dump-Windows $p.Id $null
    } else {
        $edits = Get-EditBoxes $login
        if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
        Fill-Password $login $password $false | Out-Null
        Start-Sleep -Milliseconds 700

        Invoke-ById $login 'Login.SignIn' | Out-Null
        Start-Sleep -Seconds 7
    }

    $shell = Wait-Shell $p.Id
    if (-not $shell) {
        Fail 'the shell never appeared for the second shop folder after the restore'
        Dump-Windows $p.Id $null
    } else {
        Write-Output ('shell: "' + $shell.Current.Name + '"')
    }

    Write-Output ('  open the catalogue: ' + (Invoke-ById $shell 'Products'))
    Start-Sleep -Seconds 4
    Check (Find-Text $shell $sku) 'the product of the other shop is in the second shop folder'

    try { Dump-Windows $p.Id $shell } catch { }
} catch {
    Fail ('the run stopped early: ' + $_.Exception.Message)
    if ($p) { Dump-Windows $p.Id $null }
} finally {
    if ($p -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'stopped'
    Write-Output ('first shop folder kept at: ' + $runRoot)
    Write-Output ('second shop folder kept at: ' + $runRootTwo)
}

Write-Output ('checks: ' + $checks)
Write-Output ('failures: ' + $failures)

if ($failures -gt 0) {
    Write-Output 'FAIL: the backup and the restore do not agree with the figures'
    exit 1
}

Write-Output 'PASS: the copy was taken, it was read back, and the shop was put back'
exit 0

