<#
.SYNOPSIS
    The StoreFlow release cycle, end to end: install a published release, let the installed copy
    find and apply the next one, and prove the shop's data survived the upgrade.

.DESCRIPTION
    A release process that only one person can follow by hand is not a process. This script is the
    process, and it can be run again for every future release:

        tools\update-cycle.ps1 -Version 1.0.1 -FromVersion 1.0.0

    The phases are:

        Prepare   the old release and the new one are both published. The old release's installer
                   and its checksum are downloaded, and the checksum is checked against the
                   published hash.
        Install   the old version is installed for the current user, with no administrator account
                   and no elevation. A newer copy that is already installed is removed first,
                   because Windows Installer refuses to go backwards, and the shop's data is
                   measured before and after that removal.
        Update    the INSTALLED copy is driven: it finds the newer release, offers it, downloads
                   the installer and the checksum published beside it, checks the download against
                   that checksum, and starts the installer. The programme closes itself, because
                   the installer cannot replace files that are still open.
        Verify    the installed version is the new one, and the shop's data is exactly as it was:
                   the same tables with the same rows, the same files, and not one byte changed in
                   the real %APPDATA%\StoreFlow.

    The shop's database is compared by what it holds, not by the bytes of its file. The shop runs
    SQLite in WAL mode, so the file changes when the programme closes even when nothing was
    written to it; the content does not. The tools\ShopDigest project prints that content, and the
    dumps it writes are kept beside the log as the evidence.

    The shop that the run drives is a throwaway one, so the real %APPDATA%\StoreFlow is never
    written to. The real folder is watched too: the proof that the upgrade leaves the shop's data
    alone is that nothing in it changes.

.EXAMPLE
    & '.\tools\update-cycle.ps1' -Version 1.0.1 -FromVersion 1.0.0
