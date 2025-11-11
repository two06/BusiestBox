using System;
using System.Collections.Generic;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Rm
    {
        // Usage:
        //   rm file1 file2 ...
        //   rm -f file1 ...
        //   rm -r dir
        //   rm -rf dir1 dir2 ...
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: rm [-f] [-r] [-v] <path1> [path2] ...");
                return;
            }

            bool recursive = false;
            bool force = false;
            bool verbose = false;
            var targets = new List<string>();

            // Parse flags
            foreach (var a in args)
            {
                if (a.StartsWith("-"))
                {
                    for (int i = 1; i < a.Length; i++)
                    {
                        char c = a[i];
                        if (c == 'r' || c == 'R') recursive = true;
                        else if (c == 'f' || c == 'F') force = true;
                        else if (c == 'v' || c == 'V') verbose = true;
                        else
                        {
                            Console.WriteLine("[!] rm: unknown option -" + c);
                            return;
                        }
                    }
                }
                else
                {
                    targets.Add(a);
                }
            }

            if (targets.Count == 0)
            {
                Console.WriteLine("Usage: rm [-f] [-r] [-v] <path1> [path2] ...");
                return;
            }

            foreach (var raw in targets)
            {
                try
                {
                    string resolved = VfsLayer.ResolvePath(currentDirectory, raw);

                    bool isDir = VfsLayer.DirectoryExists(resolved);
                    bool isFile = VfsLayer.FileExists(resolved);

                    if (!isDir && !isFile)
                    {
                        if (!force)
                            Console.WriteLine("rm: cannot remove '" + raw + "': No such file or directory");
                        continue;
                    }

                    if (isDir && !recursive)
                    {
                        if (!force)
                            Console.WriteLine("rm: cannot remove '" + raw + "': Is a directory");
                        continue;
                    }

                    VfsLayer.Delete(resolved, recursive);

                    if (verbose)
                    {
                        if (isDir) Console.WriteLine("removed directory '" + resolved + "'");
                        else Console.WriteLine("removed '" + resolved + "'");
                    }
                }
                catch (Exception ex)
                {
                    if (!force)
                        Console.WriteLine("rm: failed to remove '" + raw + "': " + ex.Message);
                }
            }
        }
    }
}
