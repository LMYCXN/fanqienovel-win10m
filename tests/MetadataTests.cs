using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FanqieReader.Services;
using Windows.Data.Json;

internal static class MetadataTests
{
    private static int count;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
    private static void Invalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { count++; return; }
        throw new Exception("Malformed or failed response must not appear successful.");
    }

    private static int Main(string[] args)
    {
        try
        {
            string next; bool more;
            var search = JsonObject.Parse(@"{""code"":0,""search_tabs"":[{""tab_type"":""3"",""passback"":9,""has_more"":""1"",""data"":[{""show_type"":112,""book_data"":[{""book_id"":""advert""}]},{""show_type"":110,""book_data"":[{""book_id"":""1234567890123456789"",""book_name"":""灯塔"",""author"":""作者""},{""book_id"":""1234567890123456789""},{""book_id"":""0""}]}]}]}");
            var books = OnlineBookSource.ParseSearch(search, out next, out more);
            Check(books.Count == 1 && books[0].Id == "1234567890123456789", "Filter inserts, deduplicate, preserve 64-bit IDs as strings");
            Check(books[0].Title == "灯塔" && books[0].Author == "作者", "Unicode metadata");
            Check(next == "9" && more, "Use server cursor rather than book count");
            search.GetNamedArray("search_tabs")[0].GetObject()["has_more"] = JsonValue.CreateStringValue("0");
            OnlineBookSource.ParseSearch(search, out next, out more);
            Check(!more, "String zero must mean false");
            search.GetNamedArray("search_tabs")[0].GetObject()["data"] = new JsonArray();
            var empty = OnlineBookSource.ParseSearch(search, out next, out more);
            Check(empty.Count == 0 && !more, "Empty terminal page must stop pagination");
            search.GetNamedArray("search_tabs")[0].GetObject()["has_more"] = JsonValue.CreateBooleanValue(true);
            OnlineBookSource.ParseSearch(search, out next, out more);
            Check(more && next == "9", "Non-book page with advancing server cursor must not discard later results");
            var featured = JsonObject.Parse(@"{""code"":0,""search_tabs"":[{""tab_type"":1,""passback"":""11"",""has_more"":true,""data"":[{""show_type"":549,""book_data"":[{""book_id"":""7665193065501445145"",""book_name"":""神通者"",""author"":""天蚕土豆""}]},{""show_type"":110,""book_data"":[{""book_id"":""7392034984015449112"",""book_name"":""神通之树，我以大道，贯通万法""}]}]}]}");
            var featuredBooks = OnlineBookSource.ParseSearch(featured, out next, out more, "1");
            Check(featuredBooks.Count == 2 && featuredBooks[0].Title == "神通者" && featuredBooks[0].Author == "天蚕土豆", "Real 549 featured-card regression: exact-title result must not disappear");
            featured.GetNamedArray("search_tabs")[0].GetObject()["tab_type"] = JsonValue.CreateNumberValue(3);
            featured.GetNamedArray("search_tabs")[0].GetObject()["data"] = JsonArray.Parse(@"[{""show_type"":999,""cell_data"":[{""book_data"":[{""book_id"":""7665193065501445145"",""book_name"":""神通者""},{""book_id"":""7665193065501445145"",""book_name"":""神通者""}]}]}]");
            Check(OnlineBookSource.ParseSearch(featured, out next, out more).Count == 1, "Nested book cards are collected once regardless of presentation type");
            Invalid(() => OnlineBookSource.ParseSearch(JsonObject.Parse(@"{""code"":7,""message"":""restricted""}"), out next, out more));
            Invalid(() => OnlineBookSource.ParseSearch(JsonObject.Parse(@"{""code"":0,""search_tabs"":[]}"), out next, out more));
            var catalog = JsonObject.Parse(@"{""code"":0,""data"":{""item_data_list"":[{""item_id"":""c2"",""title"":""二"",""isChapterLock"":true},{""item_id"":""c1"",""title"":""一""},{""item_id"":""c2"",""title"":""二""}]}}");
            var chapters = OnlineBookSource.ParseCatalog(catalog);
            Check(chapters.Count == 2 && chapters[0].Id == "c2" && chapters[1].Id == "c1", "APP catalog keeps server order without duplicates");
            Check(chapters[0].Title == "二" && chapters[1].Title == "一", "Catalog titles do not acquire web-lock labels or require web availability fields");
            catalog.GetNamedObject("data").GetNamedArray("item_data_list").Add(JsonObject.Parse(@"{""title"":""missing id""}"));
            Invalid(() => OnlineBookSource.ParseCatalog(catalog));
            Invalid(() => OnlineBookSource.ParseCatalog(JsonObject.Parse(@"{""code"":0,""data"":{""item_data_list"":[]}}")));
            Console.WriteLine("PASS: " + count + " metadata assertions.");
            if (args.Length > 1 && args[0] == "--store-test")
            {
                var store = new ProtectedDeviceStore(args[1]);
                var state = new DeviceState { DeviceId = "1234567890123456789", InstallId = "987654321", RegistrationBody = "synthetic", Key = Enumerable.Repeat((byte)0x17, 16).ToArray(), KeyVersion = "3" };
                store.SaveAsync(state, CancellationToken.None).GetAwaiter().GetResult();
                store.SaveAsync(state, CancellationToken.None).GetAwaiter().GetResult();
                var loaded = store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(loaded.DeviceId == state.DeviceId && loaded.Key.SequenceEqual(state.Key), "Protected device store preserves 64-bit IDs and key through replacement");
                var restored = new DeviceSession(args[1]).EnsureAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(restored.DeviceId == state.DeviceId && restored.KeyVersion == "3", "New session reuses persisted identity and key");
                Console.WriteLine("PASS: protected store round trip and session reuse; " + count + " assertions total.");
            }
            if (args.Length > 1 && args[0] == "--export-request")
            {
                var rawQuery = OnlineBookSource.BuildQuery(new[]
                {
                    new KeyValuePair<string,string>("query", "部族荣光"), new KeyValuePair<string,string>("passback", "0"),
                    new KeyValuePair<string,string>("selected_items", ""), new KeyValuePair<string,string>("tab_type", "3")
                });
                var url = "https://reading.snssdk.com/reading/bookapi/search/tab/v?" + rawQuery;
                var headers = new JsonObject();
                foreach (var header in FanqieReader.Core.RequestSigner.SignGet(new Uri(url).Query.TrimStart('?')))
                    headers[header.Key] = JsonValue.CreateStringValue(header.Value);
                File.WriteAllText(args[1], new JsonObject { ["url"] = JsonValue.CreateStringValue(url), ["headers"] = headers }.Stringify());
            }
            if (args.Length > 0 && args[0] == "--online")
            {
                var client = new OnlineBookSource();
                var found = client.SearchAsync("部族荣光", CancellationToken.None).GetAwaiter().GetResult();
                Check(found.Count > 0, "Native C# signature accepted for live search");
                var book = client.GetBookAsync("7069948840148732967", CancellationToken.None).GetAwaiter().GetResult();
                Check(book.Id == "7069948840148732967" && book.Title.Length > 0, "Native signed detail");
                var liveCatalog = client.GetChaptersAsync(book.Id, CancellationToken.None).GetAwaiter().GetResult();
                Check(liveCatalog.Count > 0, "Native public catalog");
                Console.WriteLine("PASS: live native C# search (" + found.Count + " books), detail, catalog (" + liveCatalog.Count + " chapters). No chapter content requested.");
            }
            if (args.Length > 1 && args[0] == "--free-content")
            {
                var client = new OnlineBookSource(args[1]);
                var liveCatalog = client.GetChaptersAsync("7069948840148732967", CancellationToken.None).GetAwaiter().GetResult();
                var chapter = liveCatalog.FirstOrDefault(item => item.Id == "7069952706521236001");
                Check(chapter != null, "Known sample must occur in APP catalog");
                var content = client.GetChapterAsync(chapter.Id, CancellationToken.None).GetAwaiter().GetResult();
                Check(content.IsComplete && content.Paragraphs.Count > 0, "Native free sample must decrypt as a complete chapter");
                var textCharacters = content.Paragraphs.Sum(paragraph => paragraph.Length);
                var hanCharacters = content.Paragraphs.Sum(paragraph => paragraph.Count(c => c >= '\u3400' && c <= '\u9fff'));
                Check(hanCharacters > 100 && textCharacters > 1000, "Real chapter must contain readable Chinese text");
                var state = new ProtectedDeviceStore(args[1]).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var reloaded = new DeviceSession(args[1]).EnsureAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(state.DeviceId == reloaded.DeviceId && state.Key.SequenceEqual(reloaded.Key), "Live session can be reloaded without replacing device");
                Console.WriteLine("PASS: native free chapter; paragraphs=" + content.Paragraphs.Count + ", complete=" + content.IsComplete + ", textCharacters=" + textCharacters + ", hanCharacters=" + hanCharacters + ", protectedSessionReusable=true. Keys and chapter text omitted.");
            }
            if (args.Length > 1 && args[0] == "--regressions")
            {
                var client = new OnlineBookSource(args[1]);
                foreach (var keyword in new[] { "神通者", "天蚕土豆" })
                {
                    var found = client.SearchAsync(keyword, CancellationToken.None).GetAwaiter().GetResult();
                    Check(found.Any(book => book.Id == "7665193065501445145" && book.Title == "神通者" && book.Author == "天蚕土豆"), "Title and author searches must both return 神通者");
                    Console.WriteLine("PASS: APP search '" + keyword + "' includes 神通者 / 天蚕土豆; books=" + found.Count);
                    if (keyword == "神通者" && client.HasMore)
                    {
                        var ids = new HashSet<string>(found.Select(book => book.Id));
                        var page = client.LoadMoreAsync(CancellationToken.None).GetAwaiter().GetResult();
                        Check(page.Count == 0 || page.Any(book => !ids.Contains(book.Id)), "Next page must use the server cursor");
                        Console.WriteLine("PASS: next search page; books=" + page.Count);
                    }
                }
                foreach (var bookId in new[] { "7665193065501445145", "7069948840148732967" })
                {
                    var appCatalog = client.GetChaptersAsync(bookId, CancellationToken.None).GetAwaiter().GetResult();
                    Check(appCatalog.Count >= 20, "Late-chapter regression requires at least twenty chapters");
                    var chapter = appCatalog[Math.Min(49, appCatalog.Count - 1)];
                    var content = client.GetChapterAsync(chapter.Id, CancellationToken.None).GetAwaiter().GetResult();
                    Check(content.Paragraphs.Count > 0, "Later chapters must reach APP content endpoint and display text");
                    Console.WriteLine("PASS: later chapter; book=" + bookId + ", chapter=" + chapter.Id + ", title=" + chapter.Title + ", paragraphs=" + content.Paragraphs.Count + ", complete=" + content.IsComplete);
                }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
