param([string]$DeviceIp = '127.0.0.1', [string]$Pin, [switch]$Update)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environment = & (Join-Path $PSScriptRoot 'Get-UwpEnvironment.ps1')
if ($DeviceIp -eq '127.0.0.1' -and -not $environment.UsbConnectivityInstalled) {
    throw 'USB 部署缺少“USB 设备连接性”。请在 VS2017 安装器的单个组件中安装它。'
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'src\FanqieReader\Package.appxmanifest') -Raw
$packageName = 'FanqieReader_' + [string]$manifest.Package.Identity.Version + '_ARM'
$drop = Join-Path $projectRoot "artifacts\deliverables\${packageName}_Test"
$package = Join-Path $drop "$packageName.appx"
if (-not (Test-Path -LiteralPath $package)) { throw '请先生成已签名的 ARM 测试包。' }
$tool = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin\10.0.19041.0\x86\WinAppDeployCmd.exe'
$dependencies = @(Get-ChildItem -LiteralPath (Join-Path $drop 'Dependencies\ARM') -Filter '*.appx' | ForEach-Object FullName)
$operation = if ($Update) { 'update' } else { 'install' }
$arguments = @($operation, '-file', $package, '-dependency') + $dependencies + @('-ip', $DeviceIp, '-connecttimeout', '15')
if ($Pin) { $arguments += @('-pin', $Pin) }
& $tool @arguments
if ($LASTEXITCODE -ne 0) { throw "真机部署失败，退出码 $LASTEXITCODE" }
