using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FanqieReader.Core;
using Windows.Data.Json;
using Windows.Foundation.Metadata;
using Windows.Security.Cryptography;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

namespace FanqieReader.Services
{
    internal static class OnlineTransport
    {
        internal static async Task<JsonObject> SendAsync(string url, bool signed, CancellationToken cancellationToken,
            byte[] body = null, string contentType = "application/json; charset=utf-8", bool gzip = false, string userAgent = "com.dragon.read")
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                try
                {
                    var filter = new HttpBaseProtocolFilter { AllowAutoRedirect = false, AllowUI = false, CookieUsageBehavior = HttpCookieUsageBehavior.NoCookies };
                    filter.CacheControl.WriteBehavior = HttpCacheWriteBehavior.NoCache;
                    filter.CacheControl.ReadBehavior = ApiInformation.IsEnumNamedValuePresent("Windows.Web.Http.Filters.HttpCacheReadBehavior", "NoCache")
                        ? HttpCacheReadBehavior.NoCache : HttpCacheReadBehavior.MostRecent;
                    using (var client = new HttpClient(filter))
                    using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(url)))
                    {
                        if (signed)
                            foreach (var header in RequestSigner.Sign(new Uri(url).Query.TrimStart('?'), body))
                                if (!request.Headers.TryAppendWithoutValidation(header.Key, header.Value)) throw new InvalidOperationException("无法设置协议请求头：" + header.Key);
                        request.Headers.TryAppendWithoutValidation("User-Agent", userAgent);
                        if (body != null)
                        {
                            request.Content = new HttpBufferContent(CryptographicBuffer.CreateFromByteArray(body));
                            if (!request.Content.Headers.TryAppendWithoutValidation("Content-Type", contentType)) throw new InvalidOperationException("无法设置请求内容类型。");
                            if (gzip && !request.Content.Headers.TryAppendWithoutValidation("Content-Encoding", "gzip")) throw new InvalidOperationException("无法设置请求压缩格式。");
                        }
                        using (var response = await client.SendRequestAsync(request, HttpCompletionOption.ResponseHeadersRead).AsTask(timeout.Token))
                        {
                            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode);
                            var bytes = await NetworkDiagnostics.ReadBodyAsync(response, timeout.Token);
                            if (bytes.Length == 0) throw new InvalidDataException("服务返回空响应，请稍后重试。");
                            return JsonObject.Parse(Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new TimeoutException("请求超过 20 秒，请检查网络后重试。");
                }
            }
        }
    }
}
