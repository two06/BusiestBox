using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal static class Zip
    {
        // Entry point from Program.cs: Zip.Execute(currentDirectory, argsOnly)
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Usage();
                return;
            }

            bool recursive = false;
            bool verbose = false;
            var tokens = new List<string>();

            // Parse flags
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.Length > 0 && a[0] == '-')
                {
                    for (int j = 1; j < a.Length; j++)
                    {
                        char c = a[j];
                        if (c == 'r' || c == 'R') recursive = true;
                        else if (c == 'v' || c == 'V') verbose = true;
                        else { Console.WriteLine("[!] zip: unknown option -" + c); return; }
                    }
                }
                else
                {
                    tokens.Add(a);
                }
            }

            if (tokens.Count < 2)
            {
                Usage();
                return;
            }

            string destRaw = tokens[0];
            var sourcesRaw = tokens.Skip(1).ToArray();

            // Resolve destination (no wildcards allowed)
            string dest = ResolvePathAllowVfs(currentDirectory, destRaw);
            bool destIsVfs = IsVfs(dest);

            try
            {
                if (destIsVfs)
                {
                    using (var ms = new MemoryStream())
                    {
                        // Build the zip
                        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                        {
                            AddSources(currentDirectory, za, recursive, verbose, sourcesRaw);
                        } // disposing 'za' writes the central directory into 'ms'

                        // Now persist the finalized bytes to VFS
                        VfsLayer.WriteAllBytes(dest, ms.ToArray());
                    }

                    if (verbose) Console.WriteLine("[*] zip: wrote archive to " + dest);
                }
                else
                {
                    // Ensure parent directory (FS only)
                    try
                    {
                        string parent = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    }
                    catch { /* ignore */ }

                    using (var fs = File.Create(dest))
                    using (var za = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        AddSources(currentDirectory, za, recursive, verbose, sourcesRaw);
                    }
                    if (verbose) Console.WriteLine("[*] zip: wrote archive to " + dest);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] zip: " + ex.Message);
            }
        }

        // ---------------- core add logic ----------------

        private static void AddSources(string currentDirectory, ZipArchive za, bool recursive, bool verbose, string[] sourcesRaw)
        {
            // If caller didn't expand globs, do a light expansion here
            var expanded = new List<string>();
            for (int i = 0; i < sourcesRaw.Length; i++)
            {
                var s = sourcesRaw[i];
                if (HasWildcards(s))
                    expanded.AddRange(ExpandWildcard(currentDirectory, s));
                else
                    expanded.Add(ResolvePathAllowVfs(currentDirectory, s));
            }

            if (expanded.Count == 0)
                throw new FileNotFoundException("no sources matched");

            foreach (var src in expanded)
            {
                bool isVfs = IsVfs(src);
                bool isDir = isVfs ? VfsLayer.DirectoryExists(src) : Directory.Exists(src);
                bool isFile = isVfs ? VfsLayer.FileExists(src) : File.Exists(src);

                if (!isDir && !isFile)
                    throw new FileNotFoundException("source not found: " + src);

                if (isFile)
                {
                    var entryName = GetLeafUniversal(src);
                    AddOneFile(za, src, entryName, verbose);
                }
                else
                {
                    if (!recursive)
                        throw new InvalidOperationException("'" + src + "' is a directory; use -r to include it.");

                    var baseEntry = GetLeafUniversal(src);
                    if (IsVfs(src))
                        AddVfsTree(za, src, baseEntry, verbose);
                    else
                        AddFsTree(za, src, baseEntry, verbose);
                }
            }
        }

        private static void AddOneFile(ZipArchive za, string absPath, string entryName, bool verbose)
        {
            if (string.IsNullOrEmpty(entryName)) entryName = "unnamed";
            // Normalize entry to forward slashes
            entryName = entryName.Replace('\\', '/');

            var entry = za.CreateEntry(entryName, CompressionLevel.Optimal);

            if (IsVfs(absPath))
            {
                var data = VfsLayer.ReadAllBytes(absPath);
                using (var s = entry.Open())
                    s.Write(data, 0, data.Length);
            }
            else
            {
                using (var s = entry.Open())
                using (var fs = File.OpenRead(absPath))
                    fs.CopyTo(s);
            }
            if (verbose) Console.WriteLine("[*] zip: added " + entryName);
        }

        private static void AddFsTree(ZipArchive za, string dirAbs, string entryBase, bool verbose)
        {
            // include files in this directory
            string[] files;
            try { files = Directory.GetFiles(dirAbs); } catch { files = new string[0]; }
            for (int i = 0; i < files.Length; i++)
            {
                var name = Path.GetFileName(files[i]);
                var entry = JoinEntry(entryBase, name);
                AddOneFile(za, files[i], entry, verbose);
            }

            // recurse into subdirectories
            string[] dirs;
            try { dirs = Directory.GetDirectories(dirAbs); } catch { dirs = new string[0]; }

            for (int i = 0; i < dirs.Length; i++)
            {
                var sub = dirs[i];
                var leaf = Path.GetFileName(sub);
                AddFsTree(za, sub, JoinEntry(entryBase, leaf), verbose);
            }
        }

        private static void AddVfsTree(ZipArchive za, string vfsDirAbs, string entryBase, bool verbose)
        {
            string[] entries;
            try { entries = VfsLayer.ListDirectory(vfsDirAbs).ToArray(); }
            catch { entries = new string[0]; }

            for (int i = 0; i < entries.Length; i++)
            {
                var abs = entries[i];
                var leaf = GetLeafUniversal(abs);

                if (VfsLayer.DirectoryExists(abs))
                {
                    AddVfsTree(za, abs, JoinEntry(entryBase, leaf), verbose);
                }
                else if (VfsLayer.FileExists(abs))
                {
                    AddOneFile(za, abs, JoinEntry(entryBase, leaf), verbose);
                }
            }
        }

        // ---------------- helpers: resolve, globbing, utils ----------------

        private static bool IsVfs(string p)
        {
            return p.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolvePathAllowVfs(string currentDirectory, string input)
        {
            // Let VfsLayer do the heavy lifting (it returns either vfs://… or an FS absolute path)
            return VfsLayer.ResolvePath(currentDirectory, input);
        }

        private static bool HasWildcards(string s)
        {
            return s != null && (s.IndexOf('*') >= 0 || s.IndexOf('?') >= 0);
        }

        private static IEnumerable<string> ExpandWildcard(string currentDirectory, string pattern)
        {
            // VFS?
            bool isVfsPattern = pattern.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);
            bool currentIsVfs = currentDirectory.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);

            if (isVfsPattern || (currentIsVfs && !PathLooksRootedFs(pattern)))
            {
                // VFS wildcard
                // Split into baseDir + leaf pattern
                string baseAbs;
                string leafPattern;

                string unified = pattern.Replace('\\', '/');
                if (unified.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                {
                    // absolute vfs://
                    int lastSlash = unified.LastIndexOf('/');
                    if (lastSlash < 6) { baseAbs = "vfs://"; leafPattern = unified.Length > 6 ? unified.Substring(6) : "*"; }
                    else { baseAbs = unified.Substring(0, lastSlash); leafPattern = unified.Substring(lastSlash + 1); }
                }
                else
                {
                    // relative while in VFS
                    int lastSlash = Math.Max(unified.LastIndexOf('/'), unified.LastIndexOf('\\'));
                    if (lastSlash < 0)
                    {
                        baseAbs = currentDirectory;
                        leafPattern = unified;
                    }
                    else
                    {
                        string head = unified.Substring(0, lastSlash);
                        baseAbs = VfsLayer.Combine(currentDirectory, head);
                        leafPattern = unified.Substring(lastSlash + 1);
                    }
                }

                string[] entries;
                try { entries = VfsLayer.ListDirectory(baseAbs).ToArray(); } catch { entries = new string[0]; }

                for (int i = 0; i < entries.Length; i++)
                {
                    var abs = entries[i];
                    var leaf = GetLeafUniversal(abs);
                    if (WildcardMatch(leaf, leafPattern))
                        yield return abs; // already absolute vfs://
                }
                yield break;
            }

            // FS wildcard
            string prefix = pattern;

            // Expand ~ if present
            if (prefix == "~" || prefix.StartsWith("~/") || prefix.StartsWith("~\\"))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                prefix = prefix.Length == 1 ? home : Path.Combine(home, prefix.Substring(2));
            }

            string baseDir;
            string leafPat;

            try
            {
                if (PathLooksRootedFs(prefix))
                {
                    baseDir = Path.GetDirectoryName(prefix);
                    if (string.IsNullOrEmpty(baseDir))
                        baseDir = Directory.Exists(prefix) ? prefix : Directory.GetCurrentDirectory();
                    leafPat = Path.GetFileName(prefix);
                }
                else
                {
                    string head = Path.GetDirectoryName(prefix); // may be null
                    string baseForEnum = string.IsNullOrEmpty(head) ? currentDirectory : Path.Combine(currentDirectory, head);
                    baseDir = baseForEnum;
                    leafPat = Path.GetFileName(prefix);
                }
            }
            catch
            {
                yield break;
            }

            string[] matches;
            try { matches = Directory.GetFileSystemEntries(baseDir, string.IsNullOrEmpty(leafPat) ? "*" : leafPat); }
            catch { matches = new string[0]; }

            for (int i = 0; i < matches.Length; i++)
                yield return matches[i];
        }

        private static bool PathLooksRootedFs(string p)
        {
            // Avoid throwing on garbage like "C:Users" (we won't call Path.IsPathRooted on broken input)
            if (string.IsNullOrEmpty(p)) return false;
            if (p.StartsWith("\\\\") || p.StartsWith("//")) return true; // UNC
            if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/')) return true; // C:\...
            if (p.StartsWith("\\") || p.StartsWith("/")) return true;
            return false;
        }

        private static string GetLeafUniversal(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            int i = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
            return i < 0 ? p : p.Substring(i + 1);
        }

        private static string JoinEntry(string baseEntry, string name)
        {
            if (string.IsNullOrEmpty(baseEntry)) return name.Replace('\\', '/');
            if (baseEntry.EndsWith("/")) return baseEntry + name.Replace('\\', '/');
            return baseEntry.Replace('\\', '/') + "/" + name.Replace('\\', '/');
        }

        private static bool WildcardMatch(string text, string pattern)
        {
            // Simple *, ? matcher (case-insensitive)
            return WildcardMatch(text, 0, pattern ?? "*", 0);
        }

        private static bool WildcardMatch(string text, int ti, string pat, int pi)
        {
            int tlen = text.Length, plen = pat.Length;
            while (pi < plen)
            {
                char pc = char.ToLowerInvariant(pat[pi]);
                if (pc == '*')
                {
                    // Collapse multiple *
                    while (pi + 1 < plen && pat[pi + 1] == '*') pi++;
                    if (pi + 1 == plen) return true; // trailing * matches all
                    // try to match the rest at all positions
                    for (int skip = 0; ti + skip <= tlen; skip++)
                        if (WildcardMatch(text, ti + skip, pat, pi + 1)) return true;
                    return false;
                }
                else if (pc == '?')
                {
                    if (ti >= tlen) return false;
                    ti++; pi++; continue;
                }
                else
                {
                    if (ti >= tlen) return false;
                    if (char.ToLowerInvariant(text[ti]) != pc) return false;
                    ti++; pi++; continue;
                }
            }
            return ti == tlen;
        }

        private static void Usage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  zip [-r] [-v] <destZipPath> <src1> [src2 ...]");
            Console.WriteLine("Notes:");
            Console.WriteLine("  - Sources can be real FS paths or vfs:// paths.");
            Console.WriteLine("  - Use -r to include directories recursively.");
        }
    }
}
