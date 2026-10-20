# Fanqie Reader for Windows 10 Mobile

面向 Lumia 950 / 950 XL 的原生 UWP 阅读原型，也可构建 x64 电脑版。提供原生 C# 的在线搜索、详情、目录和章节阅读。

## 📋 当前可检查的功能

- 字号、全应用夜间模式（含电脑版标题栏）、章节进度保存和继续阅读
- 系统返回键：正文 → 目录 → 书籍列表
- 在线入口：APP 书籍搜索、服务端游标分页、详情、APP 目录与正文
- 简介默认四行，可展开／收起，并与章节列表一起滚动
- 目录每页 100 章，支持上一页／下一页和章节范围选择；从正文返回目录时定位到当前章节所在页
- 原生设备登记、密钥协商、AES 解密、gzip 解压；加密保存设备状态与密钥
- 章节加载进度、取消加载、在线阅读进度保存与恢复

在线阅读首次登记匿名设备与内容密钥，后续沿用加密保存的状态。目录和正文均走 APP 接口；章节是否能读取依据正文接口实际返回，不再用网页目录锁定标记拦截。完整性依据服务端段落声明与实际 HTML 段落数核对，单项网络请求保留 20 秒超时与取消。

## 🔧 构建环境

| 项目 | 设置 |
| --- | --- |
| 推荐 IDE | Visual Studio 2017 **15.9** |
| 工作负载 | 通用 Windows 平台开发，含 C# / XAML、.NET Native、ARM 工具 |
| 手机 USB 部署组件 | 单个组件 → USB 设备连接性（USB Device Connectivity） |
| Windows SDK | 19041 已通过 ARM Debug / Release 构建；14393 为备用选项 |
| Target / Minimum | 默认 Target 19041；Minimum 保持 10586 |
| NuGet | Microsoft.NETCore.UniversalWindowsPlatform 6.2.14 |
| 手机架构 | ARM（32 位） |
| PC 调试架构 | x86 或 x64 |

