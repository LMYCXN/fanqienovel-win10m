param([switch]$Install)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installerRoot = Join-Path $projectRoot 'artifacts\installers'
[void][System.IO.Directory]::CreateDirectory($installerRoot)
$bootstrapper = Join-Path $installerRoot 'vs2017_community.exe'
Invoke-WebRequest -Uri 'https://aka.ms/vs/15/release/vs_community.exe' -OutFile $bootstrapper -MaximumRedirection 8
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation(?:,|$)') {
    throw '安装器签名未通过 Microsoft Corporation 验证，停止执行。'
}
$signature | Select-Object Status, @{ Name = 'Signer'; Expression = { $_.SignerCertificate.Subject } } | Format-List | Out-Host
if (-not $Install) { Write-Output "已下载并验证安装器：$bootstrapper"; return }
$arguments = @(
    '--passive', '--wait', '--norestart',
    '--add', 'Microsoft.VisualStudio.Workload.Universal',
    '--add', 'Microsoft.VisualStudio.Component.Windows10SDK.14393',
    '--includeRecommended'
)
Write-Output '开始安装 VS2017 Community、UWP 工作负载和 SDK 14393。可能需要管理员授权及下载较大组件。'
$process = Start-Process -FilePath $bootstrapper -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
$statePath = Join-Path $installerRoot 'installation-state.json'
[pscustomobject]@{ ProcessId = $process.Id; StartedAt = [DateTimeOffset]::Now.ToString('o'); State = 'Running' } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
$process.WaitForExit()
[pscustomobject]@{ ProcessId = $process.Id; FinishedAt = [DateTimeOffset]::Now.ToString('o'); ExitCode = $process.ExitCode; State = 'Exited' } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
if ($process.ExitCode -eq 3010) { Write-Output '安装器要求重启。请保存工作并自行选择重启时间。'; return }
if ($process.ExitCode -ne 0) { throw "安装器退出码 $($process.ExitCode)。请查看 Visual Studio Installer 日志。" }
& (Join-Path $PSScriptRoot 'Get-UwpEnvironment.ps1') -OutFile (Join-Path $projectRoot 'artifacts\uwp-environment.json') | Format-List
