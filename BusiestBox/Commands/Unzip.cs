using System;
using System.IO;
using System.IO.Compression;

using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Unzip
    {
        // Usage:
        //   unzip <zipSource> [destinationDir]
        //     - zipSource can be http(s):// URL, vfs://..., or FS path
        //     - destinationDir defaults to currentDirectory (VFS or FS)
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length < 1 || args.Length > 2)
            {
                Console.WriteLine("Usage: unzip <zipSource> [destinationDir]");
                return;
            }

            string sourceRaw = args[0];
            string destRaw = args.Length == 2 ? args[1] : currentDirectory;

            try
            {
                // Resolve destination (dir) and ensure it exists
                string destResolved = VfsLayer.ResolvePath(currentDirectory, destRaw);
                if (!VfsLayer.DirectoryExists(destResolved))
                {
                    VfsLayer.CreateDirectory(destResolved);
                }

                // Get a stream for the zip bytes (URL/VFS/FS)
                using (var zipStream = OpenZipStream(currentDirectory, sourceRaw))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false))
                {
                    int files = 0, dirs = 0;

                    foreach (var entry in archive.Entries)
                    {
                        // Normalize entry path
                        string rel = NormalizeZipEntryPath(entry.FullName);
                        if (rel.Length == 0) continue;

                        bool isDir = rel.EndsWith("/") || rel.EndsWith("\\");
                        string target = VfsLayer.Combine(destResolved, rel);

                        if (isDir)
                        {
                            VfsLayer.CreateDirectory(target);
                            dirs++;
                            continue;
                        }

                        // Ensure parent dir exists
                        string parent = ParentOf(target);
                        if (!string.IsNullOrEmpty(parent))
                            VfsLayer.CreateDirectory(parent);

                        // Extract file
                        try
                        {
                            using (var es = entry.Open())
                            {
                                // Read all bytes and write via VfsLayer
                                using (var ms = new MemoryStream())
                                {
                                    es.CopyTo(ms);
                                    VfsLayer.WriteAllBytes(target, ms.ToArray());
                                }
                            }
                            files++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[!] unzip: failed to extract '{0}': {1}", rel, ex.Message);
                        }
                    }

                    Console.WriteLine("[*] unzip: extracted {0} file(s), {1} dir(s) to {2}", files, dirs, destResolved);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] unzip failed: " + ex.Message);
            }
        }

        private static Stream OpenZipStream(string currentDirectory, string sourceRaw)
        {
            if (IsHttpUrl(sourceRaw))
            {
                string _;
                byte[] data = NetUtils.DownloadDataSmart(sourceRaw, out _);
                return new MemoryStream(data, writable: false);
            }

            // VFS / FS
            string resolved = VfsLayer.ResolvePath(currentDirectory, sourceRaw);
            if (!VfsLayer.FileExists(resolved))
                throw new FileNotFoundException("Zip file not found", sourceRaw);

            if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                var data = VfsLayer.ReadAllBytes(resolved);
                return new MemoryStream(data, writable: false);
            }
            else
            {
                return File.OpenRead(resolved);
            }
        }

        private static bool IsHttpUrl(string s)
        {
            return Uri.IsWellFormedUriString(s, UriKind.Absolute) &&
                   (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeZipEntryPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string s = p.Replace('\\', '/');

            // Collapse '.', '..'
            var parts = new System.Collections.Generic.List<string>();
            var tokens = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (t == ".") continue;
                if (t == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(t);
            }

            // Preserve trailing slash semantics for directories
            string result = string.Join("/", parts.ToArray());
            if (s.EndsWith("/") && result.Length > 0) result += "/";

            return result;
        }

        private static string ParentOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            bool isVfs = path.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);

            if (isVfs)
            {
                // vfs://abc/def → vfs://abc
                string v = path.Substring(6).Trim('/');
                if (v.Length == 0) return "vfs://";
                int i = v.LastIndexOf('/');
                if (i < 0) return "vfs://";
                return "vfs://" + v.Substring(0, i);
            }
            else
            {
                try { return Path.GetDirectoryName(path) ?? ""; }
                catch { return ""; }
            }
        }
    }
}
