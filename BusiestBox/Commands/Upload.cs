using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Upload
    {
        // Usage:
        //   upload <src1> [src2 ...] <dest_url>
        // Notes:
        //   - Each source is posted as its own multipart/form-data request to the same URL
        //   - Source may be FS, VFS (vfs://...), or an HTTP/HTTPS URL (which we download first)
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.WriteLine("Usage: upload <source1> [source2 ...] <dest_url>");
                return;
            }

            string destUrl = args[args.Length - 1];
            if (!IsHttpUrl(destUrl))
            {
                Console.WriteLine("[!] upload: destination must be an http/https URL");
                return;
            }

            // If in Updog mode, try to fetch the XOR key once up-front (reuse for all files).
            byte[] updogKey = null;
            if (NetUtils.UpdogMode)
            {
                updogKey = TryFetchUpdogKey(destUrl);
                if (updogKey == null)
                {
                    Console.WriteLine("[!] upload: updog_mode is ON, but could not extract key from the page. Refusing to continue");
                    return;
                }
            }

            // All but last are sources
            for (int i = 0; i < args.Length - 1; i++)
            {
                var srcRaw = args[i];
                try
                {
                    string fileName;
                    byte[] data;

                    if (IsHttpUrl(srcRaw))
                    {
                        // Download source first (handles smuggled download pages if needed)
                        string suggested;
                        data = NetUtils.DownloadDataSmart(srcRaw, out suggested);
                        fileName = !string.IsNullOrEmpty(suggested)
                                   ? suggested
                                   : SafeLeafFromUrl(srcRaw);
                    }
                    else
                    {
                        // Resolve FS or VFS
                        string resolved = VfsLayer.ResolvePath(currentDirectory, srcRaw);

                        if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!VfsLayer.FileExists(resolved))
                            {
                                Console.WriteLine("[!] upload: not found: " + srcRaw);
                                continue;
                            }
                            data = VfsLayer.ReadAllBytes(resolved);
                            fileName = VfsLeaf(resolved);
                        }
                        else
                        {
                            if (!File.Exists(resolved))
                            {
                                Console.WriteLine("[!] upload: not found: " + srcRaw);
                                continue;
                            }
                            data = File.ReadAllBytes(resolved);
                            fileName = Path.GetFileName(resolved);
                        }
                    }

                    // Post this file (smuggled if updog key present, else normal)
                    PostMultipartFileSmart(destUrl, fileName, data, updogKey);
                    Console.WriteLine("[*] Uploaded {0} ({1} bytes) -> {2}", fileName, data.Length, destUrl);
                }
                catch (WebException wex)
                {
                    string msg = wex.Message;
                    try
                    {
                        using (var resp = wex.Response as HttpWebResponse)
                        {
                            if (resp != null)
                                msg = string.Format("{0} {1}", (int)resp.StatusCode, resp.StatusDescription);
                        }
                    }
                    catch { }
                    Console.WriteLine("[!] upload: {0} -> {1} failed: {2}", srcRaw, destUrl, msg);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] upload: {0} -> {1} failed: {2}", srcRaw, destUrl, ex.Message);
                }
            }
        }

        // --- Smarts: Use Updog XOR when a key is available; otherwise normal upload
        private static void PostMultipartFileSmart(string url, string fileName, byte[] fileData, byte[] updogKeyOrNull)
        {
            byte[] payload = fileData;

            // If we have an XOR key (Updog smuggling), encode the payload before posting
            if (updogKeyOrNull != null && updogKeyOrNull.Length > 0)
            {
                payload = Mask(fileData, updogKeyOrNull, 0);
            }

            PostMultipartFile(url, fileName, payload);
        }

        // Build a minimal multipart/form-data body in memory and POST
        private static void PostMultipartFile(string url, string fileName, byte[] fileData)
        {
            string boundary = "----" + Guid.NewGuid().ToString("N");
            string header =
                "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename=\"" + SanitizeFileName(fileName) + "\"\r\n" +
                "Content-Type: application/octet-stream\r\n\r\n";

            string trailer = "\r\n--" + boundary + "--\r\n";

            byte[] headBytes = Encoding.ASCII.GetBytes(header);
            byte[] tailBytes = Encoding.ASCII.GetBytes(trailer);

            byte[] body = new byte[headBytes.Length + fileData.Length + tailBytes.Length];
            Buffer.BlockCopy(headBytes, 0, body, 0, headBytes.Length);
            Buffer.BlockCopy(fileData, 0, body, headBytes.Length, fileData.Length);
            Buffer.BlockCopy(tailBytes, 0, body, headBytes.Length + fileData.Length, tailBytes.Length);

            using (var wc = NetUtils.CreateWebClient())
            {
                wc.Headers[HttpRequestHeader.ContentType] = "multipart/form-data; boundary=" + boundary;
                wc.UploadData(url, "POST", body);
            }
        }

        // Try to fetch the XOR key from the upload page (Updog)
        // Looks for: const xorKey = atob("BASE64==");
        private static byte[] TryFetchUpdogKey(string url)
        {
            try
            {
                using (var wc = NetUtils.CreateWebClient())
                {
                    byte[] htmlBytes = wc.DownloadData(url);
                    string html = TryToUtf8(htmlBytes);

                    var m = Regex.Match(
                        html,
                        @"const\s+xorKey\s*=\s*atob\(\s*[""'](?<b64>[^""']+)[""']\s*\)",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    if (!m.Success) return null;

                    string b64 = m.Groups["b64"].Value;
                    if (string.IsNullOrEmpty(b64)) return null;

                    return Convert.FromBase64String(b64);
                }
            }
            catch
            {
                return null;
            }
        }

        private static byte[] Mask(byte[] data, byte[] key, int offset)
        {
            if (key == null || key.Length == 0) return data;
            var outBuf = new byte[data.Length];
            int kLen = key.Length;
            for (int i = 0; i < data.Length; i++)
                outBuf[i] = (byte)(data[i] ^ key[(i + offset) % kLen]);
            return outBuf;
        }

        private static string TryToUtf8(byte[] bytes)
        {
            if (bytes == null) return "";
            try { return Encoding.UTF8.GetString(bytes); } catch { return ""; }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "upload.bin";
            name = name.Replace("\"", "_"); // safety for header quoting
            int i = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
            return i < 0 ? name : name.Substring(i + 1);
        }

        private static string VfsLeaf(string vfsResolved)
        {
            string s = vfsResolved.Substring("vfs://".Length).TrimEnd('/');
            int i = s.LastIndexOf('/');
            return i < 0 ? s : s.Substring(i + 1);
        }

        private static string SafeLeafFromUrl(string url)
        {
            try
            {
                var u = new Uri(url);
                var leaf = Path.GetFileName(u.AbsolutePath);
                return string.IsNullOrEmpty(leaf) ? "download" : leaf;
            }
            catch { return "download"; }
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                   Uri.IsWellFormedUriString(s, UriKind.Absolute);
        }
    }
}
