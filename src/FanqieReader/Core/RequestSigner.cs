// SPDX-License-Identifier: GPL-3.0-only
// C# port of fanqie-assistant crypto/{argus,ladon,sm3,simon,speck,protobuf}.ts.
// Upstream: naiyQAQ/fanqie-assistant, b62ec5dbe6149a93bca249949549c805ac7da424.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FanqieReader.Core
{
    public static class RequestSigner
    {
        private static readonly byte[] SignKey = Hex("ac1adaae95a7af94a5114ab3b3a97dd80050aa0a39314c40528caec95256c28c");

        public static IDictionary<string, string> SignGet(string rawQuery)
        {
            return Sign(rawQuery, null);
        }

        public static IDictionary<string, string> Sign(string rawQuery, byte[] body)
        {
            var random = new byte[8];
            using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(random);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var headers = Sign(rawQuery, body, (uint)(now / 1000), Read32Le(random, 0), random.Skip(4).ToArray());
            headers["x-ss-req-ticket"] = now.ToString(CultureInfo.InvariantCulture);
            return headers;
        }

        // Fixed randomness is exposed for cross-language protocol fixtures only.
        internal static IDictionary<string, string> SignGet(string query, uint timestamp, uint protobufRandom, byte[] ladonRandom)
        {
            return Sign(query, null, timestamp, protobufRandom, ladonRandom);
        }

        internal static IDictionary<string, string> Sign(string query, byte[] body, uint timestamp, uint protobufRandom, byte[] ladonRandom)
        {
            if (ladonRandom == null || ladonRandom.Length != 4) throw new ArgumentException("Ladon randomness must be 4 bytes.");
            var stub = body != null && body.Length > 0 ? Md5(body) : new byte[16];
            var headers = new Dictionary<string, string>
            {
                ["x-argus"] = Argus(query, stub, timestamp, protobufRandom),
                ["x-ladon"] = Ladon(timestamp, ladonRandom),
                ["x-khronos"] = timestamp.ToString(CultureInfo.InvariantCulture)
            };
            if (body != null && body.Length > 0) headers["X-SS-STUB"] = BitConverter.ToString(stub).Replace("-", "").ToLowerInvariant();
            return headers;
        }

        private static string QueryValue(string query, string name)
        {
            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split(new[] { '=' }, 2);
                if (Uri.UnescapeDataString(parts[0].Replace("+", " ")) == name)
                    return parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : "";
            }
            return "";
        }

        private static string Argus(string query, byte[] stub, uint timestamp, uint random)
        {
            var proto = new Proto();
            proto.Number(1, 0x20200929u * 2).Number(2, 2).Number(3, random % 0x7fffffffu)
                .Text(4, "1967").Text(5, QueryValue(query, "device_id")).Text(6, "1611921764")
                .Text(7, QueryValue(query, "version_name")).Text(8, "v04.04.05-ov-android")
                .Number(9, 134744640).Bytes(10, new byte[8]).Number(11, 0).Number(12, unchecked(timestamp * 2))
                .Bytes(13, Sm3(stub).Take(6).ToArray())
                .Bytes(14, Sm3(query.Length == 0 ? new byte[16] : Encoding.UTF8.GetBytes(query)).Take(6).ToArray())
                .Bytes(15, new Proto().Number(1, 1).Number(2, 1).Number(3, 1).Number(7, 3348294860).Array())
                .Text(16, "").Text(20, "none").Number(21, 738)
                .Bytes(23, new Proto().Text(1, "NX551J").Number(2, 8196).Number(4, 2162219008).Array())
                .Number(25, 2);
            var padded = Pad(proto.Array());
            var digest = Sm3(Join(SignKey, new byte[] { 0xf2, 0x81, 0x61, 0x6f }, SignKey));
            var keys = new ulong[72];
            for (int i = 0; i < 4; i++) keys[i] = Read64Le(digest, i * 8);
            for (int i = 4; i < keys.Length; i++)
            {
                var temp = RotateRight(keys[i - 1], 3) ^ keys[i - 3];
                temp ^= RotateRight(temp, 1);
                keys[i] = ~keys[i - 4] ^ temp ^ ((0x3dc94c3a046d678bUL >> ((i - 4) % 62)) & 1) ^ 3;
            }
            var encrypted = new byte[padded.Length];
            for (int offset = 0; offset < padded.Length; offset += 16)
            {
                ulong x = Read64Le(padded, offset), y = Read64Le(padded, offset + 8);
                foreach (var key in keys)
                {
                    var previous = y;
                    y = x ^ (RotateLeft(y, 1) & RotateLeft(y, 8)) ^ RotateLeft(y, 2) ^ key;
                    x = previous;
                }
                Write64Le(encrypted, offset, x); Write64Le(encrypted, offset + 8, y);
            }
            var data = Join(new byte[] { 0xf2, 0xf7, 0xfc, 0xff, 0xf2, 0xf7, 0xfc, 0xff }, encrypted);
            for (int i = 8; i < data.Length; i++) data[i] ^= data[i % 8];
            System.Array.Reverse(data);
            var plaintext = Join(new byte[] { 0xa6, 0x6e, 0xad, 0x9f, 0x77, 0x01, 0xd0, 0x0c, 0x18 }, data, new byte[] { 0x61, 0x6f });
            using (var aes = Aes.Create())
            {
                aes.Key = Md5(SignKey.Take(16).ToArray()); aes.IV = Md5(SignKey.Skip(16).ToArray());
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var transform = aes.CreateEncryptor())
                    return Convert.ToBase64String(Join(new byte[] { 0xf2, 0x81 }, transform.TransformFinalBlock(plaintext, 0, plaintext.Length)));
            }
        }

        private static string Ladon(uint timestamp, byte[] random)
        {
            var md5 = Md5(Join(random, Encoding.UTF8.GetBytes("1967")));
            var key = Encoding.ASCII.GetBytes(BitConverter.ToString(md5).Replace("-", "").ToLowerInvariant());
            var keys = new ulong[34];
            keys[0] = Read64Le(key, 0);
            var ls = new List<ulong> { Read64Le(key, 8), Read64Le(key, 16), Read64Le(key, 24) };
            for (int i = 0; i < 33; i++)
            {
                var x = (ulong)i ^ unchecked(RotateRight(ls[i], 8) + keys[i]);
                ls.Add(x); keys[i + 1] = x ^ RotateLeft(keys[i], 3);
            }
            var plaintext = Pad(Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "-1611921764-1967"));
            var encrypted = new byte[plaintext.Length];
            for (int offset = 0; offset < plaintext.Length; offset += 16)
            {
                ulong y = Read64Le(plaintext, offset), x = Read64Le(plaintext, offset + 8);
                foreach (var k in keys)
                {
                    x = k ^ unchecked(RotateRight(x, 8) + y);
                    y = x ^ RotateLeft(y, 3);
                }
                Write64Le(encrypted, offset, y); Write64Le(encrypted, offset + 8, x);
            }
            return Convert.ToBase64String(Join(random, encrypted));
        }

        internal static byte[] Sm3(byte[] input)
        {
            var message = new byte[((input.Length + 9 + 63) / 64) * 64];
            Buffer.BlockCopy(input, 0, message, 0, input.Length); message[input.Length] = 0x80;
            ulong bits = (ulong)input.Length * 8;
            for (int i = 0; i < 8; i++) message[message.Length - 1 - i] = (byte)(bits >> (i * 8));
            var state = new uint[] { 0x7380166f, 0x4914b2b9, 0x172442d7, 0xda8a0600, 0xa96f30bc, 0x163138aa, 0xe38dee4d, 0xb0fb0e4e };
            var w = new uint[68];
            for (int offset = 0; offset < message.Length; offset += 64)
            {
                for (int i = 0; i < 16; i++) w[i] = (uint)message[offset + i * 4] << 24 | (uint)message[offset + i * 4 + 1] << 16 | (uint)message[offset + i * 4 + 2] << 8 | message[offset + i * 4 + 3];
                for (int i = 16; i < 68; i++)
                {
                    var value = w[i - 16] ^ w[i - 9] ^ RotateLeft(w[i - 3], 15);
                    w[i] = value ^ RotateLeft(value, 15) ^ RotateLeft(value, 23) ^ RotateLeft(w[i - 13], 7) ^ w[i - 6];
                }
                uint a = state[0], b = state[1], c = state[2], d = state[3], e = state[4], f = state[5], g = state[6], h = state[7];
                for (int i = 0; i < 64; i++)
                {
                    uint ss1 = RotateLeft(unchecked(RotateLeft(a, 12) + e + RotateLeft(i < 16 ? 0x79cc4519u : 0x7a879d8au, i)), 7);
                    uint tt1 = unchecked((i < 16 ? a ^ b ^ c : (a & b) | (a & c) | (b & c)) + d + (ss1 ^ RotateLeft(a, 12)) + (w[i] ^ w[i + 4]));
                    uint tt2 = unchecked((i < 16 ? e ^ f ^ g : (e & f) | (~e & g)) + h + ss1 + w[i]);
                    d = c; c = RotateLeft(b, 9); b = a; a = tt1;
                    h = g; g = RotateLeft(f, 19); f = e; e = tt2 ^ RotateLeft(tt2, 9) ^ RotateLeft(tt2, 17);
                }
                state[0] ^= a; state[1] ^= b; state[2] ^= c; state[3] ^= d;
                state[4] ^= e; state[5] ^= f; state[6] ^= g; state[7] ^= h;
            }
            var output = new byte[32];
            for (int i = 0; i < 8; i++) for (int j = 0; j < 4; j++) output[i * 4 + j] = (byte)(state[i] >> (24 - j * 8));
            return output;
        }

        private static uint RotateLeft(uint value, int n) { n &= 31; return value << n | value >> ((32 - n) & 31); }
        private static ulong RotateLeft(ulong value, int n) { return value << n | value >> (64 - n); }
        private static ulong RotateRight(ulong value, int n) { return value >> n | value << (64 - n); }
        private static uint Read32Le(byte[] data, int offset) { return (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24); }
        private static ulong Read64Le(byte[] data, int offset) { return Read32Le(data, offset) | (ulong)Read32Le(data, offset + 4) << 32; }
        private static void Write64Le(byte[] output, int offset, ulong value) { for (int i = 0; i < 8; i++) output[offset + i] = (byte)(value >> (i * 8)); }
        private static byte[] Hex(string value) { return Enumerable.Range(0, value.Length / 2).Select(i => Convert.ToByte(value.Substring(i * 2, 2), 16)).ToArray(); }
        private static byte[] Md5(byte[] data) { using (var md5 = MD5.Create()) return md5.ComputeHash(data); }
        private static byte[] Join(params byte[][] arrays) { return arrays.SelectMany(a => a).ToArray(); }
        private static byte[] Pad(byte[] data) { var count = 16 - data.Length % 16; return Join(data, Enumerable.Repeat((byte)count, count).ToArray()); }

        private sealed class Proto
        {
            private readonly List<byte> bytes = new List<byte>();
            private void Varint(uint value) { while (value >= 128) { bytes.Add((byte)(value | 128)); value >>= 7; } bytes.Add((byte)value); }
            public Proto Number(int field, uint value) { Varint((uint)field << 3); Varint(value); return this; }
            public Proto Bytes(int field, byte[] data) { Varint(((uint)field << 3) | 2); Varint((uint)data.Length); bytes.AddRange(data); return this; }
            public Proto Text(int field, string value) { return Bytes(field, Encoding.UTF8.GetBytes(value)); }
            public byte[] Array() { return bytes.ToArray(); }
        }
    }
}
