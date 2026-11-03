param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $Test,

    [switch] $Publish,

    [switch] $Restore
)

$ErrorActionPreference = 'Continue'

# Builds StoreFlow the way it is meant to be built.
#
# The working copy used to hold two solution files: StoreFlow.sln (the real one) and a StoreFlow.slnx
# left behind by the first `dotnet new sln`. A bare `dotnet build` in the repository root was then
# ambiguous, and MSBuild answered with a list of the two rather than with a build. Asking every
# reader to remember to name the solution is a rule that gets broken.
#
# This script is that rule, written down once. It always names the solution file, so there is
# nothing to remember:
#
#     tools\build.ps1                 debug build
#     tools\build.ps1 -Test          build, then run the whole suite
#     tools\build.ps1 -Publish        build, then publish the self-contained win-x64 payload
#     tools\build.ps1 -Configuration Release -Test
#
# The stray solution file has been moved to build\legacy\StoreFlow.slnx rather than deleted, because
# this machine's file-safety policy refuses deletions. It is out of the root, so it can no longer
# make the build ambiguous.

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'StoreFlow.sln'

if (-not (Test-Path -LiteralPath $solution)) {
    Write-Output ('The solution was not found at ' + $solution)
    exit 1
}

# The .NET SDK is not on PATH by default on this machine.
$dotnetRoot = 'D:\dotnet'
if (Test-Path -LiteralPath (Join-Path $dotnetRoot 'dotnet.exe')) {
    $env:PATH = $dotnetRoot + ';' + $env:PATH
    $env:DOTNET_ROOT = $dotnetRoot
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    Write-Output 'dotnet was not found. Install the .NET 10 SDK, or set PATH to include it.'
    exit 1
}

$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

Write-Output ('solution: ' + $solution)
Write-Output ('configuration: ' + $Configuration)

if ($Restore) {
    Write-Output ''
    Write-Output '--- restore ---'
    & $dotnet restore $solution --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Output ('restore failed with exit code ' + $LASTEXITCODE)
        exit $LASTEXITCODE
    }
}

Write-Output ''
Write-Output '--- build ---'
& $dotnet build $solution -c $Configuration -v q --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Output ('build failed with exit code ' + $LASTEXITCODE)
    exit $LASTEXITCODE
}

if ($Test) {
    Write-Output ''
    Write-Output '--- test ---'
    & $dotnet test $solution -c $Configuration --nologo --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Output ('test failed with exit code ' + $LASTEXITCODE)
        exit $LASTEXITCODE
    }
}

if ($Publish) {
    Write-Output ''
    Write-Output '--- publish (self-contained win-x64) ---'

    $publishRoot = Join-Path $root 'artifacts\publish'
    $desktopProject = Join-Path $root 'src\StoreFlow.Desktop\StoreFlow.Desktop.csproj'

    & $dotnet publish $desktopProject -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
        -o $publishRoot --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Output ('publish failed with exit code ' + $LASTEXITCODE)
        exit $LASTEXITCODE
    }

    Write-Output ('payload: ' + $publishRoot)
}

Write-Output ''
Write-Output 'done.'
exit 0
