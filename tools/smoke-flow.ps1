param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# Drives the StoreFlow first-run wizard and the sign-in screen through UI Automation, so the whole
# start-up path is exercised against the real application rather than mocked.
#
# The application draws its own windows: the script asks it to photograph one of them over the
# capture pipe, so the picture is what the shopkeeper sees and not a mirrored copy of it.

. (Join-Path $PSScriptRoot 'lib\storeflow-ui.ps1')

$exe = $ExePath
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot 'docs\phases\phase0-screens'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('storeflow-phase0-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$env:STOREFLOW_HOME = $runRoot

$pipeName = 'storeflow-phase0-' + [Guid]::NewGuid().ToString('N')
$password = 'StoreFlow!2026'
$checks = 0
$failures = 0

function Check([bool]$condition, [string]$message) {
    $script:checks++
    if ($condition) { Write-Output ('  ok   ' + $message) } else { $script:failures++; Write-Output ('  FAIL ' + $message) }
}

$p = $null

try {
    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id)

    # --- The first-run wizard ----------------------------------------------------------------
    $wizard = Wait-WindowById 'FirstRunWindow' 40

    if (-not $wizard) {
        Write-Output 'FAIL: the wizard never appeared'
        exit 1
    }

    Write-Output 'the wizard is up'

    # Built from code points so the Arabic survives this file being read as ANSI.
    $arabicStoreName = A @(0x0645, 0x062A, 0x062C, 0x0631, 0x0020, 0x0645, 0x0635, 0x0631, 0x064A, 0x0629)

    Check (Fill-Fields $wizard @('Al-Masria Store', $arabicStoreName, '12 Tahrir Street, Cairo', '+20 100 000 0000') $true) 'the shop details were written into the wizard'
    Start-Sleep -Milliseconds 700
    Save-WindowShot $pipeName $outDir 'FirstRunWindow' 'phase0-01-wizard-step1.png' | Out-Null
    Check (Invoke-ById $wizard 'FirstRun.Next') 'the wizard went on to the account'
    Start-Sleep -Seconds 2

    Check (Fill-Fields $wizard @('Owner', 'owner', $password, $password) $true) 'the owner account was written into the wizard'
    Start-Sleep -Milliseconds 700
    Save-WindowShot $pipeName $outDir 'FirstRunWindow' 'phase0-02-wizard-step2.png' | Out-Null
    Check (Invoke-ById $wizard 'FirstRun.Next') 'the wizard went on to its last step'
    Start-Sleep -Seconds 2

    Save-WindowShot $pipeName $outDir 'FirstRunWindow' 'phase0-03-wizard-step3.png' | Out-Null
    Check (Invoke-ById $wizard 'FirstRun.Finish') 'the shop was set up'
    Start-Sleep -Seconds 6

    # --- The shop runs in English --------------------------------------------------------------
    # The application keeps the language it starts in in its theme file, and this script reads the
    # screen in English. The file is written when the shop is set up.
    $themeFile = Join-Path $runRoot 'themes\active.sftheme.json'

    Check (Wait-ThemeFile $themeFile 30) 'the theme file is where the application keeps it'
    Check (Set-EnglishLanguage $themeFile) 'the shop now starts in English'

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $p = Start-Process -FilePath $exe -ArgumentList @('--capture-pipe', $pipeName) -PassThru
    Write-Output ('launched pid=' + $p.Id + ' in English')
    Start-Sleep -Seconds 8

    # --- The sign-in screen --------------------------------------------------------------------
    $login = Wait-WindowById 'LoginWindow' 40

    if (-not $login) {
        Write-Output 'FAIL: the sign-in window never appeared'
        exit 1
    }

    Write-Output 'the sign-in window is up'
    Save-WindowShot $pipeName $outDir 'LoginWindow' 'phase0-04-login.png' | Out-Null

    $edits = Get-EditBoxes $login
    if ($edits.Count -ge 1) { Set-EditValue $edits[0] 'owner' | Out-Null }
    Fill-Password $login $password $false | Out-Null
    Start-Sleep -Milliseconds 700

    Check (Invoke-ById $login 'Login.SignIn') 'the shopkeeper signed in'
    Start-Sleep -Seconds 8

    # --- The shell -----------------------------------------------------------------------------
    $shell = Wait-Shell $p.Id 40

    Check ($null -ne $shell) 'the shell of the shop is up'

    if ($shell) {
        Save-WindowShot $pipeName $outDir 'MainWindow' 'phase0-05-shell.png' | Out-Null
        Write-Output ('shell: "' + $shell.Current.Name + '"')
    }
} catch {
    $failures++
    Write-Output ('FAIL: the run stopped early: ' + $_.Exception.Message)
} finally {
    if ($p -and -not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'stopped'
    Write-Output ('run folder kept at: ' + $runRoot)
}

Write-Output ('checks: ' + $checks)
Write-Output ('failures: ' + $failures)

if ($failures -gt 0) { exit 1 }

Write-Output 'PASS: the wizard, the sign-in and the shell all came up'
exit 0
