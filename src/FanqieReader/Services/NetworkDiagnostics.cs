using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace FanqieReader.Services
{
    public sealed class DiagnosticTarget
    {
        public string Name { get; private set; }
        public Uri Uri { get; private set; }
        public bool Catalog { get; private set; }
        public bool TransportOnly { get; private set; }
        public DiagnosticTarget(string name, string url, bool catalog = false, bool transportOnly = false)
        {
            Name = name; Uri = new Uri(url); Catalog = catalog; TransportOnly = transportOnly;
        }
    }

    public sealed class DiagnosticResult
    {
        public string Name { get; set; }
        public string Endpoint { get; set; }
        public int? HttpStatus { get; set; }
        public long ElapsedMs { get; set; }
        public int Bytes { get; set; }
        public bool Passed { get; set; }
        public string Detail { get; set; }
        public string ErrorCode { get; set; }
        public string Summary { get { return (Passed ? "通过 · " : "未通过 · ") + Name; } }
        public string Metrics { get { return "HTTP " + (HttpStatus.HasValue ? HttpStatus.Value.ToString() : "未取得") + " · " + ElapsedMs + " ms · " + Bytes + " 字节"; } }

        public JsonObject ToJson()
        {
            var json = new JsonObject
            {
                ["name"] = JsonValue.CreateStringValue(Name),
                ["endpoint"] = JsonValue.CreateStringValue(Endpoint),
                ["elapsedMs"] = JsonValue.CreateNumberValue(ElapsedMs),
                ["bytes"] = JsonValue.CreateNumberValue(Bytes),
                ["passed"] = JsonValue.CreateBooleanValue(Passed),
                ["detail"] = JsonValue.CreateStringValue(Detail ?? ""),
                ["errorCode"] = JsonValue.CreateStringValue(ErrorCode ?? "")
            };
            if (HttpStatus.HasValue) json["httpStatus"] = JsonValue.CreateNumberValue(HttpStatus.Value);
            return json;
        }
    }

    public sealed class NetworkDiagnostics
    {
        private const int MaxBodyBytes = 1024 * 1024;

        public static DiagnosticTarget[] DefaultTargets()
        {
            return new[]
            {
                new DiagnosticTarget("官网 HTTPS", "https://fanqienovel.com/"),
                new DiagnosticTarget("阅读网关 TLS", "https://reading.snssdk.com/", transportOnly: true),
                new DiagnosticTarget("样本书籍目录", "https://fanqienovel.com/api/reader/directory/detail?bookId=7069948840148732967", catalog: true)
            };
        }

        public async Task<DiagnosticResult> RunAsync(DiagnosticTarget target, CancellationToken cancellationToken)
        {
            var result = new DiagnosticResult { Name = target.Name, Endpoint = target.Uri.Scheme + "://" + target.Uri.Authority + target.Uri.AbsolutePath };
            var timer = Stopwatch.StartNew();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    var filter = new HttpBaseProtocolFilter { AllowAutoRedirect = false, AllowUI = false };
                    filter.CookieUsageBehavior = HttpCookieUsageBehavior.NoCookies;
                    filter.CacheControl.ReadBehavior = ApiInformation.IsEnumNamedValuePresent("Windows.Web.Http.Filters.HttpCacheReadBehavior", "NoCache")
                        ? HttpCacheReadBehavior.NoCache : HttpCacheReadBehavior.MostRecent;
                    filter.CacheControl.WriteBehavior = HttpCacheWriteBehavior.NoCache;
                    using (var client = new HttpClient(filter))
                    using (var request = new HttpRequestMessage(HttpMethod.Get, target.Uri))
                    using (var response = await client.SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead).AsTask(timeout.Token))
                    {
                        result.HttpStatus = (int)response.StatusCode;
                        if (target.TransportOnly)
                        {
                            result.Passed = true;
                            result.Detail = "已收到 HTTPS 响应，证明本次 TLS 连接成功；此项不判断正文接口可用性。";
                        }
                        else
                        {
                            var body = await ReadBodyAsync(response, timeout.Token);
                            result.Bytes = body.Length;
                            if (!response.IsSuccessStatusCode) result.Detail = "服务端返回非成功 HTTP 状态。";
                            else if (body.Length == 0) result.Detail = "HTTP 成功，但响应体为空。";
                            else if (target.Catalog)
                            {
                                var json = JsonObject.Parse(Encoding.UTF8.GetString(body, 0, body.Length));
                                var code = json.GetNamedNumber("code", -1);
                                if (code != 0) result.Detail = "目录业务码：" + code;
                                else
                                {
                                    var ids = json.GetNamedObject("data").GetNamedArray("allItemIds");
                                    result.Passed = ids.Count > 0;
                                    result.Detail = "返回 " + ids.Count + " 个章节标识；本次未请求正文。";
                                }
                            }
                            else
                            {
                                result.Passed = true;
                                result.Detail = "成功读取 HTTPS 响应体。";
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    result.Detail = cancellationToken.IsCancellationRequested ? "已取消。" : "请求超过 20 秒，已停止。";
                }
                catch (Exception error)
                {
                    result.Detail = "请求或响应解析失败：" + error.Message;
                    result.ErrorCode = "0x" + error.HResult.ToString("X8");
                }
            }
            result.ElapsedMs = timer.ElapsedMilliseconds;
            return result;
        }

        internal static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using (var stream = await response.Content.ReadAsInputStreamAsync().AsTask(cancellationToken))
            using (var output = new MemoryStream())
            {
                while (true)
                {
                    var buffer = new Windows.Storage.Streams.Buffer(32768);
                    var read = await stream.ReadAsync(buffer, buffer.Capacity, InputStreamOptions.Partial).AsTask(cancellationToken);
                    if (read.Length == 0) break;
                    if (output.Length + read.Length > MaxBodyBytes) throw new InvalidDataException("响应超过 1 MiB 上限。");
                    var bytes = new byte[(int)read.Length];
                    using (var reader = DataReader.FromBuffer(read)) reader.ReadBytes(bytes);
                    output.Write(bytes, 0, bytes.Length);
                }
                return output.ToArray();
            }
        }
    }
}
