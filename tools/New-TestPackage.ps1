param([string]$SdkVersion = '10.0.19041.0')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$sourceManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'src\FanqieReader\Package.appxmanifest') -Raw
$packageVersion = [string]$sourceManifest.Package.Identity.Version
$packageName = "FanqieReader_${packageVersion}_ARM"
$sourceRoot = Join-Path $projectRoot "src\FanqieReader\AppPackages\${packageName}_Test"
$sourcePackage = Join-Path $sourceRoot "$packageName.appx"
if (-not (Test-Path -LiteralPath $sourcePackage)) { throw '请先构建 ARM Release。' }
$signTool = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\$SdkVersion\x64\signtool.exe"
if (-not (Test-Path -LiteralPath $signTool)) { throw '没有找到 SDK 签名工具。' }
$certificateRoot = Join-Path $projectRoot 'artifacts\dev-certificate'
$dropRoot = Join-Path $projectRoot "artifacts\deliverables\${packageName}_Test"
[void][System.IO.Directory]::CreateDirectory($certificateRoot)
[void][System.IO.Directory]::CreateDirectory($dropRoot)
$pfxPath = Join-Path $certificateRoot 'FanqieReaderDev.pfx'
$cerPath = Join-Path $dropRoot 'FanqieReaderDev.cer'
if (-not (Test-Path -LiteralPath $pfxPath)) {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=FanqieReaderDev', $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true))
        $usages = [System.Security.Cryptography.OidCollection]::new()
        [void]$usages.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
        $request.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $true))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(1))
        try { [System.IO.File]::WriteAllBytes($pfxPath, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, '')) }
        finally { $certificate.Dispose() }
    }
    finally { $rsa.Dispose() }
}
$keyFlags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet -bor [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, '', $keyFlags)
try {
    if ($certificate.Subject -ne 'CN=FanqieReaderDev' -or $certificate.NotAfter -lt [DateTime]::Now) { throw '测试证书主题或有效期不符合要求。' }
    # SDK 19041 SignTool cannot import the modern default PFX encryption.
    $legacyPbe = [System.Security.Cryptography.PbeParameters]::new([System.Security.Cryptography.PbeEncryptionAlgorithm]::TripleDes3KeyPkcs12, [System.Security.Cryptography.HashAlgorithmName]::SHA1, 2000)
    [System.IO.File]::WriteAllBytes($pfxPath, $certificate.ExportPkcs12($legacyPbe, ''))
    [System.IO.File]::WriteAllBytes($cerPath, $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $thumbprint = $certificate.Thumbprint
}
finally { $certificate.Dispose() }
$package = Join-Path $dropRoot "$packageName.appx"
Copy-Item -LiteralPath $sourcePackage -Destination $package -Force
& $signTool sign /fd SHA256 /f $pfxPath $package
if ($LASTEXITCODE -ne 0) { throw "APPX 签名失败：$LASTEXITCODE" }

$dependencyRoot = Join-Path $dropRoot 'Dependencies\ARM'
[void][System.IO.Directory]::CreateDirectory($dependencyRoot)
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Dependencies\ARM') -Filter '*.appx' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dependencyRoot -Force }
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $manifestReader = [System.IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $manifestReader.ReadToEnd() } finally { $manifestReader.Dispose() }
    if ($manifest.Package.Identity.ProcessorArchitecture -ne 'arm') { throw '安装包架构不是 ARM。' }
    if ($manifest.Package.Dependencies.TargetDeviceFamily.MinVersion -ne '10.0.10586.0') { throw '安装包最低版本意外变化。' }
    # Release embeds compiled XAML in resources.pri instead of loose XBF files.
    $validationRoot = Join-Path $projectRoot 'artifacts\package-validation'
    [void][System.IO.Directory]::CreateDirectory($validationRoot)
    $priEntry = $archive.GetEntry('resources.pri')
    if (-not $priEntry) { throw '安装包缺少资源索引。' }
    $priPath = Join-Path $validationRoot 'resources.pri'
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($priEntry, $priPath, $true)
    $priDump = Join-Path $validationRoot 'resources.xml'
    $makePri = Join-Path (Split-Path -Parent $signTool) 'makepri.exe'
    & $makePri dump /if $priPath /of $priDump /o | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Select-String -LiteralPath $priDump -SimpleMatch 'name="MainPage.xbf"' -Quiet)) { throw '资源索引未包含已编译的主页面。' }
    $signatureStream = $archive.GetEntry('AppxSignature.p7x').Open()
    $buffer = [System.IO.MemoryStream]::new()
    try { $signatureStream.CopyTo($buffer); $signatureBytes = $buffer.ToArray() }
    finally { $buffer.Dispose(); $signatureStream.Dispose() }
    if ([System.Text.Encoding]::ASCII.GetString($signatureBytes, 0, 4) -ne 'PKCX') { throw 'APPX 签名格式异常。' }
    $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$signatureBytes[4..($signatureBytes.Length - 1)])
    $cms.CheckSignature($true)
    if ($cms.SignerInfos[0].Certificate.Thumbprint -ne $thumbprint) { throw 'APPX 签名证书不匹配。' }
    $evidence = [pscustomobject]@{
        BuiltAt = [DateTimeOffset]::Now.ToString('o')
        Configuration = 'Release (.NET Native)'
        Architecture = [string]$manifest.Package.Identity.ProcessorArchitecture
        MinimumVersion = [string]$manifest.Package.Dependencies.TargetDeviceFamily.MinVersion
        TargetVersion = [string]$manifest.Package.Dependencies.TargetDeviceFamily.MaxVersionTested
        PackageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
        TestCertificateThumbprint = $thumbprint
        CmsSignatureValidIgnoringTrustChain = $true
        CompiledXamlInPri = $true
        PhoneRuntimeVerified = $false
        Dependencies = @(Get-ChildItem -LiteralPath $dependencyRoot -Filter '*.appx' | ForEach-Object Name)
    }
    $evidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $dropRoot 'build-evidence.json') -Encoding UTF8
    $evidence | Format-List
}
finally { $archive.Dispose() }
Write-Output "测试包目录：$dropRoot"

