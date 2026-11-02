using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FanqieReader.Core;
using Windows.Data.Json;
using Windows.Storage;

namespace FanqieReader.Services
{
    public sealed class OnlineBookSource : IBookSource
    {
        public bool HasMore { get; private set; }
        private string query;
        private string cursor = "0";
        private readonly Dictionary<string, string> chapterBooks = new Dictionary<string, string>();
        private readonly string sessionFolder;
        private readonly OnlineRequest sendAsync;
        private DeviceSession deviceSession;
        private readonly Dictionary<string, Tuple<Book, DateTimeOffset>> recommendationDetails = new Dictionary<string, Tuple<Book, DateTimeOffset>>();
        // Category IDs from the official rank page; both channels rotate independently of reading history.
        private static readonly string[] MaleCategories = { "258", "539", "261", "273", "8", "1140", "1141", "262" };
        private static readonly string[] FemaleCategories = { "748", "253", "267", "246", "248", "79", "1139", "749" };

        public OnlineBookSource() : this(null, OnlineTransport.SendAsync) { }
        internal OnlineBookSource(string sessionFolder) : this(sessionFolder, OnlineTransport.SendAsync) { }
        internal OnlineBookSource(string sessionFolder, OnlineRequest sendAsync)
        {
            this.sessionFolder = sessionFolder;
            this.sendAsync = sendAsync;
        }

