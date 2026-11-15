using System;
using System.Diagnostics;
using System.Text;

namespace Noir
{
    internal static class NoirSelfTest
    {
        private static int _failures;
        private static int _checks;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("NOIR-ARX self-test");
            Console.WriteLine("==================");

            TestRoundTrips();
            TestRandomization();
            TestWrongPassphrase();
            TestTampering();
            TestArmorHandling();
            TestFormatErrors();
            TestThroughput();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "PASS: " + _checks + " checks completed."
                : "FAIL: " + _failures + " of " + _checks + " checks failed.");

            return _failures == 0 ? 0 : 1;
        }

        private static void TestRoundTrips()
        {
            string longText = BuildLongText();
            string allScalars = BuildScalarSample();

            string[] samples =
            {
                string.Empty,
                "hello",
                "The quick brown fox jumps over the lazy dog.\r\nLine two.\nLine three.",
                "1234567890 !@#$%^&*()_+-=[]{};:'\",.<>/?\\|`~",
                "\0\t\r\n\u2028\u2029\uFEFF",
                "汉字，标点。日本語のテキスト。한국어 텍스트.",
                "👩🏽‍🚀🌒🔐🧪 — emoji and a family: 👨‍👩‍👧‍👦",
                "العربية עברית پارسی",
                "e\u0301 vs \u00e9 — combining marks must survive exactly.",
                "\uFFFD\uFFFF\uD7FF\uE000\u10FFFF",
                longText,
                allScalars
            };

            for (int i = 0; i < samples.Length; i++)
            {
                string plain = samples[i];
                string cipher = NoirCipher.Encrypt(plain, "correct horse battery staple");
                string returned = NoirCipher.Decrypt(cipher, "correct horse battery staple");
                Check(returned == plain, "round-trip sample " + i + " (" + plain.Length + " chars)");
                Check(cipher.StartsWith(NoirCipher.ArmorPrefix, StringComparison.Ordinal),
                    "armor prefix sample " + i);
                Check(!cipher.Contains("\n") && !cipher.Contains("\r"),
                    "armor is a single line sample " + i);
            }

            string builtIn = "This text uses the built-in key.";
            string builtInCipher = NoirCipher.Encrypt(builtIn, null);
            Check(NoirCipher.Decrypt(builtInCipher, string.Empty) == builtIn, "built-in key round-trip");
            Check(NoirCipher.LooksLikeCiphertext(" \r\n" + builtInCipher), "looks-like-ciphertext helper");
            Check(!NoirCipher.LooksLikeCiphertext("plain text"), "plain text is not ciphertext");
        }

        private static void TestRandomization()
        {
            string plain = "The same input must never produce the same armor.";
            string first = NoirCipher.Encrypt(plain, "noir");
            string second = NoirCipher.Encrypt(plain, "noir");
            Check(first != second, "random salt and nonce make ciphertexts unique");

            byte[] firstPayload = Unarmor(first);
            byte[] secondPayload = Unarmor(second);
            Check(firstPayload.Length == secondPayload.Length, "same-length inputs produce same-length payloads");
            Check(!Equal(firstPayload, 6, firstPayload.Length - 6, secondPayload, 6, secondPayload.Length - 6),
                "salt/nonce/ciphertext differ between encryptions");
            Check(firstPayload[0] == (byte)'N' && firstPayload[1] == (byte)'O' &&
                  firstPayload[2] == (byte)'I' && firstPayload[3] == (byte)'R',
                "payload magic is NOIR");
            Check(firstPayload[4] == NoirCipher.Version && firstPayload[5] == NoirCipher.Suite,
                "payload version and suite are correct");
        }

        private static void TestWrongPassphrase()
        {
            string cipher = NoirCipher.Encrypt("secret message", "alpha");
            ExpectFailure(() => NoirCipher.Decrypt(cipher, "beta"), "wrong passphrase is rejected");
            ExpectFailure(() => NoirCipher.Decrypt(cipher, string.Empty), "built-in key cannot open passphrase ciphertext");
        }

        private static void TestTampering()
        {
            string plain = "Authentication must catch every modified payload.";
            string cipher = NoirCipher.Encrypt(plain, "tamper-test");
            byte[] payload = Unarmor(cipher);

            byte[] tagChanged = (byte[])payload.Clone();
            tagChanged[38] ^= 0x01; // first tag byte
            ExpectFailure(() => NoirCipher.Decrypt(Rearmor(tagChanged), "tamper-test"),
                "modified authentication tag is rejected");

            if (payload.Length > 54)
            {
                byte[] bodyChanged = (byte[])payload.Clone();
                bodyChanged[54] ^= 0x80; // first ciphertext byte
                ExpectFailure(() => NoirCipher.Decrypt(Rearmor(bodyChanged), "tamper-test"),
                    "modified ciphertext is rejected");
            }

            byte[] truncated = new byte[payload.Length - 1];
            Array.Copy(payload, truncated, truncated.Length);
            ExpectFailure(() => NoirCipher.Decrypt(Rearmor(truncated), "tamper-test"),
                "truncated ciphertext is rejected");
        }

        private static void TestArmorHandling()
        {
            string plain = "Line wrapping should not break decryption. 任意文本。";
            string cipher = NoirCipher.Encrypt(plain, "armor");

            StringBuilder wrapped = new StringBuilder();
            wrapped.Append("  \r\n");
            for (int i = 0; i < cipher.Length; i++)
            {
                wrapped.Append(cipher[i]);
                if (i > 0 && i % 47 == 0) wrapped.Append("\r\n\t");
            }
            wrapped.Append("  ");

            Check(NoirCipher.Decrypt(wrapped.ToString(), "armor") == plain,
                "whitespace-wrapped armor decrypts");

            string upperPrefix = "NOIR1." + cipher.Substring(NoirCipher.ArmorPrefix.Length);
            Check(NoirCipher.Decrypt(upperPrefix, "armor") == plain, "prefix is case-insensitive");
        }

        private static void TestFormatErrors()
        {
            ExpectFailure(() => NoirCipher.Decrypt("hello", "x"), "missing prefix is rejected");
            ExpectFailure(() => NoirCipher.Decrypt("noir1.", "x"), "empty body is rejected");
            ExpectFailure(() => NoirCipher.Decrypt("noir1.%%%%", "x"), "invalid base64 is rejected");
            ExpectFailure(() => NoirCipher.Decrypt("noir1.AAAA", "x"), "short payload is rejected");

            byte[] payload = Unarmor(NoirCipher.Encrypt("x", "format"));
            payload[0] = (byte)'X';
            ExpectFailure(() => NoirCipher.Decrypt(Rearmor(payload), "format"), "bad magic is rejected");

            payload = Unarmor(NoirCipher.Encrypt("x", "format"));
            payload[4] = 9;
            ExpectFailure(() => NoirCipher.Decrypt(Rearmor(payload), "format"), "unknown version is rejected");

            payload = Unarmor(NoirCipher.Encrypt("x", "format"));
            payload[5] = 9;
            ExpectFailure(() => NoirCipher.Decrypt(Rearmor(payload), "format"), "unknown suite is rejected");
        }

        private static void TestThroughput()
        {
            string plain = new string('A', 1024 * 1024);
            Stopwatch watch = Stopwatch.StartNew();
            string cipher = NoirCipher.Encrypt(plain, "perf");
            string returned = NoirCipher.Decrypt(cipher, "perf");
            watch.Stop();

            Check(returned.Length == plain.Length && returned == plain, "1 MiB round-trip");
            Console.WriteLine("1 MiB encrypt+decrypt: " + watch.ElapsedMilliseconds + " ms (including two PBKDF2 runs)");
        }

        private static string BuildLongText()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < 2000; i++)
                builder.Append("Line ").Append(i).Append(": café 中文 🌒\r\n");
            return builder.ToString();
        }

        private static string BuildScalarSample()
        {
            var random = new Random(20261114);
            var builder = new StringBuilder();
            for (int i = 0; i < 5000; i++)
            {
                int codePoint;
                do
                {
                    codePoint = random.Next(0x20, 0x10FFFF);
                } while (codePoint >= 0xD800 && codePoint <= 0xDFFF);

                builder.Append(char.ConvertFromUtf32(codePoint));
            }
            return builder.ToString();
        }

        private static byte[] Unarmor(string armor)
        {
            string body = armor.Substring(NoirCipher.ArmorPrefix.Length)
                .Replace('-', '+')
                .Replace('_', '/');
            switch (body.Length % 4)
            {
                case 2: body += "=="; break;
                case 3: body += "="; break;
            }
            return Convert.FromBase64String(body);
        }

        private static string Rearmor(byte[] payload)
        {
            return NoirCipher.ArmorPrefix + Convert.ToBase64String(payload)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static bool Equal(byte[] left, int leftOffset, int leftLength, byte[] right, int rightOffset, int rightLength)
        {
            if (leftLength != rightLength) return false;
            for (int i = 0; i < leftLength; i++)
            {
                if (left[leftOffset + i] != right[rightOffset + i]) return false;
            }
            return true;
        }

        private static void Check(bool condition, string name)
        {
            _checks++;
            if (condition)
            {
                Console.WriteLine("  ok   " + name);
            }
            else
            {
                _failures++;
                Console.WriteLine("  FAIL " + name);
            }
        }

        private static void ExpectFailure(Action action, string name)
        {
            try
            {
                action();
                Check(false, name);
            }
            catch (NoirCipherException)
            {
                Check(true, name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAIL " + name + " (unexpected " + ex.GetType().Name + ": " + ex.Message + ")");
                _checks++;
                _failures++;
            }
        }
    }
}
