using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BusiestBox.Crypto
{
    internal static class Crypto
    {
        // Format versioning (in case you change parameters later)
        private const byte FormatVersion = 1;

        // Sizes (bytes)
        private const int AesBlockSize = 16;          // 128-bit block size
        private const int AesKeySize = 32;            // 256-bit AES key
        private const int HmacKeySize = 32;           // 256-bit HMAC key
        private const int Pbkdf2SaltSize = 16;        // 128-bit salt
        private const int HmacSize = 32;              // HMAC-SHA256

        private const int Pbkdf2Iterations = 5000; // Small, but performant

        private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

        // ---------- Public API ----------

        // Hash helpers
        public static byte[] Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(data);
        }

        public static string Sha256Hex(byte[] data)
        {
            var hash = Sha256(data);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        // High-level: passphrase-based encryption (returns versioned blob)
        public static byte[] EncryptBytes(string passphrase, byte[] plaintext)
        {
            if (plaintext == null) plaintext = new byte[0];
            byte[] salt = RandomBytes(Pbkdf2SaltSize);
            byte[] iv = RandomBytes(AesBlockSize);

            // If you need to debug the crypto code...
            // Console.WriteLine("Passphrase bytes: " + BitConverter.ToString(Encoding.UTF8.GetBytes(passphrase)).Replace("-", "").ToLower());
            // Console.WriteLine("Salt length: " + salt.Length);

            // Derive independent keys for encryption and MAC
            DeriveKeys(passphrase, salt, out var encKey, out var macKey);

            // Console.WriteLine("Salt: " + BitConverter.ToString(salt).Replace("-", "").ToLower());
            // Console.WriteLine("Derived key material: " + BitConverter.ToString(encKey).Replace("-", "").ToLower() +
            //                   BitConverter.ToString(macKey).Replace("-", "").ToLower());

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = AesKeySize * 8;
                aes.BlockSize = AesBlockSize * 8;
                using (var enc = aes.CreateEncryptor(encKey, iv))
                {
                    ciphertext = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }

            // Build payload without tag: [ver][salt][iv][cipher]
            var header = new[] { FormatVersion };
            byte[] withoutTag = Concat(header, salt, iv, ciphertext);

            // Tag = HMAC(macKey, salt || iv || cipher) — version is excluded from tag on purpose
            byte[] tag;
            using (var hmac = new HMACSHA256(macKey))
            {
                tag = hmac.ComputeHash(Concat(salt, iv, ciphertext));
            }

            // Final blob
            return Concat(withoutTag, tag);
        }

        public static byte[] DecryptBytes(string passphrase, byte[] blob)
        {
            if (blob == null || blob.Length < 1 + Pbkdf2SaltSize + AesBlockSize + HmacSize)
                throw new InvalidDataException("Ciphertext blob too short.");

            int offset = 0;

            byte version = blob[offset++];
            if (version != FormatVersion)
                throw new InvalidDataException("Unsupported ciphertext version.");

            byte[] salt = Slice(blob, ref offset, Pbkdf2SaltSize);
            byte[] iv = Slice(blob, ref offset, AesBlockSize);

            // Remaining must be at least HMAC
            int remaining = blob.Length - offset;
            if (remaining < HmacSize)
                throw new InvalidDataException("Ciphertext blob corrupted.");

            int cipherLen = remaining - HmacSize;
            byte[] ciphertext = Slice(blob, ref offset, cipherLen);
            byte[] tag = Slice(blob, ref offset, HmacSize);

            // Derive keys
            DeriveKeys(passphrase, salt, out var encKey, out var macKey);

            // Verify tag before decrypting (ETM)
            byte[] expectedTag;
            using (var hmac = new HMACSHA256(macKey))
            {
                expectedTag = hmac.ComputeHash(Concat(salt, iv, ciphertext));
            }
            if (!ConstantTimeEquals(tag, expectedTag))
                throw new CryptographicException("Invalid password or data tampered.");

            // Decrypt
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = AesKeySize * 8;
                aes.BlockSize = AesBlockSize * 8;

                using (var dec = aes.CreateDecryptor(encKey, iv))
                {
                    return dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
            }
        }

        public static byte[] EncryptBytesWithKey(byte[] key, byte[] iv, byte[] plaintext)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (key.Length != AesKeySize)
                throw new ArgumentException($"Key must be {AesKeySize} bytes for AES-256.", nameof(key));
            if (iv.Length != AesBlockSize)
                throw new ArgumentException($"IV must be {AesBlockSize} bytes.", nameof(iv));

            if (plaintext == null) plaintext = new byte[0];

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = AesKeySize * 8;
                aes.BlockSize = AesBlockSize * 8;

                using (var enc = aes.CreateEncryptor(key, iv))
                {
                    ciphertext = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }

            // Optional: prepend IV so decryptor doesn't have to store it separately
            return Concat(iv, ciphertext);
        }

        public static byte[] DecryptBytesWithKey(byte[] key, byte[] ciphertextWithIv)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != AesKeySize)
                throw new ArgumentException($"Key must be {AesKeySize} bytes for AES-256.", nameof(key));

            if (ciphertextWithIv == null || ciphertextWithIv.Length < AesBlockSize)
                throw new InvalidDataException("Ciphertext too short (missing IV).");

            int offset = 0;
            byte[] iv = Slice(ciphertextWithIv, ref offset, AesBlockSize);
            byte[] ciphertext = Slice(ciphertextWithIv, ref offset, ciphertextWithIv.Length - AesBlockSize);

            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.KeySize = AesKeySize * 8;
                aes.BlockSize = AesBlockSize * 8;

                using (var dec = aes.CreateDecryptor(key, iv))
                {
                    return dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                }
            }
        }

        public static byte[] RandomBytes(int count)
        {
            var buf = new byte[count];
            Rng.GetBytes(buf);
            return buf;
        }

        // ---------- Internals ----------

        private static void DeriveKeys(string passphrase, byte[] salt, out byte[] encKey, out byte[] macKey)
        {
            // Derive 64 bytes, split into two independent 32-byte keys.
            using (var kdf = new Rfc2898DeriveBytes(passphrase, salt, Pbkdf2Iterations))
            {
                var material = kdf.GetBytes(AesKeySize + HmacKeySize);
                encKey = new byte[AesKeySize];
                macKey = new byte[HmacKeySize];
                Buffer.BlockCopy(material, 0, encKey, 0, AesKeySize);
                Buffer.BlockCopy(material, AesKeySize, macKey, 0, HmacKeySize);
            }
        }

        private static byte[] Slice(byte[] src, ref int offset, int count)
        {
            var b = new byte[count];
            Buffer.BlockCopy(src, offset, b, 0, count);
            offset += count;
            return b;
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            int len = 0;
            for (int i = 0; i < arrays.Length; i++) len += arrays[i].Length;
            var output = new byte[len];
            int pos = 0;
            for (int i = 0; i < arrays.Length; i++)
            {
                Buffer.BlockCopy(arrays[i], 0, output, pos, arrays[i].Length);
                pos += arrays[i].Length;
            }
            return output;
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