UWP 6.2.x 的 VS2017 支持要求 15.9，见 [Microsoft 发布说明](https://github.com/microsoft/dotnet/blob/main/releases/UWP/net-native2.2/README.md)。安装了 SDK 的 bin 目录并不等于拥有该版本的 UAP 引用文件与构建组件。

当前安装器可能只列出 SDK 19041。可以先安装该 SDK 与 UWP 工作负载，构建脚本会在 14393、19041 中选择已完整安装的版本，也可显式指定。编译 SDK 不等于最低运行版本；较新 API 仍需适配，最终以 ARM 构建与手机测试为准，见 [Microsoft 版本选择说明](https://learn.microsoft.com/en-us/windows/uwp/updates-and-versions/choose-a-uwp-version)。

2026-10-19 更新：VS2017 15.9.83 位于 `D:\develop\vs2017`，SDK 19041 已通过 XAML 编译、ARM Debug 和 ARM Release（.NET Native）构建。0.1.0 已通过 USB 部署，用户确认 Lumia 950 / 15254.603 上启动、演示阅读正常，三项联网诊断通过。此结果来自用户反馈，未收集诊断 JSON。

0.2.0 已成功更新到 Lumia，用户确认搜索、简介和目录都正常。PC 原生元数据验证返回 6 本书和 351 章。

0.3.0 已完成 ARM Release 构建、签名和手机更新，用户确认“正文、翻章和继续阅读都正常”。核心测试 62 项断言通过（GET / POST 签名、TT 注册封装、AES / gzip、文本解析与完整性等）；元数据与加密状态存储测试共 14 项断言通过。PC 原生免费章节实测返回 122 段、4223 个文本字符，其中汉字 3657 个；实际段落与服务端总段落、免费段落声明一致，加密状态可重新加载。记录见 [手机验收](docs/phone-validation-0.3.0.json)，交付包为 `artifacts/deliverables/FanqieReader_0.3.0.0_ARM_Test.zip`。

此前 USB 部署返回 `0x8007274D`。用户在 VS2017 中补装“USB 设备连接性”（`Microsoft.VisualStudio.Component.Windows10SDK.IpOverUsb`）后，IpOverUsbSvc 从旧 8.0 版本升级为 10.0.19041.5609，部署成功。

2026-10-20，0.4.0 修复：搜索切换至 APP“书籍”分类并兼容不同书籍卡片，直接搜“神通者”能返回天蚕土豆的作品；移除网页锁定状态拦截，改为 APP 目录；简介放入可滚动的目录头部，支持展开／收起；删除诊断页及其实现。核心 62 项断言、元数据与加密存储 16 项断言通过，原生联网验证通过两种搜索词、分页及《神通者》第49章、《部族荣光》第50章。ARM Release 构建、签名与 USB 更新成功，用户确认手机搜索、简介展开／收起与滚动、较后章节、返回目录及诊断页移除“全部正常”。记录见 [0.4.0 手机验收](docs/phone-validation-0.4.0.json)，交付 ZIP 为 `artifacts/deliverables/FanqieReader_0.4.0.0_ARM_Test.zip`。

## 🚀 编译与部署

在 PowerShell 中从项目根目录执行：

```powershell
# 查看 UWP 构建前提
.\tools\Get-UwpEnvironment.ps1

# 不需要 UWP 工具链的核心逻辑测试
.\tools\Test-Core.ps1

# 元数据解析测试；加 -Online 可请求一次搜索、详情和目录
.\tools\Test-Metadata.ps1

# 复用本地加密状态验证一个免费章节；首次执行登记匿名设备及密钥
.\tools\Test-Metadata.ps1 -FreeContent

# 回归验证书名／作者搜索、分页与较后章节
.\tools\Test-Metadata.ps1 -Regressions

# 使用本机已有 SDK 检查 C# API 与工程文件
.\tools\Test-UwpSource.ps1

# 补齐工具链后构建 ARM Debug
.\tools\Build-Uwp.ps1 -Platform ARM -Configuration Debug

# 只安装了 SDK 19041 时，也可显式选择它
.\tools\Build-Uwp.ps1 -Platform ARM -Configuration Debug -SdkVersion 10.0.19041.0

# Release 测试包及本地开发签名（签名脚本使用 PowerShell 7.5+）
.\tools\Build-Uwp.ps1 -Platform ARM -Configuration Release
.\tools\New-TestPackage.ps1

# 连接手机后部署；需要时加 -Pin 参数提供本次配对码
.\tools\Deploy-Phone.ps1

# 更新手机中已安装的旧版本，保留本地设置
.\tools\Deploy-Phone.ps1 -Update
```

`Test-UwpSource.ps1` 使用独立编译占位文件检查代码与现有 SDK API，**不执行 XAML 编译**，也不证明 SDK 14393 或手机运行兼容性。其临时文件位于 `artifacts/`，不包含在应用工程中。

在 VS2017 中打开 `FanqieReader.sln`，允许 NuGet 还原，选择 `Debug | ARM`。若只有 SDK 19041，可将项目的目标版本改为 19041，最低版本保持 10586；命令行脚本会自动传入所选目标版本。清单中 `MaxVersionTested="10.0.0.0"` 由 UWP 构建工具替换为目标版本。手机开启开发者模式、连接 USB 并解锁后，选择“设备”部署。命令行 Release 包部署已在 Lumia 950 上成功。

工程的原始构建输出默认不签名；`New-TestPackage.ps1` 在 `artifacts/` 创建本地测试证书并签名交付副本，主题与清单中的 `CN=FanqieReaderDev` 匹配。私钥 PFX 留在 `artifacts/dev-certificate/`，不进入交付目录。测试证书未自动安装为系统信任根。签名校验记录、最低版本、包哈希及依赖清单位于交付目录的 `build-evidence.json`。

Release 依赖为 ARM 的 `Microsoft.NET.Native.Framework.1.7`、`Microsoft.NET.Native.Runtime.1.7` 和 `Microsoft.VCLibs.140.00`。Debug 使用不同运行时依赖，不混用两套依赖目录。

## 💻 在电脑上修改与测试

电脑与手机共用 `FanqieReader.sln` 和全部应用源码。在 Windows 设置中开启“开发者模式”后，在 VS2017 中打开解决方案，选择 `Debug | x64`，启动目标选择“本地计算机”（Local Machine），按 **F5** 构建、部署并启动调试。可以在 C# 代码中设置断点；停止调试、修改代码后再次按 F5。

如果工具栏没有 x64，进入“生成 → 配置管理器”选择已有的 x64 平台。测试手机时切回 `Debug | ARM | 设备`。

只想快速运行当前代码时，从项目根目录执行：

```powershell
.\tools\Run-Desktop.ps1
```

脚本会构建 x64 Debug，按 MSBuild 的部署清单准备调试文件，安装缺失的 x64 调试运行时，注册到当前 Windows 用户并启动应用。首次需要本机已安装的 UWP 工具链和 Windows 开发者模式；开发部署无需签名或导入测试证书。可以从 PowerShell 7 或 Windows PowerShell 运行，脚本自动使用 Windows PowerShell 的 Appx 模块。

修改代码后重新执行同一命令；脚本会关闭由它启动的旧实例并更新应用，保留阅读进度与设置。部署目录为 `artifacts/desktop/x64-Debug/`，运行期间保留该目录。此命令用于运行验证，需要断点调试时使用 VS 的 F5。

电脑上先检查搜索、目录、正文、翻章、夜间模式及关闭重开后的继续阅读，再部署到手机检查触屏布局和手机系统兼容性。

## 🧪 真机验收顺序

1. 启动后直接进入阅读界面，确认诊断页已移除。
2. 直接搜索“神通者”，确认天蚕土豆的作品出现；试作者搜索和“加载更多”。
3. 打开书籍，检查简介默认四行、展开／收起，以及简介与目录连续滚动。
4. 打开较后章节，例如《神通者》第49章或《部族荣光》第50章，检查正文与翻章。
5. 检查字号、夜间模式，以及系统返回键依次返回目录与书籍列表。
6. 关闭重开应用，检查“继续上次阅读”恢复章节。

进度目前保存到**章节**，不包含章内滚动位置。应用仅提供在线阅读，章节尚无磁盘缓存，独立书架未实现。旧版在线阅读进度继续保留，旧版离线演示进度不再恢复。新格式或接口请求失败会显示具体错误。

## 🏗️ 代码结构

```text
FanqieReader.sln
src/FanqieReader/
  Core/                  数据模型、章节状态、签名与正文编解码
  Services/              原生网络、在线源、设备登记与加密状态、本地设置
  MainPage.xaml          书籍、简介、目录与阅读界面
  Package.appxmanifest   10586 最低版本与 internetClient
tests/CoreTests.cs        无第三方依赖的核心测试
tools/                   环境检查、构建、源码检查、图标生成
docs/                    技术可行性报告
research/                协议研究与固定版本上游源码
```

协议研究与 UWP 应用分开存放；签名、设备协议与正文算法移植自固定版本上游，许可证与修改说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。交付包包含 GPL v3 许可证及 `CorrespondingSource.zip`。

## 🔗 研究与后续工作

Lumia 已通过搜索、详情、目录、免费正文、翻章与重启继续阅读验收，原生阅读原型的核心可行性已验证。后续重点是扩大书籍样本、跨日稳定性、缓存与书架。证据与适用范围见[技术报告](docs/TECHNICAL_FEASIBILITY_REPORT.md)。

网络传输保留系统证书验证，禁用 Cookie、自动重定向及认证弹窗。10586 不提供 `HttpCacheReadBehavior.NoCache`；代码在运行时检测该枚举值，旧系统使用 `MostRecent`，见 [Microsoft API 版本说明](https://learn.microsoft.com/en-us/uwp/api/windows.web.http.filters.httpcachereadbehavior)。
