$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environment = & (Join-Path $PSScriptRoot 'Get-UwpEnvironment.ps1')
if ($environment.CompilerPaths.Count -eq 0) { throw '未找到 Visual Studio Roslyn C# 编译器。' }
$output = Join-Path $projectRoot 'artifacts\core-tests'
[void][System.IO.Directory]::CreateDirectory($output)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\FanqieReader\Core') -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $projectRoot 'tests\CoreTests.cs'
$executable = Join-Path $output 'CoreTests.exe'
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:7.3', '/warnaserror+', '/target:exe', "/out:$executable")
foreach ($name in @('mscorlib', 'System', 'System.Core')) { $arguments += '/reference:' + (Join-Path $framework "$name.dll") }
& $environment.CompilerPaths[0] @arguments @sources
if ($LASTEXITCODE -ne 0) { throw "C# 编译失败，退出码 $LASTEXITCODE" }
& $executable (Join-Path $projectRoot 'tests\signature-vectors.tsv') (Join-Path $projectRoot 'tests\content-vectors.tsv')
if ($LASTEXITCODE -ne 0) { throw "核心测试失败，退出码 $LASTEXITCODE" }
