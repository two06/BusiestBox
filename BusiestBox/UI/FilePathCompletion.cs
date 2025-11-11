using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BusiestBox.Vfs; // keep your VFS support

namespace BusiestBox.UI
{
    // Generic file/dir completion for non-first tokens.
    // Keeps behavior close to your existing path rules, but lives outside TabComplete.
    public sealed class FilePathCompletion : ICompletionProvider
    {
        public bool TryGetCompletions(CompletionContext ctx, out string[] matches)
        {
            matches = null;
            if (ctx == null) return false;

            // Only handle when NOT completing the first token
            if (ctx.TokenStart == 0) return false;

            string token = ctx.CurrentToken ?? string.Empty;
            string[] results;

            try
            {
                results = CompletePath(token, ctx.CurrentDirectory);
            }
            catch
            {
                results = Array.Empty<string>();
            }

            matches = results;
            return true; // handled (even if empty)
        }

        // Adapted, trimmed version of your path completion (FS + VFS + ~)
        private static string[] CompletePath(string pathPrefix, string currentDirectory)
        {
            pathPrefix = pathPrefix ?? string.Empty;

            // Guard invalid chars/wildcards
            try
            {
                char[] bad = Path.GetInvalidPathChars();
                for (int i = 0; i < pathPrefix.Length; i++)
                {
                    char c = pathPrefix[i];
                    if (c == '*' || c == '?') return new string[0];
                    for (int j = 0; j < bad.Length; j++)
                        if (c == bad[j]) return new string[0];
                }
            }
            catch { }

            // ~ expansion
            string prefixForFs = pathPrefix;
            if (prefixForFs == "~" || prefixForFs.StartsWith("~/") || prefixForFs.StartsWith("~\\"))
            {
                try
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(home))
                    {
                        prefixForFs = (prefixForFs.Length == 1) ? home
                            : Path.Combine(home, prefixForFs.Substring(2));
                    }
                }
                catch { return new string[0]; }
            }

