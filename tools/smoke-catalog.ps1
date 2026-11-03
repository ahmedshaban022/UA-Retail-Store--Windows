param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Drives the catalogue screens through UI Automation against the real application, in Arabic and then
# in English. The application is pointed at a throwaway data folder, so the shop's own data is never
# touched.
#
# Every control is found by its automation identifier rather than by its label, so the script does
# not depend on the wording of the interface in either language.
#
# The screens are captured by asking each window to draw itself, rather than by photographing the
# screen. That keeps the evidence faithful when the desktop is locked or another window is in front.

$exe = $ExePath
$outDir = 'C:\Users\ahmed\.minimax\v2\sessions\2026\10\27\14-05-08-826-session_bXZzXzU4Yjk5ODYyYzUwMDQxNGJiODIwNjY0MDc0ZTk2ZTU0\artifacts\phase1'
$runRoot = Join-Path $env:TEMP ('storeflow-phase1-' + [Guid]::NewGuid().ToString('N'))

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

Write-Output ("run folder: " + $runRoot)
Write-Output ("shots folder: " + $outDir)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StoreFlowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    public const uint PW_RENDERFULLCONTENT = 2;
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

    // The controls of the dialog worth naming: the file name box and the buttons.
    public static List<string> Describe(IntPtr dialog) {
        var lines = new List<string>();
        EnumChildWindows(dialog, delegate(IntPtr h, IntPtr l) {
            string cls = ClassOf(h);
            if (cls == "Edit" || cls == "ComboBox" || cls == "ComboBoxEx32" || cls == "Button") {
                lines.Add("id=" + GetDlgCtrlID(h) + " class=" + cls + " text=[" + TextOf(h) + "]");
            }
            return true;
        }, IntPtr.Zero);
        return lines;
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
        return edit == IntPtr.Zero ? "" : TextOf(edit);
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
    $AE::RootElement.FindFirst($TS::Children, $cond)
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

# The shell is the one window of the application that is wider than a dialog.
function Wait-Shell([int]$processId, [int]$seconds = 30) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) {
            try {
                if ($w.Current.BoundingRectangle.Width -gt 600) { return $w }
            } catch { }
        }
        Start-Sleep -Milliseconds 700
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

# Captures a window by asking it to draw itself. A right-to-left window comes out mirrored, so the
# capture is flipped back and both versions are written out. A window that reads left to right comes
# out as it is, and flipping it would be a lie about the screen.
function Save-WindowShot($element, [string]$fileName, [bool]$rightToLeft = $true) {
    if (-not $element) { Write-Output ('  no window for ' + $fileName); return }

    $handle = [IntPtr]$element.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { Write-Output ('  no handle for ' + $fileName); return }

    $rect = New-Object StoreFlowCapture+RECT
    if (-not [StoreFlowCapture]::GetWindowRect($handle, [ref]$rect)) {
        Write-Output ('  no rectangle for ' + $fileName)
        return
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { Write-Output ('  empty rectangle for ' + $fileName); return }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [StoreFlowCapture]::PrintWindow($handle, $hdc, [StoreFlowCapture]::PW_RENDERFULLCONTENT)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()

    $path = Join-Path $outDir $fileName
    $bitmap.Save([System.IO.Path]::ChangeExtension($path, '.raw.png'), [System.Drawing.Imaging.ImageFormat]::Png)

    if ($rightToLeft) {
        $flipped = New-Object System.Drawing.Bitmap $width, $height
        $g2 = [System.Drawing.Graphics]::FromImage($flipped)
        $g2.TranslateTransform($width, 0)
        $g2.ScaleTransform(-1, 1)
        $g2.DrawImage($bitmap, 0, 0)
        $g2.Dispose()
        $flipped.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $flipped.Dispose()
    } else {
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bitmap.Dispose()

    Write-Output ('  screenshot -> ' + $fileName + ' (drawn=' + $ok + ', ' + $width + 'x' + $height + ', flipped=' + $rightToLeft + ')')
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

# The file dialog belongs to Windows, so it is driven by the identifiers Windows gives it: the file
# name box is the edit "1001" inside the combo "1148", and the button that accepts the name is "1".
# Every note printed from these functions goes to the host and never to the pipeline, so the function
# can return a plain yes or no. (A note in the pipeline turns the answer into an array, and the check
# that reads it then refuses to bind an array to a yes or no.)
# The name the dialog's file name box holds, read from the box itself rather than from the
# accessibility tree, which shows the address bar of the modern dialog under the same identifier.
function Get-FileNameText($handle) {
    return [StoreFlowDialog]::FileName($handle)
}

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
        Write-Host ('    a candidate window: 0x' + $candidate.ToString('X'))
        if ([StoreFlowDialog]::HasFileNameBox($candidate)) { $handle = $candidate; break }
    }

    if ($handle -eq [IntPtr]::Zero) {
        Write-Host '    the file dialog has no window with a file name box'
        return $false
    }

    foreach ($line in [StoreFlowDialog]::Describe($handle)) { Write-Host ('      the dialog offers: ' + $line) }

    if (-not [StoreFlowDialog]::TypeFileName($handle, $path)) {
        Write-Host '    the path did not land in the file name box'
        return $false
    }
    Write-Host ('    the file name box holds: [' + (Get-FileNameText $handle) + ']')

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
    $processCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $windowCond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    Write-Output '  --- the windows of the application right now ---'
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $processCond)) {
        Write-Output ('    root: [' + $w.Current.Name + '] class=' + $w.Current.ClassName + ' id=' + $w.Current.AutomationId)
    }
    if ($shell) {
        foreach ($w in $shell.FindAll($TS::Subtree, $windowCond)) {
            Write-Output ('    inside the shell: [' + $w.Current.Name + '] class=' + $w.Current.ClassName + ' id=' + $w.Current.AutomationId)
        }
    }
}

