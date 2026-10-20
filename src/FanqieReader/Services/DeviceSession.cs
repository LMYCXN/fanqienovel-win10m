// SPDX-License-Identifier: GPL-3.0-only
// Registration protocol ported from fanqie-assistant api/device.ts and api/devicebody.ts.
// Upstream commit b62ec5dbe6149a93bca249949549c805ac7da424.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FanqieReader.Core;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FanqieReader.Services
{
    internal sealed class DeviceState
    {
        internal string DeviceId = "";
        internal string InstallId = "";
        internal string RegistrationBody = "";
        internal string KeyVersion = "";
        internal byte[] Key;
    }

    internal sealed class ProtectedDeviceStore
    {
        private readonly string folderPath;
        internal ProtectedDeviceStore(string folderPath) { this.folderPath = folderPath; }

        internal async Task<DeviceState> LoadAsync(CancellationToken token)
        {
            var path = Path.Combine(folderPath, "device-session.dat");
            if (!File.Exists(path)) return null;
            var protectedBytes = CryptographicBuffer.CreateFromByteArray(File.ReadAllBytes(path));
            var plain = await new DataProtectionProvider().UnprotectAsync(protectedBytes).AsTask(token);
            var json = JsonObject.Parse(CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, plain));
            var state = new DeviceState
            {
                DeviceId = json.GetNamedString("deviceId"), InstallId = json.GetNamedString("installId"),
                RegistrationBody = json.GetNamedString("registrationBody"), KeyVersion = json.GetNamedString("keyVersion")
            };
            var key = json.GetNamedString("key");
            if (key.Length > 0) state.Key = Convert.FromBase64String(key);
            if ((state.DeviceId.Length == 0 || state.InstallId.Length == 0) && state.RegistrationBody.Length == 0)
                throw new InvalidDataException("本地阅读设备记录不完整，已停止登记。");
            return state;
        }

        internal async Task SaveAsync(DeviceState state, CancellationToken token)
        {
            var json = new JsonObject
            {
                ["deviceId"] = JsonValue.CreateStringValue(state.DeviceId), ["installId"] = JsonValue.CreateStringValue(state.InstallId),
                ["registrationBody"] = JsonValue.CreateStringValue(state.RegistrationBody), ["keyVersion"] = JsonValue.CreateStringValue(state.KeyVersion),
                ["key"] = JsonValue.CreateStringValue(state.Key == null ? "" : Convert.ToBase64String(state.Key))
            };
            var bytes = CryptographicBuffer.ConvertStringToBinary(json.Stringify(), BinaryStringEncoding.Utf8);
            var encrypted = await new DataProtectionProvider("LOCAL=user").ProtectAsync(bytes).AsTask(token);
            var folder = await StorageFolder.GetFolderFromPathAsync(folderPath).AsTask(token);
            var file = await folder.CreateFileAsync("device-session.dat", CreationCollisionOption.OpenIfExists).AsTask(token);
            using (var transaction = await file.OpenTransactedWriteAsync().AsTask(token))
            {
                using (var writer = new DataWriter(transaction.Stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBuffer(encrypted);
                    await writer.StoreAsync().AsTask(token);
                    await writer.FlushAsync().AsTask(token);
                    writer.DetachStream();
                }
                transaction.Stream.Size = encrypted.Length;
                await transaction.CommitAsync().AsTask(token);
            }
        }
    }

    internal sealed class DeviceSession
    {
        private readonly ProtectedDeviceStore store;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private DeviceState state;
        internal DeviceSession(string folder) { store = new ProtectedDeviceStore(folder); }

        internal async Task<DeviceState> EnsureAsync(CancellationToken token)
        {
            await gate.WaitAsync(token);
            try
            {
                if (state != null && state.Key != null) return state;
                if (state == null) state = await store.LoadAsync(token) ?? new DeviceState { RegistrationBody = RegistrationJson() };
                if (state.Key != null) return state;
                // Persist the registration seed before networking; retries keep the same identity.
                await store.SaveAsync(state, token);
                if (state.DeviceId.Length == 0)
                {
                    var compressed = ContentCodec.Gzip(Encoding.UTF8.GetBytes(state.RegistrationBody));
                    var encrypted = ContentCodec.EncryptRegistration(compressed, ContentCodec.RandomBytes(32));
                    var json = await OnlineTransport.SendAsync("https://i.snssdk.com/service/2/device_register/?tt_data=a", false, token,
                        encrypted, "application/octet-stream; tt-data=a", false, "okhttp/4.10.0");
                    var deviceId = OnlineBookSource.Text(json, "device_id_str");
                    var installId = OnlineBookSource.Text(json, "install_id_str");
                    ulong parsed;
                    if (!ulong.TryParse(deviceId, out parsed) || parsed == 0 || !ulong.TryParse(installId, out parsed) || parsed == 0)
                        throw new InvalidDataException("阅读设备登记失败：服务未返回有效标识。");
                    state.DeviceId = deviceId; state.InstallId = installId;
                    await store.SaveAsync(state, token);
                }
                var query = "device_id=" + state.DeviceId + "&iid=" + state.InstallId + "&device_type=P30&aid=1967&app_name=novelapp&channel=0&device_platform=android&os_version=0&version_code=6.3.9.32";
                var body = new JsonObject { ["content"] = JsonValue.CreateStringValue(ContentCodec.EncryptDeviceId(state.DeviceId)) };
                var response = await OnlineTransport.SendAsync("https://reading.snssdk.com/reading/crypt/registerkey?" + query, true, token,
                    ContentCodec.Gzip(Encoding.UTF8.GetBytes(body.Stringify())), gzip: true);
                OnlineBookSource.CheckCode(response);
                var data = response.GetNamedObject("data");
                var key = ContentCodec.DecryptRegisteredKey(data.GetNamedString("key"));
                var version = OnlineBookSource.Text(data, "keyver");
                if (version.Length == 0) throw new InvalidDataException("阅读密钥登记缺少版本。");
                state.Key = key; state.KeyVersion = version;
                await store.SaveAsync(state, token);
                return state;
            }
            finally { gate.Release(); }
        }

        private static string RegistrationJson()
        {
            // Stable protocol profile; only the installation UUIDs are created once.
            var header = JsonObject.Parse(@"{""display_name"":""番茄免费小说"",""aid"":1967,""channel"":""43536163a"",""package"":""com.dragon.read"",""sdk_version"":""3.7.0-rc.25-fanqie-xiaoshuo"",""sdk_target_version"":29,""git_hash"":""711d1a7"",""density_dpi"":240,""display_density"":""hdpi"",""resolution"":""720x1280"",""language"":""zh"",""timezone"":8,""access"":""wifi"",""not_request_sender"":0,""carrier"":""CHINA MOBILE"",""mcc_mnc"":""46000"",""region"":""CN"",""tz_name"":""Asia/Shanghai"",""tz_offset"":28800,""sim_region"":""cn"",""sim_serial_number"":[],""oaid_may_support"":false,""device_platform"":""android"",""custom"":{""host_bit"":32,""dragon_device_type"":0},""pre_installed_channel"":"""",""is_system_app"":0,""sdk_flavor"":""china"",""guest_mode"":0,""os"":""Android"",""os_version"":""10"",""os_api"":29,""device_model"":""P30"",""device_brand"":""Huawei"",""device_manufacturer"":""Huawei"",""cpu_abi"":""arm64-v8a"",""release_build"":""711d1a7"",""sig_hash"":""a4a27c2633195374c15651ffc3c4a497"",""rom"":""EMUI10"",""rom_version"":""EMUI10 release-keys"",""ipv6_list"":[{""type"":""client_anpi"",""value"":""::1""}]}");
            foreach (var field in new[] { "cdid", "clientudid", "req_id" }) header[field] = JsonValue.CreateStringValue(Guid.NewGuid().ToString());
            header["openudid"] = JsonValue.CreateStringValue(Guid.NewGuid().ToString("N").Substring(0, 20));
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            header["apk_first_install_time"] = JsonValue.CreateNumberValue(now);
            return new JsonObject { ["magic_tag"] = JsonValue.CreateStringValue("ss_app_log"), ["header"] = header, ["_gen_time"] = JsonValue.CreateNumberValue(now) }.Stringify();
        }
    }
}
