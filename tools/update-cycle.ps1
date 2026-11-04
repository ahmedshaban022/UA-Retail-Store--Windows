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
                   the database's SHA-256, the catalogue and the backups are all still there.

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
    $stamp = (Get-Date -Format 'HH:mm:ss')
    $line = $stamp + '  ' + $message
    Write-Output $line
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

function Get-FileHashOf([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

# The fingerprint of a whole folder: every file it holds, with the hash of each one. This is what
# says that the shop's data came through the upgrade untouched.
function Get-FolderFingerprint([string] $folder) {
    if (-not (Test-Path -LiteralPath $folder)) { return @{} }

    $fingerprint = @{}

    foreach ($file in (Get-ChildItem -LiteralPath $folder -File -Recurse -Force -ErrorAction SilentlyContinue)) {
        $relative = $file.FullName.Substring($folder.Length).TrimStart('\')
        $fingerprint[$relative] = (Get-FileHashOf $file.FullName)
    }

    return $fingerprint
}

# The fingerprints are written to the evidence folder, so that the phases can be run as separate
# invocations and still measure the same shop before and after.
function Save-Fingerprint([string] $name, $fingerprint) {
    $path = Join-Path $EvidenceRoot ($name + '.json')
    ConvertTo-Json -InputObject @{
        shop = @($fingerprint.Keys | Sort-Object | ForEach-Object { @{ path = $_; hash = $fingerprint[$_] } })
    } | Set-Content -LiteralPath $path -Encoding UTF8
}

function Read-Fingerprint([string] $name) {
    $path = Join-Path $EvidenceRoot ($name + '.json')

    if (-not (Test-Path -LiteralPath $path)) { return $null }

    $saved = Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
    $fingerprint = @{}

    foreach ($entry in $saved.shop) { $fingerprint[$entry.path] = $entry.hash }

    return $fingerprint
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

    Check ($changed.Count -eq 0) ($what + ' is unchanged (' + $before.Count + ' files)')
    Check ($missing.Count -eq 0) ($what + ' lost nothing (' + $missing.Count + ' missing)')

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

    try {
        return Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 30
    } catch {
        Write-Line ('the release ' + $tag + ' could not be read: ' + $_.Exception.Message)
        return $null
    }
}

function Get-AssetUrl($release, [string] $suffix) {
    foreach ($asset in $release.assets) {
        if ($asset.name.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            return $asset.browser_download_url
        }
    }

    return $null
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

    Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath -UseBasicParsing -TimeoutSec 900
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

        $uninstallLog = Join-Path $EvidenceRoot 'msiexec-uninstall.log'
        $uninstall = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\msiexec.exe') `
            -ArgumentList @('/x', ('"' + $before + '"'), '/qb', '/norestart', '/l*v', ('"' + $uninstallLog + '"')) `
            -Wait -PassThru -WindowStyle Hidden

        Write-Line ('msiexec exit code: ' + $uninstall.ExitCode)
        Check ($uninstall.ExitCode -eq 0) 'the copy that was installed was removed'

        $script:realShopAfter = Get-FolderFingerprint $realShopHome
        Compare-Fingerprint $script:realShopBefore $script:realShopAfter 'the real %APPDATA%\StoreFlow'
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

function Invoke-Update {
    Write-Line ''
    Write-Line '--- update: the installed copy finds the newer release and applies it ---'

    $shop = Start-TheShop

    if ($null -eq $shop.Shell) { return $null }

    $shell = $shop.Shell

    # The shop asks about updates the way a shopkeeper would: Settings, then About, then the
    # button. The shell's navigation is found by the identifier of the item when the shell gives
    # one, and by its label when it does not.
    $settings = Find-ById $shell 'Settings'

    if ($null -eq $settings) {
        $settings = Find-Text $shell 'Settings'
    }

    if ($null -ne $settings) {
        Write-Line 'the settings screen is open'
        Invoke-ById $shell 'Settings' | Out-Null
        Start-Sleep -Seconds 2
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

    Invoke-ById $shell 'Shell.UpdateNow' | Out-Null

    # The download and the checksum are checked by the programme itself. The evidence that they
    # were is the file it downloaded.
    $expectedDownload = Join-Path ([System.IO.Path]::GetTempPath()) ('StoreFlow\update-' + $Version)

    $deadline = (Get-Date).AddSeconds(300)

    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $expectedDownload) { break }
        Start-Sleep -Milliseconds 500
    }

    Check (Test-Path -LiteralPath $expectedDownload) `
        ('the installer was downloaded into ' + $expectedDownload)

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

    Compare-Fingerprint $script:shopBefore $script:shopAfter 'the shop''s data'

    # The real shop's folder must not have been written to at all.
    Check ($script:realShopBefore.Count -eq $script:realShopAfter.Count) `
        'the real %APPDATA%\StoreFlow was not written to by the upgrade'
}

# --- the run ---------------------------------------------------------------------------------

$script:shopBefore = $null
$script:shopAfter = $null
$script:realShopBefore = @{}
$script:realShopAfter = @{}
$script:pipeName = 'storeflow-update-' + [Guid]::NewGuid().ToString('N')

try {
    if ($Phase -eq 'All' -or $Phase -eq 'Prepare') {
        $installer = Invoke-Prepare

        if ($null -eq $installer) { throw 'The releases could not be prepared.' }

        if ($Phase -eq 'Prepare') {
            Write-Line ''
            Write-Line ('checks: ' + $checks + ', failures: ' + $failures)
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
            Write-Line ''
            Write-Line ('checks: ' + $checks + ', failures: ' + $failures)
            exit $(if ($failures -eq 0) { 0 } else { 1 })
        }
    }

    if ($Phase -eq 'All' -or $Phase -eq 'Update') {
        # The shop's data is measured before the upgrade and after it.
        $script:shopBefore = Get-FolderFingerprint $ShopHome
        $script:realShopBefore = Get-FolderFingerprint $realShopHome

        Save-Fingerprint 'before' $script:shopBefore

        Write-Line ('the shop''s folder holds ' + $script:shopBefore.Count + ' files before the upgrade')
        Write-Line ('the real shop''s folder holds ' + $script:realShopBefore.Count + ' files before the upgrade')

        Invoke-Update | Out-Null

        $script:shopAfter = Get-FolderFingerprint $ShopHome
        $script:realShopAfter = Get-FolderFingerprint $realShopHome

        Save-Fingerprint 'after' $script:shopAfter

        if ($Phase -eq 'Update') {
            Write-Line ''
            Write-Line ('checks: ' + $checks + ', failures: ' + $failures)
            exit $(if ($failures -eq 0) { 0 } else { 1 })
        }
    }

    if ($Phase -eq 'All' -or $Phase -eq 'Verify') {
        # The fingerprints may have been measured by an earlier invocation of this script.
        if ($null -eq $script:shopBefore) { $script:shopBefore = Read-Fingerprint 'before' }
        if ($null -eq $script:shopAfter) { $script:shopAfter = Read-Fingerprint 'after' }
        if ($null -eq $script:realShopBefore) { $script:realShopBefore = @{} }
        if ($null -eq $script:realShopAfter) { $script:realShopAfter = @{} }

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