$arStoreNameAr = A @(0x0645,0x062A,0x062C,0x0631,0x0020,0x0645,0x0635,0x0631,0x064A,0x0629)

# --- The file the import reads -------------------------------------------------
$importCsv = Join-Path $runRoot 'import.csv'
$oilAr = A @(0x0632,0x064A,0x062A,0x0020,0x0632,0x064A,0x062A,0x0648,0x0646,0x0020,0x0031,0x0020,0x0644,0x062A,0x0631)
$teaAr = A @(0x0634,0x0627,0x064A,0x0020,0x0623,0x062E,0x0636,0x0631)
# The rows are built one at a time. Inside a comma-separated array literal PowerShell binds the
# comma tighter than the plus sign, so 'a' + $name + 'c' would arrive as three separate elements
# and the Arabic name would end up on a line of its own.
$rows = @('sku,name,namear,category,unit,saleprice,costprice,reorderlevel,barcode,isactive')
$rows += 'OIL-001,Olive Oil 1L,' + $oilAr + ',Grains,Box,320.00,260.00,5,6221000000024,true'
$rows += 'TEA-001,Green Tea 250g,' + $teaAr + ',Grains,,180.00,120.00,3,6221000000031,true'
$rows += 'BAD-001,Broken Row,,Grains,Box,-5.00,0,0,6221000000048,true'
$csvText = $rows -join "`n"
[System.IO.File]::WriteAllText($importCsv, $csvText, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ('  the import file has ' + ((Get-Content $importCsv).Count) + ' lines')

$p = Start-Process -FilePath $exe -PassThru
Write-Output ('launched pid=' + $p.Id)
Start-Sleep -Seconds 7

# --- The first-run wizard -----------------------------------------------------
$wizard = Wait-Window $p.Id (A @(0x0625,0x0639,0x062F,0x0627,0x062F,0x0020,0x0633,0x062A,0x0648,0x0631,0x0020,0x0641,0x0644,0x0648)) 30
if (-not $wizard) { Write-Output 'FAIL: the wizard never appeared'; Stop-Process -Id $p.Id -Force; exit 1 }
Write-Output ('wizard: "' + $wizard.Current.Name + '" is up')
Save-WindowShot $wizard 'phase1-01-wizard-arabic.png'

$edits = Get-VisibleEdits $wizard
Write-Output ('  step 1 text fields = ' + $edits.Count)
$values = @('Al-Masria Store', $arStoreNameAr, '12 Tahrir Street, Cairo', '+20 100 000 0000')
for ($i = 0; $i -lt [Math]::Min($edits.Count, $values.Count); $i++) { Set-EditValue $edits[$i] $values[$i] }
Start-Sleep -Milliseconds 700
Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
Start-Sleep -Seconds 2

$edits = Get-VisibleEdits $wizard
$passwords = Get-PasswordEdits $wizard
Write-Output ('  step 2 text fields = ' + $edits.Count + ', password fields = ' + $passwords.Count)
if ($edits.Count -ge 2) { Set-EditValue $edits[0] 'Owner'; Set-EditValue $edits[1] 'owner' }
if ($passwords.Count -ge 2) { Set-EditValue $passwords[0] 'StoreFlow!2026'; Set-EditValue $passwords[1] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 700
Write-Output ('  next: ' + (Invoke-ById $wizard 'FirstRun.Next'))
Start-Sleep -Seconds 2
Write-Output ('  finish: ' + (Invoke-ById $wizard 'FirstRun.Finish'))
Start-Sleep -Seconds 6

$afterFinish = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
Write-Output ('  the application is still running: ' + (-not $p.HasExited))
foreach ($w in $AE::RootElement.FindAll($TS::Children, $afterFinish)) {
    Write-Output ('  window after the wizard: [' + $w.Current.Name + '] class=' + $w.Current.ClassName + ' rect=' + $w.Current.BoundingRectangle)
}

# --- Sign in -----------------------------------------------------------------
$login = Wait-Window $p.Id (A @(0x062A,0x0633,0x062C,0x064A,0x0644,0x0020,0x0627,0x0644,0x062F,0x062E,0x0648,0x0644)) 30
if (-not $login) {
    Write-Output 'FAIL: the sign-in window never appeared'
    $processCond0 = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $processCond0)) {
        Write-Output ('  window: [' + $w.Current.Name + '] class=' + $w.Current.ClassName + ' rect=' + $w.Current.BoundingRectangle)
    }
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output ('login: "' + $login.Current.Name + '" is up')
Save-WindowShot $login 'phase1-02-login-arabic.png'

$edits = Get-VisibleEdits $login
$passwords = Get-PasswordEdits $login
if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' }
if ($passwords.Count -ge 1) { Set-EditValue $passwords[0] 'StoreFlow!2026' }
Start-Sleep -Milliseconds 700
Write-Output ('  sign in: ' + (Invoke-ById $login 'Login.SignIn'))
Start-Sleep -Seconds 7

# --- The shell ---------------------------------------------------------------
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$shell = $null
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    foreach ($w in $AE::RootElement.FindAll($TS::Children, $cond)) {
        Write-Output ('  window: [' + $w.Current.Name + '] offscreen=' + $w.Current.IsOffscreen + ' width=' + [Math]::Round($w.Current.BoundingRectangle.Width))
        if ($w.Current.BoundingRectangle.Width -gt 600) { $shell = $w; break }
    }
    if ($shell) { break }
    Start-Sleep -Milliseconds 700
}
if (-not $shell) { Write-Output 'FAIL: no shell window'; Stop-Process -Id $p.Id -Force; exit 1 }
Write-Output ('shell: "' + $shell.Current.Name + '"')

