using System;
using System.Text;
using System.Threading;

using BusiestBox.Vfs;
using BusiestBox.Utils;

namespace BusiestBox.Commands
{
    internal class Cat
    {
        public static void Execute(string currentDirectory, string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                Console.WriteLine("Usage: cat <path1> [path2] ...");
                return;
            }

            // Local CTS so we can cancel downloads/printing immediately on Ctrl+C
            using (var cts = new CancellationTokenSource())
            {
                bool cancelled = false;
                ConsoleCancelEventHandler handler = (s, e) =>
                {
                    // Don't terminate the process; just cancel this command
                    e.Cancel = true;
                    cancelled = true;
                    try { cts.Cancel(); } catch { }
                };

                Console.CancelKeyPress += handler;
                try
                {
                    foreach (var rawPath in paths)
                    {
                        if (cts.IsCancellationRequested) break;

                        try
                        {
                            byte[] data;

                            if (IsHttpUrl(rawPath))
                            {
                                // Cancellable HTTP/VPN/proxy-aware download (also handles Updog smuggled mode)
                                data = NetUtils.DownloadDataSmart(rawPath, out _, cts.Token);
                            }
                            else
                            {
                                // VFS / FS
                                string resolved = VfsLayer.ResolvePath(currentDirectory, rawPath);
                                if (!VfsLayer.FileExists(resolved))
                                {
                                    Console.WriteLine("[!] File not found: {0}", rawPath);
                                    continue;
                                }

                                // Local reads are typically fast; still honor cancellation right before/after
                                cts.Token.ThrowIfCancellationRequested();
                                data = VfsLayer.ReadAllBytes(resolved);
                                cts.Token.ThrowIfCancellationRequested();
                            }

                            // --- Decode-first strategy ---
                            string text;
                            if (TryDecodeText(data, out text))
                            {
                                WriteWithCancellation(text, cts.Token);
                                continue;
                            }

                            // If we couldn't confidently decode, treat as binary (avoid gibberish)
                            if (LooksBinaryBytes(data))
                            {
                                Console.WriteLine("[!] cat: appears to be binary data ({0} bytes): {1}", data.Length, rawPath);
                                continue;
                            }

                            // Last resort: system ANSI (rare)
                            WriteWithCancellation(Encoding.Default.GetString(data), cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Respect immediate cancel; Program already prints a Ctrl+C message.
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (cancelled) break; // If we were cancelling, just stop quietly
                            Console.WriteLine("[!] Error reading {0}: {1}", rawPath, ex.Message);
                        }
                    }
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }
            }
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                   Uri.IsWellFormedUriString(s, UriKind.Absolute);
        }

        // Write large text in chunks so we can stop mid-print when cancelled
        private static void WriteWithCancellation(string text, System.Threading.CancellationToken ct, int chunkChars = 4096)
        {
            if (string.IsNullOrEmpty(text)) return;
            int i = 0;
            while (i < text.Length)
            {
                ct.ThrowIfCancellationRequested();
                int take = Math.Min(chunkChars, text.Length - i);
                Console.Write(text.Substring(i, take));
                i += take;
            }
        }

        // Try to decode as text using BOM, UTF-16 heuristics, or strict UTF-8.
        private static bool TryDecodeText(byte[] data, out string text)
        {
            text = null;
            if (data == null || data.Length == 0) { text = string.Empty; return true; }

            int skip;
            Encoding enc = DetectEncodingFromBom(data, out skip);
            if (enc != null)
            {
                text = enc.GetString(data, skip, data.Length - skip);
                return true;
            }

            // UTF-16 heuristic (lots of NULs in one byte lane)
            if (LooksLikeUtf16(data))
            {
                try
                {
                    // Prefer LE first on Windows
                    text = Encoding.Unicode.GetString(data);
                    return true;
                }
                catch { }
                try
                {
                    text = Encoding.BigEndianUnicode.GetString(data);
                    return true;
                }
                catch { }
            }

            // Strict UTF-8 (will throw on invalid)
            try
            {
                var strictUtf8 = new UTF8Encoding(false, true);
                text = strictUtf8.GetString(data);
                return true;
            }
            catch { }

            return false;
        }

        private static Encoding DetectEncodingFromBom(byte[] b, out int bomSkip)
        {
            bomSkip = 0;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            { bomSkip = 3; return new UTF8Encoding(false, true); }            // UTF-8 BOM
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            { bomSkip = 2; return Encoding.Unicode; }                          // UTF-16 LE
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            { bomSkip = 2; return Encoding.BigEndianUnicode; }                 // UTF-16 BE
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0x00 && b[3] == 0x00)
            { bomSkip = 4; return new UTF32Encoding(false, true); }            // UTF-32 LE
            if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0xFE && b[3] == 0xFF)
            { bomSkip = 4; return new UTF32Encoding(true, true); }             // UTF-32 BE
            return null;
        }

        private static bool LooksLikeUtf16(byte[] b)
        {
            int probe = Math.Min(b.Length, 256);
            if (probe < 4) return false;

            int nulEven = 0, nulOdd = 0;
            for (int i = 0; i + 1 < probe; i += 2)
            {
                if (b[i] == 0) nulEven++;
                if (b[i + 1] == 0) nulOdd++;
            }

            int halfPairs = probe / 2;
            return (nulEven > halfPairs / 6) || (nulOdd > halfPairs / 6);
        }

        // Now only used when decoding failed; conservative so we don't mislabel UTF-16 as binary.
        private static bool LooksBinaryBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return false;

            int len = Math.Min(data.Length, 2048);
            int control = 0;

            for (int i = 0; i < len; i++)
            {
                byte b = data[i];
                // Allow common text whitespace/control
                if (b == 0x09 || b == 0x0A || b == 0x0D || b == 0x0C) continue;
                if (b < 0x08) { control++; continue; }
                if (b >= 0x0E && b < 0x20) { control++; continue; }
            }

            // If many unusual control bytes remain, call it binary
            return control * 10 > len; // >10%
        }
    }
}
