# C# API smoke check with installed metadata; does not build or run UWP/XAML.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appRoot = Join-Path $projectRoot 'src\FanqieReader'
$output = Join-Path $projectRoot 'artifacts\source-check'
[void][System.IO.Directory]::CreateDirectory($output)
$environment = & (Join-Path $PSScriptRoot 'Get-UwpEnvironment.ps1')
if ($environment.CompilerPaths.Count -eq 0) { throw '未找到 C# 编译器。' }
$metadataRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
$metadata = Get-ChildItem -LiteralPath $metadataRoot -Directory | Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $metadata) { throw '未找到可供源码检查使用的 Windows.winmd。' }

[xml]$project = Get-Content -LiteralPath (Join-Path $appRoot 'FanqieReader.csproj') -Raw
$sources = @()
foreach ($item in $project.Project.ItemGroup.ChildNodes) {
    if (-not $item.Include -or $item.LocalName -eq 'PackageReference') { continue }
    $path = Join-Path $appRoot $item.Include
    if (-not (Test-Path -LiteralPath $path)) { throw ('工程引用缺失：' + $item.Include) }
    if ($item.LocalName -eq 'Compile') { $sources += $path }
}

[xml]$page = Get-Content -LiteralPath (Join-Path $appRoot 'MainPage.xaml') -Raw
$codeBehind = Get-Content -LiteralPath (Join-Path $appRoot 'MainPage.xaml.cs') -Raw
$fields = @()
foreach ($element in $page.SelectNodes('//*')) {
    $name = $element.GetAttribute('Name', 'http://schemas.microsoft.com/winfx/2006/xaml')
    if ($name) { $fields += 'private Windows.UI.Xaml.Controls.' + $element.LocalName + ' ' + $name + ';' }
    foreach ($attribute in $element.Attributes) {
        if ($attribute.LocalName -in @('Click','Toggled','KeyDown','SelectionChanged','ItemClick')) {
            if ($codeBehind -notmatch ('\b' + [regex]::Escape($attribute.Value) + '\s*\(')) { throw ('事件处理函数缺失：' + $attribute.Value) }
        }
    }
}
$stubPath = Join-Path $output 'XamlCompileStubs.cs'
$stub = "// Compile-only stand-ins for generated fields. Never included in the app project.`nnamespace FanqieReader { public sealed partial class MainPage { private void InitializeComponent() {} " + ($fields -join "`n") + " } sealed partial class App { private void InitializeComponent() {} } }"
[System.IO.File]::WriteAllText($stubPath, $stub)
$sources += $stubPath
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$arguments = @('/nologo', '/noconfig', '/nostdlib+', '/langversion:7.3', '/nowarn:0649', '/target:library', ('/out:' + (Join-Path $output 'UwpSourceSmoke.dll')))
foreach ($assembly in @('mscorlib','System','System.Core','System.Runtime','System.Runtime.WindowsRuntime','System.Runtime.WindowsRuntime.UI.Xaml','System.Runtime.InteropServices.WindowsRuntime','System.ObjectModel','System.Collections','System.Threading.Tasks')) {
    $path = Join-Path $frameworkRoot ($assembly + '.dll')
    if (Test-Path -LiteralPath $path) { $arguments += '/reference:' + $path }
}
$arguments += '/reference:' + $metadata
& $environment.CompilerPaths[0] @arguments @sources
if ($LASTEXITCODE -ne 0) { throw "UWP C# 源码检查失败，退出码 $LASTEXITCODE" }
Write-Output "PASS: project files, XAML event names, C# source API smoke check against $metadata"
Write-Output 'NOT CHECKED: XAML compilation, SDK 14393 compatibility, APPX generation, device runtime.'