$railButton = Find-ById $shell 'Products'
if ($railButton) {
    $railRect = $railButton.Current.BoundingRectangle
    $shellRect = $shell.Current.BoundingRectangle
    Write-Output ('  rail button x=' + [Math]::Round($railRect.Left) + ' shell x=' + [Math]::Round($shellRect.Left) + ' width=' + [Math]::Round($shellRect.Width))
    if ($railRect.Left -lt $shellRect.Left + $shellRect.Width / 2) {
        Fail 'the navigation rail is not on the right in Arabic'
    } else {
        Write-Output '  ok   the navigation rail is on the right: the interface reads right to left'
    }
} else {
    Fail 'the products entry is not in the navigation rail'
}

Save-WindowShot $shell 'phase1-03-shell-arabic.png'

# --- The catalogue -----------------------------------------------------------
Write-Output ('  open products: ' + (Invoke-ById $shell 'Products'))
Start-Sleep -Seconds 3
Save-WindowShot $shell 'phase1-04-products-empty-arabic.png'
Check ((Find-ById $shell 'Products.Grid') -ne $null) 'the products grid is on the page'

Write-Output ('  new product: ' + (Invoke-ById $shell 'Products.New'))
Start-Sleep -Seconds 3

$editor = Wait-ById $shell 'ProductEditorWindow' 20
if (-not $editor) {
    Fail 'the product editor never appeared'
    Stop-Process -Id $p.Id -Force
    exit 1
}
Write-Output '  the product editor is up'
Save-WindowShot $editor 'phase1-05-product-editor-arabic.png'

