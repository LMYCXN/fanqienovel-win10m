# Fanqie Reader for Windows 10 Mobile — Codex 项目上下文

> 由 ChatGPT 临时聊天整理的开发交接摘要（2026-10-19）。本文件是需求和技术研究备忘，不代表任何第三方 API 已通过实测或获得授权。

## 1. 项目目标

开发一款在 **Lumia 950 / Lumia 950 XL** 上运行的 **Windows 10 Mobile 原生番茄小说阅读客户端**。最终希望手机**独立联网**，直接获取经允许使用的书籍搜索、书籍详情、章节目录与章节正文，不依赖另一台 Android 手机、PC 或自建中转服务器持续在线。

理想功能：

- 搜索番茄小说的书籍（书名、作者）
- 书籍详情、章节目录、按章节在线阅读
- 书架、阅读进度、缓存、字号、夜间模式
- 失败和权限限制的合理提示，不把试读误当全文

**最重要的技术验证：能否从第一方服务在合法、可持续使用的条件下获得完整章节正文。** 搜索、目录成功不代表正文可用。

## 2. 目标设备和技术基线

- 设备：Lumia 950 / 950 XL
- 系统：Windows 10 Mobile；重点在 ARM32 真机上验证
- 技术：C#、UWP、XAML、原生 `HttpClient`、JSON、本地缓存
- 开发工具：优先 Visual Studio 2017（含 UWP 开发工作负载）；Windows 10 SDK 14393
- 推荐初始设置：Target Version 10.0.14393.0；Minimum Version 10.0.10586.0（按 API 调用实际情况复核）
- 部署：ARM APPX；开发者模式 / USB 真机部署；应用清单声明 `internetClient`
- 注意：Windows 10 Mobile 的 15254 分支并不等同于桌面 Windows 10 的 16299；避免将最小版本设置得高于手机支持范围。
- 旧设备 HTTPS/TLS 和根证书、登录控件及系统 API 差异需要单独验证。

## 3. 数据来源研究结论（待复核）

之前讨论的重点：**并非只能抓取网页 HTML**，可以研究番茄小说客户端所用的第一方非公开网络接口，但它们不等于面向第三方开放的官方 API。

需要区分：

1. 官方公开/授权的开发者 API（暂未确认存在供第三方小说阅读客户端使用的版本）；
2. 官方 App / 站点使用的第一方非公开接口（现有第三方项目声称调用这类接口）；
3. 第三方 API、代理或中转服务（不符合最终“Lumia 独立直连”的目标）。

已讨论的参考项目（仅作为研究入口；请重新检查当前 README、LICENSE、代码、依赖及维护状态）：

- `https://github.com/hesan1232/fanqie.koplugin` —— KOReader 插件，值得研究其客户端架构、搜索/目录/正文适配层；是否完全不依赖第三方服务需代码级确认。
- `https://github.com/naiyQAQ/fanqie-assistant` —— TypeScript 用户脚本，声称能连接部分第一方 App 接口；需要确认对浏览器登录态、签名和新版本兼容性的依赖。
- `https://marketplace.visualstudio.com/items?itemName=zwb8926.fanqie-novel` —— VS Code 扩展，可参考仅官方来源模式和失败处理。
- `https://github.com/zhongbai2333/Tomato-Novel-Downloader` —— Web UI / 下载工具，部分模式依赖第三方正文源，**不应默认视为直连可移植方案**。
- `https://github.com/6DDUU6/FQWeb` —— Android/Xposed 伴侣服务，作为技术参考，不是最终方案。

本聊天此前提到的一些项目功能来自各自的项目说明，**尚未进行完整代码审计或 Lumia 真机验证**。不要凭名称中的 `official-api` 就认定存在正式授权。

## 4. 架构建议

```
Lumia 950/950 XL
  └── UWP 原生界面 (Search / Book / Catalog / Reader / Shelf)
        └── IBookSource 抽象层
              ├── DemoBookSource（先验证本机 UI）
              └── DirectFirstPartySource（仅在接口使用权和可用性确认后实现）
                    └── HTTPS → 第一方服务
```

