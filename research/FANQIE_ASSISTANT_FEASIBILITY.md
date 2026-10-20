# fanqie-assistant 可行性验证

验证日期：2026-10-19（北京时间）。

结论：**独立客户端获取免费正文的网络链路已在 Windows PC / Node 24 上实测跑通，值得继续做 UWP 原型。Lumia 真机可用性、长期稳定性和接口使用授权尚未确认。**

本次成功完成：本地签名 → 匿名设备注册 → 内容密钥协商 → 请求一个免费且未锁定的章节 → 本地解密和解压。没有激活设备会员，没有登录账号，也没有执行被拒后的设备替换。成功范围限于一个样本章节，不能外推到付费内容、网页锁定章节或整个平台书库。

## 1. 审阅对象和证据

- 仓库：<https://github.com/naiyQAQ/fanqie-assistant>
- 本地源码：`research/fanqie-assistant/`，保持上游源码未修改。
- 固定提交：`b62ec5dbe6149a93bca249949549c805ac7da424`。
- 提交时间：2026-09-19 23:51:05 +08:00；package.json 版本：0.0.6。
- 原始指标：`fanqie-probe-results.json`、`fanqie-free-content-results.json`。
- 本地合成测试：`fanqie-offline-results.json`。
- 复现脚本：`probe-fanqie.mjs`；直接加载上游 TypeScript 算法，未安装 npm 包或运行完整用户脚本。

## 2. 实测结果

