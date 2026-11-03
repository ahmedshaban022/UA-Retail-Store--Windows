param(
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $Version = '1.0.0',
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $NextVersion = '1.0.1',
    [ValidateRange(1, 600)]
    [int] $MsiTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$env:PATH = 'D:\dotnet;' + $env:PATH
$env:DOTNET_ROOT = 'D:\dotnet'

# Installs, upgrades, and uninstalls without starting the app or deleting the shop's data.
# Every MSI is the exact, identity-checked package reported by its build. A timeout is a
# failure with an uncertain outcome, so it must never trigger a blind retry.
$root = Split-Path -Parent $PSScriptRoot
$installerDir = Join-Path $root 'artifacts\installer'
$buildScript = Join-Path $PSScriptRoot 'build-installer.ps1'
$installedFolder = Join-Path $env:LOCALAPPDATA 'Programs\StoreFlow'
$dataFolder = Join-Path $env:APPDATA 'StoreFlow'
$mark = Join-Path $dataFolder 'installer-check.txt'
$logDir = Join-Path $installerDir ('verify-' + [guid]::NewGuid().ToString('N'))

function Write-Step([string]$message) {
    Write-Output ''
    Write-Output ('=== ' + $message + ' ===')
}

function Build-Msi([string]$requestedVersion) {
    $output = New-Object System.Collections.Generic.List[string]
    & $buildScript -Version $requestedVersion | ForEach-Object {
        Write-Host $_
        $output.Add([string]$_)
    }
    $buildExit = $LASTEXITCODE
    if ($buildExit -ne 0) { throw ('The installer build failed for ' + $requestedVersion + ' (exit ' + $buildExit + ').') }

    $paths = @($output | Where-Object { $_ -is [string] -and $_.StartsWith('installer: ') })
    $hashes = @($output | Where-Object { $_ -is [string] -and $_.StartsWith('sha256: ') })
    if ($paths.Count -ne 1 -or $hashes.Count -ne 1) { throw 'The build did not report one exact MSI path and hash.' }
    $path = $paths[0].Substring('installer: '.Length)
    $expectedPath = Join-Path $installerDir ($requestedVersion + '\StoreFlow-' + $requestedVersion + '-x64.msi')
    if ($path -ne $expectedPath -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw ('The build reported an unexpected or missing MSI: ' + $path)
    }
    $hash = $hashes[0].Substring('sha256: '.Length)
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash) { throw 'The MSI changed after its build.' }
    return [pscustomobject]@{ Path = $path; Hash = $hash }
}

function Invoke-Msi([ValidateSet('/i', '/x')][string]$action, [string]$target, [string]$expectedHash) {
    # These arguments are paths or product GUIDs, never arbitrary command-line fragments.
    # ProcessStartInfo.Arguments is a single native command line on PowerShell 5.1: quote
    # every path explicitly, including the verbose log path, instead of joining an array.
    if ($target.Contains('"') -or $target.EndsWith('\')) { throw 'Invalid MSI target.' }
    if ($action -eq '/i' -and -not $expectedHash) { throw 'An install requires a verified MSI hash.' }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        if ($expectedHash -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expectedHash) {
            throw ('The requested package changed; refusing to run it: ' + $target)
        }
        $logPath = Join-Path $logDir ([guid]::NewGuid().ToString('N') + '.log')
        Write-Host ('MSI target: ' + $target)
        Write-Host ('MSI log: ' + $logPath)
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = Join-Path $env:SystemRoot 'System32\msiexec.exe'
        $startInfo.Arguments = $action + ' "' + $target + '" /qn /norestart /L*v "' + $logPath + '"'
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.ErrorDialog = $false
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        try {
            if (-not $process.Start()) { throw 'Could not start msiexec.' }
            if (-not $process.WaitForExit($MsiTimeoutSeconds * 1000)) {
                $process.Kill()
                if (-not $process.WaitForExit(10000)) { throw ('Could not stop timed-out msiexec; see ' + $logPath) }
                throw ('msiexec timed out after ' + $MsiTimeoutSeconds + ' seconds and was terminated. No retry; see ' + $logPath)
            }
            $code = $process.ExitCode
        } finally {
            $process.Dispose()
        }
        # Only 1618 guarantees that this request did not run because another transaction
        # is active. The target and its hash stay fixed across these bounded retries.
        if ($code -eq 1618 -and $attempt -lt 3) {
            Write-Host ('msiexec exit: 1618; another install is active; retrying the same target (attempt ' + ($attempt + 1) + ' of 3)')
            Start-Sleep -Seconds 5
            continue
        }
        return $code
    }
}

function Install-Msi($package) {
    return (Invoke-Msi '/i' $package.Path $package.Hash)
}

function Uninstall-Msi($package) {
    return (Invoke-Msi '/x' $package.Path $package.Hash)
}

function Get-InstalledProducts {
    # Query uninstall registration without Win32_Product, which can repair unrelated MSIs.
    foreach ($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                       'HKCU:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
                       'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
                       'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall')) {
        if (Test-Path -LiteralPath $key) {
            Get-ChildItem -LiteralPath $key | ForEach-Object {
                $product = Get-ItemProperty -LiteralPath $_.PSPath
                if ($product.DisplayName -eq 'StoreFlow' -and $product.WindowsInstaller -eq 1 -and
                    $product.PSChildName -match '^\{[0-9A-Fa-f-]{36}\}$') { $product }
            }
        }
    }
}

