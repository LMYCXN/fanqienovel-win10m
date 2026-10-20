param(
    [ValidateSet('ARM', 'x86', 'x64')][string]$Platform = 'ARM',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$SdkVersion
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environment = & (Join-Path $PSScriptRoot 'Get-UwpEnvironment.ps1') -TargetSdk $SdkVersion
if (-not $environment.CanAttemptUwpBuild) {
    $environment | Format-List | Out-Host
    throw "缺少 UWP XAML 构建组件或完整 SDK $($environment.TargetSdk)。请先按 README 补齐工具链。"
}
$matchingBuild = @($environment.MSBuildPaths | Where-Object {
    $candidateRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $_)))
    @($environment.UwpXamlTargets | Where-Object { $_.StartsWith($candidateRoot, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
})
if ($matchingBuild.Count -eq 0) { throw '没有找到与 UWP XAML 工具匹配的 MSBuild。' }
$packageRoot = Join-Path $projectRoot 'artifacts\packages'
& $matchingBuild[0] (Join-Path $projectRoot 'FanqieReader.sln') /restore /t:Build "/p:Configuration=$Configuration" "/p:Platform=$Platform" "/p:TargetPlatformVersion=$($environment.TargetSdk)" "/p:RestorePackagesPath=$packageRoot" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "UWP 构建失败，退出码 $LASTEXITCODE" }
