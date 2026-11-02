# 第三方代码说明

`src/FanqieReader/Core/RequestSigner.cs`、`Core/ContentCodec.cs` 与 `Services/DeviceSession.cs` 由 [naiyQAQ/fanqie-assistant](https://github.com/naiyQAQ/fanqie-assistant) 的 `src/crypto/{argus,ladon,sm3,simon,speck,protobuf,unidbgconfig,ttencrypt,content}.ts` 及 `src/api/{device,devicebody}.ts` 移植而来，固定提交为 `b62ec5dbe6149a93bca249949549c805ac7da424`，原项目许可证为 GNU GPL v3。

修改：将 TypeScript / Web Crypto 实现转换为 C# / .NET 实现，提供 GET / POST 签名、设备登记和内容解密，增加固定随机数入口用于跨语言测试，固定协议设备型号并加密保存安装状态，未移植会员激活或设备轮换。SM3 源文件还致谢 JuneAndGreen/sm-crypto；设备协议原文件注明移植自 fq-go-api/fanqie。

许可证原文保存在 `licenses/fanqie-assistant-GPL-3.0.txt`。包含这部分代码的应用分发应履行 GPL v3 的对应源码与许可证要求。测试安装包同时提供本工程对应源码。

`Services/OnlineBookSource.cs` 的推荐接口参数、推荐卡片结构和封面字段参考同一固定提交的 `src/api/search.ts`（`getSearchLanding`、`collectSugBooks`）与 `src/api/app.ts`。修改为原生 C# 匿名请求、小说筛选、跨卡片去重及 UWP 封面显示。