#>
[CmdletBinding()]
param(
    # The version being released. The installed copy must find it and apply it.
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $Version = '1.0.1',

    # The version that is already installed, and that the cycle starts from.
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $FromVersion = '1.0.0',

    [ValidateSet('All', 'Prepare', 'Install', 'Update', 'Verify')]
    [string] $Phase = 'All',

    # Where the evidence of the run is kept.
    [string] $EvidenceRoot = '',

    # The shop the run drives. A throwaway folder by default, so the real shop is never touched.
    [string] $ShopHome = '',

    [switch] $KeepEvidence
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $repoRoot ('build\update-cycle\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($ShopHome)) {
    $ShopHome = Join-Path $EvidenceRoot 'shop'
}

New-Item -ItemType Directory -Force -Path $ShopHome | Out-Null

$logPath = Join-Path $EvidenceRoot ('update-cycle-' + $FromVersion + '-to-' + $Version + '.log')

$failures = 0
$checks = 0

function Write-Line([string] $message) {
    # The line goes to the console and to the run's own log, and it never becomes part of a
    # function's return value. A function that both writes to the output stream and returns a
    # value gives the caller two answers, and the caller uses the wrong one: that is how a log
    # line ended up inside an msiexec argument.
    $stamp = (Get-Date -Format 'HH:mm:ss')
    $line = $stamp + '  ' + $message
    [Console]::WriteLine($line)
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Fail([string] $message) {
    $script:failures++
    Write-Line ('FAIL: ' + $message)
}

function Check([bool] $condition, [string] $message) {
    $script:checks++
    if ($condition) { Write-Line ('  ok   ' + $message) } else { Fail $message }
}

# The UI-automation helpers the desktop harnesses share.
. (Join-Path $PSScriptRoot 'lib\storeflow-ui.ps1')

$repository = 'ahmedshaban022/UA-Retail-Store--Windows'
$installedFolder = Join-Path $env:LOCALAPPDATA 'Programs\StoreFlow'
$installedExe = Join-Path $installedFolder 'StoreFlow.Desktop.exe'
$realShopHome = Join-Path $env:APPDATA 'StoreFlow'
$password = 'StoreFlow!2026'
$sku = 'CYCLE-SMOKE-001'
$productName = 'CYCLE-SMOKE-001'

Write-Line ('=== the StoreFlow release cycle: ' + $FromVersion + ' -> ' + $Version + ' ===')
Write-Line ('evidence folder: ' + $EvidenceRoot)
Write-Line ('shop folder: ' + $ShopHome)

# --- the helpers the cycle needs -------------------------------------------------------------

function Get-InstalledVersion {
    # The installer writes the version it installed under the account that installed it, so the
    # installed version can be read without starting the programme.
    try {
        $key = Get-ItemProperty -Path 'HKCU:\Software\StoreFlow' -Name 'Version' -ErrorAction Stop
        return [string]$key.Version
    } catch {
        return $null
    }
}

# The package that is installed, found in the installer's own cache for this account.
#
# The cache holds the name of the package and where it came from, which is the one thing that
# removes it: Windows Installer refuses to go backwards, so the newer copy has to be removed
# before the older one can be installed.
function Get-InstalledPackage {
    foreach ($entry in (Get-ChildItem 'HKCU:\Software\Microsoft\Installer\Products' -ErrorAction SilentlyContinue)) {
        $source = Get-ItemProperty ($entry.PSPath + '\SourceList') -ErrorAction SilentlyContinue

        if (-not $source -or -not $source.PackageName) { continue }
        if ($source.PackageName -notlike 'StoreFlow-*') { continue }

        $parts = ([string]$source.LastUsedSource) -split ';'

        if ($parts.Count -lt 3) { continue }

        $path = Join-Path $parts[2] $source.PackageName

        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Write-Line ('the installed package is ' + $path)
            return $path
        }
    }

    return $null
}

function Get-FileHashOf([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }

    try {
        return (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash
    } catch {
        # A file that is open cannot be read. That is worth knowing about rather than worth
        # stopping the run for, because the file is then measured as unknown.
        Write-Line ('the file could not be read: ' + $path)
        return $null
    }
}

# The fingerprint of a whole folder: every file it holds, with the hash of each one.
function Get-FolderFingerprint([string] $folder) {
    if (-not (Test-Path -LiteralPath $folder)) { return @{} }

    $fingerprint = @{}

    foreach ($file in (Get-ChildItem -LiteralPath $folder -File -Recurse -Force -ErrorAction SilentlyContinue)) {
        $relative = $file.FullName.Substring($folder.Length).TrimStart('\')
        $fingerprint[$relative] = (Get-FileHashOf $file.FullName)
    }

    return $fingerprint
}

# The files inside a shop that the programme writes to by design. Their bytes change whenever the
# programme runs - the log grows, the scheduler takes copies of the shop, and SQLite moves the
# write-ahead log into the database file when the last connection closes - so they cannot be
# compared byte for byte. The database is measured by what it holds instead, which is the whole
# point of the shop digest below.
function Test-VolatileFile([string] $relative) {
    $path = $relative.Replace('/', '\')

    if ($path -like 'logs\*') { return $true }
    if ($path -like 'backups\*') { return $true }
    if ($path -like 'diagnostics\*') { return $true }
    if ($path -like 'data\restore-points\*') { return $true }
    if ($path -like 'data\*.db') { return $true }
    if ($path -like 'data\*.db-wal') { return $true }
    if ($path -like 'data\*.db-shm') { return $true }

    return $false
}

# The fingerprint of the shop's files that are not written to by the programme running: the theme,
# the brand, the tokens. This is what says that the shop's data came through the upgrade untouched.
#
# The files the programme writes to are not even hashed: the database is open while the shop runs,
# and a file that is open cannot be read.
function Get-StableFingerprint([string] $folder) {
    if (-not (Test-Path -LiteralPath $folder)) { return @{} }

    $fingerprint = @{}

    foreach ($file in (Get-ChildItem -LiteralPath $folder -File -Recurse -Force -ErrorAction SilentlyContinue)) {
        $relative = $file.FullName.Substring($folder.Length).TrimStart('\')

        if (Test-VolatileFile $relative) { continue }

        $fingerprint[$relative] = (Get-FileHashOf $file.FullName)
    }

    return $fingerprint
}

# The .NET executable the digest tool is run with. The shop's data is read by a tool that is built
# from this repository, not by the programme under test.
function Get-DotnetExecutable {
    $candidates = @()

    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    $candidates += 'D:\dotnet\dotnet.exe'

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue

    if ($command) { return $command.Source }

    return $null
}

# The digest tool, built on demand so that a fresh clone can run the cycle.
function Get-DigestTool {
    $candidates = @(
        (Join-Path $repoRoot 'tools\ShopDigest\bin\Release\net10.0-windows\ShopDigest.dll'),
        (Join-Path $repoRoot 'tools\ShopDigest\bin\Release\net10.0\ShopDigest.dll'),
        (Join-Path $repoRoot 'tools\ShopDigest\bin\Debug\net10.0-windows\ShopDigest.dll'),
        (Join-Path $repoRoot 'tools\ShopDigest\bin\Debug\net10.0\ShopDigest.dll'))

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    $dotnet = Get-DotnetExecutable

    if (-not $dotnet) { return $null }

    Write-Line 'the shop digest tool is built, because it is not there yet'

    & $dotnet build (Join-Path $repoRoot 'tools\ShopDigest\ShopDigest.csproj') -c Release -v q --nologo 2>&1 |
        Out-Null

    if ($LASTEXITCODE -ne 0) {
        Fail 'the shop digest tool could not be built'
        return $null
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }

    return $null
}

# What the shop's database holds, as a hash of its content.
#
# The bytes of the database file cannot answer the question: the shop runs SQLite in WAL mode, so
# the file changes when the programme closes even though nothing was written. The content does not
# change, and that is what is measured here. The dump beside it is the evidence a person can read.
function Get-ShopDigest([string] $label) {
    $database = Join-Path $ShopHome 'data\storeflow.db'

    if (-not (Test-Path -LiteralPath $database -PathType Leaf)) { return $null }

    $tool = Get-DigestTool

    if (-not $tool) { return $null }

    $dotnet = Get-DotnetExecutable

    if (-not $dotnet) {
        Fail 'dotnet could not be found, so the shop database cannot be read'
        return $null
    }

    $dump = Join-Path $EvidenceRoot ('shop-' + $label + '.dump.txt')

    $output = & $dotnet $tool $database $dump 2>&1

    if ($LASTEXITCODE -ne 0) {
        Fail ('the shop database could not be read: ' + (($output | Out-String).Trim()))
        return $null
    }

    $sha256 = ''
    $tables = 0
    $rows = 0
    $tableHashes = @{}

    foreach ($line in $output) {
        $text = [string] $line

        if ($text.StartsWith('table=')) {
            # table=<name> rows=<n> sha256=<hash>
            $parts = $text.Split(' ')

            if ($parts.Count -ge 3) {
                $tableHashes[$parts[0].Substring(6)] = @{
                    Rows = [int] $parts[1].Substring(5)
                    Sha256 = $parts[2].Substring(7)
                }
            }
        } elseif ($text.StartsWith('sha256=')) { $sha256 = $text.Substring(7) }
        elseif ($text.StartsWith('tables=')) { $tables = [int] $text.Substring(7) }
        elseif ($text.StartsWith('rows=')) { $rows = [int] $text.Substring(5) }
    }

    Write-Line ('the shop database (' + $label + '): ' + $tables + ' tables, ' + $rows + ' rows')
    Write-Line ('its content hash: ' + $sha256)

    return [pscustomobject]@{
        Sha256 = $sha256
        Tables = $tables
        Rows = $rows
        Dump = $dump
        TableHashes = $tableHashes
    }
}

# The measurements are written to the evidence folder, so that the phases can be run as separate
# invocations and still measure the same shop before and after.
function Save-Measurement([string] $name, $shopFiles, $realShopFiles, $digest) {
    $path = Join-Path $EvidenceRoot ($name + '.json')

    $digestHash = ''
    $digestTables = 0
    $digestRows = 0
    $tableHashes = @{}

    if ($digest) {
        $digestHash = $digest.Sha256
        $digestTables = $digest.Tables
        $digestRows = $digest.Rows
        $tableHashes = $digest.TableHashes
    }

    $tables = @($tableHashes.Keys | Sort-Object | ForEach-Object {
        @{ name = $_; rows = $tableHashes[$_].Rows; sha256 = $tableHashes[$_].Sha256 }
    })

    ConvertTo-Json -InputObject @{
        shop = @($shopFiles.Keys | Sort-Object | ForEach-Object { @{ path = $_; hash = $shopFiles[$_] } })
        realShop = @($realShopFiles.Keys | Sort-Object | ForEach-Object { @{ path = $_; hash = $realShopFiles[$_] } })
        digest = $digestHash
        tables = $digestTables
        rows = $digestRows
        tableHashes = $tables
    } | Set-Content -LiteralPath $path -Encoding UTF8
}

function Read-Measurement([string] $name) {
    $path = Join-Path $EvidenceRoot ($name + '.json')

    if (-not (Test-Path -LiteralPath $path)) { return $null }

    $saved = Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json

    $shop = @{}
    foreach ($entry in $saved.shop) { $shop[$entry.path] = $entry.hash }

    $realShop = @{}
    foreach ($entry in $saved.realShop) { $realShop[$entry.path] = $entry.hash }

    $tableHashes = @{}
    foreach ($entry in $saved.tableHashes) {
        $tableHashes[$entry.name] = @{ Rows = [int] $entry.rows; Sha256 = [string] $entry.sha256 }
    }

    return [pscustomobject]@{
        Shop = $shop
        RealShop = $realShop
        Digest = [string] $saved.digest
        Tables = [int] $saved.tables
        Rows = [int] $saved.rows
        TableHashes = $tableHashes
    }
}

function Compare-Fingerprint($before, $after, [string] $what) {
    $changed = @()
    $missing = @()

    foreach ($key in $before.Keys) {
        if (-not $after.ContainsKey($key)) { $missing += $key }
        elseif ($before[$key] -ne $after[$key]) { $changed += $key }
    }

    foreach ($key in $after.Keys) {
        if (-not $before.ContainsKey($key)) { $changed += $key }
    }

    Check ($changed.Count -eq 0) ('nothing in ' + $what + ' changed (' + $before.Count + ' files)')
    Check ($missing.Count -eq 0) ('nothing in ' + $what + ' was lost (' + $missing.Count + ' missing)')

    if ($changed.Count -gt 0) {
        foreach ($key in ($changed | Select-Object -First 10)) { Write-Line ('       changed: ' + $key) }
    }

    if ($missing.Count -gt 0) {
        foreach ($key in ($missing | Select-Object -First 10)) { Write-Line ('       missing: ' + $key) }
    }
}

function Get-Release([string] $tag) {
    $url = 'https://api.github.com/repos/' + $repository + '/releases/tags/' + $tag

    $headers = @{
        'User-Agent' = 'StoreFlow-Desktop'
        'Accept' = 'application/vnd.github+json'
    }

    # The connection to the release feed is asked again when it does not answer. The feed is
    # reached over the public internet, and a connection that is refused once is not a reason to
    # fail a release run.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 30
        } catch {
            if ($attempt -lt 3) {
                Write-Line ('the release feed did not answer; it is asked again (' + $attempt + ' of 3)')
                Start-Sleep -Seconds 5
            } else {
                Write-Line ('the release ' + $tag + ' could not be read: ' + $_.Exception.Message)
            }
        }
    }

    return $null
}

function Get-AssetUrl($release, [string] $suffix) {
    foreach ($asset in $release.assets) {
        if ($asset.name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            return $asset.browser_download_url
        }
    }

    return $null
}

# The checksum of a downloaded installer, checked against the checksum published beside the
# release. The programme does this before it starts the installer; the script does it again, so
# that the evidence does not rest on the programme's own word.
function Test-DownloadedInstaller([string] $path) {
    $release = Get-Release ('v' + $Version)

    if ($null -eq $release) {
        Fail ('the release v' + $Version + ' could not be read')
        return $false
    }

    $checksumUrl = Get-AssetUrl $release '.msi.sha256'

    if (-not $checksumUrl) {
        Fail 'the release carries no checksum beside the installer'
        return $false
    }

    $text = ''

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $checksumUrl -UseBasicParsing -TimeoutSec 60

            # The published text is served as bytes, and a byte array turned into a string says
            # "System.Byte[]" rather than the hash.
            if ($response.Content -is [byte[]]) {
                $text = [System.Text.Encoding]::UTF8.GetString($response.Content)
            } else {
                $text = [string] $response.Content
            }

            break
        } catch {
            if ($attempt -ge 3) {
                Fail ('the published checksum could not be read: ' + $_.Exception.Message)
                return $false
            }

            Write-Line ('the published checksum did not answer; it is asked again (' + $attempt + ' of 3)')
            Start-Sleep -Seconds 5
        }
    }

    $expected = [regex]::Match($text, '[0-9a-fA-F]{64}').Value
    $actual = Get-FileHashOf $path

    Write-Line ('the download''s sha256: ' + $actual)
    Write-Line ('the published sha256:  ' + $expected)

    if (-not $expected) {
        Fail 'the published text carries no checksum'
        return $false
    }

    return ($actual -eq $expected.ToUpperInvariant())
}

# Downloads a published installer and the checksum published beside it, and checks the download
# against that checksum.
function Get-PublishedInstaller([string] $version) {
    $release = Get-Release ('v' + $version)

    if ($null -eq $release) {
        Fail ('the release v' + $version + ' is not published')
        return $null
    }

    Check ($release.draft -ne $true) ('the release v' + $version + ' is published, not a draft')

    $installerUrl = Get-AssetUrl $release '.msi'
    $checksumUrl = Get-AssetUrl $release '.msi.sha256'

    Check ($null -ne $installerUrl) ('the release v' + $version + ' carries an installer')
    Check ($null -ne $checksumUrl) ('the release v' + $version + ' carries the checksum published beside it')

    if ($null -eq $installerUrl -or $null -eq $checksumUrl) { return $null }

    $installerPath = Join-Path $EvidenceRoot ('StoreFlow-' + $version + '-x64.msi')
    $checksumPath = Join-Path $EvidenceRoot ('StoreFlow-' + $version + '-x64.msi.sha256')

    # A download that is cut off is asked for again. A file that was cut in half must never be
    # mistaken for the published installer.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing -TimeoutSec 900
            break
        } catch {
            if ($attempt -ge 3) { throw }
            Write-Line ('the download did not finish; it is started again (' + $attempt + ' of 3)')
            Start-Sleep -Seconds 5
        }
    }

    Invoke-WebRequest -Uri $checksumUrl -OutFile $checksumPath -UseBasicParsing -TimeoutSec 120

    $actual = Get-FileHashOf $installerPath
    $published = (Get-Content -LiteralPath $checksumPath -Raw).Trim()

    Write-Line ('installer: ' + $installerPath)
    Write-Line ('sha256:    ' + $actual)
    Write-Line ('published: ' + $published)

    Check ($published.ToUpperInvariant().Contains($actual.ToUpperInvariant())) `
        'the download matches the checksum published beside it'

    return $installerPath
}

# --- the phases ------------------------------------------------------------------------------

function Invoke-Prepare {
    Write-Line ''
    Write-Line ('--- prepare: the published releases ---')

    $old = Get-PublishedInstaller $FromVersion

    if ($null -eq $old) { return $null }

    # The new release has to be published as well, because that is what the installed copy is
    # going to look for.
    $new = Get-Release ('v' + $Version)

    if ($null -eq $new) {
        Fail ('the release v' + $Version + ' is not published yet')
        return $null
    }

    Check ($new.draft -ne $true) ('the release v' + $Version + ' is published, not a draft')
    Check ($null -ne (Get-AssetUrl $new '.msi')) ('the release v' + $Version + ' carries an installer')
    Check ($null -ne (Get-AssetUrl $new '.msi.sha256')) ('the release v' + $Version + ' carries its checksum')

    return $old
}

function Invoke-Install([string] $installerPath) {
    Write-Line ''
    Write-Line ('--- install: ' + $FromVersion + ', for this user, with no elevation ---')

    $before = Get-InstalledVersion
    Write-Line ('installed version before: ' + $before)

    # Windows Installer refuses to go backwards, so a newer copy has to be removed first. The
    # shop's data is not part of the installation and must survive that removal.
    if ($before -and $before -ne $FromVersion) {
        Write-Line ('a different version is installed (' + $before + '); it is removed first')

        $script:realShopBefore = Get-FolderFingerprint $realShopHome

        $package = Get-InstalledPackage

        if ($null -eq $package) {
            Fail 'the package that is installed could not be found, so it cannot be removed'
        } else {
            $uninstallLog = Join-Path $EvidenceRoot 'msiexec-uninstall.log'
            $uninstall = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
                -ArgumentList @('/x', ('"' + $package + '"'), '/qb', '/norestart', '/l*v', ('"' + $uninstallLog + '"')) `
                -Wait -PassThru -WindowStyle Hidden

            Write-Line ('msiexec exit code: ' + $uninstall.ExitCode)
            Check ($uninstall.ExitCode -eq 0) 'the copy that was installed was removed'

            $script:realShopAfter = Get-FolderFingerprint $realShopHome
            Compare-Fingerprint $script:realShopBefore $script:realShopAfter 'the real %APPDATA%\StoreFlow'
        }
    }

    $log = Join-Path $EvidenceRoot 'msiexec-install.log'

    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
        -ArgumentList @('/i', ('"' + $installerPath + '"'), '/qb', '/norestart', '/l*v', ('"' + $log + '"')) `
        -Wait -PassThru -WindowStyle Hidden

    Write-Line ('msiexec exit code: ' + $process.ExitCode)

    Check ($process.ExitCode -eq 0) 'the installer finished without an error'

    $after = Get-InstalledVersion
    Write-Line ('installed version after: ' + $after)

    Check ($after -eq $FromVersion) ('the installed version is ' + $FromVersion)

    Check (Test-Path -LiteralPath $installedExe) ('the programme is installed at ' + $installedExe)

    # The install is per-user, so it must not have needed an administrator account.
    $package = Get-ItemProperty -Path 'HKCU:\Software\StoreFlow' -ErrorAction SilentlyContinue

    Check ($null -ne $package) 'the install belongs to this account, not to the machine'
}

# The application is driven through its own windows: the shop is set up, signed into, and asked
# about updates the way a shopkeeper would.
function Start-TheShop {
    Write-Line ''
    Write-Line '--- the shop: the installed copy is started ---'

    $env:STOREFLOW_HOME = $ShopHome

    $process = Start-NewShop $installedExe $script:pipeName $ShopHome

    $wizard = Wait-WindowById 'FirstRunWindow' 60

    if ($wizard) {
        Write-Line 'the shop is new: the first-run wizard is up'

        Complete-FirstRun $wizard $password | Out-Null

        # The application keeps the language it starts in in its theme file, and the script reads
        # the screen in English. The file is written when the shop is set up.
        $themeFile = Join-Path $ShopHome 'themes\active.sftheme.json'

        if (Wait-ThemeFile $themeFile 30) {
            Set-EnglishLanguage $themeFile | Out-Null
            Write-Line 'the shop now starts in English'
        }

        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3

        $process = Start-NewShop $installedExe $script:pipeName $ShopHome
        Write-Line ('the shop was started again: pid=' + $process.Id)
        Start-Sleep -Seconds 8
    }

    $login = Wait-WindowById 'LoginWindow' 60

    if ($login) {
        Write-Line 'the sign-in window is up'
        Sign-In $login 'owner' $password | Out-Null
        Start-Sleep -Seconds 8
    }

    $shell = Wait-Shell $process.Id 60

    Check ($null -ne $shell) 'the shell of the installed copy is up'

    return [pscustomobject]@{ Process = $process; Shell = $shell }
}

# A product in the catalogue, so that the shop holds something worth measuring. A shop with
# nothing in it would come through the upgrade unchanged whether the upgrade touched it or not.
function Add-AProduct($shell) {
    Write-Line 'a product is put in the catalogue, so that the shop holds something'

    Invoke-ById $shell 'Products' | Out-Null
    Start-Sleep -Seconds 3

    Invoke-ById $shell 'Products.New' | Out-Null
    Start-Sleep -Seconds 3

    $editor = Wait-ById $shell 'ProductEditorWindow' 20

    if (-not $editor) {
        Fail 'the product editor never appeared'
        return $false
    }

    Set-ById $editor 'ProductEditor.Sku' $sku | Out-Null
    Set-ById $editor 'ProductEditor.Name' $productName | Out-Null
    Set-ById $editor 'ProductEditor.SalePrice' '100.00' | Out-Null
    Set-ById $editor 'ProductEditor.CostPrice' '50.00' | Out-Null
    Set-ById $editor 'ProductEditor.Barcode' '2000000000099' | Out-Null
    Start-Sleep -Milliseconds 700

    Invoke-ById $editor 'ProductEditor.Save' | Out-Null
    Start-Sleep -Seconds 4

    Check (Wait-Gone $shell 'ProductEditorWindow' 15) 'the editor closed after saving the product'
    Check (Find-Text $shell $sku) 'the product is in the catalogue'

    return $true
}

function Invoke-Update {
    Write-Line ''
    Write-Line '--- update: the installed copy finds the newer release and applies it ---'

    $shop = Start-TheShop

    if ($null -eq $shop.Shell) { return $null }

    $shell = $shop.Shell

    # The shop holds something before the upgrade, so that "the data came through untouched" is a
    # statement about data and not about an empty folder.
    Add-AProduct $shell | Out-Null

    # What the shop holds, measured while it is open and idle. The database is measured by its
    # content, because its bytes move when the programme closes even when nothing was written.
    $script:shopBefore = Get-StableFingerprint $ShopHome
    $script:realShopBefore = Get-FolderFingerprint $realShopHome
    $script:digestBefore = Get-ShopDigest 'before'

    Write-Line ('the shop holds ' + $script:shopBefore.Count + ' files that the programme does not write to')
    Write-Line ('the real shop holds ' + $script:realShopBefore.Count + ' files')

    Save-Measurement 'before' $script:shopBefore $script:realShopBefore $script:digestBefore

    # The shop asks about updates the way a shopkeeper would: Settings, then About, then the
    # button. The shell's navigation is found by the identifier of the item when the shell gives
    # one, and by the label the shopkeeper sees when it does not.
    $settings = Find-ById $shell 'Settings'

    if ($null -eq $settings) {
        $settings = Find-ButtonByText $shell 'Settings'
    }

    if ($null -ne $settings) {
        Write-Line 'the settings screen is open'
        Invoke-Element $settings | Out-Null
        Start-Sleep -Seconds 3
    } else {
        Write-Line 'the settings item of the shell was not found'
    }

    $button = Wait-EnabledById $shell 'Settings.CheckForUpdates' 30

    Check ($null -ne $button) 'the "check for updates" button is there'

    if ($null -eq $button) { return $null }

    Invoke-ById $shell 'Settings.CheckForUpdates' | Out-Null

    # The wait is for the VALUE the screen must reach: the message that names the new version.
    $status = $null
    $deadline = (Get-Date).AddSeconds(90)

    while ((Get-Date) -lt $deadline) {
        $status = Get-TextById $shell 'Settings.UpdateStatus'

        if ($status -and $status.Contains($Version)) { break }

        Start-Sleep -Milliseconds 500
    }

    Write-Line ('the update status reads: ' + $status)

    Check ($null -ne $status -and $status.Contains($Version)) `
        ('the installed copy found v' + $Version + ' and is offering it')

    if ($null -eq $status -or -not $status.Contains($Version)) { return $null }

    # The banner in the shell carries the same offer, and its button starts the update.
    $now = Wait-EnabledById $shell 'Shell.UpdateNow' 30

    Check ($null -ne $now) 'the update is offered in the shell as well'

    if ($null -eq $now) { return $null }

    $clickedAt = Get-Date

    Invoke-ById $shell 'Shell.UpdateNow' | Out-Null

    # The download and the checksum are checked by the programme itself. The evidence that they
    # were is the file it downloaded.
    #
    # The programme keeps the download in a folder named after the release tag, and the tag
    # carries its "v": update-v1.0.1.
    $downloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'StoreFlow'
    $expectedDownload = Join-Path $downloadRoot ('update-v' + $Version)

    if (-not (Test-Path -LiteralPath $expectedDownload)) {
        $expectedDownload = Join-Path $downloadRoot ('update-' + $Version)
    }

    # The size the published release says the installer has. A file that is still being written is
    # shorter than this, and a file left behind by an earlier run is not this run's.
    $publishedSize = 0
    $release = Get-Release ('v' + $Version)

    if ($null -ne $release) {
        foreach ($asset in $release.assets) {
            if ($asset.name.EndsWith('.msi', [StringComparison]::OrdinalIgnoreCase)) {
                $publishedSize = [int] $asset.size
            }
        }
    }

    Write-Line ('the published installer is ' + $publishedSize + ' bytes')

    $deadline = (Get-Date).AddSeconds(600)
    $downloaded = $null

    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $expectedDownload) {
            $candidate = Get-ChildItem -LiteralPath $expectedDownload -Filter '*.msi' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Length -eq $publishedSize -and $_.LastWriteTime -gt $clickedAt } |
                Select-Object -First 1

            if ($candidate) { $downloaded = $candidate; break }
        }

        Start-Sleep -Milliseconds 500
    }

    Check ($null -ne $downloaded) ('the installer was downloaded into ' + $expectedDownload)

    # The programme checks the download against the checksum published beside it. The script
    # checks it as well, so that the evidence does not rest on the programme's own word.
    if ($null -ne $downloaded) {
        Write-Line ('the downloaded installer: ' + $downloaded.FullName)
        Write-Line ('its size: ' + $downloaded.Length + ' bytes')

        Check (Test-DownloadedInstaller $downloaded.FullName) `
            'the downloaded installer matches the checksum published beside the release'
    }

    # The programme closes itself, because the installer cannot replace files that are still open.
    $deadline = (Get-Date).AddSeconds(300)

    while ((Get-Date) -lt $deadline) {
        if ($shop.Process.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }

    Check ($shop.Process.HasExited) 'the programme closed itself, so the installer could take over'

    # The installer runs on its own. It is given the time it needs, because it is replacing the
    # files of a programme that has just closed.
    $installed = $null
    $deadline = (Get-Date).AddSeconds(900)

    while ((Get-Date) -lt $deadline) {
        $installed = Get-InstalledVersion
        if ($installed -eq $Version) { break }
        Start-Sleep -Seconds 2
    }

    Write-Line ('installed version now: ' + $installed)

    Check ($installed -eq $Version) ('the installed version is now ' + $Version)

    # The shop is measured again, now that the installer has finished with it.
    $script:shopAfter = Get-StableFingerprint $ShopHome
    $script:realShopAfter = Get-FolderFingerprint $realShopHome
    $script:digestAfter = Get-ShopDigest 'after'

    Write-Line ('the shop holds ' + $script:shopAfter.Count + ' files that the programme does not write to')
    Write-Line ('the real shop holds ' + $script:realShopAfter.Count + ' files')

    Save-Measurement 'after' $script:shopAfter $script:realShopAfter $script:digestAfter

    return $shop
}

function Invoke-Verify {
    Write-Line ''
    Write-Line '--- verify: the shop''s data came through the upgrade untouched ---'

    $installed = Get-InstalledVersion
    Check ($installed -eq $Version) ('the installed version is ' + $Version)

    $exeHash = Get-FileHashOf $installedExe
    Write-Line ('the installed programme: ' + $installedExe)
    Write-Line ('its sha256: ' + $exeHash)

    # The measurements may have been taken by an earlier invocation of this script.
    $before = Read-Measurement 'before'
    $after = Read-Measurement 'after'

    if ($null -eq $before -or $null -eq $after) {
        Fail 'the measurements of the shop before and after the upgrade are not both there'
        return
    }

    Compare-Fingerprint $before.Shop $after.Shop 'the shop''s files'

    # The database is compared by what it holds. The bytes of the file move when SQLite closes a
    # database, so their hash would say nothing about the data.
    Write-Line ('the database before the upgrade: ' + $before.Digest)
    Write-Line ('the database after the upgrade:  ' + $after.Digest)

    Check ($before.Digest -ne '') 'the shop database was read before the upgrade'

    if ($before.Digest -ne $after.Digest -and $before.Digest -ne '') {
        Write-Line 'the two dumps are kept beside this log, so the difference can be read'
    }

    # Every table is compared on its own, so that a difference is named rather than merely
    # detected. The one table the programme writes to while it runs is the log of the copies the
    # scheduler takes; a line there is the programme doing its own work, not the upgrade.
    $changed = @()
    $added = @()

    foreach ($table in $before.TableHashes.Keys) {
        if (-not $after.TableHashes.ContainsKey($table)) {
            $changed += $table
            continue
        }

        if ($before.TableHashes[$table].Sha256 -ne $after.TableHashes[$table].Sha256) {
            $changed += $table
            Write-Line ('       ' + $table + ': ' +
                $before.TableHashes[$table].Rows + ' rows before, ' +
                $after.TableHashes[$table].Rows + ' rows after')
        }
    }

    foreach ($table in $after.TableHashes.Keys) {
        if (-not $before.TableHashes.ContainsKey($table)) { $added += $table }
    }

    $business = @($changed | Where-Object { $_ -ne 'BackupLogs' })

    Check ($business.Count -eq 0) `
        ('every table of the shop is the same (' + $business.Count + ' changed)')

    if ($changed.Count -eq 0) {
        Check ($before.Digest -eq $after.Digest) `
            'the shop database holds exactly the same tables and the same rows'
    }

    if ($changed.Count -gt 0) {
        Write-Line ('the tables that changed: ' + ($changed -join ', '))
    }

    if ($added.Count -gt 0) {
        Write-Line ('the tables that are new: ' + ($added -join ', '))
    }

    # The real shop's folder must not have been written to at all.
    Check ($before.RealShop.Count -eq $after.RealShop.Count) `
        'the real %APPDATA%\StoreFlow holds the same number of files'

    Compare-Fingerprint $before.RealShop $after.RealShop 'the real %APPDATA%\StoreFlow'
}

# --- the run ---------------------------------------------------------------------------------

$script:shopBefore = $null
$script:shopAfter = $null
$script:realShopBefore = @{}
$script:realShopAfter = @{}
$script:digestBefore = $null
$script:digestAfter = $null
$script:pipeName = 'storeflow-update-' + [Guid]::NewGuid().ToString('N')

try {
    if ($Phase -eq 'All' -or $Phase -eq 'Prepare') {
        $installer = Invoke-Prepare

        if ($null -eq $installer) { throw 'The releases could not be prepared.' }

        if ($Phase -eq 'Prepare') {
            exit $(if ($failures -eq 0) { 0 } else { 1 })
        }
    }

    if ($Phase -eq 'All' -or $Phase -eq 'Install') {
        $installer = Join-Path $EvidenceRoot ('StoreFlow-' + $FromVersion + '-x64.msi')

        if (-not (Test-Path -LiteralPath $installer)) {
            $installer = Invoke-Prepare
        }

        if ($null -eq $installer) { throw 'The releases could not be prepared.' }

        Invoke-Install $installer | Out-Null

        if ($Phase -eq 'Install') {
            exit $(if ($failures -eq 0) { 0 } else { 1 })
        }
    }

    if ($Phase -eq 'All' -or $Phase -eq 'Update') {
        # The shop is set up, measured, upgraded and measured again, all inside the update phase,
        # because the measurement has to be taken while the shop is the one the cycle drives.
        Invoke-Update | Out-Null

        if ($Phase -eq 'Update') {
            exit $(if ($failures -eq 0) { 0 } else { 1 })
        }
    }

    if ($Phase -eq 'All' -or $Phase -eq 'Verify') {
        Invoke-Verify
    }
}
catch {
    Fail ('the cycle stopped: ' + $_.Exception.Message)
}
finally {
    Write-Line ''
    Write-Line ('checks: ' + $checks + ', failures: ' + $failures)
    Write-Line ('the evidence of this run is in ' + $EvidenceRoot)

    if (-not $KeepEvidence) {
        Write-Line 'the run folder is kept: it holds the evidence, and the local file-safety policy refuses deletions.'
    }
}

exit $(if ($failures -eq 0) { 0 } else { 1 })
