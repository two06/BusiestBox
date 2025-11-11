using System;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Mkdir
    {
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: mkdir <directory> [more directories...]");
                return;
            }

            foreach (var dir in args)
            {
                try
                {
                    // resolve path relative to currentDirectory
                    string resolved = VfsLayer.Combine(currentDirectory, dir);

                    // only one arg here
                    VfsLayer.CreateDirectory(resolved);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[!] mkdir: cannot create directory '{dir}': {ex.Message}");
                }
            }
        }
    }
}
