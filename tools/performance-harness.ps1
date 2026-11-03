param(
    [string] $Shop,

    [int] $Products = 10000,

    [int] $Invoices = 100000,

    [int] $Repeat = 3,

    [switch] $SkipSeed
)

$ErrorActionPreference = 'Continue'

# Fills a shop with the volume a busy shop reaches, and measures the screens against it.
#
#     tools\performance-harness.ps1
#     tools\performance-harness.ps1 -Products 2000 -Invoices 5000
#     tools\performance-harness.ps1 -Home C:\perf -SkipSeed
#
# The shop is built in a scratch folder, so the real shop on this machine is never touched. The
# numbers it prints are the times the application's own query handlers take against that volume —
# the same handlers the screens call, on the same database, so what is measured is the work the
# shop actually does rather than a lookalike.
#
# The volume the harness fills is the volume PRD §1.3 names: ten thousand products and a hundred
# thousand invoices.

$root = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $root 'tools\StoreFlow.VolumeTool\StoreFlow.VolumeTool.csproj'

$env:PATH = 'D:\dotnet;' + $env:PATH
$env:DOTNET_ROOT = 'D:\dotnet'
$env:DOTNET_NOLOGO = '1'

$dotnet = 'D:\dotnet\dotnet.exe'

if ([string]::IsNullOrWhiteSpace($Shop)) {
    $Shop = Join-Path $env:TEMP 'storeflow-performance'
}

Write-Output ('shop: ' + $Shop)
Write-Output ('products: ' + $Products)
Write-Output ('invoices: ' + $Invoices)
Write-Output ''

if (-not $SkipSeed) {
    Write-Output '--- filling the shop ---'

    & $dotnet run --project $toolProject -c Release -- `
        seed --home $Shop --products $Products --invoices $Invoices
    if ($LASTEXITCODE -ne 0) {
        Write-Output ('the shop could not be filled, exit code ' + $LASTEXITCODE)
        exit $LASTEXITCODE
    }

    Write-Output ''
}

Write-Output '--- measuring ---'

& $dotnet run --project $toolProject -c Release -- `
    measure --home $Shop --repeat $Repeat
if ($LASTEXITCODE -ne 0) {
    Write-Output ('the measurement failed, exit code ' + $LASTEXITCODE)
    exit $LASTEXITCODE
}

Write-Output ''
Write-Output ('the shop that was measured is at ' + $Shop)
exit 0
