// File: BusiestBox/Utils/Glob.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BusiestBox.Vfs;

namespace BusiestBox.Utils
{
    internal static class Glob
    {
        public static string[] ExpandArgs(string currentDirectory, IEnumerable<string> tokens)
        {
            if (tokens == null) return new string[0];

            var list = new List<string>();
            foreach (var tok in tokens)
            {
                if (string.IsNullOrEmpty(tok)) continue;

                // 1) Don't expand flags like -r / --foo
                if (tok[0] == '-' && tok.Length > 1)
                {
                    list.Add(tok);
                    continue;
                }

                // 2) Don't expand URLs (cat/zip/unzip/etc. should receive them verbatim)
                if (IsHttpUrl(tok))
                {
                    list.Add(tok);
                    continue;
                }

                // 3) Only expand tokens that actually contain wildcards
                if (tok.IndexOf('*') < 0 && tok.IndexOf('?') < 0)
                {
                    // Normalize to absolute path (FS or VFS) — but don't crash on bad inputs
                    try
                    {
                        list.Add(VfsLayer.ResolvePath(currentDirectory, tok));
                    }
                    catch
                    {
                        // If resolution fails (illegal chars, etc.), just pass it through
                        list.Add(tok);
                    }
                    continue;
                }

                // 4) Wildcard expansion
                foreach (var e in ExpandOne(currentDirectory, tok))
                    list.Add(e);
            }
            return list.ToArray();
        }

        private static IEnumerable<string> ExpandOne(string currentDirectory, string token)
        {
            // Decide namespace for expansion
            bool tokIsVfs = token.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);
            bool curIsVfs = currentDirectory.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);

            // If token is explicit VFS, or we’re in VFS and token is not an absolute FS path, do VFS expansion
            if (tokIsVfs || (curIsVfs && !LooksRootedFs(token)))
                return ExpandVfs(currentDirectory, token);

            return ExpandFs(currentDirectory, token);
        }

        // ---------- VFS expansion (no System.IO.Path calls) ----------
        private static IEnumerable<string> ExpandVfs(string currentDirectory, string token)
        {
            string abs;
            if (token.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                abs = token;
            }
            else
            {
                abs = VfsLayer.Combine(currentDirectory, token); // returns vfs://...
            }

            // Split "baseDir + pattern"
            string baseDir, pattern;
            SplitDirAndPatternVfs(abs, out baseDir, out pattern);

            string[] entries;
            try { entries = VfsLayer.ListDirectory(baseDir).ToArray(); }
            catch { yield break; }

            for (int i = 0; i < entries.Length; i++)
            {
                string full = entries[i]; // vfs://.../<name>
                string name = GetLeafUniversal(full);
                if (WildcardMatch(name, pattern))
                    yield return full; // already absolute vfs://
            }
        }

        // ---------- FS expansion (safe Path/Directory usage) ----------
        private static IEnumerable<string> ExpandFs(string currentDirectory, string token)
        {
            string baseDir, pattern;

            try
            {
                if (LooksRootedFs(token))
                {
                    baseDir = SafeGetDirectoryName(token);
                    if (string.IsNullOrEmpty(baseDir))
                        baseDir = token; // e.g., "C:\"
                    pattern = SafeGetFileName(token);
                    if (string.IsNullOrEmpty(pattern)) pattern = "*";
                }
                else
                {
                    var combined = Path.Combine(currentDirectory, token);
                    baseDir = SafeGetDirectoryName(combined);
                    if (string.IsNullOrEmpty(baseDir)) baseDir = currentDirectory;
                    pattern = SafeGetFileName(combined);
                    if (string.IsNullOrEmpty(pattern)) pattern = "*";
                }
            }
            catch
            {
                yield break; // illegal chars, etc.
            }

            string[] items;
            try { items = Directory.GetFileSystemEntries(baseDir, pattern); }
            catch { yield break; }

            for (int i = 0; i < items.Length; i++)
                yield return Path.GetFullPath(items[i]);
        }

        // ---------- helpers ----------
        private static bool LooksRootedFs(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            // Don’t call Path.IsPathRooted on funky strings (like "vfs://")
            if (s.StartsWith("\\\\") || s.StartsWith("//")) return true; // UNC
            // Drive root or absolute like C:\ or C:/
            if (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/')) return true;
            // A bare "/" we’ll treat as rooted on *nix-y setups (rare on Windows, but harmless)
            if (s[0] == '/' || s[0] == '\\') return true;
            return false;
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (!(s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return false;

            Uri uri;
            return Uri.TryCreate(s, UriKind.Absolute, out uri);
        }

        private static void SplitDirAndPatternVfs(string absVfs, out string dir, out string pattern)
        {
            // absVfs is vfs://… possibly with slashes; we never use System.IO.Path here.
            string s = absVfs.Replace('\\', '/');
            int lastSlash = s.LastIndexOf('/');
            if (lastSlash < "vfs://".Length)
            {
                dir = "vfs://";
                pattern = s.Length > 6 ? s.Substring(6) : "*";
                if (string.IsNullOrEmpty(pattern)) pattern = "*";
                return;
            }

            dir = s.Substring(0, lastSlash);
            pattern = s.Substring(lastSlash + 1);
            if (string.IsNullOrEmpty(pattern)) pattern = "*";
        }

        private static string GetLeafUniversal(string full)
        {
            int i = Math.Max(full.LastIndexOf('/'), full.LastIndexOf('\\'));
            return i < 0 ? full : full.Substring(i + 1);
        }

        private static bool WildcardMatch(string name, string pattern)
        {
            // simple case-insensitive *, ? matcher
            int n = 0, p = 0, star = -1, mark = -1;
            while (n < name.Length)
            {
                if (p < pattern.Length &&
                    (char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(name[n]) || pattern[p] == '?'))
                { n++; p++; continue; }

                if (p < pattern.Length && pattern[p] == '*')
                { star = p++; mark = n; continue; }

                if (star >= 0)
                { p = star + 1; n = ++mark; continue; }

                return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private static string SafeGetDirectoryName(string path)
        {
            try { return Path.GetDirectoryName(path); } catch { return null; }
        }

        private static string SafeGetFileName(string path)
        {
            try { return Path.GetFileName(path); } catch { return null; }
        }
    }
}