$filled = @(
    @('ProductEditor.Sku', 'RICE-001'),
    @('ProductEditor.Name', 'Rice 1kg'),
    @('ProductEditor.NameAr', (A @(0x0623,0x0631,0x0632,0x0020,0x0643,0x064A,0x0644,0x0648,0x062C,0x0631,0x0645))),
    @('ProductEditor.SalePrice', '450.00'),
    @('ProductEditor.CostPrice', '400.00'),
    @('ProductEditor.ReorderLevel', '5'),
    @('ProductEditor.Barcode', '6221000000017')
)
foreach ($pair in $filled) {
    Check (Set-ById $editor $pair[0] $pair[1]) ('the field ' + $pair[0] + ' took its value')
}
Start-Sleep -Milliseconds 800
Save-WindowShot $editor 'phase1-06-product-editor-filled-arabic.png'

Write-Output ('  save: ' + (Invoke-ById $editor 'ProductEditor.Save'))
Start-Sleep -Seconds 4

Check (Wait-Gone $shell 'ProductEditorWindow' 15) 'the editor closed after saving'
Save-WindowShot $shell 'phase1-07-products-one-row-arabic.png'

$found = Get-TextsContaining $shell 'RICE-001'
Check ($found.Count -gt 0) 'the saved product is in the list'

$taxCombo = Find-ById $editor 'ProductEditor.TaxCategory'
if ($taxCombo) {
    $shown = Get-TextById $editor 'ProductEditor.TaxCategory'
    Write-Output ('  the tax picker shows: [' + $shown + ']')
    if ($shown -and $shown.Contains('{')) { Fail 'the tax picker shows the record instead of the name' }
    else { Write-Output '  ok   the tax picker shows the name of the tax category' }
}

# --- A second product with the same barcode ----------------------------------
Write-Output ('  new product again: ' + (Invoke-ById $shell 'Products.New'))
Start-Sleep -Seconds 3
$editor = Wait-ById $shell 'ProductEditorWindow' 20
if ($editor) {
    Set-ById $editor 'ProductEditor.Sku' 'RICE-002' | Out-Null
    Set-ById $editor 'ProductEditor.Name' 'Rice 2kg' | Out-Null
    Set-ById $editor 'ProductEditor.SalePrice' '460.00' | Out-Null
    Set-ById $editor 'ProductEditor.CostPrice' '410.00' | Out-Null
    Set-ById $editor 'ProductEditor.Barcode' '6221000000017' | Out-Null
    Start-Sleep -Milliseconds 600
    Write-Output ('  save: ' + (Invoke-ById $editor 'ProductEditor.Save'))
    Start-Sleep -Seconds 3

    $barcodeError = Get-NameById $editor 'ProductEditor.BarcodeError'
    Write-Output ('  the barcode error says: [' + $barcodeError + ']')
    Check (-not [string]::IsNullOrWhiteSpace($barcodeError)) 'the duplicate barcode is refused and reported'
    Check ((Find-ById $shell 'ProductEditorWindow') -ne $null) 'the editor stays open when the barcode is refused'
    Save-WindowShot $editor 'phase1-08-duplicate-barcode-arabic.png'

    Set-ById $editor 'ProductEditor.Sku' 'RICE-001' | Out-Null
    Set-ById $editor 'ProductEditor.Barcode' '6221000000099' | Out-Null
    Start-Sleep -Milliseconds 400
    Invoke-ById $editor 'ProductEditor.Save' | Out-Null
    Start-Sleep -Seconds 3

    $skuError = Get-NameById $editor 'ProductEditor.SkuError'
    Write-Output ('  the code error says: [' + $skuError + ']')
    Check (-not [string]::IsNullOrWhiteSpace($skuError)) 'the duplicate stock-keeping unit code is refused and reported'
    Save-WindowShot $editor 'phase1-09-duplicate-code-arabic.png'

    Write-Output ('  cancel: ' + (Invoke-ById $editor 'ProductEditor.Cancel'))
    Start-Sleep -Seconds 2
}

