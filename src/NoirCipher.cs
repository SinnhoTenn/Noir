using System;
using System.Security.Cryptography;
using System.Text;

namespace Noir
{
    /// <summary>
    /// A user-facing error raised for malformed, unauthentic, or incompatible ciphertext.
    /// </summary>
    public sealed class NoirCipherException : Exception
    {
        public NoirCipherException(string message) : base(message) { }
    }

    /// <summary>
    /// NOIR-ARX text cipher.
    ///
    /// Pipeline:
    ///   UTF-8 text -> NOIR-ARX-512 keystream -> encrypt-then-MAC container -> Base64URL
    ///
    /// This is an original construction built for this project. It uses PBKDF2-HMAC-SHA256
    /// for key derivation, a custom 512-bit ARX permutation for the keystream, and a
    /// truncated HMAC-SHA256 for authentication.
    /// </summary>
    public static class NoirCipher
    {
        public const string ArmorPrefix = "noir1.";
        public const int Version = 1;
        public const int Suite = 1;
        public const int DefaultIterations = 180000;

        private const int SaltSize = 16;
        private const int NonceSize = 16;
        private const int TagSize = 16;
        private const int HeaderSize = 4 + 1 + 1 + SaltSize + NonceSize; // 38 bytes
        private const int MinimumPayloadSize = HeaderSize + TagSize;     // 54 bytes

        private static readonly byte[] Magic = { 0x4E, 0x4F, 0x49, 0x52 }; // "NOIR"
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        // Used only when the passphrase box is left empty. It is deliberately public:
        // it makes the app convenient for casual use, but it does not make the
        // built-in-key mode suitable for real secrets.
        private const string BuiltInSecret =
            "NOIR-BUILTIN-V1-6f3a91d84c7b2e05a8d1f4c9b6e37250d4a8f1c6b9e25073a1d8c5f2e9b6047";

        /// <summary>Encrypts any string. Empty strings are valid input.</summary>
        public static string Encrypt(string plaintext, string passphrase)
        {
            if (plaintext == null) plaintext = string.Empty;

            byte[] salt = RandomBytes(SaltSize);
            byte[] nonce = RandomBytes(NonceSize);
            byte[] header = BuildHeader(salt, nonce);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] cipherBytes = new byte[plainBytes.Length];

            KeyMaterial keys = DeriveKeys(passphrase, salt);
            try
            {
                NoirArx.Transform(keys.EncryptionKey, nonce, plainBytes, cipherBytes);
                byte[] tag = ComputeTag(keys.AuthenticationKey, header, cipherBytes);

                byte[] payload = new byte[header.Length + TagSize + cipherBytes.Length];
                Buffer.BlockCopy(header, 0, payload, 0, header.Length);
                Buffer.BlockCopy(tag, 0, payload, header.Length, TagSize);
                Buffer.BlockCopy(cipherBytes, 0, payload, header.Length + TagSize, cipherBytes.Length);

                return ArmorPrefix + Base64UrlEncode(payload);
            }
            finally
            {
                keys.Clear();
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }

        /// <summary>Decrypts a NOIR armored string. Throws NoirCipherException on any failure.</summary>
        public static string Decrypt(string armoredText, string passphrase)
        {
            if (string.IsNullOrWhiteSpace(armoredText))
                throw new NoirCipherException("请先输入要解密的 NOIR 密文。");

            string trimmed = armoredText.Trim();
            if (!trimmed.StartsWith(ArmorPrefix, StringComparison.OrdinalIgnoreCase))
                throw new NoirCipherException("这不是 NOIR 密文：内容应以 noir1. 开头。");

            string body = RemoveWhitespace(trimmed.Substring(ArmorPrefix.Length));
            if (body.Length == 0)
                throw new NoirCipherException("密文内容为空。");

            byte[] payload = Base64UrlDecode(body);
            if (payload.Length < MinimumPayloadSize)
                throw new NoirCipherException("密文长度不正确，可能已被截断。");

            if (payload[0] != Magic[0] || payload[1] != Magic[1] ||
                payload[2] != Magic[2] || payload[3] != Magic[3])
                throw new NoirCipherException("密文标记无效：这不是 NOIR 密文。");

            int version = payload[4];
            int suite = payload[5];
            if (version != Version)
                throw new NoirCipherException("不支持的 NOIR 密文版本：" + version + "。");
            if (suite != Suite)
                throw new NoirCipherException("不支持的 NOIR 加密套件：" + suite + "。");

            byte[] salt = Slice(payload, 6, SaltSize);
            byte[] nonce = Slice(payload, 6 + SaltSize, NonceSize);
            byte[] storedTag = Slice(payload, HeaderSize, TagSize);
            byte[] cipherBytes = Slice(payload, HeaderSize + TagSize, payload.Length - HeaderSize - TagSize);
            byte[] header = Slice(payload, 0, HeaderSize);

            KeyMaterial keys = DeriveKeys(passphrase, salt);
            try
            {
                byte[] expectedTag = ComputeTag(keys.AuthenticationKey, header, cipherBytes);
                if (!FixedTimeEquals(expectedTag, storedTag, TagSize))
                    throw new NoirCipherException("无法解密：口令错误，或密文已被修改。");

                byte[] plainBytes = new byte[cipherBytes.Length];
                NoirArx.Transform(keys.EncryptionKey, nonce, cipherBytes, plainBytes);

                try
                {
                    return StrictUtf8.GetString(plainBytes);
                }
                catch (DecoderFallbackException)
                {
                    throw new NoirCipherException("解密失败：明文不是有效的 UTF-8 文本。");
                }
                finally
                {
                    Array.Clear(plainBytes, 0, plainBytes.Length);
                }
            }
            finally
            {
                keys.Clear();
            }
        }

