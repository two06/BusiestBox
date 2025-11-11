using System;
using System.IO;
using System.Net;
using System.Reflection;

using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Load
    {
        // Usage:
        //   load <path-or-url> [more...]
        // Examples:
        //   load C:\tools\MyLib.dll
        //   load vfs://libs/Helper.dll
        //   load https://example.com/SomePlugin.dll
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Usage: load <assemblyPathOrUrl> [more...]");
                return;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                try
                {
                    if (IsHttpUrl(raw))
                    {
                        string _;
                        byte[] data = NetUtils.DownloadDataSmart(raw, out _);
                        Assembly asm = Assembly.Load(data);
                        Console.WriteLine("[*] Loaded (URL): " + (asm.FullName ?? raw));
                        continue;
                    }

                    // Resolve path via VfsLayer (works for both FS and VFS, relative or absolute)
                    string resolved = VfsLayer.ResolvePath(currentDirectory, raw);

                    // VFS -> read bytes + Assembly.Load(byte[])
                    if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!VfsLayer.FileExists(resolved))
                        {
                            Console.WriteLine("[!] load: VFS file not found: " + resolved);
                            continue;
                        }

                        byte[] bytes = VfsLayer.ReadAllBytes(resolved);
                        Assembly asm = Assembly.Load(bytes);
                        Console.WriteLine("[*] Loaded (VFS): " + (asm.FullName ?? resolved));
                    }
                    else
                    {
                        // Filesystem -> Assembly.LoadFrom(path)
                        if (!File.Exists(resolved))
                        {
                            Console.WriteLine("[!] load: File not found: " + resolved);
                            continue;
                        }

                        Assembly asm = Assembly.LoadFrom(resolved);
                        Console.WriteLine("[*] Loaded (FS): " + (asm.FullName ?? resolved));
                    }
                }
                catch (BadImageFormatException ex)
                {
                    Console.WriteLine("[!] load: Not a valid .NET assembly: " + raw + " (" + ex.Message + ")");
                }
                catch (FileLoadException ex)
                {
                    Console.WriteLine("[!] load: Assembly load failed: " + raw + " (" + ex.Message + ")");
                }
                catch (WebException ex)
                {
                    Console.WriteLine("[!] load: Download failed: " + raw + " (" + ex.Message + ")");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] load: Error loading '" + raw + "': " + ex.Message);
                }
            }
        }

        private static bool IsHttpUrl(string s)
        {
            return Uri.IsWellFormedUriString(s, UriKind.Absolute) &&
                   (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }
    }
}
