param([string]$OutFile, [string]$TargetSdk)
$ErrorActionPreference = 'Stop'

$vsRoots = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ } | Select-Object -Unique
$installRoots = @()
$usbConnectivityInstalls = @()
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $instances = & $vswhere -all -products '*' -format json -utf8 | ConvertFrom-Json
    $installRoots += @($instances | Sort-Object { [version]$_.installationVersion } | ForEach-Object installationPath)
    $usbConnectivityInstalls = @(& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.Windows10SDK.IpOverUsb -property installationPath | Where-Object { $_ })
}
foreach ($root in $vsRoots) {
    foreach ($version in @('2017', '2019', '2022')) {
        foreach ($edition in @('Community', 'Professional', 'Enterprise', 'BuildTools')) {
            $installRoots += Join-Path $root "Microsoft Visual Studio\$version\$edition"
        }
    }
}
$msbuildPaths = @()
$compilerPaths = @()
$xamlTargets = @()
foreach ($install in @($installRoots | Select-Object -Unique)) {
            foreach ($buildVersion in @('15.0', 'Current')) {
                $buildRoot = Join-Path $install "MSBuild\$buildVersion\Bin"
                $candidate = Join-Path $buildRoot 'MSBuild.exe'
                if (Test-Path -LiteralPath $candidate) { $msbuildPaths += $candidate }
                $compiler = Join-Path $buildRoot 'Roslyn\csc.exe'
                if (Test-Path -LiteralPath $compiler) { $compilerPaths += $compiler }
            }
            $targetsRoot = Join-Path $install 'MSBuild\Microsoft\WindowsXaml'
            if (Test-Path -LiteralPath $targetsRoot) {
                $xamlTargets += @(Get-ChildItem -LiteralPath $targetsRoot -Filter 'Microsoft.Windows.UI.Xaml.CSharp.targets' -Recurse | ForEach-Object FullName)
            }
}
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$candidateVersions = @('10.0.19041.0', '10.0.14393.0')
if ($TargetSdk -and $TargetSdk -notin $candidateVersions) { throw '构建检查支持 SDK 14393 或 19041。' }
$sdkChecks = @(foreach ($version in $candidateVersions) {
    $platformPresent = Test-Path -LiteralPath (Join-Path $sdkRoot "Platforms\UAP\$version\Platform.xml")
    $referencesPresent = Test-Path -LiteralPath (Join-Path $sdkRoot "References\$version")
    $metadataPresent = Test-Path -LiteralPath (Join-Path $sdkRoot "UnionMetadata\$version\Windows.winmd")
    [pscustomobject]@{
        Version = $version
        PlatformPresent = $platformPresent
        ReferencesPresent = $referencesPresent
        MetadataPresent = $metadataPresent
        Complete = $platformPresent -and $referencesPresent -and $metadataPresent
    }
})
$availableSdks = @($sdkChecks | Where-Object Complete | ForEach-Object Version)
if (-not $TargetSdk) { $TargetSdk = if ($availableSdks.Count -gt 0) { $availableSdks[0] } else { $candidateVersions[0] } }
$selectedSdk = $sdkChecks | Where-Object { $_.Version -eq $TargetSdk } | Select-Object -First 1
$result = [pscustomobject]@{
    CheckedAt = [DateTimeOffset]::Now.ToString('o')
    MSBuildPaths = @($msbuildPaths | Select-Object -Unique)
    CompilerPaths = @($compilerPaths | Select-Object -Unique)
    UwpXamlTargets = @($xamlTargets | Select-Object -Unique)
    UsbConnectivityInstalled = $usbConnectivityInstalls.Count -gt 0
    TargetSdk = $TargetSdk
    AvailableSdks = $availableSdks
    SdkCandidates = $sdkChecks
    SdkPlatformPresent = $selectedSdk.PlatformPresent
    SdkReferencesPresent = $selectedSdk.ReferencesPresent
    SdkMetadataPresent = $selectedSdk.MetadataPresent
    CanAttemptUwpBuild = $msbuildPaths.Count -gt 0 -and $xamlTargets.Count -gt 0 -and $selectedSdk.Complete
}
if ($OutFile) {
    $parent = Split-Path -Parent $OutFile
    if ($parent) { [void][System.IO.Directory]::CreateDirectory($parent) }
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutFile -Encoding UTF8
}
$result