        public static bool LooksLikeCiphertext(string text)
        {
            if (text == null) return false;
            return text.TrimStart().StartsWith(ArmorPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Derives independent encryption and authentication keys from the secret.</summary>
        private static KeyMaterial DeriveKeys(string passphrase, byte[] salt)
        {
            byte[] secretBytes = BuildSecret(passphrase);
            byte[] master = null;

            try
            {
                using (var kdf = new Rfc2898DeriveBytes(
                    secretBytes, salt, DefaultIterations, HashAlgorithmName.SHA256))
                {
                    master = kdf.GetBytes(32);
                }

                byte[] encKey = Hmac(master, "NOIR/v1/encryption-key");
                byte[] macKey = Hmac(master, "NOIR/v1/authentication-key");
                return new KeyMaterial(encKey, macKey);
            }
            finally
            {
                Array.Clear(secretBytes, 0, secretBytes.Length);
                if (master != null) Array.Clear(master, 0, master.Length);
            }
        }

        private static byte[] BuildSecret(string passphrase)
        {
            if (string.IsNullOrEmpty(passphrase))
                return Encoding.UTF8.GetBytes(BuiltInSecret);

            string normalized = passphrase.Normalize(NormalizationForm.FormKC);
            return Encoding.UTF8.GetBytes("NOIR-PASSPHRASE-V1\u0000" + normalized);
        }

        private static byte[] Hmac(byte[] key, string label)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(label));
        }

        private static byte[] ComputeTag(byte[] macKey, byte[] header, byte[] cipherBytes)
        {
            byte[] input = new byte[header.Length + cipherBytes.Length];
            Buffer.BlockCopy(header, 0, input, 0, header.Length);
            Buffer.BlockCopy(cipherBytes, 0, input, header.Length, cipherBytes.Length);

            using (var hmac = new HMACSHA256(macKey))
                return hmac.ComputeHash(input);
        }

        private static byte[] BuildHeader(byte[] salt, byte[] nonce)
        {
            byte[] header = new byte[HeaderSize];
            Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
            header[4] = (byte)Version;
            header[5] = (byte)Suite;
            Buffer.BlockCopy(salt, 0, header, 6, SaltSize);
            Buffer.BlockCopy(nonce, 0, header, 6 + SaltSize, NonceSize);
            return header;
        }

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return bytes;
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            byte[] result = new byte[count];
            Buffer.BlockCopy(source, offset, result, 0, count);
            return result;
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right, int count)
        {
            if (left == null || right == null || left.Length < count || right.Length < count)
                return false;

            int difference = 0;
            for (int i = 0; i < count; i++)
                difference |= left[i] ^ right[i];
            return difference == 0;
        }

