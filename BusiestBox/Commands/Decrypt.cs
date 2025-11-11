using System;
using System.IO;

using BusiestBox.Vfs;
using BusiestBox.Utils;

namespace BusiestBox.Commands
{
    internal class Decrypt
    {
        public static void Execute(string[] args)
        {
            if (args == null || args.Length != 3)
            {
                Console.WriteLine("Usage: decrypt <passphrase> <inputFile|url> <outputFile>");
                return;
            }

            string passphrase = args[0];
            string inputRaw = args[1];
            string outputRaw = args[2];

            try
            {
                // ----- read ciphertext -----
                byte[] blob;

                if (IsHttpUrl(inputRaw))
                {
                    blob = NetUtils.DownloadDataSmart(inputRaw, out _);
                }
                else
                {
                    string inResolved = ResolveAny(inputRaw);
                    if (!VfsLayer.FileExists(inResolved))
                    {
                        Console.WriteLine("[!] File not found: {0}", inputRaw);
                        return;
                    }
                    blob = VfsLayer.ReadAllBytes(inResolved);
                }

                // ----- decrypt -----
                byte[] plaintext = Crypto.Crypto.DecryptBytes(passphrase, blob);

                // ----- write plaintext (VFS or FS) -----
                string outResolved = ResolveAny(outputRaw);
                EnsureParent(outResolved);
                VfsLayer.WriteAllBytes(outResolved, plaintext);

                Console.WriteLine("[*] Decrypted {0} -> {1}", inputRaw, outResolved);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Decryption failed: {0}", ex.Message);
            }
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                   Uri.IsWellFormedUriString(s, UriKind.Absolute);
        }

        // Resolve to either absolute VFS ("vfs://...") or absolute FS path.
        private static string ResolveAny(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;

            // Keep explicit VFS as-is
            if (p.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                return p;

            // Expand ~ and ~/...
            if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\"))
            {
                try
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!string.IsNullOrEmpty(home))
                        p = p.Length == 1 ? home : Path.Combine(home, p.Substring(2));
                }
                catch { /* ignore */ }
            }

            try
            {
                return Path.IsPathRooted(p) ? p : Path.GetFullPath(p);
            }
            catch
            {
                // Fallback: return raw; VfsLayer will validate if it's actually VFS, else FS ops will fail upstream.
                return p;
            }
        }

        private static void EnsureParent(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath)) return;

            if (resolvedPath.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string p = resolvedPath.Substring("vfs://".Length).TrimEnd('/');
                int idx = p.LastIndexOf('/');
                string parent = idx > 0 ? "vfs://" + p.Substring(0, idx) : "vfs://";
                VfsLayer.CreateDirectory(parent);
            }
            else
            {
                try
                {
                    var dir = Path.GetDirectoryName(resolvedPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                }
                catch { /* ignore */ }
            }
        }
    }
}