            bool prefixIsVfs = pathPrefix.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);
            bool currentIsVfs = currentDirectory.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);
            bool fsRooted = false;
            try { fsRooted = Path.IsPathRooted(prefixForFs); } catch { }
            bool useVfs = prefixIsVfs || (currentIsVfs && !fsRooted && !pathPrefix.StartsWith("~"));

            // helpers
            Func<string, string> FileNameOnly = p =>
            {
                int i = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
                return i < 0 ? p : p.Substring(i + 1);
            };
            Func<string, string> DirPart = p =>
            {
                int i = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
                return i < 0 ? "" : p.Substring(0, i);
            };
            Func<string, bool> EndsWithSep = p => p.EndsWith("\\") || p.EndsWith("/");

            // ---------- VFS ----------
            if (useVfs)
            {
                string baseDirAbs;
                string partial;
                string vfsHeadTyped;

                if (prefixIsVfs)
                {
                    string abs = pathPrefix.Replace('\\', '/');
                    string rest = abs.Length > 6 ? abs.Substring(6) : "";
                    vfsHeadTyped = DirPart(rest);
                    partial = FileNameOnly(rest);
                    baseDirAbs = string.IsNullOrEmpty(vfsHeadTyped)
                        ? "vfs://"
                        : VfsLayer.Combine("vfs://", vfsHeadTyped);
                }
                else
                {
                    string rel = pathPrefix.Replace('\\', '/');
                    vfsHeadTyped = DirPart(rel);
                    partial = FileNameOnly(rel);
                    baseDirAbs = string.IsNullOrEmpty(vfsHeadTyped)
                        ? currentDirectory
                        : VfsLayer.Combine(currentDirectory, vfsHeadTyped);
                }

                string[] entries;
                try { entries = VfsLayer.ListDirectory(baseDirAbs)?.ToArray() ?? new string[0]; }
                catch { return new string[0]; }

                var matches = new List<string>();
                for (int i = 0; i < entries.Length; i++)
                {
                    string full = entries[i];              // vfs://.../name
                    string name = FileNameOnly(full);
                    if (!name.StartsWith(partial, StringComparison.OrdinalIgnoreCase)) continue;

                    string suggestion = prefixIsVfs
                        ? VfsLayer.Combine(baseDirAbs, name)
                        : (string.IsNullOrEmpty(vfsHeadTyped)
                            ? name
                            : (vfsHeadTyped.TrimEnd('/', '\\') + "/" + name));

                    try
                    {
                        string check = prefixIsVfs ? suggestion : VfsLayer.Combine(currentDirectory, suggestion);
                        if (VfsLayer.DirectoryExists(check) && !suggestion.EndsWith("/"))
                            suggestion += "/";
                    }
                    catch { }

                    matches.Add(suggestion);
                }
                return matches.ToArray();
            }

            // ---------- Filesystem ----------
            string fsDir = currentDirectory;
            string fsHeadKeep = "";
            string partialName = "";
            bool endsWithSep = EndsWithSep(prefixForFs);

            try
            {
                if (string.IsNullOrWhiteSpace(prefixForFs))
                {
                    fsDir = currentDirectory; fsHeadKeep = ""; partialName = "";
                }
                else if (fsRooted)
                {
                    if (endsWithSep)
                    {
                        fsDir = prefixForFs; fsHeadKeep = prefixForFs; partialName = "";
                    }
                    else
                    {
                        fsHeadKeep = Path.GetDirectoryName(prefixForFs);
                        if (string.IsNullOrEmpty(fsHeadKeep)) fsHeadKeep = currentDirectory;
                        fsDir = fsHeadKeep;
                        partialName = Path.GetFileName(prefixForFs);
                    }
                }
                else
                {
                    if (endsWithSep)
                    {
                        fsHeadKeep = prefixForFs;
                        fsDir = Path.Combine(currentDirectory, fsHeadKeep);
                        partialName = "";
                    }
                    else
                    {
                        fsHeadKeep = Path.GetDirectoryName(prefixForFs) ?? "";
                        fsDir = string.IsNullOrEmpty(fsHeadKeep) ? currentDirectory : Path.Combine(currentDirectory, fsHeadKeep);
                        partialName = Path.GetFileName(prefixForFs);
                    }
                }
            }
            catch { return new string[0]; }

            string[] items;
            try { items = Directory.GetFileSystemEntries(fsDir); }
            catch { return new string[0]; }

            string fsHeadKeepNorm = fsHeadKeep;
            try { if (!string.IsNullOrEmpty(fsHeadKeepNorm)) fsHeadKeepNorm = fsHeadKeepNorm.TrimEnd(' ', '.'); } catch { }

            var results = new List<string>();
            for (int i = 0; i < items.Length; i++)
            {
                string full = items[i];
                string name;
                try { name = Path.GetFileName(full); } catch { continue; }
                if (!name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase)) continue;

                bool typedAbsolute;
                try
                {
                    typedAbsolute = Path.IsPathRooted(prefixForFs) &&
                                    (prefixForFs.StartsWith("\\") || prefixForFs.StartsWith("/") ||
                                     prefixForFs.IndexOf(":\\", StringComparison.Ordinal) >= 0 ||
                                     prefixForFs.IndexOf(":/", StringComparison.Ordinal) >= 0);
                }
                catch { typedAbsolute = false; }

                string suggestion;
                if (typedAbsolute)
                {
                    string headAbs = string.IsNullOrEmpty(fsHeadKeepNorm) ? fsDir : fsHeadKeepNorm;
                    try { suggestion = Path.Combine(headAbs, name); }
                    catch { suggestion = headAbs + Path.DirectorySeparatorChar + name; }
                }
                else
                {
                    try { suggestion = string.IsNullOrEmpty(fsHeadKeepNorm) ? name : Path.Combine(fsHeadKeepNorm, name); }
                    catch { suggestion = (string.IsNullOrEmpty(fsHeadKeepNorm) ? "" : fsHeadKeepNorm + Path.DirectorySeparatorChar) + name; }
                }

                try
                {
                    if (Directory.Exists(full) && !suggestion.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        suggestion += Path.DirectorySeparatorChar;
                }
                catch { }

                results.Add(suggestion);
            }

            return results.ToArray();
        }
    }
}