        public async Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken)
        {
            query = (keyword ?? "").Trim();
            cursor = "0"; HasMore = false;
            if (query.Length == 0) return new List<Book>().AsReadOnly();
            return await SearchPageAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Book>> LoadMoreAsync(CancellationToken cancellationToken)
        {
            if (!HasMore) return new List<Book>().AsReadOnly();
            return await SearchPageAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Book>> GetRecommendationsAsync(CancellationToken cancellationToken)
        {
            var state = ApplicationData.Current.LocalSettings.CreateContainer("recommendations", ApplicationDataCreateDisposition.Always);
            var savedRound = state.Values["round"];
            var round = savedRound is int ? Math.Max(0, (int)savedRound) % 1000000 : 0;
            var recentIds = ((state.Values["recentIds"] as string) ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var offset = round / MaleCategories.Length % 3 * 20;
            var feeds = new[]
            {
                GetRecommendationBatchAsync(() => GetLandingRecommendationsAsync(cancellationToken), cancellationToken),
                GetRecommendationBatchAsync(() => GetRankCandidatesAsync("1", MaleCategories[round * 2 % MaleCategories.Length], "1", offset, cancellationToken), cancellationToken),
                GetRecommendationBatchAsync(() => GetRankCandidatesAsync("1", MaleCategories[(round * 2 + 1) % MaleCategories.Length], "2", offset, cancellationToken), cancellationToken),
                GetRecommendationBatchAsync(() => GetRankCandidatesAsync("0", FemaleCategories[round * 2 % FemaleCategories.Length], "1", offset, cancellationToken), cancellationToken),
                GetRecommendationBatchAsync(() => GetRankCandidatesAsync("0", FemaleCategories[(round * 2 + 1) % FemaleCategories.Length], "2", offset, cancellationToken), cancellationToken)
            };
            var batches = await Task.WhenAll(feeds);
            cancellationToken.ThrowIfCancellationRequested();
            if (batches.All(batch => batch.Item1 == null))
                throw new InvalidOperationException("推荐数据加载失败：" + batches[0].Item2.Message, batches[0].Item2);
            var pool = batches.Where(batch => batch.Item1 != null).SelectMany(batch => batch.Item1);
            var random = new Random();
            var candidates = RecommendationSelector.Select(pool, recentIds, 24, 6, random);
            Book[] details;
            using (var gate = new SemaphoreSlim(4))
                details = await Task.WhenAll(candidates.Select(book => GetRecommendationDetailAsync(book, gate, cancellationToken)));
            cancellationToken.ThrowIfCancellationRequested();
            var selected = RecommendationSelector.Select(details.Where(book => book != null), recentIds, 12, 3, random);
            if (selected.Count == 0) throw new InvalidDataException("暂未获取到可用的推荐书籍，请点击换一批重试。");
            foreach (var book in selected) { recentIds.Remove(book.Id); recentIds.Add(book.Id); }
            // Only exposure IDs are kept, separate from reading history, under the LocalSettings value size limit.
            state.Values["recentIds"] = string.Join(",", recentIds.Skip(Math.Max(0, recentIds.Count - 120)));
            state.Values["round"] = (round + 1) % 1000000;
            return selected;
        }

        private static async Task<Tuple<IReadOnlyList<Book>, Exception>> GetRecommendationBatchAsync(Func<Task<IReadOnlyList<Book>>> load, CancellationToken token)
        {
            try { return Tuple.Create<IReadOnlyList<Book>, Exception>(await load(), null); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { return Tuple.Create<IReadOnlyList<Book>, Exception>(null, error); }
        }

        private async Task<IReadOnlyList<Book>> GetLandingRecommendationsAsync(CancellationToken cancellationToken)
        {
            // Same anonymous search-landing feed as the pinned upstream's getSearchLanding.
            var url = "https://fanqienovel.com/reading/bookapi/plan/v?" + BuildQuery(new[]
            {
                Pair("search_source", "1"), Pair("scene", "10"), Pair("new_search_middle_page", "true"),
                Pair("search_middle_page_version", "2"), Pair("from", "search_input_page"), Pair("tab_name", "store"),
                Pair("bookstore_tab", "2"), Pair("bookstore_tab_type", "2"), Pair("hot_word_exchange", "false"),
                Pair("query_history_removed", "false"), Pair("user_is_login", "0")
            });
            return ParseRecommendations(await GetAsync(url, true, cancellationToken));
        }

        private async Task<IReadOnlyList<Book>> GetRankCandidatesAsync(string gender, string category, string mold, int offset, CancellationToken token)
        {
            var url = "https://fanqienovel.com/api/rank/category/list?app_id=2503&rank_list_type=3&limit=20&rank_version=&gender=" + gender
                + "&category_id=" + category + "&rankMold=" + mold + "&offset=" + offset.ToString(CultureInfo.InvariantCulture);
            var json = await GetAsync(url, false, token);
            CheckCode(json);
            var books = new List<Book>();
            foreach (var value in Array(json.GetNamedObject("data"), "book_list"))
            {
                if (value.ValueType != JsonValueType.Object) continue;
                var raw = value.GetObject();
                var id = Text(raw, "bookId");
                if (id.Length == 0 || id == "0") continue;
                // Web rank text may use a custom font. Fetch display metadata from the APP detail endpoint later.
                books.Add(new Book(id, "", "", "", null, new BookMetrics
                {
                    Readers = Count(raw, "read_count"), Status = Text(raw, "creationStatus"),
                    UpdatedAt = Count(raw, "lastChapterUpdateTime"), Category = gender + ":" + category
                }));
            }
            return books.AsReadOnly();
        }

        private async Task<Book> GetRecommendationDetailAsync(Book candidate, SemaphoreSlim gate, CancellationToken token)
        {
            await gate.WaitAsync(token);
            try
            {
                Tuple<Book, DateTimeOffset> cached;
                lock (recommendationDetails) recommendationDetails.TryGetValue(candidate.Id, out cached);
                var cacheHit = cached != null && DateTimeOffset.UtcNow - cached.Item2 < TimeSpan.FromMinutes(30);
                var book = cacheHit ? cached.Item1 : await GetBookAsync(candidate.Id, token);
                if (book.Id != candidate.Id || string.IsNullOrWhiteSpace(book.Title)) return null;
                lock (recommendationDetails)
                {
                    if (!cacheHit) recommendationDetails[book.Id] = Tuple.Create(book, DateTimeOffset.UtcNow);
                    if (recommendationDetails.Count > 256)
                        recommendationDetails.Remove(recommendationDetails.OrderBy(pair => pair.Value.Item2).First().Key);
                }
                // Current rank readership supplements sparse detail responses without changing cached objects.
                var metrics = new BookMetrics
                {
                    Readers = candidate.Metrics.Readers ?? book.Metrics.Readers, Rating = book.Metrics.Rating,
                    Shelves = book.Metrics.Shelves, UpdatedAt = book.Metrics.UpdatedAt ?? candidate.Metrics.UpdatedAt,
                    Status = string.IsNullOrEmpty(book.Metrics.Status) ? candidate.Metrics.Status : book.Metrics.Status,
                    Category = string.IsNullOrEmpty(book.Metrics.Category) ? candidate.Metrics.Category : book.Metrics.Category
                };
                return new Book(book.Id, book.Title, book.Author, book.Summary, book.CoverUrl, metrics);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return null; }
            finally { gate.Release(); }
        }

        private async Task<IReadOnlyList<Book>> SearchPageAsync(CancellationToken token)
        {
            var url = "https://reading.snssdk.com/reading/bookapi/search/tab/v?" + BuildQuery(new[]
            {
                Pair("query", query), Pair("passback", cursor), Pair("selected_items", ""), Pair("tab_type", "3")
            });
            var json = await GetAsync(url, true, token);
            string next; bool more;
            var books = ParseSearch(json, out next, out more);
            HasMore = more && next != cursor;
            cursor = next;
            return books;
        }

        public async Task<Book> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            var json = await GetAsync("https://reading.snssdk.com/reading/bookapi/detail/v?" + BuildQuery(new[] { Pair("book_id", bookId) }), true, cancellationToken);
            CheckCode(json);
            return ParseBook(json.GetNamedObject("data"));
        }

        public async Task<IReadOnlyList<Chapter>> GetChaptersAsync(string bookId, CancellationToken cancellationToken)
        {
            var json = await GetAsync("https://reading.snssdk.com/reading/bookapi/directory/all_items/v?" + BuildQuery(new[] { Pair("book_id", bookId) }), true, cancellationToken);
            var chapters = ParseCatalog(json);
            foreach (var chapter in chapters) chapterBooks[chapter.Id] = bookId;
            return chapters;
        }

        public async Task<ChapterContent> GetChapterAsync(string chapterId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string bookId;
            if (!chapterBooks.TryGetValue(chapterId, out bookId)) throw new InvalidOperationException("请先打开书籍目录。");
            if (deviceSession == null) deviceSession = new DeviceSession(sessionFolder ?? ApplicationData.Current.LocalFolder.Path, sendAsync);
            var device = await deviceSession.EnsureAsync(cancellationToken);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var json = await GetAsync("https://reading.snssdk.com/reading/reader/full/v?" + BuildQuery(new[] { Pair("item_id", chapterId), Pair("req_type", "1") }, device), true, cancellationToken);
                CheckCode(json);
                var data = json.GetNamedObject("data");
                var novel = data.GetNamedObject("novel_data", new JsonObject());
                var returnedChapter = Text(novel, "item_id");
                var returnedBook = Text(novel, "book_id");
                if ((returnedChapter.Length > 0 && returnedChapter != chapterId) || (returnedBook.Length > 0 && returnedBook != bookId))
                    throw new InvalidDataException("服务返回的正文与所选章节不一致。");
                var encrypted = Text(data, "content");
                if (encrypted.Length == 0 || encrypted == "Invalid" || Text(data, "key_version") != device.KeyVersion)
                {
                    if (attempt == 1) break;
                    // Registration can expire even while the reported key version is unchanged.
                    device = await deviceSession.RefreshKeyAsync(cancellationToken);
                    continue;
                }
                var compression = Text(data, "compress_status");
                if (compression != "0" && compression != "1") throw new InvalidDataException("服务返回了尚不支持的正文压缩格式。");
                var html = ContentCodec.DecryptChapter(encrypted, device.Key, compression == "1");
                int total, free;
                if (!int.TryParse(Text(data, "paragraphs_num"), out total)) total = -1;
                if (!int.TryParse(Text(data, "free_para_nums"), out free)) free = -1;
                return ContentCodec.ParseHtml(chapterId, html, total, free);
            }
            throw new InvalidDataException("已刷新阅读密钥，但服务仍未返回可读正文，请稍后重试。");
        }

        private Task<JsonObject> GetAsync(string url, bool signed, CancellationToken cancellationToken)
        {
            return sendAsync(url, signed, cancellationToken);
        }

        internal static IReadOnlyList<Book> ParseSearch(JsonObject json, out string next, out bool more, string tabType = "3")
        {
            CheckCode(json);
            next = "0"; more = false;
            var books = new List<Book>();
            var ids = new HashSet<string>();
            foreach (var tabValue in json.GetNamedArray("search_tabs"))
            {
                var tab = tabValue.GetObject();
                if (Text(tab, "tab_type") != tabType) continue;
                next = Text(tab, "passback");
                more = Flag(tab, "has_more");
                CollectSearchBooks(tab.GetNamedArray("data"), books, ids);
                more = more && next.Length > 0;
                return books.AsReadOnly();
            }
            throw new InvalidDataException("搜索响应缺少书籍结果页。");
        }

        internal static IReadOnlyList<Book> ParseRecommendations(JsonObject json)
        {
            CheckCode(json);
            IJsonValue data;
            if (!json.TryGetValue("data", out data) || data.ValueType != JsonValueType.Array)
                throw new InvalidDataException("推荐响应缺少书籍列表。");
            var books = new List<Book>();
            CollectSearchBooks(data.GetArray(), books, new HashSet<string>(), true);
            return books.AsReadOnly();
        }

        private static void CollectSearchBooks(JsonArray cells, List<Book> books, HashSet<string> ids, bool novelsOnly = false)
        {
            foreach (var value in cells)
            {
                if (value.ValueType != JsonValueType.Object) continue;
                var cell = value.GetObject();
                // The landing feed also includes comic and audio cards; keep text novels.
                var showType = Text(cell, "show_type");
                if (novelsOnly && (showType == "332" || showType == "334")) continue;
                foreach (var bookValue in Array(cell, "book_data"))
                {
                    if (bookValue.ValueType != JsonValueType.Object) continue;
                    var raw = bookValue.GetObject();
                    var genre = Text(raw, "genre");
                    if (novelsOnly && genre.Length > 0 && genre != "0") continue;
                    var book = ParseBook(raw);
                    if (book.Id.Length > 0 && book.Id != "0" && !string.IsNullOrWhiteSpace(book.Title) && ids.Add(book.Id)) books.Add(book);
                }
                CollectSearchBooks(Array(cell, "cell_data"), books, ids, novelsOnly);
            }
        }

        private static JsonArray Array(JsonObject json, string key)
        {
            IJsonValue value;
            return json.TryGetValue(key, out value) && value.ValueType == JsonValueType.Array ? value.GetArray() : new JsonArray();
        }

        internal static IReadOnlyList<Chapter> ParseCatalog(JsonObject json)
        {
            CheckCode(json);
            var data = json.GetNamedObject("data");
            var chapters = new List<Chapter>();
            var seen = new HashSet<string>();
            foreach (var value in data.GetNamedArray("item_data_list"))
            {
                var item = value.GetObject();
                var id = Text(item, "item_id");
                if (id.Length == 0) throw new InvalidDataException("目录章节缺少标识。");
                if (seen.Add(id)) chapters.Add(new Chapter(id, Text(item, "title")));
            }
            if (chapters.Count == 0) throw new InvalidDataException("目录为空。");
            return chapters.AsReadOnly();
        }

        private static Book ParseBook(JsonObject value)
        {
            var title = Text(value, "book_name");
            if (title.Length == 0) title = Text(value, "original_book_name");
            var cover = Text(value, "thumb_url");
            if (cover.Length == 0) cover = Text(value, "detail_page_thumb_url");
            if (cover.StartsWith("//", StringComparison.Ordinal)) cover = "https:" + cover;
            Uri coverUri;
            if (!Uri.TryCreate(cover, UriKind.Absolute, out coverUri) || (coverUri.Scheme != "https" && coverUri.Scheme != "http")) cover = null;
            var rating = Number(value, "score");
            return new Book(Text(value, "book_id"), title, Text(value, "author"), Text(value, "abstract"), cover, new BookMetrics
            {
                Readers = Count(value, "read_count"), Rating = rating > 0 && rating <= 10 ? rating : null,
                Shelves = Count(value, "all_bookshelf_count"), UpdatedAt = Count(value, "last_publish_time"),
                Status = Text(value, "creation_status"), Category = Text(value, "category")
            });
        }

        private static long? Count(JsonObject json, string key)
        {
            long value;
            return long.TryParse(Text(json, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0 ? (long?)value : null;
        }

        private static double? Number(JsonObject json, string key)
        {
            double value;
            return double.TryParse(Text(json, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) ? (double?)value : null;
        }

        internal static void CheckCode(JsonObject json)
        {
            if (Text(json, "code") != "0") throw new InvalidDataException("服务业务错误 " + Text(json, "code") + "：" + Text(json, "message"));
        }

        internal static string Text(JsonObject json, string key)
        {
            IJsonValue value;
            if (!json.TryGetValue(key, out value) || value.ValueType == JsonValueType.Null) return "";
            return value.ValueType == JsonValueType.String ? value.GetString() : value.Stringify();
        }
        private static bool Flag(JsonObject json, string key) { var value = Text(json, key); return value == "true" || value == "1"; }
        private static KeyValuePair<string, string> Pair(string key, string value) { return new KeyValuePair<string, string>(key, value); }
        internal static string BuildQuery(IEnumerable<KeyValuePair<string, string>> extra, DeviceState device = null)
        {
            // Same public anonymous metadata sample as the pinned upstream. No identity rotation.
            var query = new[]
            {
                Pair("iid", device == null ? "2187355326270644" : device.InstallId), Pair("device_id", device == null ? "2187355326004404" : device.DeviceId), Pair("ac", "wifi"), Pair("channel", "43536163a"),
                Pair("aid", "1967"), Pair("app_name", "novelapp"), Pair("version_code", "70132"), Pair("version_name", "7.0.1.32"),
                Pair("device_platform", "android"), Pair("os", "android"), Pair("ssmix", "a"), Pair("os_version", "10"),
                Pair("device_type", "P30"), Pair("device_brand", device == null ? "realme" : "Huawei"), Pair("update_version_code", "70132"), Pair("manifest_version_code", "70132"), Pair("pv_player", "70132")
            }.Concat(extra);
            return string.Join("&", query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value).Replace("%20", "+")));
        }
    }
}
