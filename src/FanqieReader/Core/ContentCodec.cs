// SPDX-License-Identifier: GPL-3.0-only
// Port of fanqie-assistant crypto/ttencrypt.ts, crypto/content.ts and api/device.ts.
// Upstream commit b62ec5dbe6149a93bca249949549c805ac7da424.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FanqieReader.Core
{
    public static class ContentCodec
    {
        private static readonly byte[] SharedKey = Convert.FromBase64String("rCXGfd2POMGzeiNIgo4iLg==");
        private const int MaxPlaintextBytes = 4 * 1024 * 1024;

        public static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(bytes);
            return bytes;
        }

        public static byte[] Gzip(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) gzip.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        public static byte[] Gunzip(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[16384];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > MaxPlaintextBytes) throw new InvalidDataException("正文解压超过 4 MiB 上限。");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        internal static byte[] EncryptRegistration(byte[] compressed, byte[] random)
        {
            if (random == null || random.Length != 32) throw new ArgumentException("Registration randomness must be 32 bytes.");
            var fixedBytes = Convert.FromBase64String("TdTC5rgxYgkOUrPHpnM7pByyRiuCmrWKGWs521cXdST0m69/COjWjSanLjfBqVovHwWlGJKu8pSXMrYqOKrdWA==");
            using (var sha = SHA512.Create())
            {
                var digest = sha.ComputeHash(Join(sha.ComputeHash(random), fixedBytes));
                var plaintext = Join(sha.ComputeHash(compressed), compressed);
                return Join(new byte[] { 116, 99, 5, 16, 0, 0 }, random,
                    Encrypt(plaintext, digest.Take(16).ToArray(), digest.Skip(16).Take(16).ToArray()));
            }
        }

        public static string EncryptDeviceId(string deviceId)
        {
            return EncryptDeviceId(deviceId, RandomBytes(16));
        }

        internal static string EncryptDeviceId(string deviceId, byte[] iv)
        {
            ulong id;
            if (!ulong.TryParse(deviceId, NumberStyles.None, CultureInfo.InvariantCulture, out id)) throw new InvalidDataException("设备标识格式错误。");
            var bytes = new byte[16];
            for (int i = 0; i < 8; i++) bytes[i] = (byte)(id >> (i * 8));
            return Convert.ToBase64String(Join(iv, Encrypt(bytes, SharedKey, iv)));
        }

        public static byte[] DecryptRegisteredKey(string payload)
        {
            var key = DecryptEnvelope(payload, SharedKey);
            if (key.Length != 16 && key.Length != 24 && key.Length != 32) throw new InvalidDataException("服务返回的阅读密钥长度错误。");
            return key;
        }

        public static string DecryptChapter(string payload, byte[] key, bool compressed)
        {
            var data = DecryptEnvelope(payload, key);
            if (compressed) data = Gunzip(data);
            if (data.Length > MaxPlaintextBytes) throw new InvalidDataException("正文超过 4 MiB 上限。");
            return new UTF8Encoding(false, true).GetString(data, 0, data.Length);
        }

        public static ChapterContent ParseHtml(string chapterId, string html, int totalParagraphs, int freeParagraphs)
        {
            var paragraphs = new List<string>();
            var matches = Regex.Matches(html, @"<p(?:\s[^>]*)?>(.*?)</p\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            foreach (Match match in matches)
            {
                var text = Regex.Replace(match.Groups[1].Value, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                text = Regex.Replace(text, @"<[^>]*>", "", RegexOptions.None, TimeSpan.FromSeconds(1));
                text = WebUtility.HtmlDecode(text).Trim();
                if (text.Length > 0) paragraphs.Add(text);
            }
            if (paragraphs.Count == 0) throw new InvalidDataException("未识别到可显示的正文段落。");
            var complete = totalParagraphs > 0 && totalParagraphs == freeParagraphs && matches.Count == totalParagraphs;
            return new ChapterContent(chapterId, paragraphs, complete);
        }

        private static byte[] DecryptEnvelope(string payload, byte[] key)
        {
            var data = Convert.FromBase64String(payload);
            if (data.Length < 32 || (data.Length - 16) % 16 != 0) throw new InvalidDataException("加密数据长度错误。");
            using (var aes = Aes.Create())
            {
                aes.Key = key; aes.IV = data.Take(16).ToArray(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor()) return decryptor.TransformFinalBlock(data, 16, data.Length - 16);
            }
        }

        private static byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor()) return encryptor.TransformFinalBlock(data, 0, data.Length);
            }
        }
        private static byte[] Join(params byte[][] arrays) { return arrays.SelectMany(bytes => bytes).ToArray(); }
    }
}