样本来自官网首页链接：[《部族荣光》详情页](https://fanqienovel.com/page/7069948840148732967)。book_id 为 `7069948840148732967`；测试章节 item_id 为 `7069952706521236001`。章节由实时网页目录筛选：`needPay === 0`、`isChapterLock === false`，且不是付费出版物或付费故事。

| 检查项 | 结果 | 可证明的范围 |
| --- | --- | --- |
| 本地签名 | 成功生成 x-argus / x-ladon 等头 | 纯本地运行，无远程签名服务 |
| 官网 HTTPS 对照请求 | HTTP 200，163,347 字节 | 此 PC 网络可以连接官网 |
| 网页目录 | HTTP 200、code 0、351 章 | 一个样本书籍的目录可用 |
| 带签名的匿名搜索 | HTTP 200、code 0、综合页返回 10 个 cell | 搜索入口接受请求；cell 不等于书籍数 |
| App 书籍详情 | HTTP 200、code 0 | 一个样本书籍的详情可用 |
| App 目录 | HTTP 200、code 0、351 章 | 与网页目录章节数量一致 |
| 网页正文 JSON 接口 | HTTP 200、响应体 0 字节 | 这次直接调用没有取得正文，原因未判定 |
| App 正文，仓库示例设备 | HTTP 200、code 0、正文载荷 10,776 字符 | 获得非 Invalid 的载荷，尚不能单凭这一项证明可读 |
| 一次新匿名设备注册 | HTTP 200、返回有效设备标识 | 无需常驻 Android 设备即可完成这一步 |
| 新设备内容密钥协商 | HTTP 200、取得可用密钥 | 没有调用会员激活函数 |
| 新设备请求同一免费章节 | HTTP 200、code 0，并成功解密、解压 | 本次免费正文链路端到端成功 |

免费正文验证的具体指标：

- 解密后类型为字符串，包含中文和 HTML，共 16,136 字符；这是含标签的长度。
- 检测到 243 个 `<p>` 段落；服务端 `paragraphs_num = 243`、`free_para_nums = 243`。
- 服务端声明章节字数为 4,223；本次没有将 HTML 长度与小说字数混为一谈。
- 返回的密钥版本与协商结果一致；`compress_status = 1`，走通了解压路径。
- 段落计数与服务端声明一致，为该免费样本未被截断提供支持；未与另一份权威全文逐字比对。
- 设备标识、密钥、Cookie 和小说正文不写入结果文件。测试设备只在本次进程内使用。

首次沙箱内请求因 `EACCES` 未发出；取得联网执行权限后才得到上述业务结果。不能把本地网络权限错误归因为番茄接口拒绝。

## 3. 源码中的实际调用链

以下链接均固定在本次审阅的提交。

| 能力 | 入口及协议 | 关键依赖 |
| --- | --- | --- |
| 搜索 | [search.ts:162](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/search.ts#L162) → fanqienovel.com/reading/bookapi/search/tab/v | 本地签名；默认 omit Cookie |
| 详情 | [book.ts:23](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/book.ts#L23) → /reading/bookapi/detail/v | appGet 请求层 |
| 目录 | [catalog.ts:12](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/catalog.ts#L12) → /reading/bookapi/directory/all_items/v | 另有网页目录回退入口 |
| 单章 | [content.ts:74](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/content.ts#L74) → /reading/reader/full/v | 设备标识、签名、密钥、解密、可选解压 |
| 批量正文 | [content.ts:117](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/content.ts#L117) → /reading/reader/batch_full/v | 本次未请求 |
| 签名 | [sign.ts:30](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/crypto/sign.ts#L30) → argus / ladon | 源码内实现的算法和内置参数 |
| 设备和密钥 | [device.ts](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/device.ts) | i.snssdk.com 注册；reading.snssdk.com 协商密钥 |
| 正文处理 | [crypto/content.ts:7](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/crypto/content.ts#L7) | AES-CBC、UTF-8、可选 gzip，输出 HTML 或 JSON |

本次实测仅请求 `fanqienovel.com`、`reading.snssdk.com`、`i.snssdk.com`，未经过研究者搭建的中转服务。算法中未发现必须调用 Android APK、Xposed、原生 .so、远程 unidbg 或外部签名服务的路径。`unidbgconfig.ts` 是参数配置文件，不能据此推断需要运行 unidbg。

项目还支持 `api5-sinfonlinec.jxbhmy.com` 的备用通道；本次没有调用，也没有独立核实该域名的运营主体。用户脚本的 Vue、Moment、JSZip 依赖由 CDN 加载，它们属于现有 UI / 导出运行环境，不是此次网络和解密测试的依赖。

## 4. UWP / ARM32 移植判断

**从已审阅的调用链推断，可以把网络和协议层移植到 C#；这仍是工程判断，尚无 UWP 编译或真机结果。**

| 上游实现 | UWP 实现方向 | 需要验证的内容 |
| --- | --- | --- |
| GM_xmlhttpRequest、页面 fetch | 原生 HttpClient | UA、自定义头、Cookie 隔离、超时、真机 HTTPS |
| crypto.subtle 的 AES-CBC | Windows.Security.Cryptography.Core | 字节序、IV、PKCS7、固定测试向量 |
| CompressionStream / DecompressionStream | 原生或 .NET gzip 流 | 请求压缩字节与签名一致；正文解压 |
| BigInt、getBigUint64、Simon / Speck | C# ulong 和明确的 unchecked 运算 | 64 位掩码、循环移位、小端读写，ARM32 测试 |
| ProtobufWriter、MD5、SM3 | C# 逐字节移植及适用的系统哈希 API | 与固定输入向量逐字节对照 |
| GM_getValue / GM_setValue | ApplicationData 本地存储 | 设备与密钥绑定、失效状态 |
| Vue / DOM / HTML 阅读视图 | XAML 阅读页面及正文解析 | HTML 标签、段落、注释、字体和长章性能 |

Microsoft 文档确认 [HttpClient](https://learn.microsoft.com/en-us/uwp/api/windows.web.http.httpclient) 和 [SymmetricKeyAlgorithmProvider](https://learn.microsoft.com/en-us/uwp/api/windows.security.cryptography.core.symmetrickeyalgorithmprovider) 从 Windows 10 10240 / UniversalApiContract v1 已提供，后者支持 AES_CBC_PKCS7。API 的存在支持原生实现方向，但不能代替 Lumia 对当前服务端证书和 TLS 的实测。

不建议直接把现有用户脚本塞入旧 WebView：它面向现代浏览器，使用 ES2023、BigInt、现代压缩流及 Tampermonkey 特权 API。它们应在原生移植时替换。

本机仅检测到 UAP SDK 10.0.22621.0 和 10.0.26100.0，未检测到项目基线所需的 14393 SDK；本轮没有进行 UWP / ARM 构建。

## 5. 不能直接照搬的行为

1. [provision.ts:61](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/provision.ts#L61) 初始化串联了设备注册、会员激活和密钥注册。本次将它们拆开，只调用 `registerDevice` 与 `registerKey`；免费样本成功不依赖 `activatePremium`。
2. [app.ts:108](https://github.com/naiyQAQ/fanqie-assistant/blob/b62ec5dbe6149a93bca249949549c805ac7da424/src/api/app.ts#L108) 与 provision.ts 包含空响应后更换设备的恢复策略。正式客户端应明确报告拒绝或失效，不能照搬成规避访问限制的逻辑。本次没有调用这些路径。
3. `getCatalog` 映射为业务模型时没有保留网页目录的 `needPay`、`isChapterLock` 等字段。原型应保留访问状态，否则无法准确解释章节是否可读。
4. 正文能解密不等于所有章节都完整可读。必须分别检查业务错误、付费或试读标识、内容缺失和解析失败，不能仅依赖 HTTP 200。

仓库包含 GPL-3.0 许可证；复用其实现前需要确定本项目的代码分发方案。代码许可证与平台接口、小说内容的使用授权是不同事项，本次没有获得后两者的授权确认。

## 6. 下一步决策

可推进的下一步是小型 UWP 真机验证页：原生 HTTPS → 相同免费样本的详情 / 目录 → 签名与内容处理模块 → 原生阅读显示。网络层与 IBookSource 隔离，保留演示数据源。

真机门槛依次为：

1. SDK 14393 / ARM 工程能部署到 Lumia 950 或 950 XL。
2. 手机独立联网能连接上述服务，证书验证正常。
3. C# 算法与固定输入向量一致，再完成一个免费样本的端到端请求。
4. 后续验证多本免费书籍、更多章节和跨日复用；出现明确拒绝或限制时停止并记录。

在这些步骤之前，不应宣布“Windows 10 Mobile 已可用”或“长期稳定全文源已确认”。

## 7. 复现

从项目根目录执行，需要 Node 24，以及固定提交的源码目录：

```powershell
node research/probe-fanqie.mjs --offline
node research/probe-fanqie.mjs
node research/probe-fanqie.mjs --free-content
```

- `--offline`：纯本地签名及合成内容测试。
- 默认模式：少量 GET；使用上游内置示例设备标识，无账号 Cookie，不执行注册和会员逻辑。
- `--free-content`：每次执行会注册一台新匿名测试设备并协商一次密钥，只请求一个已确认免费且未锁定的章节；不宜当作周期性检测脚本反复运行。
- 结果仅保存响应统计和错误信息；完整正文和密钥只存在于进程内存。每次执行会覆盖该模式的结果 JSON。