# --- The reference screens ----------------------------------------------------
Write-Output ('  open categories: ' + (Invoke-ById $shell 'Categories'))
Start-Sleep -Seconds 3
Save-WindowShot $shell 'phase1-10-categories-arabic.png'

Write-Output ('  new category: ' + (Invoke-ById $shell 'Categories.New'))
Start-Sleep -Seconds 3
$reference = Wait-ById $shell 'ReferenceEditorWindow' 20
if ($reference) {
    Set-ById $reference 'ReferenceEditor.name' 'Grains' | Out-Null
    Set-ById $reference 'ReferenceEditor.nameAr' (A @(0x062D,0x0628,0x0648,0x0628)) | Out-Null
    Start-Sleep -Milliseconds 500
    Save-WindowShot $reference 'phase1-11-category-editor-arabic.png'
    Write-Output ('  save: ' + (Invoke-ById $reference 'ReferenceEditor.Save'))
    Start-Sleep -Seconds 3

    Check (Wait-Gone $shell 'ReferenceEditorWindow' 15) 'the category editor closed after saving'
    Check ((Get-TextsContaining $shell 'Grains').Count -gt 0) 'the new category is in the list'
} else {
    Fail 'the category editor never appeared'
}
Save-WindowShot $shell 'phase1-12-categories-one-row-arabic.png'

Write-Output ('  open units: ' + (Invoke-ById $shell 'Units'))
Start-Sleep -Seconds 3
Write-Output ('  new unit: ' + (Invoke-ById $shell 'Units.New'))
Start-Sleep -Seconds 3
$reference = Wait-ById $shell 'ReferenceEditorWindow' 20
if ($reference) {
    Set-ById $reference 'ReferenceEditor.name' 'Box' | Out-Null
    Set-ById $reference 'ReferenceEditor.nameAr' (A @(0x0639,0x0644,0x0628,0x0629)) | Out-Null
    Start-Sleep -Milliseconds 500
    Write-Output ('  save: ' + (Invoke-ById $reference 'ReferenceEditor.Save'))
    Start-Sleep -Seconds 3
    Check (Wait-Gone $shell 'ReferenceEditorWindow' 15) 'the unit editor closed after saving'
    Check ((Get-TextsContaining $shell 'Box').Count -gt 0) 'the new unit is in the list'
} else {
    Fail 'the unit editor never appeared'
}
Save-WindowShot $shell 'phase1-13-units-arabic.png'

Write-Output ('  open suppliers: ' + (Invoke-ById $shell 'Suppliers'))
Start-Sleep -Seconds 3
Write-Output ('  new supplier: ' + (Invoke-ById $shell 'Suppliers.New'))
Start-Sleep -Seconds 3
$reference = Wait-ById $shell 'ReferenceEditorWindow' 20
if ($reference) {
    Set-ById $reference 'ReferenceEditor.name' 'Delta Mills' | Out-Null
    Set-ById $reference 'ReferenceEditor.nameAr' (A @(0x062F,0x0644,0x062A,0x0627,0x0020,0x0645,0x0644,0x0627,0x064A,0x0629)) | Out-Null
    Set-ById $reference 'ReferenceEditor.phone' '+20 122 111 2233' | Out-Null
    Start-Sleep -Milliseconds 500
    Write-Output ('  save: ' + (Invoke-ById $reference 'ReferenceEditor.Save'))
    Start-Sleep -Seconds 3
    Check (Wait-Gone $shell 'ReferenceEditorWindow' 15) 'the supplier editor closed after saving'
    Check ((Get-TextsContaining $shell 'Delta Mills').Count -gt 0) 'the new supplier is in the list'
} else {
    Fail 'the supplier editor never appeared'
}
Save-WindowShot $shell 'phase1-14-suppliers-arabic.png'

