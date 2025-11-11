using System;
using System.Collections.Generic;
using System.IO;

using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Copy
    {
        public static void Execute(string currentDirectory, params string[] args)
        {
            Execute(currentDirectory, false, args);
        }

        public static void Execute(string currentDirectory, bool quiet, params string[] args)
        {
            if (args == null || args.Length < 2)
            {
                if (!quiet) Console.WriteLine("Usage: copy [-r] <source1> [source2 ...] <destination>");
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
                if (!quiet) Console.WriteLine("Usage: copy [-r] <source1> [source2 ...] <destination>");
                return;
            }

            string destRaw = items[items.Count - 1];
            var sourcesRaw = items.GetRange(0, items.Count - 1);

            try
            {
                string destResolved = VfsLayer.ResolvePath(currentDirectory, destRaw);

                bool multiSource = sourcesRaw.Count > 1;
                bool destIsDir = IsDestinationDirectory(destResolved) || multiSource;

                if (multiSource && !destIsDir)
                {
                    if (!quiet) Console.WriteLine("[!] When copying multiple sources, destination must be a directory.");
                    return;
                }

                if (destIsDir && !VfsLayer.DirectoryExists(destResolved))
                {
                    VfsLayer.CreateDirectory(destResolved);
                }

                foreach (var srcRaw in sourcesRaw)
                {
                    try
                    {
                        if (IsHttpUrl(srcRaw))
                        {
                            string suggestedName;
                            byte[] data = NetUtils.DownloadDataSmart(srcRaw, out suggestedName);

                            // Fallback to URL leaf if no suggestion provided
                            string fileName = !string.IsNullOrEmpty(suggestedName)
                                ? suggestedName
                                : Path.GetFileName(new Uri(srcRaw).AbsolutePath);

                            if (string.IsNullOrEmpty(fileName))
                                fileName = "download";

                            string finalDest = destIsDir
                                ? VfsLayer.Combine(destResolved, fileName)
                                : destResolved;

                            EnsureParent(finalDest);
                            VfsLayer.WriteAllBytes(finalDest, data);

                            if (!quiet) Console.WriteLine("[*] Copied to: " + finalDest);
                            continue;
                        }

                        // Normal paths
                        string srcResolved = VfsLayer.ResolvePath(currentDirectory, srcRaw);
                        bool srcIsFile = VfsLayer.FileExists(srcResolved);
                        bool srcIsDir = VfsLayer.DirectoryExists(srcResolved);

                        if (!srcIsFile && !srcIsDir)
                        {
                            if (!quiet) Console.WriteLine("[!] File not found: " + srcRaw);
                            continue;
                        }

                        if (srcIsDir)
                        {
                            if (!recursive)
                            {
                                if (!quiet) Console.WriteLine("cp: -r not specified; omitting directory '" + srcRaw + "'");
                                continue;
                            }

                            string targetDir = destIsDir
                                ? VfsLayer.Combine(destResolved, LeafName(srcResolved))
                                : destResolved;

                            CopyDirectoryRecursive(srcResolved, targetDir);
                            if (!quiet) Console.WriteLine("[*] Copied directory: " + srcResolved + " -> " + targetDir);
                        }
                        else
                        {
                            byte[] data = VfsLayer.ReadAllBytes(srcResolved);
                            string targetFile = destIsDir
                                ? VfsLayer.Combine(destResolved, LeafName(srcResolved))
                                : destResolved;

                            EnsureParent(targetFile);
                            VfsLayer.WriteAllBytes(targetFile, data);
                            if (!quiet) Console.WriteLine("[*] Copied: " + srcResolved + " -> " + targetFile);
                        }
                    }
                    catch (Exception exItem)
                    {
                        if (!quiet) Console.WriteLine("[!] copy: " + srcRaw + " -> " + destRaw + " failed: " + exItem.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!quiet) Console.WriteLine("[!] Copy failed: " + ex.Message);
            }
        }

        // ---- helpers (unchanged) ----

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

        private static void EnsureParent(string resolvedPath)
        {
            string parent;
            if (resolvedPath.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string p = resolvedPath.Substring("vfs://".Length);
                int idx = p.LastIndexOf('/');
                parent = idx > 0 ? "vfs://" + p.Substring(0, idx) : "vfs://";
            }
            else
            {
                parent = Path.GetDirectoryName(resolvedPath);
            }
            if (!string.IsNullOrEmpty(parent))
                VfsLayer.CreateDirectory(parent);
        }

        private static void CopyDirectoryRecursive(string srcDirResolved, string destDirResolved)
        {
            VfsLayer.CreateDirectory(destDirResolved);
            foreach (var entry in VfsLayer.ListDirectory(srcDirResolved))
            {
                bool isDir = VfsLayer.DirectoryExists(entry);
                bool isFile = VfsLayer.FileExists(entry);
                string leaf = LeafName(entry);

                if (isDir)
                {
                    CopyDirectoryRecursive(entry, VfsLayer.Combine(destDirResolved, leaf));
                }
                else if (isFile)
                {
                    byte[] data = VfsLayer.ReadAllBytes(entry);
                    string targetFile = VfsLayer.Combine(destDirResolved, leaf);
                    EnsureParent(targetFile);
                    VfsLayer.WriteAllBytes(targetFile, data);
                }
            }
        }
    }
}
