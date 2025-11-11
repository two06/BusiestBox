using System;
using System.IO;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Cd
    {
        public static void Execute(ref string currentDirectory, string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                Console.WriteLine("Usage: cd <path>");
                return;
            }

            try
            {
                string resolved = VfsLayer.ResolvePath(currentDirectory, argument);

                if (VfsLayer.DirectoryExists(resolved))
                {
                    currentDirectory = resolved;

                    // If it’s a real FS path, keep process working dir in sync
                    if (!resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.SetCurrentDirectory(resolved);
                    }
                }
                else
                {
                    Console.WriteLine($"The system cannot find the path specified: {argument}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error changing directory: {ex.Message}");
            }
        }
    }
}
