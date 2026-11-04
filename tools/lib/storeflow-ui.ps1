# The UI-automation helpers the desktop harnesses share.
#
#     . (Join-Path $PSScriptRoot 'lib\storeflow-ui.ps1')
#
# The dot-source belongs in a script that sits in the tools folder, which is where $PSScriptRoot
# points when the line above is read.
#
# Everything here drives the running application through Windows UI Automation. Two rules are
# followed everywhere:
#
#   - A wait is for a VALUE the screen must reach, never for a number of rows that is already at
#     its target. Three scans of the same barcode are one line of three, so a row count never
#     changes and the next input arrives while the programme is still answering the last one.
#   - A button that is still disabled cannot be invoked: WPF throws the click away without a word,
#     so the screen never changes and the script blames the programme for a click that never
#     happened. A button is therefore waited for until it is usable.


Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

# The two handles every automation helper uses.
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

# The interop helper is only added once. A script that dot-sources this file twice would
# otherwise fail with "the type name already exists", which is a puzzle nobody should have to
# solve at half past two in the morning.
if (-not ('StoreFlowUi' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class StoreFlowUi
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
}

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

# A button that is still disabled cannot be invoked: WPF throws the click away without a word, so
# the screen never changes and the script blames the programme for a click that never happened.
# The rule this harness follows everywhere else is that a wait is for a VALUE the screen must
# reach - so this waits for the button to become usable.
function Wait-EnabledById($scope, [string]$id, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $el = Find-ById $scope $id
        if ($el) {
            try { if ($el.Current.IsEnabled) { return $el } } catch { }
        }
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

# Pictures are taken by the application itself. The script sends one command down the capture pipe
# and the application draws the window it names and writes the picture itself.
#
# A picture taken from outside the process comes back mirrored for a right-to-left window, and the
# mirrored version of an Arabic screen is a lie about the screen.
function Send-Capture([string]$pipeName, [string]$command) {
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

# Asks the running application to draw the window and writes the picture into the folder.
#
# Returns true when the picture was taken. The caller decides what a missing picture means: a
# harness that has no picture is still a harness that made its checks.
function Save-WindowShot([string]$pipeName, [string]$outDir, [string]$windowId, [string]$fileName) {
    $path = Join-Path $outDir $fileName
    $reply = Send-Capture $pipeName ('CAPTURE ' + $windowId + ' ' + $path)

    if ($reply -and $reply.StartsWith('OK')) {
        Write-Output ('  screenshot -> ' + $fileName + ' (' + $windowId + ')')
        return $true
    }

    Write-Output ('  no screenshot for ' + $fileName + ': ' + $reply)
    return $false
}

# The application keeps the language it starts in in its theme file. A harness that reads the
# screen in English rewrites that file after the shop has been set up, and starts the application
# again.
function Set-EnglishLanguage([string]$themeFile) {
    if (-not (Test-Path -LiteralPath $themeFile)) { return $false }

    $theme = Get-Content -Raw -Encoding UTF8 -LiteralPath $themeFile
    $theme = [regex]::Replace($theme, '"default"\s*:\s*"ar"', '"default": "en"')
    [System.IO.File]::WriteAllText($themeFile, $theme, (New-Object System.Text.UTF8Encoding($false)))

    return $true
}

function Wait-ThemeFile([string]$themeFile, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)

    while (-not (Test-Path -LiteralPath $themeFile) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
    }

    return (Test-Path -LiteralPath $themeFile)
}

# Starts the application on a shop folder of its own, so that a harness never writes into the
# shop's real data.
function Start-NewShop([string]$exe, [string]$pipeName, [string]$runRoot) {
    $env:STOREFLOW_HOME = $runRoot

    return (Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru)
}

# Walks the first-run wizard to the end and sets the shop up.
#
# The shop's details are written in both languages, and the owner's account is created with the
# password the harness signs in with afterwards.
function Complete-FirstRun($wizard, [string]$password = 'StoreFlow!2026') {
    if (-not $wizard) { return $false }

    $arabicStoreName = A @(0x0645, 0x062A, 0x062C, 0x0631, 0x0020, 0x0645, 0x0635, 0x0631, 0x064A, 0x0629)

    Fill-Fields $wizard @('Al-Masria Store', $arabicStoreName, '12 Tahrir Street, Cairo', '+20 100 000 0000') $true | Out-Null
    Start-Sleep -Milliseconds 700
    Invoke-ById $wizard 'FirstRun.Next' | Out-Null
    Start-Sleep -Seconds 2

    Fill-Fields $wizard @('Owner', 'owner', $password, $password) $true | Out-Null
    Start-Sleep -Milliseconds 700
    Invoke-ById $wizard 'FirstRun.Next' | Out-Null
    Start-Sleep -Seconds 2

    Invoke-ById $wizard 'FirstRun.Finish' | Out-Null
    Start-Sleep -Seconds 6

    return $true
}

# Signs in with the account the wizard created.
function Sign-In($login, [string]$userName = 'owner', [string]$password = 'StoreFlow!2026') {
    if (-not $login) { return $false }

    $edits = Get-EditBoxes $login
    if ($edits.Count -ge 1) { Set-EditValue $edits[0] $userName | Out-Null }
    Fill-Password $login $password $false | Out-Null
    Start-Sleep -Milliseconds 700

    return (Invoke-ById $login 'Login.SignIn')
}

# A button that carries a name the shopkeeper sees. It is found by that name rather than by an
# identifier, because a control's identifier is the one thing a view is free not to give.
function Find-ButtonByText($scope, [string]$name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))

    return $scope.FindFirst($TS::Subtree, $cond)
}

# Presses a control that has been found already, wherever it was found.
function Invoke-Element($element) {
    if (-not $element) { return $false }

    try {
        $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        return $true
    } catch {
        return $false
    }
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

# Puts the caret in a field the way a person does: with a click of the mouse. A field that only
# the automation can address does not always take the keys a scanner sends.
function Click-Element($element) {
    if (-not $element) { return $false }

    try {
        $rect = $element.Current.BoundingRectangle
        $x = [int]($rect.Left + ($rect.Width / 2))
        $y = [int]($rect.Top + ($rect.Height / 2))

        [void][StoreFlowUi]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 150

        [StoreFlowUi]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 60
        [StoreFlowUi]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 250

        return $true
    } catch {
        return $false
    }
}

# Puts the application in front, so that the keys the script sends reach the field and not the
# console the script is running in.
function Focus-App($window) {
    if (-not $window) { return $false }

    try {
        $handle = [IntPtr]$window.Current.NativeWindowHandle

        if ($handle -eq [IntPtr]::Zero) { return $false }

        [void][StoreFlowUi]::ShowWindow($handle, 9)
        [void][StoreFlowUi]::BringWindowToTop($handle)
        [void][StoreFlowUi]::SetForegroundWindow($handle)
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
