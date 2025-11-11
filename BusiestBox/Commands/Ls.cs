using System;
using System.IO;
using System.Linq;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Ls
    {
        public static void Execute(string currentDirectory, params string[] args)
        {
            string targetRaw = (args != null && args.Length > 0) ? args[0] : currentDirectory;

            try
            {
                string resolved = VfsLayer.ResolvePath(currentDirectory, targetRaw);
                bool isVfs = resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase);

                long biggestFileSize = 0;
                int sizeCharLength = 4;     // minimum for "Size"
                int biggestOwnerSize = 5;

                if (isVfs)
                {
                    // VFS branch
                    string vfsPath = resolved.Length > 6 ? resolved.Substring(6) : "/";

                    if (!VfsStorage.DirectoryExists(vfsPath))
                    {
                        Console.WriteLine("[!] Directory not found in VFS: " + resolved);
                        return;
                    }

                    var entries = VfsStorage.List(vfsPath).ToArray();

                    // widths
                    for (int i = 0; i < entries.Length; i++)
                    {
                        var e = entries[i];
                        if (e.Type == VfsEntryType.File && e.Size > biggestFileSize)
                            biggestFileSize = e.Size;

                        var ownerLen = string.IsNullOrEmpty(e.Owner) ? 0 : e.Owner.Length;
                        if (ownerLen > biggestOwnerSize) biggestOwnerSize = ownerLen;
                    }
                    if (biggestFileSize.ToString().Length > sizeCharLength)
                        sizeCharLength = biggestFileSize.ToString().Length;

                    Console.WriteLine("\n  Directory listing of " + resolved + "\n");
                    Console.WriteLine("Last Modify      Type     " +
                        "Owner" + new string(' ', biggestOwnerSize - 5) + "   " +
                        "Size" + new string(' ', sizeCharLength - 4) + "   File/Dir Name");
                    Console.WriteLine("==============   ======   " +
                        new string('=', biggestOwnerSize) + "   " +
                        new string('=', sizeCharLength) + "   =============");

                    for (int i = 0; i < entries.Length; i++)
                    {
                        var e = entries[i];
                        string lastWrite = e.LastModified.ToString("MM/dd/yy HH:mm");
                        string type = e.IsDir ? "<Dir>" : "<File>";
                        string owner = string.IsNullOrEmpty(e.Owner) ? "???" : e.Owner;
                        string sizeStr = e.IsDir ? new string('.', sizeCharLength) : e.Size.ToString().PadLeft(sizeCharLength);

                        Console.WriteLine(
                            lastWrite + "   " +
                            type.PadRight(6) + "   " +
                            owner + new string(' ', biggestOwnerSize - owner.Length) + "   " +
                            sizeStr + "   " +
                            e.Name
                        );
                    }
                }
                else
                {
                    // Filesystem branch // Really this should be refactored so it's all FS/VFS agnostic
                    if (!Directory.Exists(resolved))
                    {
                        Console.WriteLine("[!] Directory not found: " + resolved);
                        return;
                    }

                    var files = Directory.GetFiles(resolved);
                    var subdirs = Directory.GetDirectories(resolved);
                    var dirContents = files.Concat(subdirs).ToArray();

                    // biggest size
                    for (int i = 0; i < files.Length; i++)
                    {
                        long sz = new FileInfo(files[i]).Length;
                        if (sz > biggestFileSize) biggestFileSize = sz;
                    }
                    if (biggestFileSize.ToString().Length > sizeCharLength)
                        sizeCharLength = biggestFileSize.ToString().Length;

                    // owner width
                    for (int i = 0; i < dirContents.Length; i++)
                    {
                        try
                        {
                            var owner = File.GetAccessControl(dirContents[i])
                                .GetOwner(typeof(System.Security.Principal.NTAccount)).ToString();
                            if (owner.Length > biggestOwnerSize) biggestOwnerSize = owner.Length;
                        }
                        catch { }
                    }

                    Console.WriteLine("\n  Directory listing of " + resolved + "\n");
                    Console.WriteLine("Last Modify      Type     " +
                        "Owner" + new string(' ', biggestOwnerSize - 5) + "   " +
                        "Size" + new string(' ', sizeCharLength - 4) + "   File/Dir Name");
                    Console.WriteLine("==============   ======   " +
                        new string('=', biggestOwnerSize) + "   " +
                        new string('=', sizeCharLength) + "   =============");

                    for (int i = 0; i < dirContents.Length; i++)
                    {
                        string item = dirContents[i];
                        string name = Path.GetFileName(item);
                        DateTime lastWriteDate = File.GetLastWriteTime(item);
                        string lastWrite = lastWriteDate.ToString("MM/dd/yy HH:mm");

                        string owner;
                        try
                        {
                            owner = File.GetAccessControl(item)
                                .GetOwner(typeof(System.Security.Principal.NTAccount)).ToString();
                        }
                        catch
                        {
                            owner = "???";
                        }

                        bool isFile = files.Contains(item);
                        if (isFile)
                        {
                            long fileSize = new FileInfo(item).Length;
                            Console.WriteLine(
                                lastWrite + "   <File>   " +
                                owner + new string(' ', biggestOwnerSize - owner.Length) + "   " +
                                fileSize.ToString().PadLeft(sizeCharLength) + "   " +
                                name
                            );
                        }
                        else
                        {
                            Console.WriteLine(
                                lastWrite + "   <Dir>    " +
                                owner + new string(' ', biggestOwnerSize - owner.Length) + "   " +
                                new string('.', sizeCharLength) + "   " +
                                name
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Error listing directory: " + ex.Message);
            }
        }
    }
}
