param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# The sign-in screen of a shop that has been set up: the window that stands between the shop and
# the till, and the one that proves the login is enforced. The picture is taken by the application
# itself, so it is what the shopkeeper sees.

. (Join-Path $PSScriptRoot 'lib\storeflow-ui.ps1')

$exe = $ExePath
$repoRoot = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repoRoot 'docs\phases\phase0-screens'

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('storeflow-phase0-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$pipeName = 'storeflow-phase0-' + [Guid]::NewGuid().ToString('N')
$checks = 0
$failures = 0

function Check([bool]$condition, [string]$message) {
    $script:checks++
    if ($condition) { Write-Output ('  ok   ' + $message) } else { $script:failures++; Write-Output ('  FAIL ' + $message) }
}

$p = $null

try {
    $p = Start-NewShop $exe $pipeName $runRoot
    Write-Output ('launched pid=' + $p.Id)

    $wizard = Wait-WindowById 'FirstRunWindow' 40
    Check (Complete-FirstRun $wizard) 'the shop was set up'

    # The shop runs in English, so that the screen the script reads is the one in the picture.
    $themeFile = Join-Path $runRoot 'themes\active.sftheme.json'
    Check (Wait-ThemeFile $themeFile 30) 'the theme file is where the application keeps it'
    Check (Set-EnglishLanguage $themeFile) 'the shop now starts in English'

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    $p = Start-NewShop $exe $pipeName $runRoot
    Write-Output ('launched pid=' + $p.Id + ' in English')
    Start-Sleep -Seconds 8

    $login = Wait-WindowById 'LoginWindow' 40

    Check ($null -ne $login) 'the sign-in window is up'

    if ($login) {
        Save-WindowShot $pipeName $outDir 'LoginWindow' 'phase0-login.png' | Out-Null
        Check (Test-Path -LiteralPath (Join-Path $outDir 'phase0-login.png')) 'the picture of the sign-in window was written'
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

Write-Output 'PASS: the sign-in window came up and was photographed'
exit 0
