param(
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $Version = '1.0.0',

    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
$versionParts = $Version.Split('.')
if ([int]$versionParts[0] -gt 255 -or [int]$versionParts[1] -gt 255 -or [int]$versionParts[2] -gt 65535) {
    throw 'The version must fit the MSI major.minor.build limits (255.255.65535).'
}

# Builds the installer.
#
#     installer\build-installer.ps1                 build the 1.0.0 installer
#     installer\build-installer.ps1 -Version 1.0.1   build a different version
#
# The result is artifacts\installer\<version>\StoreFlow-<version>-x64.msi.
#
# The installer is a per-user MSI. It installs into %LOCALAPPDATA%\Programs\StoreFlow and leaves
# the shop's data, which lives in %APPDATA%\StoreFlow, exactly where it was. That is what makes an
# upgrade safe: the ledger, the catalogue and the backups are not part of the installation.

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root ('artifacts\publish\' + $Version)
$installerDir = Join-Path $root ('artifacts\installer\' + $Version)
$outputName = 'StoreFlow-' + $Version + '-x64'
$msiPath = Join-Path $installerDir ($outputName + '.msi')
$wixProject = Join-Path $root 'installer\StoreFlow.Installer\StoreFlow.Installer.wixproj'
$desktopProject = Join-Path $root 'src\StoreFlow.Desktop\StoreFlow.Desktop.csproj'
$iconDir = Join-Path $root 'src\StoreFlow.Desktop\Assets'

# The identity of this build, worked out from the version. WiX would work one out from the
# contents of the package, which would mean that rebuilding the same version produced a
# different product and the shop was left with two copies of the same version installed.
$md5 = [System.Security.Cryptography.MD5]::Create()
$hash = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes('StoreFlow-' + $Version))
$productCode = '{' + [guid]::new($hash).ToString().ToUpperInvariant() + '}'

$env:PATH = 'D:\dotnet;' + $env:PATH
$env:DOTNET_ROOT = 'D:\dotnet'
$env:DOTNET_NOLOGO = '1'

$dotnet = 'D:\dotnet\dotnet.exe'

if (-not $SkipPublish) {
    Write-Output '--- publish ---'

    # Each version publishes into a folder of its own, so that a leftover file from an earlier
    # build can never end up inside the installer.
    & $dotnet publish $desktopProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
        -p:Version=$Version -p:AssemblyVersion=$Version -p:FileVersion=$Version `
        -o $publishDir --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Output ('publish failed with exit code ' + $LASTEXITCODE)
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'StoreFlow.Desktop.exe'))) {
    Write-Output 'The published programme is not where it was expected. Publish first.'
    exit 1
}

Write-Output ''
Write-Output '--- build the installer ---'

if (-not (Test-Path -LiteralPath $installerDir)) {
    New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
}

# The list of files is written out rather than harvested. The published folder holds the same
# file name under several language folders, and a harvest cannot tell which of them belongs
# where. Only the Arabic satellite is wanted: it carries the shop's own Arabic strings.
$productWxs = Join-Path $installerDir 'Product.wxs'
$template = Join-Path $root 'installer\StoreFlow.Installer\Product.wxs'

$groups = New-Object System.Collections.Generic.List[string]
$index = 0

function New-ComponentGroup([System.Collections.Generic.List[string]]$target, [string]$groupId, [string]$directoryId, [object[]]$files) {
    if ($files.Count -eq 0) { return }
    $target.Add('    <ComponentGroup Id="' + $groupId + '" Directory="' + $directoryId + '">')
    foreach ($file in $files) {
        $script:index++
        # One component per file. A component that holds several files cannot have an
        # automatically generated identity, and the installer would need one for every file.
        $target.Add('      <Component Id="cmp_' + $script:index + '">')
        $target.Add('        <File Id="fil_' + $script:index + '" Source="' + $file.FullName + '" Name="' + $file.Name + '" KeyPath="yes" />')
        $target.Add('      </Component>')
    }
    $target.Add('    </ComponentGroup>')
}

$rootFiles = @(Get-ChildItem -LiteralPath $publishDir -File | Where-Object { $_.Extension -ne '.pdb' })
New-ComponentGroup $groups 'ApplicationFiles' 'INSTALLFOLDER' $rootFiles

foreach ($folder in (Get-ChildItem -LiteralPath $publishDir -Directory | Where-Object { $_.Name -eq 'ar' })) {
    $folderFiles = @(Get-ChildItem -LiteralPath $folder.FullName -File | Where-Object { $_.Extension -ne '.pdb' })
    New-ComponentGroup $groups 'ArabicResources' 'ArabicFolder' $folderFiles
}

$templateText = [System.IO.File]::ReadAllText($template)
if (-not $templateText.Contains('<!-- ApplicationFiles -->')) {
    Write-Output 'The installer template has lost its marker. The file list cannot be placed.'
    exit 1
}

$generated = $templateText.Replace('<!-- ApplicationFiles -->', ($groups -join "`n"))

$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($productWxs, $generated, $encoding)

Write-Output ('installer source: ' + $productWxs)

# Preserve an earlier package rather than letting a successful incremental no-op pass as a
# fresh build. Only this version's exact output is eligible for installation.
if (Test-Path -LiteralPath $msiPath) {
    $scratch = Join-Path $root 'build\scratch'
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    Move-Item -LiteralPath $msiPath -Destination (Join-Path $scratch ($outputName + '-' + [guid]::NewGuid().ToString('N') + '.msi'))
}

# The publish folder is passed without a trailing backslash: MSBuild reads a trailing separator
# as the start of the next argument.
& $dotnet build $wixProject -c Release --nologo -v q -t:Rebuild `
    -p "ProductVersion=$Version" -p "ProductCode=$productCode" -p "PublishDir=$publishDir" `
    -p "IconDir=$iconDir" -p "ProductWxs=$productWxs" -p "OutputName=$outputName" `
    -o $installerDir
if ($LASTEXITCODE -ne 0) {
    Write-Output ('the installer build failed with exit code ' + $LASTEXITCODE)
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
    throw ('The build did not produce the expected MSI: ' + $msiPath)
}

# Verify the package itself, rather than trusting its filename or MSBuild's incremental state.
$installer = $database = $view = $record = $null
try {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($msiPath, 0)
    $expectedProperties = @{
        ProductVersion = $Version
        ProductCode = $productCode
        UpgradeCode = '{8F3C2A16-4B7E-4D2E-9A55-6C1D3E7B0A21}'
    }
    foreach ($name in $expectedProperties.Keys) {
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$name'")
        [void]$view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record -or $record.StringData(1) -ne $expectedProperties[$name]) {
            throw ('The built MSI has an unexpected ' + $name + '. Refusing to report it as usable.')
        }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        $record = $null
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        $view = $null
    }
} finally {
    foreach ($comObject in @($record, $view, $database, $installer)) {
        if ($null -ne $comObject) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) }
    }
}

$msi = Get-Item -LiteralPath $msiPath
Write-Output ''
Write-Output ('verified package: version ' + $Version + ', ProductCode ' + $productCode)
Write-Output ('installer: ' + $msi.FullName)
Write-Output ('sha256: ' + (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash)
Write-Output ('size: ' + [math]::Round($msi.Length / 1MB, 2) + ' MB')
exit 0