# Deliver the corresponding application sources and GPL notices with the package.
$sourceZip = Join-Path $dropRoot 'CorrespondingSource.zip'
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
$sourceArchive = [System.IO.Compression.ZipFile]::Open($sourceZip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $appRoot = Join-Path $projectRoot 'src\FanqieReader'
    [xml]$project = Get-Content -LiteralPath (Join-Path $appRoot 'FanqieReader.csproj') -Raw
    $files = @((Join-Path $projectRoot 'FanqieReader.sln'), (Join-Path $appRoot 'FanqieReader.csproj'), (Join-Path $projectRoot 'README.md'), (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'), (Join-Path $projectRoot 'CODEX_PROJECT_CONTEXT.md'), (Join-Path $projectRoot '.gitignore'))
    foreach ($item in $project.Project.ItemGroup.ChildNodes) {
        if ($item.Include -and $item.LocalName -ne 'PackageReference') { $files += [System.IO.Path]::GetFullPath((Join-Path $appRoot $item.Include)) }
    }
    foreach ($folder in @('tools','tests','licenses','docs')) {
        $files += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse | ForEach-Object FullName)
    }
    foreach ($file in ($files | Sort-Object -Unique)) {
        $entryName = $file.Substring($projectRoot.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $file, $entryName)
    }
}
finally { $sourceArchive.Dispose() }
@"
FanqieReader $packageVersion / ARM Release
手机开启开发者模式，连接 USB 并解锁。
新安装：在对应源码根目录执行 .\tools\Deploy-Phone.ps1
更新已有应用：执行 .\tools\Deploy-Phone.ps1 -Update
本版本在线入口支持搜索、书籍详情、目录与免费章节阅读。首次阅读需要联网登记阅读密钥。
许可证见 THIRD_PARTY_NOTICES.md 与 GPL-3.0.txt；对应源码见 CorrespondingSource.zip。
"@ | Set-Content -LiteralPath (Join-Path $dropRoot 'INSTALL.txt') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $dropRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses\fanqie-assistant-GPL-3.0.txt') -Destination (Join-Path $dropRoot 'GPL-3.0.txt') -Force
$deliveryZip = "$dropRoot.zip"
if (Test-Path -LiteralPath $deliveryZip) { Remove-Item -LiteralPath $deliveryZip }
[System.IO.Compression.ZipFile]::CreateFromDirectory($dropRoot, $deliveryZip)
Write-Output "测试包 ZIP：$deliveryZip"