# --- The audit trail ---------------------------------------------------------
Write-Output ('  open the audit trail: ' + (Invoke-ById $shell 'Audit'))
Start-Sleep -Seconds 3
Check ((Get-TextsContaining $shell (A @(0x0645,0x0644,0x062E,0x0635))).Count -gt 0) 'the audit trail is in Arabic'
$typeChoice = Get-TextById $shell 'Audit.EntityType'
$recordShown = Get-TextsContaining $shell 'AuditEntityTypeOption'
if ($recordShown.Count -gt 0) {
    Fail ('the audit trail filter shows the record instead of the name: ' + ($recordShown -join ' | '))
} else {
    Write-Output '  ok   the audit trail filter shows the name of the choice, not the record'
}
$auditRows = Get-TextsContaining $shell 'Product'
Write-Output ('  the trail mentions the product: ' + ($auditRows.Count -gt 0))
Check ($auditRows.Count -gt 0) 'the audit trail records the catalogue changes'
Save-WindowShot $shell 'phase1-15-audit-arabic.png'

# --- The search --------------------------------------------------------------
Write-Output ('  open products: ' + (Invoke-ById $shell 'Products'))
Start-Sleep -Seconds 3
Set-ById $shell 'Products.Search' 'RICE' | Out-Null
Start-Sleep -Seconds 2
Check ((Get-TextsContaining $shell 'RICE-001').Count -gt 0) 'the search finds the product by its code'
Check ((Get-TextsContaining $shell 'OIL-001').Count -eq 0) 'the search leaves out what does not match'
Save-WindowShot $shell 'phase1-16-products-search-arabic.png'
Set-ById $shell 'Products.Search' '' | Out-Null
Start-Sleep -Seconds 2

# --- The export --------------------------------------------------------------
$exportCsv = Join-Path $runRoot 'export.csv'
Write-Output ('  export: ' + (Invoke-ById $shell 'Products.Export'))
Start-Sleep -Seconds 3
$dialog = Wait-FileDialog $p.Id $shell 20
if ($dialog) {
    $driven = Drive-FileDialog $dialog $exportCsv $p.Id
    Check $driven 'the export file dialog was driven'
    if (-not $driven) { Close-FileDialog $dialog }
    Start-Sleep -Seconds 3

    if (-not (Test-Path $exportCsv)) {
        Write-Host '    the file was not written, so the dialog is driven once more'
        $dialog = Wait-FileDialog $p.Id $shell 5
        if ($dialog) {
            $driven = Drive-FileDialog $dialog $exportCsv $p.Id
            if (-not $driven) { Close-FileDialog $dialog }
            Start-Sleep -Seconds 3
        }
    }

    Check (Test-Path $exportCsv) 'the export wrote a file'
    if (Test-Path $exportCsv) {
        $written = Get-Content -Path $exportCsv -Encoding UTF8
        Write-Output ('  the exported file has ' + $written.Count + ' lines')
        Check (($written -join "`n").Contains('RICE-001')) 'the exported file holds the product'
    }
} else {
    Fail 'the export file dialog never appeared'
    Dump-Windows $p.Id $shell
}
Save-WindowShot $shell 'phase1-17-export-arabic.png'

# --- The import --------------------------------------------------------------
Write-Output ('  import: ' + (Invoke-ById $shell 'Products.Import'))
Start-Sleep -Seconds 3
$wizard2 = Wait-ById $shell 'CsvImportWindow' 20
if ($wizard2) {
    Write-Output ('  choose the file: ' + (Invoke-ById $wizard2 'CsvImport.ChooseFile'))
    Start-Sleep -Seconds 2
    $dialog = Wait-FileDialog $p.Id $shell 20
    if ($dialog) {
        $driven = Drive-FileDialog $dialog $importCsv $p.Id
        Check $driven 'the import file dialog was driven'
        if (-not $driven) { Close-FileDialog $dialog }
        Start-Sleep -Seconds 3

        Check ((Get-TextsContaining $wizard2 'OIL-001').Count -gt 0) 'the preview shows the row that would be created'
        Check ((Get-TextsContaining $wizard2 'BAD-001').Count -gt 0) 'the preview shows the row that would be refused'
        Save-WindowShot $wizard2 'phase1-18-import-preview-arabic.png'

        Write-Output ('  import: ' + (Invoke-ById $wizard2 'CsvImport.Import'))
        Start-Sleep -Seconds 4
        Check (Wait-Gone $shell 'CsvImportWindow' 15) 'the import window closed after importing'
    } else {
        Fail 'the import file dialog never appeared'
        Dump-Windows $p.Id $shell
    }
} else {
    Fail 'the import window never appeared'
}

