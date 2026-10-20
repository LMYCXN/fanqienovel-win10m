using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FanqieReader.Core;

internal static class CoreTests
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { assertions++; return; }
        throw new Exception(message);
    }

    private static int Main(string[] args)
    {
        try
        {
            Check(BitConverter.ToString(RequestSigner.Sm3(Encoding.ASCII.GetBytes("abc"))).Replace("-", "").ToLowerInvariant()
                == "66c7f0f462eeedd9d1f2d46bdc10e4e24167c4875cf2f7a2297da02b8f4ba8e0", "SM3 abc known vector");
            Check(BitConverter.ToString(RequestSigner.Sm3(new byte[0])).Replace("-", "").ToLowerInvariant()
                == "1ab21d8355cfa17f8e61194831e81a8f22bec8c728fefb747ed035eb5082aa2b", "SM3 empty known vector");
            foreach (var row in File.ReadAllLines(args[0]))
            {
                var parts = row.Split('\t');
                var headers = RequestSigner.SignGet(parts[0], 1792396800, 0xfedcba98, new byte[] { 0x12, 0x34, 0x56, 0x78 });
                Check(headers["x-argus"] == parts[1], "Argus must match pinned upstream fixture");
                Check(headers["x-ladon"] == parts[2], "Ladon must match pinned upstream fixture");
                Check(headers["x-khronos"] == "1792396800", "Timestamp must match signature");
            }
            foreach (var row in File.ReadAllLines(args[1]))
            {
                var parts = row.Split('\t');
                if (parts[0] == "TT")
                    Check(Convert.ToBase64String(ContentCodec.EncryptRegistration(Convert.FromBase64String(parts[1]), Convert.FromBase64String(parts[2]))) == parts[3], "TT registration envelope must match upstream");
                else if (parts[0] == "DEVICE")
                    Check(ContentCodec.EncryptDeviceId(parts[1], Convert.FromBase64String(parts[2])) == parts[3], "Device ID encoding must preserve all 64 bits");
                else if (parts[0] == "KEY")
                    Check(Convert.ToBase64String(ContentCodec.DecryptRegisteredKey(parts[1])) == parts[2], "Synthetic registered key envelope");
                else if (parts[0] == "CHAPTER")
                {
                    var html = ContentCodec.DecryptChapter(parts[1], Convert.FromBase64String(parts[2]), true);
                    Check(html == Encoding.UTF8.GetString(Convert.FromBase64String(parts[3])), "AES and gzip must match upstream synthetic chapter");
                    var content = ContentCodec.ParseHtml("synthetic", html, 2, 2);
                    Check(content.IsComplete && content.Paragraphs.Count == 2, "Declared full/free counts and HTML paragraphs agree");
                    Check(content.Paragraphs[0] == "灯塔 & 海岸。" && content.Paragraphs[1] == "下一行\n仍在这里。", "Native text conversion preserves entities, inline text and breaks");
                    Check(!ContentCodec.ParseHtml("synthetic", html, 3, 3).IsComplete, "Missing paragraphs must not claim completeness");
                    Check(!ContentCodec.ParseHtml("synthetic", html, 2, 1).IsComplete, "Trial response must not claim completeness");
                    Check(!ContentCodec.ParseHtml("synthetic", html, -1, -1).IsComplete, "Missing metadata must not claim completeness");
                    Throws<InvalidDataException>(() => ContentCodec.DecryptChapter("AA==", Convert.FromBase64String(parts[2]), true), "Truncated cipher must fail");
                }
                else if (parts[0] == "POST")
                {
                    var headers = RequestSigner.Sign(parts[1], Convert.FromBase64String(parts[2]), 1792396800, 0xfedcba98, new byte[] { 0x12,0x34,0x56,0x78 });
                    Check(headers["x-argus"] == parts[3] && headers["x-ladon"] == parts[4] && headers["X-SS-STUB"] == parts[5], "Signed gzip body must match upstream POST fixture");
                }
            }
            var expanded = new byte[4 * 1024 * 1024 + 1];
            var bomb = ContentCodec.Gzip(expanded);
            Throws<InvalidDataException>(() => ContentCodec.Gunzip(bomb), "Decompression size limit");
            Throws<InvalidDataException>(() => ContentCodec.ParseHtml("x", "<html>not paragraphs</html>", 0, 0), "Unsupported content must fail clearly");
            var catalog = new List<Chapter>
            {
                new Chapter("c1", "one"), new Chapter("c2", "two"), new Chapter("c3", "three")
            };
            var session = new ReadingSession("book", catalog, catalog[0].Id);
            Check(!session.CanPrevious && !session.Move(-1) && session.Index == 0, "First chapter boundary");
            while (session.CanNext) Check(session.Move(1), "Forward chapter movement");
            Check(!session.Move(1) && session.Current.Id == catalog[catalog.Count - 1].Id, "Last chapter boundary");
            Check(!session.Move(int.MaxValue), "Movement must not overflow");
            var restored = new ReadingSession("book", catalog, session.Current.Id);
            Check(restored.Index == session.Index, "Resume exact chapter");
            Check(new ReadingSession("book", catalog, "removed-chapter").Index == 0, "Missing saved chapter falls back to first");
            Throws<ArgumentException>(() => new ReadingSession("book", new Chapter[0], null), "Empty session must fail");
            var mutableCatalog = new List<Chapter> { new Chapter("c1", "one") };
            var isolated = new ReadingSession("book", mutableCatalog, "c1");
            mutableCatalog.Clear();
            Check(isolated.Current.Id == "c1", "Session must own its catalog snapshot");
            Console.WriteLine("PASS: " + assertions + " core assertions; upstream GET/POST/TT/AES/gzip fixtures, completeness, text conversion, size limits and navigation.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
    }
}
