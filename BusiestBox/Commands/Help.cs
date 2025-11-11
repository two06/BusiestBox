using System;

namespace BusiestBox.Commands
{
    internal class Help
    {
        public static void Execute()
        {
            Console.WriteLine("\nAvailable Commands:");
            Console.WriteLine("  assemblies  - List loaded assemblies");
            Console.WriteLine("  base64      - Encode or decode files as Base64");
            Console.WriteLine("  bof         - Execute a BOF");
            Console.WriteLine("  cat         - Display the contents of a file");
            Console.WriteLine("  cd          - Change directory");
            Console.WriteLine("  copy|cp     - Copy files or directories");
            Console.WriteLine("  decrypt     - AES decrypt a file");
            Console.WriteLine("  encrypt     - AES encrypt a file");
            Console.WriteLine("  exit|quit   - Exit BusiestBox");
            Console.WriteLine("  hash        - Calculate Sha256 hash of files");
            Console.WriteLine("  help        - Show this help menu");
            Console.WriteLine("  hostinfo    - Show basic info about the host");
            Console.WriteLine("  invoke      - Invoke a method or function");
            Console.WriteLine("  jobs        - List or manage background jobs");
            Console.WriteLine("  load        - Load an assembly or resource");
            Console.WriteLine("  ls          - List directory contents");
            Console.WriteLine("  mkdir       - Make a directory");
            Console.WriteLine("  mv|move     - Move files or directories");
            Console.WriteLine("  netopts     - Show or modify WebClient options");
            Console.WriteLine("  pwd         - Print working directory");
            Console.WriteLine("  rm|del      - Remove files or directories");
            Console.WriteLine("  shell       - Execute a system command");
            Console.WriteLine("  unzip       - Unzip a ZIP archive");
            Console.WriteLine("  upload      - Upload a file via HTTP POST");
            Console.WriteLine("  zip         - Zip up files into a ZIP archive");
            Console.WriteLine();
            Console.WriteLine("Use vfs:// to access the in-memory filesystem");
            Console.WriteLine("Use !command to quickly run a shell command");
            Console.WriteLine();
        }
    }
}
