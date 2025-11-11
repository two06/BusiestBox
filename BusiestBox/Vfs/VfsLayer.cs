using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using BusiestBox.Utils;
using BusiestBox.Crypto;

namespace BusiestBox.Vfs
{
    internal sealed class VfsEntry
    {
        public string Name { get; internal set; }              // leaf name
        public string FullPath { get; internal set; }          // absolute path starting with '/'
        public VfsEntryType Type { get; internal set; }
        public string Owner { get; internal set; }
        public long Size { get; internal set; }                // plaintext size
        public DateTime LastModified { get; internal set; }    // local time

        // --- File Data ---
        public byte[] Data { get; internal set; }              // stores ciphertext if IsEncrypted=true
        public byte[] EncryptionKey { get; internal set; }     // per-file AES key (null for dirs or unencrypted files)
        public bool IsEncrypted => EncryptionKey != null;

        public Dictionary<string, VfsEntry> Children { get; internal set; } // for dirs

        public bool IsDir { get { return Type == VfsEntryType.Directory; } }

        public VfsEntry()
        {
            Name = "";
            FullPath = "/";
            Owner = "vfs";
            Size = 0;
            LastModified = DateTime.Now;
            Data = new byte[0];
            EncryptionKey = null;
            Children = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static class VfsLayer
    {
        private const string VfsPrefix = "vfs://";

        private static bool IsVfsPath(string path)
        {
            return path.StartsWith(VfsPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripVfsPrefix(string path)
        {
            return path.Length > VfsPrefix.Length ? path.Substring(VfsPrefix.Length) : "/";
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite: true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, targetSubDir);
            }
        }

        // --- File Operations ---
        public static bool FileExists(string path)
        {
            if (IsVfsPath(path))
                return VfsStorage.FileExists(StripVfsPrefix(path));
            return File.Exists(path);
        }

        public static byte[] ReadAllBytes(string path)
        {
            if (IsVfsPath(path))
            {
                byte[] data;
                if (VfsStorage.TryGetFile(StripVfsPrefix(path), out data))
                    return data; // already transparently decrypted by VfsStorage
                throw new FileNotFoundException("VFS file not found", path);
            }
            return File.ReadAllBytes(path);
        }

        public static string ReadAllText(string path, Encoding encoding = null)
        {
            if (encoding == null) encoding = Encoding.UTF8;

            if (IsVfsPath(path))
            {
                byte[] data;
                if (VfsStorage.TryGetFile(StripVfsPrefix(path), out data))
                    return encoding.GetString(data);
                throw new FileNotFoundException("VFS file not found", path);
            }
            return File.ReadAllText(path, encoding);
        }

        public static void WriteAllBytes(string path, byte[] data)
        {
            if (IsVfsPath(path))
            {
                VfsStorage.WriteFile(StripVfsPrefix(path), data);
            }
            else
            {
                File.WriteAllBytes(path, data);
            }
        }

        public static void WriteAllText(string path, string text, Encoding encoding = null)
        {
            if (encoding == null) encoding = Encoding.UTF8;

            if (IsVfsPath(path))
            {
                VfsStorage.WriteFile(StripVfsPrefix(path), encoding.GetBytes(text));
            }
            else
            {
                File.WriteAllText(path, text, encoding);
            }
        }

        // --- Directory Operations ---
        public static bool DirectoryExists(string path)
        {
            if (IsVfsPath(path))
                return VfsStorage.DirectoryExists(StripVfsPrefix(path));
            return Directory.Exists(path);
        }

        public static void CreateDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            if (IsVfsPath(path))
            {
                VfsStorage.CreateDirectory(StripVfsPrefix(path));
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        public static IEnumerable<string> ListDirectory(string path)
        {
            if (IsVfsPath(path))
            {
                var entries = VfsStorage.List(StripVfsPrefix(path));
                return entries.Select(e => VfsPrefix + e.FullPath.TrimStart('/'));
            }
            else
            {
                var dirs = Directory.GetDirectories(path);
                var files = Directory.GetFiles(path);
                return dirs.Concat(files);
            }
        }

        public static void Copy(string sourcePath, string destPath, bool recursive = false)
        {
            bool sourceIsVfs = IsVfsPath(sourcePath);
            bool destIsVfs = IsVfsPath(destPath);

            if (sourceIsVfs && !destIsVfs)
            {
                // --- VFS -> Real FS copy (decrypt then zero) ---
                string vfsPath = StripVfsPrefix(sourcePath);
                byte[] data;
                if (!VfsStorage.TryGetFile(vfsPath, out data))
                    throw new FileNotFoundException("VFS file not found", sourcePath);

                string finalDest = Directory.Exists(destPath)
                    ? Path.Combine(destPath, Path.GetFileName(sourcePath))
                    : destPath;

                File.WriteAllBytes(finalDest, data);

                // 🔒 Zero plaintext buffer after writing to disk
                if (data.Length > 0)
                    Array.Clear(data, 0, data.Length);

                return;
            }
            else if (!sourceIsVfs && destIsVfs)
            {
                // --- Real FS -> VFS copy (read plaintext then encrypt+zero) ---
                byte[] data = File.ReadAllBytes(sourcePath);
                string vfsDest = StripVfsPrefix(destPath);

                VfsStorage.WriteFile(vfsDest, data);

                // 🔒 Zero plaintext buffer after encryption
                if (data.Length > 0)
                    Array.Clear(data, 0, data.Length);

                return;
            }
            else if (sourceIsVfs && destIsVfs)
            {
                // --- VFS -> VFS copy (no decryption required) ---
                string src = StripVfsPrefix(sourcePath);
                string dst = StripVfsPrefix(destPath);

                var entry = VfsStorage.GetInfo(src);
                if (entry == null || entry.IsDir)
                    throw new IOException($"Cannot copy: '{src}' is not a file or does not exist.");

                // Reuse existing ciphertext and key
                VfsStorage.WriteFile(dst, entry.Data, entry.Owner);
                // Preserve encryption key
                var dstEntry = VfsStorage.GetInfo(dst);
                dstEntry.EncryptionKey = (byte[])entry.EncryptionKey.Clone();
                dstEntry.Size = entry.Size;

                return;
            }

            // --- Real FS -> Real FS fallback ---
            if (File.Exists(sourcePath))
            {
                string finalDest = Directory.Exists(destPath)
                    ? Path.Combine(destPath, Path.GetFileName(sourcePath))
                    : destPath;

                File.Copy(sourcePath, finalDest, overwrite: true);
            }
            else if (Directory.Exists(sourcePath))
            {
                if (!recursive)
                    throw new IOException($"omitting directory '{sourcePath}'");

                CopyDirectoryRecursive(sourcePath, destPath);
            }
            else
            {
                throw new FileNotFoundException("Source not found", sourcePath);
            }
        }


        public static void Delete(string path, bool recursive)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path");

            if (IsVfsPath(path))
            {
                string vfsPath = StripVfsPrefix(path);

                // protect VFS root
                if (vfsPath == "" || vfsPath == "/")
                    throw new InvalidOperationException("Cannot delete VFS root.");

                bool isDir = false, isFile = false;
                try
                {
                    isDir = VfsStorage.DirectoryExists(vfsPath);
                    isFile = VfsStorage.FileExists(vfsPath);
                }
                catch { }

                if (!isDir && !isFile)
                    throw new FileNotFoundException("Path not found.", path);

                if (isDir && !recursive)
                    throw new IOException("Cannot delete directory without -r.");

                try
                {
                    if (isDir)
                        ZeroVfsTree(vfsPath);
                    else
                        ZeroVfsFileBytes(vfsPath);
                }
                catch { }

                VfsStorage.Delete(vfsPath, recursive);
                return;
            }

            // Real FS
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
            else
            {
                throw new FileNotFoundException("Path not found.", path);
            }
        }

        // --- Path Helpers ---
        public static string Combine(string basePath, string relative)
        {
            if (IsVfsPath(basePath))
            {
                string vfsBase = StripVfsPrefix(basePath).TrimEnd('/');
                string combined = (vfsBase + "/" + relative).Replace("\\", "/");
                return VfsPrefix + combined.TrimStart('/');
            }
            else
            {
                return Path.GetFullPath(Path.Combine(basePath, relative));
            }
        }

        public static string Normalize(string path)
        {
            if (IsVfsPath(path))
            {
                var parts = StripVfsPrefix(path).Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var stack = new Stack<string>();
                foreach (var part in parts)
                {
                    if (part == ".") continue;
                    if (part == "..")
                    {
                        if (stack.Count > 0) stack.Pop();
                    }
                    else stack.Push(part);
                }
                var normalized = string.Join("/", stack.Reverse());
                return VfsPrefix + normalized;
            }
            else
            {
                return Path.GetFullPath(path);
            }
        }

        // --- New helpers ---
        public static string ResolvePath(string currentDirectory, string input)
        {
            bool isVfs;
            string resolved;
            PathUtils.ResolvePath(currentDirectory, input, out isVfs, out resolved);

            if (isVfs)
                return "vfs://" + resolved.TrimStart('/');
            else
                return resolved;
        }

        private static void ZeroVfsFileBytes(string vfsInternalPath)
        {
            try
            {
                byte[] data;
                if (VfsStorage.TryGetFile(vfsInternalPath, out data) && data != null && data.Length > 0)
                {
                    Array.Clear(data, 0, data.Length);
                    try { VfsStorage.WriteFile(vfsInternalPath, data); } catch { }
                }
            }
            catch { }
        }

        private static void ZeroVfsTree(string vfsInternalPath)
        {
            try
            {
                if (VfsStorage.DirectoryExists(vfsInternalPath))
                {
                    foreach (var child in VfsStorage.List(vfsInternalPath))
                    {
                        string childPath = child.FullPath.TrimStart('/');
                        if (child.IsDir)
                            ZeroVfsTree(childPath);
                        else
                            ZeroVfsFileBytes(childPath);
                    }
                }
                else if (VfsStorage.FileExists(vfsInternalPath))
                {
                    ZeroVfsFileBytes(vfsInternalPath);
                }
            }
            catch { }
        }
    }
}
