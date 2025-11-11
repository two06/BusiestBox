using System;
using System.Collections.Generic;
using System.IO;

namespace BusiestBox.Utils
{
    internal static class PathUtils
    {
        public static string GetHomeDirectory()
        {
            try
            {
                var p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { }
            var up = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(up)) return up;
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home)) return home;
            return Directory.GetCurrentDirectory();
        }

        // "~" -> "C:\Users\you", "~/Desktop" -> "C:\Users\you\Desktop"
        public static string ExpandTilde(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (input == "~") return GetHomeDirectory();
            if (input.StartsWith("~/") || input.StartsWith("~\\"))
                return Path.Combine(GetHomeDirectory(), input.Substring(2));
            return input; // (skip ~username for now)
        }

        // "C:\Users\you\Desktop" -> "~/Desktop" (for prompt/display)
        public static string CompressHome(string fsPath)
        {
            if (string.IsNullOrEmpty(fsPath)) return fsPath;
            string home = GetHomeDirectory().TrimEnd('\\', '/');
            if (fsPath.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            {
                string rest = fsPath.Substring(home.Length).TrimStart('\\', '/');
                return rest.Length == 0 ? "~" : "~" + Path.DirectorySeparatorChar + rest;
            }
            return fsPath;
        }

        public static string ToPromptPath(string currentDirectory)
        {
            if (string.IsNullOrEmpty(currentDirectory)) return "vfs://";
            if (currentDirectory.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                return currentDirectory;
            return CompressHome(currentDirectory);
        }

        /// <summary>
        /// Normalize a VFS path so it has no ".", "..", duplicate slashes, etc.
        /// Always returns an absolute path starting with "/".
        /// </summary>
        public static string NormalizeVfsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "/";

            var parts = new List<string>();
            string[] tokens = path.Replace('\\', '/')
                                  .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (token == ".") continue;
                if (token == "..")
                {
                    if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                    continue;
                }
                parts.Add(token);
            }

            return "/" + string.Join("/", parts);
        }

        /// <summary>
        /// Join a base VFS dir with a child (relative) path and normalize the result.
        /// </summary>
        public static string JoinVfs(string baseDir, string tail)
        {
            string b = string.IsNullOrWhiteSpace(baseDir) ? "/" : baseDir;
            if (!b.StartsWith("/")) b = "/" + b.TrimStart('/');
            if (b != "/") b = b.TrimEnd('/');

            string combined = b == "/" ? ("/" + tail.TrimStart('/')) : (b + "/" + tail.TrimStart('/'));
            return NormalizeVfsPath(combined);
        }

        /// <summary>
        /// Resolves a user-supplied path (absolute or relative) against the currentDirectory.
        /// Returns (isVfs, resolvedAbsolutePath).
        ///   - For VFS: resolved is like "/a/b" (root is "/").
        ///   - For FS : resolved is Path.GetFullPath(...)
        /// </summary>
        public static void ResolvePath(string currentDirectory, string input, out bool isVfs, out string resolved)
        {
            input = ExpandTilde(input);

            if (input.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                isVfs = true;
                var p = input.Substring(6);
                if (string.IsNullOrWhiteSpace(p)) p = "/";
                resolved = NormalizeVfsPath(p);
                return;
            }

            // Case: explicit FS rooted path
            if (Path.IsPathRooted(input))
            {
                isVfs = false;
                resolved = Path.GetFullPath(input);
                return;
            }

            // Case: relative → depends on namespace
            bool curIsVfs = currentDirectory.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);
            if (curIsVfs)
            {
                isVfs = true;
                string cur = currentDirectory.Substring(6);
                if (string.IsNullOrEmpty(cur)) cur = "/";
                resolved = NormalizeVfsPath(JoinVfs(cur, input));
            }
            else
            {
                isVfs = false;
                string baseDir = string.IsNullOrEmpty(currentDirectory) ? Directory.GetCurrentDirectory() : currentDirectory;
                resolved = Path.GetFullPath(Path.Combine(baseDir, input));
            }
        }

        /// <summary>
        /// For logging or user display: turn an absolute VFS path "/foo/bar" into "vfs://foo/bar".
        /// Root "/" → "vfs://".
        /// </summary>
        public static string ToVfsDisplay(string absolutePath)
        {
            if (absolutePath == "/") return "vfs://";
            return "vfs://" + absolutePath.TrimStart('/');
        }
    }
}