接口建议：

```csharp
public interface IBookSource
{
    Task<IReadOnlyList<Book>> SearchAsync(string keyword);
    Task<Book> GetBookAsync(string bookId);
    Task<IReadOnlyList<Chapter>> GetChaptersAsync(string bookId);
    Task<string> GetChapterTextAsync(string chapterId);
}
```

可先做纯演示数据模式，保证 UI、目录切换、缓存逻辑在手机上运行；第三方接口/逆向研究与 UI 实现解耦。尽量采用原生 XAML 控件呈现正文，不依赖现代 WebView 渲染。

## 5. 第一版里程碑 / 验收

### P0：真机启动
- VS2017 新建 UWP C# Blank App；ARM 构建
- 安装、打开、返回键操作正确
- 在真机上用 `HttpClient` 请求自有 HTTPS 测试服务，确认 TLS / 证书可用

### P1：离线阅读框架
- DemoBookSource：搜索 → 详情 → 目录 → 阅读完整走通
- 翻章、字号、夜间模式、进度记录

### P2：第一方直连可行性验证（关键门槛）
- 对参考项目做源码审阅：请求目标究竟是第一方还是第三方、是否依赖 App 秘钥/浏览器/中转
- 明确接口协议允许的用途、账号权限、内容授权
- 分别验证搜索、目录、**完整正文**，记录试读、人机验证、访问限制及失败情况
- 若不能合法稳定获取正文，保留本地阅读器功能，不通过绕过付费、DRM、CAPTCHA 或安全控制来解决

### P3：UWP 正式接入
- 实现允许使用的直连数据源
- 超时、重试、限速、离线缓存、异常提示
- 真机长时间阅读测试

## 6. 请 Codex 先做什么

1. 先审阅现有代码仓库，确认是否已有 UWP `.sln`、`.csproj`、`Package.appxmanifest`。
2. 若工程不完整，构建可在 VS2017 中打开、针对 ARM / Windows 10 Mobile 的最小 UWP 工程。
3. 先实现 DemoBookSource + 搜索/目录/阅读页面，不要将未经验证的非公开 API 写死在业务层。
4. 将网络能力验证、接口授权核实与 UI 开发分成独立任务。
5. 在 README 中记录如何编译、目标版本、安装方式、现有问题。

## 7. 原讨论中的已有资产

曾提供名为 `FanqieReader_Starter.zip` 的示例源码包。若在当前工作目录中找不到，需要由使用者下载后手动加入 Codex 仓库；不能假定 Codex 自动拥有 ChatGPT 临时聊天里的附件。

## 8. 重要边界

- 不将第三方“官方接口”字样当作正式 API 授权。
- 不绕过 DRM、付费、验证码、风控或访问限制。
- 公开分发前评估代码 LICENSE、小说内容版权和番茄平台服务条款。
- 不宣称接口已可用，直到以允许的方式完成实测。

## 9. 2026-10-19：fanqie-assistant 验证进展

已审阅并固定上游提交 `b62ec5dbe6149a93bca249949549c805ac7da424`（0.0.6），源码位于 `research/fanqie-assistant/`。

- Windows PC / Node 24 实测：本地签名、匿名搜索、详情、目录成功。
- 独立注册一次匿名设备并协商内容密钥，在未登录、未激活会员的情况下成功获取、解密并解压一个免费且未锁定的章节。
- 样本正文检测到 243 个段落，与服务端总段落数及免费段落数一致；不代表其他章节或整个平台书库均可读。
- 核心链路无需常驻 Android 设备或自建中转服务器；UWP 移植具备继续验证的技术依据。
- Lumia TLS、ARM32 构建、C# 移植、长期稳定性及接口使用授权仍待确认。
- 上游的会员激活、失效后更换设备逻辑未执行，也不应直接照搬进正式客户端。

