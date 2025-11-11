using System;
using System.Collections.Generic;
using System.IO;

using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Move
    {
        // Usage:
        //   mv <src> <dest>
        //   mv [-r] <src1> [src2 ...] <dest>
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length < 2)
            {
                Console.WriteLine("Usage: mv [-r] <source1> [source2 ...] <destination>");
                return;
            }

            bool recursive = false;
            var items = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-R", StringComparison.OrdinalIgnoreCase))
                {
                    recursive = true;
                }
                else
                {
                    items.Add(a);
                }
            }

            if (items.Count < 2)
            {
                Console.WriteLine("Usage: mv [-r] <source1> [source2 ...] <destination>");
                return;
            }

            string destRaw = items[items.Count - 1];
            var sourcesRaw = items.GetRange(0, items.Count - 1);

            try
            {
                // 1) Perform the copy using the existing implementation (so all URL/VFS/FS logic stays in one place)
                Copy.Execute(currentDirectory, true, args);

                // 2) After copy returns, remove each original source (skip URLs)
                string destResolved = VfsLayer.ResolvePath(currentDirectory, destRaw);
                bool multiSource = sourcesRaw.Count > 1;
                bool destIsDir = IsDestinationDirectory(destResolved) || multiSource;

                foreach (var srcRaw in sourcesRaw)
                {
                    try
                    {
                        if (IsHttpUrl(srcRaw))
                        {
                            // There's nothing local to delete for a remote URL source.
                            Console.WriteLine("[*] mv: source is a URL; copied only: " + srcRaw);
                            continue;
                        }

                        string srcResolved = VfsLayer.ResolvePath(currentDirectory, srcRaw);
                        bool isFile = VfsLayer.FileExists(srcResolved);
                        bool isDir = VfsLayer.DirectoryExists(srcResolved);

                        if (!isFile && !isDir)
                        {
                            // copy would have already warned; keep going
                            continue;
                        }

                        // Extra safety: if directory but -r not given, do not delete
                        if (isDir && !recursive)
                        {
                            Console.WriteLine("mv: -r not specified; not removing directory '" + srcRaw + "'");
                            continue;
                        }

                        // Try to compute final target just for a nicer message
                        string targetDisplay = destIsDir
                            ? VfsLayer.Combine(destResolved, LeafName(srcResolved))
                            : destResolved;

                        // Delete the source now that it was copied
                        DeletePath(srcResolved, isDir);

                        Console.WriteLine("[*] Moved: " + srcResolved + " -> " + targetDisplay);
                    }
                    catch (Exception exItem)
                    {
                        Console.WriteLine("[!] mv: failed to remove source '" + srcRaw + "': " + exItem.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] mv failed: " + ex.Message);
            }
        }

        // ---- helpers (mirror copy.cs minimal bits) ----

        private static bool IsHttpUrl(string s)
        {
            return Uri.IsWellFormedUriString(s, UriKind.Absolute) &&
                   (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsDestinationDirectory(string resolvedDest)
        {
            if (VfsLayer.DirectoryExists(resolvedDest)) return true;
            if (resolvedDest.EndsWith("/") || resolvedDest.EndsWith("\\")) return true;
            return false;
        }

        private static string LeafName(string resolvedPath)
        {
            if (resolvedPath.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string p = resolvedPath.Substring("vfs://".Length).TrimEnd('/');
                int idx = p.LastIndexOf('/');
                return idx >= 0 ? p.Substring(idx + 1) : p;
            }
            return Path.GetFileName(resolvedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private static void DeletePath(string resolved, bool isDir)
        {
            // Prefer going through VfsLayer if you have a unified delete; otherwise branch here.
            // If you already added VfsLayer.Delete(resolved, recursive), replace this with that call.
            if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string vfsPath = resolved.Substring("vfs://".Length);
                BusiestBox.Vfs.VfsStorage.Delete(vfsPath, recursive: isDir);
            }
            else
            {
                if (isDir)
                    Directory.Delete(resolved, recursive: true);
                else
                    File.Delete(resolved);
            }
        }
    }
}