        private static string RemoveWhitespace(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                    builder.Append(text[i]);
            }
            return builder.ToString();
        }

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string text)
        {
            string base64 = text.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 0:
                    break;
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
                default:
                    throw new NoirCipherException("密文格式损坏：Base64URL 长度无效。");
            }

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new NoirCipherException("密文格式损坏：包含无效的 Base64URL 字符。");
            }
        }

        private sealed class KeyMaterial
        {
            public readonly byte[] EncryptionKey;
            public readonly byte[] AuthenticationKey;

            public KeyMaterial(byte[] encryptionKey, byte[] authenticationKey)
            {
                EncryptionKey = encryptionKey;
                AuthenticationKey = authenticationKey;
            }

            public void Clear()
            {
                Array.Clear(EncryptionKey, 0, EncryptionKey.Length);
                Array.Clear(AuthenticationKey, 0, AuthenticationKey.Length);
            }
        }
    }

    /// <summary>
    /// NOIR-ARX-512: a 16-word (512-bit) add-rotate-xor keystream generator.
    /// Every 64-byte block is generated from the key, nonce, block counter, and
    /// twelve permutation rounds, then fed forward with its initial state.
    /// </summary>
    internal static class NoirArx
    {
        private const int Rounds = 12;
        private const int StateWords = 16;
        private const int BlockBytes = 64;

        private static readonly uint[] RoundConstants =
        {
            0x243F6A88u, 0x85A308D3u, 0x13198A2Eu, 0x03707344u,
            0xA4093822u, 0x299F31D0u, 0x082EFA98u, 0xEC4E6C89u,
            0x452821E6u, 0x38D01377u, 0xBE5466CFu, 0x34E90C6Cu
        };

        public static void Transform(byte[] key, byte[] nonce, byte[] input, byte[] output)
        {
            if (key == null || key.Length != 32)
                throw new ArgumentException("NOIR-ARX needs a 256-bit key.", "key");
            if (nonce == null || nonce.Length != 16)
                throw new ArgumentException("NOIR-ARX needs a 128-bit nonce.", "nonce");
            if (input == null || output == null || output.Length < input.Length)
                throw new ArgumentException("Input/output buffers are invalid.");

            uint[] initial = new uint[StateWords];
            uint[] state = new uint[StateWords];
            byte[] block = new byte[BlockBytes];

            initial[0] = 0x4E4F4952u; // "NOIR"
            initial[1] = 0x41525831u; // "ARX1"

            for (int i = 0; i < 4; i++)
                initial[2 + i] = Load32(nonce, i * 4);
            for (int i = 0; i < 8; i++)
                initial[6 + i] = Load32(key, i * 4);

            ulong blockCounter = 0;
            int position = 0;

            while (position < input.Length)
            {
                initial[14] = (uint)blockCounter;
                initial[15] = (uint)(blockCounter >> 32);
                Array.Copy(initial, state, StateWords);

                for (int round = 0; round < Rounds; round++)
                {
                    Mix(ref state[0], ref state[1], ref state[2], ref state[3]);
                    Mix(ref state[4], ref state[5], ref state[6], ref state[7]);
                    Mix(ref state[8], ref state[9], ref state[10], ref state[11]);
                    Mix(ref state[12], ref state[13], ref state[14], ref state[15]);

                    RotateRows(state);

                    uint roundConstant = RoundConstants[round];
                    for (int i = 0; i < StateWords; i++)
                        state[i] += roundConstant + unchecked((uint)i * 0x9E3779B9u);
                }

                for (int i = 0; i < StateWords; i++)
                {
                    uint word = state[i] + initial[i];
                    block[i * 4] = (byte)word;
                    block[i * 4 + 1] = (byte)(word >> 8);
                    block[i * 4 + 2] = (byte)(word >> 16);
                    block[i * 4 + 3] = (byte)(word >> 24);
                }

                int take = Math.Min(BlockBytes, input.Length - position);
                for (int i = 0; i < take; i++)
                    output[position + i] = (byte)(input[position + i] ^ block[i]);

                position += take;
                blockCounter++;
            }

            Array.Clear(state, 0, state.Length);
            Array.Clear(initial, 0, initial.Length);
            Array.Clear(block, 0, block.Length);
        }

        private static void Mix(ref uint a, ref uint b, ref uint c, ref uint d)
        {
            a += b;
            d = RotateLeft(d ^ a, 7);
            c += d;
            b = RotateLeft(b ^ c, 11);
            a += b;
            d = RotateLeft(d ^ a, 19);
            c += d;
            b = RotateLeft(b ^ c, 23);
        }

        private static void RotateRows(uint[] state)
        {
            uint temporary;

            temporary = state[4];
            state[4] = state[5];
            state[5] = state[6];
            state[6] = state[7];
            state[7] = temporary;

            temporary = state[8];
            state[8] = state[10];
            state[10] = temporary;
            temporary = state[9];
            state[9] = state[11];
            state[11] = temporary;

            temporary = state[15];
            state[15] = state[14];
            state[14] = state[13];
            state[13] = state[12];
            state[12] = temporary;
        }

        private static uint RotateLeft(uint value, int bits)
        {
            return (value << bits) | (value >> (32 - bits));
        }

        private static uint Load32(byte[] source, int offset)
        {
            return (uint)source[offset]
                 | ((uint)source[offset + 1] << 8)
                 | ((uint)source[offset + 2] << 16)
                 | ((uint)source[offset + 3] << 24);
        }
    }
}