详细证据、源码定位及复现方式见 [fanqie-assistant 可行性验证](research/FANQIE_ASSISTANT_FEASIBILITY.md)。

面向项目实施的正式技术报告见 [Windows 10 Mobile 番茄小说客户端技术可行性报告](docs/TECHNICAL_FEASIBILITY_REPORT.md)，汇总验证方法、实测指标、协议时序、UWP 移植方案与阶段验收条件。

## 10. 2026-10-19：UWP 原型开发进展

- 新建 `FanqieReader.sln` 和传统 UWP C# / XAML 工程，Target 14393、Minimum 10586，提供 ARM / x86 / x64 配置。
- 已实现原生联网诊断页（设备信息、官网 HTTPS、阅读网关 TLS、样本目录、自有测试地址、取消和复制结果）。
- 已实现 `IBookSource`、原创演示数据源、搜索、详情、目录、阅读、翻章、字号、夜间模式、章节进度保存与恢复。
- 核心 C# 已编译执行，38 项断言通过；完整 UWP C# 源码已通过本机新 SDK 元数据的 API 编译检查。
- 尚未完成 XAML 编译、SDK 14393 / ARM 构建或真机测试；在线签名、密钥与正文协议尚未移植到应用。
- 本机缺少 UWP XAML targets 和完整 SDK 14393；构建预检会明确停止，不能将现有源码检查当作已生成 APPX。
- 工具、编译与手机部署步骤见 [README](README.md)。

### 后续平台与 SDK 决策

- 用户确认继续只开发 Windows 10 Mobile，不新增 Windows Phone 8.1 工程。
- 用户自行安装开发工具；截图中的安装器仅列出 SDK 19041。
- 构建脚本新增 19041 目标选项，并可在完整安装的 14393 / 19041 中选择；最低运行版本仍保持 10586，手机仍构建 ARM32。
- 19041 是候选构建路径，尚未完成 UWP 构建或真机兼容性验证；不能仅凭降低 Minimum Version 就宣称兼容。

## 11. 2026-10-19：ARM 构建与真机连接

- 用户已安装 VS2017 15.9.83，路径为 `D:\develop\vs2017`；完整 SDK 19041 可用。环境检查已改用 vswhere 支持自定义安装目录。
- ARM Debug 与 ARM Release（.NET Native）均已真实构建成功，XAML 编译通过；修复了 UWP 子集缺少 `Uri.GetLeftPart` 的问题。
- 默认编译目标更新为 19041，最低运行版本保持 10586。Release 运行时依赖为 .NET Native Framework / Runtime 1.7 及 ARM VCLibs 14。
- 已生成并签名测试包，位于 `artifacts/deliverables/FanqieReader_0.1.0.0_ARM_Test/`，包含公钥证书、依赖和 build-evidence.json。私钥不在交付目录中。
- 用户真机为 Lumia 950（RM-1105），OS 10.0.15254.603，已开启开发者模式、设备发现、设备门户并连接 USB。
- WinAppDeployCmd 能发现手机，但部署连接在身份验证前返回 `0x8007274D`；电脑运行的 IpOverUsbSvc 为旧 8.0.12358.1191 版本，USB 门户 10080 没有监听。
- 正在尝试同一局域网的设备门户连接；尚未确认手机安装、启动、阅读或 HTTPS。配对码不写入项目文件。
- 已通过 VS2017 本地组件目录核实：缺少 `Microsoft.VisualStudio.Component.Windows10SDK.IpOverUsb`，中文名称“USB 设备连接性”。用户偏好自行安装工具，需要其在 VS2017 的“单个组件”中补装该项后再尝试 USB 部署。
- 手机和电脑在同一 Wi-Fi，但局域网门户访问超时；原因尚未确定。交付 ZIP 已准备完成，不含私钥。

### USB 部署问题已解决

