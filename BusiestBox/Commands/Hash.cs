using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Hash
    {
        // Usage:
        //   hash <pathOrUrl> [more...]
        // Notes:
        //   - URLs are downloaded and hashed from the response stream.
        //   - VFS/FS directories are not supported (no -r).
        //   - Globs on VFS/FS are supported (e.g., *.txt).
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: hash <path_or_url> [more...]");
                return;
            }

            // Build the worklist:
            // - keep URLs as-is
            // - expand VFS/FS tokens (including wildcards) via Glob
            var work = new System.Collections.Generic.List<Tuple<string, bool>>(); // (item, isUrl)

            for (int i = 0; i < args.Length; i++)
            {
                var tok = args[i];
                if (IsHttpUrl(tok))
                {
                    work.Add(Tuple.Create(tok, true));
                }
                else
                {
                    // Expand this one token for VFS/FS
                    var expanded = Glob.ExpandArgs(currentDirectory, new[] { tok });
                    if (expanded != null && expanded.Length > 0)
                    {
                        for (int j = 0; j < expanded.Length; j++)
                            work.Add(Tuple.Create(expanded[j], false));
                    }
                    else
                    {
                        // No glob match; resolve anyway so the error message is consistent
                        string resolved;
                        try { resolved = VfsLayer.ResolvePath(currentDirectory, tok); }
                        catch { resolved = tok; }
                        work.Add(Tuple.Create(resolved, false));
                    }
                }
            }

            using (var sha = new SHA256Managed())
            {
                foreach (var item in work)
                {
                    string id = item.Item1;
                    bool isUrl = item.Item2;

                    try
                    {
                        if (isUrl)
                        {
                            string _;
                            byte[] buf = NetUtils.DownloadDataSmart(id, out _);
                            byte[] digest = sha.ComputeHash(buf);
                            Console.WriteLine("{0}  {1}", ToHex(digest), id);
                            continue;
                        }

                        // VFS vs FS
                        if (id.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                        {
                            if (VfsLayer.DirectoryExists(id))
                            {
                                Console.WriteLine("[!] hash: '{0}' is a directory (not supported)", id);
                                continue;
                            }
                            if (!VfsLayer.FileExists(id))
                            {
                                Console.WriteLine("[!] hash: file not found: {0}", id);
                                continue;
                            }

                            var data = VfsLayer.ReadAllBytes(id); // VFS is in-memory; OK to read once
                            byte[] digest = sha.ComputeHash(data);
                            Console.WriteLine("{0}  {1}", ToHex(digest), id);
                        }
                        else
                        {
                            // Real filesystem
                            if (Directory.Exists(id))
                            {
                                Console.WriteLine("[!] hash: '{0}' is a directory (not supported)", id);
                                continue;
                            }
                            if (!File.Exists(id))
                            {
                                Console.WriteLine("[!] hash: file not found: {0}", id);
                                continue;
                            }

                            using (var fs = new FileStream(id, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                byte[] digest = sha.ComputeHash(fs);
                                Console.WriteLine("{0}  {1}", ToHex(digest), id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[!] hash: {0}: {1}", id, ex.Message);
                    }
                }
            }
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!(s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return false;
            Uri u; return Uri.TryCreate(s, UriKind.Absolute, out u);
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
