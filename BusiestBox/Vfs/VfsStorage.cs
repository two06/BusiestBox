using System;
using System.Collections.Generic;
using System.Linq;

using BusiestBox.Crypto;

namespace BusiestBox.Vfs
{
    internal enum VfsEntryType
    {
        File,
        Directory
    }

    internal static class VfsStorage
    {
        private static readonly VfsEntry Root = new VfsEntry
        {
            Name = "",
            FullPath = "/",
            Type = VfsEntryType.Directory,
            Owner = "vfs",
            LastModified = DateTime.Now,
            Data = new byte[0],
            Children = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase)
        };

        // -------- Public API --------

        public static VfsEntry GetInfo(string path)
        {
            VfsEntry e;
            if (!TryGet(path, out e))
                throw new KeyNotFoundException("Path not found.");

            // Return a shallow copy to prevent accidental external mutation
            return new VfsEntry
            {
                Name = e.Name,
                FullPath = e.FullPath,
                Type = e.Type,
                Owner = e.Owner,
                Size = e.Size,
                LastModified = e.LastModified,
                Data = e.Data, // still ciphertext if encrypted
                EncryptionKey = e.EncryptionKey != null ? (byte[])e.EncryptionKey.Clone() : null,
                Children = e.Children // only meaningful for directories, not cloned to avoid deep copies
            };
        }


