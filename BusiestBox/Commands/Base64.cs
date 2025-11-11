using System;
using System.IO;
using System.Text;
using System.Threading;
using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Base64
    {
        public static void Execute(string currentDirectory, string[] args)
        {
            if (args == null || args.Length < 2)
            {
                PrintUsage();
                return;
            }

            string mode = args[0].ToLowerInvariant();
            string inputPath = args[1];
            string outputPath = args.Length >= 3 ? args[2] : null;

            using (var cts = new CancellationTokenSource())
            {
                bool cancelled = false;
                ConsoleCancelEventHandler handler = (s, e) =>
                {
                    e.Cancel = true;
                    cancelled = true;
                    try { cts.Cancel(); } catch { }
                };

                Console.CancelKeyPress += handler;
                try
                {
                    switch (mode)
                    {
                        case "encode":
                            Encode(currentDirectory, inputPath, outputPath, cts.Token);
                            break;

                        case "decode":
                            Decode(currentDirectory, inputPath, outputPath, cts.Token);
                            break;

                        default:
                            Console.WriteLine("[!] base64: unknown mode '{0}'", mode);
                            PrintUsage();
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("\n[*] base64 operation canceled.");
                }
                catch (Exception ex)
                {
                    if (!cancelled)
                        Console.WriteLine("[!] base64: {0}", ex.Message);
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
        }

        // -------------------------
        // Encoding
        // -------------------------
        private static void Encode(string currentDirectory, string inputPath, string outputPath, CancellationToken ct)
        {
            byte[] inputBytes = ReadAllFromLocation(currentDirectory, inputPath, ct);

            string b64 = Convert.ToBase64String(inputBytes);
            ClearArray(inputBytes);

            if (string.IsNullOrEmpty(outputPath))
            {
                WriteWithCancellation(b64, ct);
                ClearString(ref b64);
                return;
            }

            string resolved = VfsLayer.ResolvePath(currentDirectory, outputPath);
            VfsLayer.WriteAllText(resolved, b64, Encoding.ASCII);
            ClearString(ref b64);
            Console.WriteLine("[*] base64: encoded data saved to {0}", resolved);
        }

        // -------------------------
        // Decoding
        // -------------------------
        private static void Decode(string currentDirectory, string inputPath, string outputPath, CancellationToken ct)
        {
            string inputText = ReadAllTextFromLocation(currentDirectory, inputPath, ct);
            if (string.IsNullOrEmpty(inputText))
            {
                Console.WriteLine("[!] base64: input file is empty");
                return;
            }

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(inputText);
            }
            catch (FormatException)
            {
                Console.WriteLine("[!] base64: input is not valid base64 data");
                return;
            }
            finally
            {
                ClearString(ref inputText);
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                // Don't print binary to the console
                if (LooksBinary(decoded))
                {
                    Console.WriteLine("[!] base64: decoded data appears to be binary. Refusing to print to console.");
                    ClearArray(decoded);
                    return;
                }

                string text;
                if (TryDecodeText(decoded, out text))
                {
                    WriteWithCancellation(text, ct);
                    ClearString(ref text);
                }
                else
                {
                    Console.WriteLine("[!] base64: decoded data could not be interpreted as text");
                }

                ClearArray(decoded);
                return;
            }

            string resolved = VfsLayer.ResolvePath(currentDirectory, outputPath);
            VfsLayer.WriteAllBytes(resolved, decoded);
            ClearArray(decoded);
            Console.WriteLine("[*] base64: decoded data saved to {0}", resolved);
        }

        // -------------------------
        // Helpers
        // -------------------------
        private static byte[] ReadAllFromLocation(string currentDirectory, string token, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (IsHttpUrl(token))
            {
                string _;
                return NetUtils.DownloadDataSmart(token, out _, ct);
            }

            string resolved = VfsLayer.ResolvePath(currentDirectory, token);
            if (!VfsLayer.FileExists(resolved))
                throw new FileNotFoundException("not found", token);

            return VfsLayer.ReadAllBytes(resolved);
        }

        private static string ReadAllTextFromLocation(string currentDirectory, string token, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (IsHttpUrl(token))
            {
                string _;
                byte[] data = NetUtils.DownloadDataSmart(token, out _, ct);
                string result = Encoding.ASCII.GetString(data);
                ClearArray(data);
                return result;
            }

            string resolved = VfsLayer.ResolvePath(currentDirectory, token);
            if (!VfsLayer.FileExists(resolved))
                throw new FileNotFoundException("not found", token);

            return VfsLayer.ReadAllText(resolved, Encoding.ASCII);
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                   Uri.IsWellFormedUriString(s, UriKind.Absolute);
        }

        private static void WriteWithCancellation(string text, CancellationToken ct, int chunkSize = 4096)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = 0;
            while (i < text.Length)
            {
                ct.ThrowIfCancellationRequested();
                int take = Math.Min(chunkSize, text.Length - i);
                Console.Write(text.Substring(i, take));
                i += take;
            }
        }

        private static bool TryDecodeText(byte[] data, out string text)
        {
            text = null;
            if (data == null || data.Length == 0) { text = string.Empty; return true; }

            try
            {
                text = Encoding.UTF8.GetString(data);
                return true;
            }
            catch { return false; }
        }

        private static bool LooksBinary(byte[] data)
        {
            if (data == null || data.Length == 0) return false;

            int len = Math.Min(data.Length, 2048);
            int control = 0;

            for (int i = 0; i < len; i++)
            {
                byte b = data[i];
                if (b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0C) continue;
                if (b < 0x08) { control++; continue; }
                if (b >= 0x0E && b < 0x20) { control++; continue; }
            }

            return control * 10 > len;
        }

        private static void ClearArray(byte[] data)
        {
            if (data == null) return;
            for (int i = 0; i < data.Length; i++) data[i] = 0;
        }

        private static void ClearString(ref string s)
        {
            if (s == null) return;
            // strings are immutable, but we can at least drop the reference
            s = null;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  base64 encode <infile> [outfile]");
            Console.WriteLine("  base64 decode <infile> [outfile]");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  - If no outfile is provided, encoded data is printed to the console.");
            Console.WriteLine("  - When decoding, binary data will not be printed to the console.");
            Console.WriteLine("  - Ctrl+C can be used to cancel large encodes/decodes.");
        }
    }
}
