param(
    [string] $ExePath = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\bin\Debug\net10.0-windows\StoreFlow.Desktop.exe'
)

# Pass -ExePath to drive another StoreFlow executable; the default is the development build.

$ErrorActionPreference = 'Continue'

# The very first thing a brand-new shop sees: the first-run wizard, before anything has been
# written. The picture is taken by the application itself, so it is what the shopkeeper sees.

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

    Check ($null -ne $wizard) 'a brand-new shop is met by the first-run wizard'

    if ($wizard) {
        Save-WindowShot $pipeName $outDir 'FirstRunWindow' 'phase0-firstrun.png' | Out-Null
        Check (Test-Path -LiteralPath (Join-Path $outDir 'phase0-firstrun.png')) 'the picture of the first window was written'
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

Write-Output 'PASS: the first window of a brand-new shop came up and was photographed'
exit 0