        public static void CreateDirectory(string path, string owner = "vfs")
        {
            path = NormalizePath(path);
            if (path == "/") return;

            VfsEntry parent;
            string leaf;
            GetParentAndLeaf(path, out parent, out leaf, true, owner);

            VfsEntry existing;
            if (parent.Children.TryGetValue(leaf, out existing))
            {
                if (existing.Type != VfsEntryType.Directory)
                    throw new InvalidOperationException("A file with that name already exists.");
                return;
            }

            var dir = new VfsEntry
            {
                Name = leaf,
                FullPath = path,
                Type = VfsEntryType.Directory,
                Owner = owner,
                LastModified = DateTime.Now,
                Data = new byte[0],
                Children = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase)
            };
            parent.Children.Add(leaf, dir);
            Touch(parent);
        }

        public static void WriteFile(string path, byte[] data, string owner = "vfs")
        {
            path = NormalizePath(path);

            VfsEntry parent;
            string leaf;
            GetParentAndLeaf(path, out parent, out leaf, true, owner);

            // --- Encrypt first ---
            byte[] key = Crypto.Crypto.RandomBytes(32);
            byte[] iv = Crypto.Crypto.RandomBytes(16);
            byte[] ciphertext = Crypto.Crypto.EncryptBytesWithKey(key, iv, data ?? new byte[0]);

            // --- Immediately wipe plaintext buffer after encryption ---
            if (data != null && data.Length > 0)
                Array.Clear(data, 0, data.Length);

            VfsEntry existing;
            if (parent.Children.TryGetValue(leaf, out existing))
            {
                if (existing.Type != VfsEntryType.File)
                    throw new InvalidOperationException("A directory exists at that path.");

                existing.EncryptionKey = key;
                existing.Data = ciphertext;
                existing.Size = (data?.LongLength ?? 0);
                existing.LastModified = DateTime.Now;
            }
            else
            {
                var file = new VfsEntry
                {
                    Name = leaf,
                    FullPath = path,
                    Type = VfsEntryType.File,
                    Owner = owner,
                    EncryptionKey = key,
                    Data = ciphertext,
                    Size = (data?.LongLength ?? 0),
                    LastModified = DateTime.Now,
                    Children = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase)
                };
                parent.Children.Add(leaf, file);
            }

            Touch(parent);
        }



        public static bool FileExists(string path)
        {
            VfsEntry e;
            return TryGet(path, out e) && e.Type == VfsEntryType.File;
        }

        public static bool DirectoryExists(string path)
        {
            VfsEntry e;
            return TryGet(path, out e) && e.Type == VfsEntryType.Directory;
        }

        public static bool TryGetFile(string path, out byte[] data)
        {
            data = new byte[0];
            VfsEntry e;
            if (!TryGet(path, out e) || e.Type != VfsEntryType.File) return false;

            if (e.EncryptionKey != null && e.Data != null && e.Data.Length > 0)
            {
                var plain = Crypto.Crypto.DecryptBytesWithKey(e.EncryptionKey, e.Data);
                data = plain;  // Caller now owns plaintext // Caller must clear 'data' when done, we can't zero it here
            }
            else
            {
                data = e.Data ?? new byte[0];
            }
            return true;
        }


        /// <summary>List entries under a directory (non-recursive). If path is a file, returns that single entry.</summary>
        public static IEnumerable<VfsEntry> List(string path)
        {
            path = NormalizePath(path);
            VfsEntry e;
            if (!TryGet(path, out e)) yield break;

            if (e.Type == VfsEntryType.File)
            {
                yield return e;
                yield break;
            }

            foreach (var child in e.Children.Values
                         .OrderBy(c => c.Type == VfsEntryType.Directory ? 0 : 1)
                         .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return child;
            }
        }

        public static IEnumerable<VfsEntry> List()
        {
            return List("/");
        }

        /// <summary>Delete a file or directory. For directories, set recursive=true to remove non-empty dirs.</summary>
        public static void Delete(string path, bool recursive = false)
        {
            path = NormalizePath(path);
            if (path == "/") throw new InvalidOperationException("Cannot delete root.");

            VfsEntry parent;
            string leaf;
            GetParentAndLeaf(path, out parent, out leaf, false, "vfs");

            VfsEntry target;
            if (!parent.Children.TryGetValue(leaf, out target))
                throw new KeyNotFoundException("Path not found.");

            if (target.IsDir && target.Children.Count > 0 && !recursive)
                throw new InvalidOperationException("Directory not empty. Use recursive=true.");

            // 🔑 NEW: wipe encryption key
            if (target.EncryptionKey != null)
            {
                Array.Clear(target.EncryptionKey, 0, target.EncryptionKey.Length);
                target.EncryptionKey = null;
            }

            parent.Children.Remove(leaf);
            Touch(parent);
        }


        /// <summary>
        /// Returns lines like: "08/20/25 11:19   &lt;File&gt;   vfs         204649   name"
        /// </summary>
        public static IEnumerable<string> ListFormatted(string path)
        {
            foreach (var e in List(path))
                yield return FormatListingLine(e);
        }

        public static IEnumerable<string> ListFormatted()
        {
            return ListFormatted("/");
        }

        [Obsolete("Use List(\"/\") or ListFormatted(\"/\") instead.")]
        public static IEnumerable<string> ListFiles()
        {
            foreach (var p in EnumeratePaths(Root))
                yield return p;
        }

        // -------- Internals --------

        private static bool TryGet(string path, out VfsEntry entry)
        {
            path = NormalizePath(path);
            if (path == "/")
            {
                entry = Root;
                return true;
            }

            var cursor = Root;
            var segments = Split(path).ToArray();
            for (int i = 0; i < segments.Length; i++)
            {
                VfsEntry next;
                if (!cursor.Children.TryGetValue(segments[i], out next))
                {
                    entry = null;
                    return false;
                }
                cursor = next;
            }
            entry = cursor;
            return true;
        }

        private static void GetParentAndLeaf(string path, out VfsEntry parent, out string leafName, bool createIntermediate, string owner)
        {
            var segs = Split(path).ToArray();
            if (segs.Length == 0)
                throw new InvalidOperationException("Path is empty.");

            leafName = segs[segs.Length - 1];

            // If the file/dir is directly under root, parent is root
            if (segs.Length == 1)
            {
                parent = Root;
                return;
            }

            var parentPath = string.Join("/", segs.Take(segs.Length - 1));
            parent = EnsurePath(parentPath, createIntermediate, owner);
        }

        private static VfsEntry EnsurePath(string dirPath, bool createIntermediate, string owner)
        {
            if (string.IsNullOrEmpty(dirPath)) return Root;

            var cursor = Root;
            var segs = Split(dirPath).ToArray();
            for (int i = 0; i < segs.Length; i++)
            {
                VfsEntry next;
                if (!cursor.Children.TryGetValue(segs[i], out next))
                {
                    if (!createIntermediate)
                        throw new KeyNotFoundException("Directory '" + segs[i] + "' not found in '" + cursor.FullPath + "'.");

                    var created = new VfsEntry
                    {
                        Name = segs[i],
                        FullPath = JoinPath(cursor.FullPath, segs[i]),
                        Type = VfsEntryType.Directory,
                        Owner = owner,
                        LastModified = DateTime.Now,
                        Data = new byte[0],
                        Children = new Dictionary<string, VfsEntry>(StringComparer.OrdinalIgnoreCase)
                    };
                    cursor.Children.Add(segs[i], created);
                    Touch(cursor);
                    cursor = created;
                }
                else
                {
                    if (next.Type != VfsEntryType.Directory)
                        throw new InvalidOperationException("'" + next.FullPath + "' is not a directory.");
                    cursor = next;
                }
            }
            return cursor;
        }

        private static void Touch(VfsEntry dir)
        {
            if (!dir.IsDir) return;
            dir.LastModified = DateTime.Now;
        }

        private static string NormalizePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "/";

            var s = raw.Trim();

            if (s.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                s = s.Substring("vfs://".Length);

            s = s.Replace('\\', '/');
            if (!s.StartsWith("/")) s = "/" + s;

            var parts = new List<string>();
            var tokens = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                var p = tokens[i];
                if (p == ".") continue;
                if (p == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(p);
            }
            return "/" + string.Join("/", parts.ToArray());
        }

        private static IEnumerable<string> Split(string normPath)
        {
            var trimmed = normPath.Trim('/');
            if (trimmed.Length == 0) return new string[0];
            return trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string JoinPath(string parent, string child)
        {
            if (parent == "/") return "/" + child;
            return parent.TrimEnd('/') + "/" + child;
        }

        private static IEnumerable<string> EnumeratePaths(VfsEntry root)
        {
            if (root.Type == VfsEntryType.File)
            {
                yield return root.FullPath;
                yield break;
            }

            foreach (var kv in root.Children)
            {
                var child = kv.Value;
                foreach (var p in EnumeratePaths(child))
                    yield return p;
            }
        }

        private static string FormatListingLine(VfsEntry e)
        {
            // Example:
            // 08/20/25 11:19   <File>   vfs         204649   name
            var dt = e.LastModified.ToString("MM/dd/yy HH:mm");
            var type = e.IsDir ? "<Dir>" : "<File>";
            var owner = string.IsNullOrEmpty(e.Owner) ? "vfs" : e.Owner;
            var size = e.IsDir ? "" : e.Size.ToString();

            // Pad similar to sample; tweak widths as needed.
            return string.Format("{0,-14}   {1,-6}   {2,-9}   {3,8}   {4}",
                                 dt, type, owner, size, e.Name);
        }
    }
}
