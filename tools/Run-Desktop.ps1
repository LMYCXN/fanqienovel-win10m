param([string]$SdkVersion)
$ErrorActionPreference = 'Stop'

# The Appx module runs in Windows PowerShell, including when called from pwsh.
if ($PSVersionTable.PSEdition -eq 'Core') {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($SdkVersion) { $arguments += @('-SdkVersion', $SdkVersion) }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @arguments
    if ($LASTEXITCODE -ne 0) { throw "Desktop deployment failed: $LASTEXITCODE" }
    return
}

$developerMode = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if ($developerMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'Enable Developer Mode in Windows Settings, then run this script again.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$layoutRoot = Join-Path $projectRoot 'artifacts\desktop\x64-Debug'
Get-Process -Name FanqieReader -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -eq (Join-Path $layoutRoot 'FanqieReader.exe')
} | Stop-Process

& (Join-Path $PSScriptRoot 'Build-Uwp.ps1') -Platform x64 -Configuration Debug -SdkVersion $SdkVersion

# Use MSBuild's deployment recipe: Debug needs the CoreCLR launcher and manifest,
# which differ from the unsigned APPX produced by the packaging target.
$recipePath = Join-Path $projectRoot 'src\FanqieReader\bin\x64\Debug\FanqieReader.build.appxrecipe'
[xml]$recipe = Get-Content -LiteralPath $recipePath -Raw
$ns = [System.Xml.XmlNamespaceManager]::new($recipe.NameTable)
$ns.AddNamespace('m', $recipe.DocumentElement.NamespaceURI)
$files = @($recipe.SelectNodes('//m:AppxPackagedFile | //m:AppXManifest', $ns))
[void][System.IO.Directory]::CreateDirectory($layoutRoot)
foreach ($file in $files) {
    $sourcePath = [Uri]::UnescapeDataString([string]$file.Include)
    $targetPath = Join-Path $layoutRoot ([string]$file.PackagePath)
    [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $targetPath))
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $layoutRoot 'AppxManifest.xml') -Raw
foreach ($dependency in @($recipe.SelectNodes('//m:ResolvedSDKReference[m:Architecture="x64"]', $ns))) {
    $required = $manifest.Package.Dependencies.PackageDependency | Where-Object { $_.Name -eq $dependency.Name }
    if (-not $required) { continue }
    $installed = Get-AppxPackage -Name $dependency.Name | Where-Object {
        $_.Architecture -eq 'X64' -and $_.Publisher -eq $required.Publisher -and [version]$_.Version -ge [version]$required.MinVersion
    }
    if (-not $installed) {
        $dependencyPath = [Uri]::UnescapeDataString([string]$dependency.AppxLocation)
        Add-AppxPackage -Path $dependencyPath
    }
}

Add-AppxPackage -Register (Join-Path $layoutRoot 'AppxManifest.xml') -ForceApplicationShutdown
$package = Get-AppxPackage -Name $manifest.Package.Identity.Name | Where-Object { $_.Publisher -eq $manifest.Package.Identity.Publisher }
if (-not $package) { throw 'Desktop package registration was not found.' }
$appId = '{0}!{1}' -f $package.PackageFamilyName, $manifest.Package.Applications.Application.Id
Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$appId"
Write-Host "Started x64 Debug: $appId"