Start-Sleep -Seconds 2
Check ((Get-TextsContaining $shell 'OIL-001').Count -gt 0) 'the imported product is in the list'
Check ((Get-TextsContaining $shell 'BAD-001').Count -eq 0) 'the refused row was not written'
Save-WindowShot $shell 'phase1-19-products-after-import-arabic.png'

# --- The same screens in English ---------------------------------------------
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
        Write-Host ('  the English entry is a ' + $item.Current.ControlType.ProgrammaticName + ' named [' + $item.Current.Name + ']')
        $chosen = $false

        try {
            $select = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $select.Select()
            $chosen = $true
        } catch {
            Write-Host ('  it cannot be selected: ' + $_.Exception.Message)
        }

        if (-not $chosen) {
            try {
                $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                $chosen = $true
            } catch {
                Write-Host ('  it cannot be pressed: ' + $_.Exception.Message)
            }
        }

        if (-not $chosen) {
            try {
                $item.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern).DoDefaultAction()
                $chosen = $true
            } catch {
                Write-Host ('  it has no default action: ' + $_.Exception.Message)
            }
        }

        if ($chosen) {
            Start-Sleep -Seconds 3
            $switched = $true
            Write-Output '  the language is now English'
            Check ((Get-TextsContaining $shell 'Products').Count -gt 0) 'the navigation rail reads in English'
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

        $p = Start-Process -FilePath $exe -PassThru
        Start-Sleep -Seconds 8

        $login = Wait-Window $p.Id 'Sign in' 30
        if (-not $login) {
            Fail 'the sign-in window never appeared in English'
        } else {
            Write-Output ('  sign in: "' + $login.Current.Name + '"')
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
            Write-Output ('  shell: "' + $shell.Current.Name + '"')
            $railButton = Find-ById $shell 'Products'
            if ($railButton) {
                $railRect = $railButton.Current.BoundingRectangle
                $shellRect = $shell.Current.BoundingRectangle
                if ($railRect.Left -lt $shellRect.Left + $shellRect.Width / 2) {
                    Write-Output '  ok   the navigation rail is on the left: the interface reads left to right'
                } else {
                    Fail 'the navigation rail is not on the left in English'
                }
            } else {
                Fail 'the products entry is not in the navigation rail in English'
            }
            Check ((Get-TextsContaining $shell 'Products').Count -gt 0) 'the navigation rail reads in English'
        }
    }
}

Write-Output ('  open products in English: ' + (Invoke-ById $shell 'Products'))
Start-Sleep -Seconds 3
Save-WindowShot $shell 'phase1-20-products-english.png' $false
Check ((Get-TextsContaining $shell 'Selling price').Count -gt 0) 'the products table reads in English'
Check ((Get-TextsContaining $shell 'EGP').Count -gt 0) 'the money reads in English'
Check ((Find-Text $shell 'RICE-001')) 'the products are still there in English'

Write-Output ('  open the audit trail in English: ' + (Invoke-ById $shell 'Audit'))
Start-Sleep -Seconds 3
Save-WindowShot $shell 'phase1-21-audit-english.png' $false
Check ((Get-TextsContaining $shell 'Summary').Count -gt 0) 'the audit trail reads in English'

Write-Output ('  open categories in English: ' + (Invoke-ById $shell 'Categories'))
Start-Sleep -Seconds 3
Save-WindowShot $shell 'phase1-22-categories-english.png' $false
Check ((Get-TextsContaining $shell 'Arabic name').Count -gt 0) 'the categories table reads in English'

# --- Report ------------------------------------------------------------------
Dump-Windows $p.Id $shell

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Write-Output 'stopped'
Write-Output ('run folder kept at: ' + $runRoot)
Write-Output ('failures: ' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0