- 用户补装“USB 设备连接性”后，环境检查确认组件已安装，IpOverUsbSvc 升级为 10.0.19041.5609 并运行。
- WinAppDeployCmd 通过 USB 识别 Lumia 950（RM-1105）。运行 `tools/Deploy-Phone.ps1` 安装已签名的 ARM Release 包与依赖，返回 `Done.`，退出码为 0。
- 已请用户打开“番茄阅读原型”，验证演示阅读与诊断结果；启动、阅读和 HTTPS 运行结果仍待用户反馈。`PhoneRuntimeVerified` 继续保持 false。

## 12. 2026-10-19：手机基础验收与在线元数据接入

- 用户反馈：“正常 三个通过”，确认 0.1.0 在 Lumia 950 / 10.0.15254.603 上启动、演示阅读及三项联网诊断通过。依据为用户反馈，未取得具体诊断 JSON 或延迟数据。验证记录见 `docs/phone-validation-0.1.0.json`。
- 0.2.0 新增在线数据源入口，与离线演示共用搜索、详情和目录页面；支持服务端游标分页，目录受限章节标记。在线章节点击暂不启用，正文协议仍待移植。
- `Core/RequestSigner.cs` 原生 C# 移植 GET 签名（Argus / Ladon、SM3、Simon、Speck、protobuf），使用 .NET AES / MD5，不依赖 PC 中转或 WebView。
- 3 组固定随机数与时间的上游签名向量逐字节一致；SM3 已知向量通过，核心共 49 项断言；元数据解析 11 项断言通过。
- 初次 Windows 原生搜索返回 HTTP 200 空体；同一 C# 签名经 Node 传输成功。统一为上游 APP User-Agent 后，Windows 原生 HTTP 的搜索、详情和公开目录实测通过（6 本书、351 章）。请求仍保留证书验证、20 秒超时、1 MiB 响应限制、无 Cookie 和无自动重定向。
- 在线元数据暂使用上游公开样例匿名标识，不替换失效设备、不激活会员；正文阶段尚需独立验证设备注册与密钥协商。
- 许可证说明见 `THIRD_PARTY_NOTICES.md` 与 `licenses/fanqie-assistant-GPL-3.0.txt`。签名交付流程按清单版本命名，附对应源码 ZIP；部署脚本新增 `-Update`。
- 0.2.0 修复请求头后的 ARM Release / .NET Native 最终构建、签名和 USB 更新全部成功，更新命令返回 `Done.` / 退出码 0。交付 ZIP 为 `artifacts/deliverables/FanqieReader_0.2.0.0_ARM_Test.zip`；包 SHA-256 为 `76F5D91E24B3A757E5C92333F401911BB0524013132A048902B8C2A7F0AE354C`。
- 已核对 APPX 含 GPL 许可证与第三方说明，对应源码 ZIP 含 36 个文件、核心源码及固定测试向量，未混入 PFX 或构建输出。已请用户重开应用、切换在线入口、搜索“部族荣光”并查看详情和目录；新增功能的手机运行状态等待反馈，0.2.0 的 `PhoneRuntimeVerified` 保持 false。

### 0.2.0 手机在线元数据已通过

- 用户确认：“搜索、简介和目录都正常”。验证记录见 `docs/phone-validation-0.2.0.json`。

## 13. 2026-10-19：0.3.0 原生免费正文

