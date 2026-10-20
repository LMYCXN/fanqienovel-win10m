param([switch]$Online, [switch]$FreeContent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environment = & (Join-Path $PSScriptRoot 'Get-UwpEnvironment.ps1')
$output = Join-Path $projectRoot 'artifacts\metadata-tests'
[void][System.IO.Directory]::CreateDirectory($output)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$metadata = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata\10.0.19041.0\Windows.winmd'
$executable = Join-Path $output 'MetadataTests.exe'
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:7.3', '/warnaserror+', '/target:exe', "/out:$executable", "/reference:$metadata")
foreach ($name in @('mscorlib', 'System', 'System.Core', 'System.Runtime', 'System.Runtime.WindowsRuntime', 'System.Runtime.InteropServices.WindowsRuntime', 'System.ObjectModel', 'System.Collections', 'System.Threading.Tasks')) {
    $path = Join-Path $framework "$name.dll"
    if (Test-Path -LiteralPath $path) { $arguments += "/reference:$path" }
}
$sources = @('Core\BookModels.cs','Core\IBookSource.cs','Core\RequestSigner.cs','Core\ContentCodec.cs','Services\OnlineBookSource.cs','Services\NetworkDiagnostics.cs','Services\OnlineTransport.cs','Services\DeviceSession.cs') | ForEach-Object { Join-Path $projectRoot "src\FanqieReader\$_" }
$sources += Join-Path $projectRoot 'tests\MetadataTests.cs'
& $environment.CompilerPaths[0] @arguments @sources
if ($LASTEXITCODE -ne 0) { throw "元数据测试编译失败：$LASTEXITCODE" }
if ($Online -and $FreeContent) { throw '请选择一种在线测试模式。' }
if ($FreeContent) {
    $sessionRoot = Join-Path $projectRoot 'artifacts\content-session'
    [void][System.IO.Directory]::CreateDirectory($sessionRoot)
    & $executable --free-content $sessionRoot
} elseif ($Online) { & $executable --online }
else {
    $fixtureRoot = Join-Path $output 'store-fixture'
    [void][System.IO.Directory]::CreateDirectory($fixtureRoot)
    & $executable --store-test $fixtureRoot
}
if ($LASTEXITCODE -ne 0) { throw "元数据测试失败：$LASTEXITCODE" }