function Remove-ExistingInstall {
    foreach ($product in @(Get-InstalledProducts)) {
        Write-Host ('removing the copy already on this machine: ' + $product.PSChildName)
        $code = Invoke-Msi '/x' $product.PSChildName
        Write-Host ('msiexec exit: ' + $code)
        if ($code -ne 0) { throw 'Removing the existing installation failed.' }
    }
}

try {
    if ([version]$NextVersion -le [version]$Version) { throw 'NextVersion must be newer than Version for an upgrade.' }
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Write-Step ('build ' + $Version)
    Remove-ExistingInstall
    $firstMsi = Build-Msi $Version

    Write-Step ('install ' + $Version)
    $code = Install-Msi $firstMsi
    Write-Output ('msiexec exit: ' + $code)
    if ($code -ne 0) { throw 'The first install did not succeed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installedFolder 'StoreFlow.Desktop.exe') -PathType Leaf)) {
        throw 'The programme was not installed.'
    }
    Write-Output 'the programme is installed'
    if (-not (Test-Path -LiteralPath (Join-Path $installedFolder 'ar\StoreFlow.Desktop.resources.dll') -PathType Leaf)) {
        throw 'The Arabic strings were not installed.'
    }
    Write-Output 'the Arabic strings are installed'
    $installedVersion = (Get-ItemProperty -LiteralPath 'HKCU:\Software\StoreFlow' -Name Version).Version
    Write-Output ('installed version: ' + $installedVersion)
    if ($installedVersion -ne $Version) { throw 'The first installed version does not match the requested package.' }
    $firstProducts = @(Get-InstalledProducts)
    if ($firstProducts.Count -ne 1 -or $firstProducts[0].DisplayVersion -ne $Version) { throw 'Expected exactly one installed product at the first version.' }

    Write-Step 'the shop has some data'
    New-Item -ItemType Directory -Force -Path $dataFolder | Out-Null
    if (-not (Test-Path -LiteralPath $mark)) {
        Set-Content -LiteralPath $mark -Value 'the shop ledger, kept' -Encoding UTF8
        Write-Output ('mark written to ' + $mark)
    } else {
        Write-Output ('existing mark preserved at ' + $mark)
    }
    $markHash = (Get-FileHash -LiteralPath $mark -Algorithm SHA256).Hash

    Write-Step ('build and install ' + $NextVersion + ' over the top')
    $nextMsi = Build-Msi $NextVersion
    if ($nextMsi.Path -eq $firstMsi.Path -or $nextMsi.Hash -eq $firstMsi.Hash) { throw 'The upgrade must use a different package.' }
    $code = Install-Msi $nextMsi
    Write-Output ('msiexec exit: ' + $code)
    if ($code -ne 0) { throw 'The upgrade did not succeed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $installedFolder 'StoreFlow.Desktop.exe') -PathType Leaf)) {
        throw 'The programme is missing after the upgrade.'
    }
    if ((Get-FileHash -LiteralPath $mark -Algorithm SHA256).Hash -ne $markHash) { throw 'The shop data changed during the upgrade.' }
    Write-Output 'the shop data survived the upgrade'
    $installedVersion = (Get-ItemProperty -LiteralPath 'HKCU:\Software\StoreFlow' -Name Version).Version
    Write-Output ('installed version: ' + $installedVersion)
    if ($installedVersion -ne $NextVersion) { throw ('The installed version is not ' + $NextVersion + '.') }
    $nextProducts = @(Get-InstalledProducts)
    if ($nextProducts.Count -ne 1 -or $nextProducts[0].DisplayVersion -ne $NextVersion -or
        $nextProducts[0].PSChildName -eq $firstProducts[0].PSChildName) { throw 'The upgrade did not replace the original product registration.' }
    Write-Output 'the upgrade replaced the original product registration'

    Write-Step 'uninstall'
    $code = Uninstall-Msi $nextMsi
    Write-Output ('msiexec exit: ' + $code)
    if ($code -ne 0) { throw 'The uninstall did not succeed.' }
    if (Test-Path -LiteralPath $installedFolder) { throw 'The programme folder is still there after the uninstall.' }
    if (@(Get-InstalledProducts).Count -ne 0) { throw 'StoreFlow is still registered after the uninstall.' }
    Write-Output 'the programme folder was removed'
    if ((Get-FileHash -LiteralPath $mark -Algorithm SHA256).Hash -ne $markHash) { throw 'The shop data changed during the uninstall.' }
    Write-Output 'the shop data survived the uninstall'

    Write-Step 'the result'
    Write-Output 'everything checked out'
    exit 0
} catch {
    Write-Step 'the result'
    Write-Output ('FAILED: ' + $_.Exception.Message)
    exit 1
}
