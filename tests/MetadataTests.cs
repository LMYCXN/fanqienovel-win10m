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
            var search = JsonObject.Parse(@"{""code"":0,""search_tabs"":[{""tab_type"":""1"",""passback"":9,""has_more"":""1"",""data"":[{""show_type"":112,""book_data"":[{""book_id"":""advert""}]},{""show_type"":110,""book_data"":[{""book_id"":""1234567890123456789"",""book_name"":""灯塔"",""author"":""作者""},{""book_id"":""1234567890123456789""},{""book_id"":""0""}]}]}]}");
            var books = OnlineBookSource.ParseSearch(search, out next, out more);
            Check(books.Count == 1 && books[0].Id == "1234567890123456789", "Filter inserts, deduplicate, preserve 64-bit IDs as strings");
            Check(books[0].Title == "灯塔" && books[0].Author == "作者", "Unicode metadata");
            Check(next == "9" && more, "Use server cursor rather than book count");
            search.GetNamedArray("search_tabs")[0].GetObject()["has_more"] = JsonValue.CreateStringValue("0");
            OnlineBookSource.ParseSearch(search, out next, out more);
            Check(!more, "String zero must mean false");
            search.GetNamedArray("search_tabs")[0].GetObject()["data"] = new JsonArray();
            var empty = OnlineBookSource.ParseSearch(search, out next, out more);
            Check(empty.Count == 0 && !more, "Empty search page must stop pagination");
            Invalid(() => OnlineBookSource.ParseSearch(JsonObject.Parse(@"{""code"":7,""message"":""restricted""}"), out next, out more));
            Invalid(() => OnlineBookSource.ParseSearch(JsonObject.Parse(@"{""code"":0,""search_tabs"":[]}"), out next, out more));
            var catalog = JsonObject.Parse(@"{""code"":0,""data"":{""allItemIds"":[""c2"",""c1"",""c2""],""chapterListWithVolume"":[[{""itemId"":""c1"",""title"":""一"",""needPay"":0,""isChapterLock"":false,""isPaidPublication"":false,""isPaidStory"":false},{""itemId"":""c2"",""title"":""二"",""needPay"":0,""isChapterLock"":true}]]}}");
            var chapters = OnlineBookSource.ParseCatalog(catalog);
            Check(chapters.Count == 2 && chapters[0].Id == "c2" && chapters[1].Id == "c1", "Catalog follows allItemIds order without duplicates");
            Check(!chapters[0].IsFreeConfirmed && chapters[1].IsFreeConfirmed && chapters[1].Title == "一", "Catalog distinguishes confirmed free chapters from locked ones");
            catalog.GetNamedObject("data").GetNamedArray("chapterListWithVolume")[0].GetArray()[0].GetObject().Remove("isPaidStory");
            Check(!OnlineBookSource.ParseCatalog(catalog)[1].IsFreeConfirmed, "Missing availability flags must remain unknown");
            catalog.GetNamedObject("data").GetNamedArray("allItemIds").Add(JsonValue.CreateStringValue("missing"));
            Invalid(() => OnlineBookSource.ParseCatalog(catalog));
            Invalid(() => OnlineBookSource.ParseCatalog(JsonObject.Parse(@"{""code"":0,""data"":{""allItemIds"":[],""chapterListWithVolume"":[]}}")));
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
                    new KeyValuePair<string,string>("selected_items", ""), new KeyValuePair<string,string>("tab_type", "1")
                });
                var url = "https://fanqienovel.com/reading/bookapi/search/tab/v?" + rawQuery;
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
                var chapter = liveCatalog.FirstOrDefault(item => item.Id == "7069952706521236001" && item.IsFreeConfirmed);
                Check(chapter != null, "Known sample must still be confirmed free and unlocked");
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
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