- 已移植 POST 签名、TT 注册封装、设备登记、阅读密钥协商、AES-CBC / gzip 解码和 HTML 段落提取；没有移植会员激活或设备轮换。
- 首次登记前加密保存固定安装种子；重试沿用原安装身份。设备标识与密钥使用 Windows `DataProtectionProvider(LOCAL=user)` 加密，并通过事务写入应用本地文件。
- PC 测试状态保存在 `artifacts/content-session/device-session.dat`，为当前用户加密数据。后续测试应复用该文件，不能为解决错误而删除它后反复登记设备。手机状态独立保存在应用本地目录。
- 请求正文前重新读取目录，仅请求字段明确表示免费且已解锁的章节；核对返回的书籍 / 章节标识、密钥版本及压缩格式。出现空响应、Invalid 或版本变化时直接显示错误，当前未实现手动续期入口。
- 62 项核心断言通过，覆盖上游固定 GET / POST / TT / AES 向量、gzip、完整性、文本转换和解压大小限制；元数据与真实 Windows 加密状态往返 / 重用共 14 项断言通过。
- Windows 原生 HTTP + C# 实测样本《部族荣光》第1章（7069952706521236001）：122 个段落，4223 个文本字符，3657 个汉字；段落数与响应声明一致，标为完整。重新加载已有加密状态后再次读取成功，沿用同一设备与密钥；未保存或输出正文 / 密钥明文。
- UI 新增在线正文、加载进度与取消、在线 / 演示来源区分的进度保存；重启恢复上次阅读来源。ARM 0.3.0 最终构建、签名和 USB 更新成功。

### 0.3.0 手机正文验收已通过

- 用户确认：“正文、翻章和继续阅读都正常”。验收范围为《部族荣光》样本章节及下一章、关闭应用后继续阅读；记录见 `docs/phone-validation-0.3.0.json`。
- 手机运行包 SHA-256 为 `A44FDF3345DECA539D20A9727793C2081D68B229770283E5D377BC89A1D0BA7C`。交付目录 `artifacts/deliverables/FanqieReader_0.3.0.0_ARM_Test/`，ZIP 同名，包含 ARM 包与依赖、证书、许可证和对应源码。
- 核心可行性阶段已完成：手机原生搜索 → 详情 → 目录 → 免费正文 → 翻章 → 重启恢复均已通过。后续功能为书架、缓存、更多书籍与跨日稳定性测试；当前未实现章内滚动位置恢复和手动密钥续期入口。

## 14. 2026-10-20：0.4.0 问题修复与诊断页移除

- 用户反馈：直接搜“神通者”漏书；简介过长占满屏幕；后续章节被网页目录免费／锁定检查误拦截。用户已授权修复三项问题并删除诊断界面，功能拓展暂停。
- 搜索实测根因：综合分类中，天蚕土豆《神通者》（7665193065501445145）使用 `show_type=549`，旧代码只收集 `110` 而漏书。现改走 `reading.snssdk.com/reading/bookapi/search/tab/v` 的书籍分类 `tab_type=3`，按 `book_data` / `cell_data` 收集不同展示形式的书籍，保留去重与服务端游标。
- 目录改为 APP `/reading/bookapi/directory/all_items/v` 的 `item_data_list`；删除 `IsFreeConfirmed`、网页锁定标签、正文前的免费／解锁检查与重复网页目录请求。正文直接依据 APP 返回处理，保留书籍／章节一致性和内容完整性核对。
- 简介移入 `ChaptersList.Header`，默认四行，可展开／收起，与虚拟化章节列表共用滚动区域。
- 删除诊断布局、诊断事件和 `NetworkDiagnostics.cs`；响应体读取迁移至 `OnlineTransport`。删除 Pivot 页签及其索引判断，返回键依据阅读视图状态处理；挂起时取消当前阅读加载。
- 本地验证：62 项核心断言、16 项元数据与加密存储断言通过；真实 UWP XAML / ARM Release / .NET Native 编译通过。联网回归验证直接书名、作者搜索均找到目标书，分页返回新结果；《神通者》第49章（7678530145342194238）72 段、《部族荣光》第50章（7083521020400763392）64 段，均完整。
- 用户接回 USB 并解锁后，0.4.0 更新成功（部署工具 `Done.`、退出码 0），安装包 SHA-256 为 `DF5844A08C9E60FBA969756209AC40A8E354DE8AAD29F7F216A454ECA4324820`。
- 用户确认手机端直接搜“神通者”、简介展开／收起及滚动、较后章节读取、返回目录、诊断页移除“全部正常”。证据为 `docs/phone-validation-0.4.0.json`；本轮三个问题与诊断界面移除已完成并通过手机验收。
