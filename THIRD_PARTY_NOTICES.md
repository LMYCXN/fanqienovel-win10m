# 第三方代码说明

`src/FanqieReader/Core/RequestSigner.cs`、`Core/ContentCodec.cs` 与 `Services/DeviceSession.cs` 由 [naiyQAQ/fanqie-assistant](https://github.com/naiyQAQ/fanqie-assistant) 的 `src/crypto/{argus,ladon,sm3,simon,speck,protobuf,unidbgconfig,ttencrypt,content}.ts` 及 `src/api/{device,devicebody}.ts` 移植而来，固定提交为 `b62ec5dbe6149a93bca249949549c805ac7da424`，原项目许可证为 GNU GPL v3。

修改：将 TypeScript / Web Crypto 实现转换为 C# / .NET 实现，提供 GET / POST 签名、设备登记和内容解密，增加固定随机数入口用于跨语言测试，固定协议设备型号并加密保存安装状态，未移植会员激活或设备轮换。SM3 源文件还致谢 JuneAndGreen/sm-crypto；设备协议原文件注明移植自 fq-go-api/fanqie。

许可证原文保存在 `licenses/fanqie-assistant-GPL-3.0.txt`。包含这部分代码的应用分发应履行 GPL v3 的对应源码与许可证要求。测试安装包同时提供本工程对应源码。
