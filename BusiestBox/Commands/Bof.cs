using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using BusiestBox.Coff;   // COFFLoader, CoffValidator, Pack.PackArgs(...)
using BusiestBox.Utils;  // JobManager, NetUtils
using BusiestBox.Vfs;    // VfsLayer

namespace BusiestBox.Commands
{
    internal class Bof
    {
        public static void Execute(string currentDirectory, params string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintUsage();
                return;
            }

            // -------------------------
            // Flag parsing
            // -------------------------
            string entrypoint = "go";
            string outFileRaw = null;
            bool runInBackground = false;

            int i = 0;
            while (i < args.Length && args[i].StartsWith("--", StringComparison.Ordinal))
            {
                var tok = args[i];

                if (tok.Equals("--entrypoint", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.WriteLine("[!] bof: --entrypoint requires a value");
                        return;
                    }
                    entrypoint = args[++i];
                    i++;
                    continue;
                }

                if (tok.Equals("--outfile", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.WriteLine("[!] bof: --outfile requires a path");
                        return;
                    }
                    outFileRaw = args[++i];
                    i++;
                    continue;
                }

                if (tok.Equals("--bg", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("--background", StringComparison.OrdinalIgnoreCase))
                {
                    runInBackground = true;
                    i++;
                    continue;
                }

                if (tok.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintUsage();
                    return;
                }

                Console.WriteLine("[!] bof: unknown option '{0}'", tok);
                return;
            }

            // -------------------------
            // Positional: COFF path
            // -------------------------
            if (i >= args.Length)
            {
                PrintUsage();
                return;
            }

            string coffToken = args[i++];
            byte[] coffBytes;
            try
            {
                coffBytes = ReadAllFromLocation(currentDirectory, coffToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] bof: failed to read COFF '{0}': {1}", coffToken, ex.Message);
                return;
            }

            // Validate arch
            if (!Validator.ValidateMatchesCurrentProcess(coffBytes, out var fileArch, out var why))
            {
                Console.WriteLine("[!] bof: {0}", why);
                return;
            }

            // -------------------------
            // Format string + args
            // -------------------------
            string fmt = "";
            if (i < args.Length && !args[i].StartsWith("-", StringComparison.Ordinal))
            {
                fmt = args[i++];
            }

            var argTokens = (i < args.Length) ? args.Skip(i).ToArray() : Array.Empty<string>();
            if (fmt.Length != argTokens.Length)
            {
                if (!(fmt.Length == 0 && argTokens.Length == 0))
                {
                    Console.WriteLine("[!] bof: format length ({0}) must match number of args ({1})",
                        fmt.Length, argTokens.Length);
                    return;
                }
            }

            object[] packObjects = new object[fmt.Length];
            for (int k = 0; k < fmt.Length; k++)
            {
                char t = fmt[k];
                string atok = argTokens[k];
                switch (t)
                {
                    case 'b':
                        try { packObjects[k] = ReadAllFromLocation(currentDirectory, atok); }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[!] bof: arg {0} ('{1}') b=bytes error: {2}", k + 1, atok, ex.Message);
                            return;
                        }
                        break;

                    case 'i':
                        if (!TryParseInt32(atok, out int i32))
                        {
                            Console.WriteLine("[!] bof: arg {0} ('{1}') is not a valid int32", k + 1, atok);
                            return;
                        }
                        packObjects[k] = i32;
                        break;

                    case 's':
                        if (!TryParseInt16(atok, out short i16))
                        {
                            Console.WriteLine("[!] bof: arg {0} ('{1}') is not a valid int16", k + 1, atok);
                            return;
                        }
                        packObjects[k] = i16;
                        break;

                    case 'z':
                    case 'Z':
                        packObjects[k] = atok ?? "";
                        break;

                    default:
                        Console.WriteLine("[!] bof: invalid format character '{0}' at position {1}", t, k + 1);
                        return;
                }
            }

            // -------------------------
            // Pack args
            // -------------------------
            byte[] argBlob;
            try
            {
                argBlob = Pack.PackArgs(fmt, packObjects);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] bof: failed packing arguments: {0}", ex.Message);
                return;
            }

            string coffB64 = Convert.ToBase64String(coffBytes);
            string argsB64 = Convert.ToBase64String(argBlob);

            // Target name for JobManager display
            string targetName = Path.GetFileName(coffToken);

            if (runInBackground)
            {
                // -------------------------
                // Background job
                // -------------------------
                JobManager.StartBackgroundJob(
                    JobType.Bof,
                    targetName,
                    outFileRaw,
                    () => RunBof(entrypoint, coffB64, argsB64, currentDirectory, outFileRaw)
                );

                Console.WriteLine("[*] bof scheduled in background");
                return;
            }
            else
            {
                // -------------------------
                // Foreground job (Ctrl+C cancellable)
                // -------------------------
                RunForegroundBof(entrypoint, coffB64, argsB64, currentDirectory, outFileRaw, targetName);
            }
        }

        private static void RunBof(string entrypoint, string coffB64, string argsB64, string currentDirectory, string outFileRaw)
        {
            var loader = new COFFLoader();
            string output = loader.RunCoff(entrypoint, coffB64, argsB64) ?? string.Empty;

            if (output.Length > 0)
                Console.WriteLine(output);

            if (!string.IsNullOrEmpty(outFileRaw))
            {
                try
                {
                    string resolved = VfsLayer.ResolvePath(currentDirectory, outFileRaw);
                    AppendTextResolved(resolved, output);
                    Console.WriteLine("[*] bof: wrote output to {0}", resolved);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] bof: failed to write outfile: {0}", ex.Message);
                }
            }
        }

        private static void RunForegroundBof(string entrypoint, string coffB64, string argsB64, string currentDirectory, string outFileRaw, string targetName)
        {
            var done = new ManualResetEvent(false);
            Exception captured = null;

            var worker = new Thread(delegate ()
            {
                try
                {
                    RunBof(entrypoint, coffB64, argsB64, currentDirectory, outFileRaw);
                }
#pragma warning disable 618
                catch (ThreadAbortException)
                {
                    try { Thread.ResetAbort(); } catch { }
                    Console.WriteLine("[!] bof aborted by Ctrl+C.");
                }
#pragma warning restore 618
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            worker.IsBackground = true;
            try { worker.SetApartmentState(ApartmentState.MTA); } catch { }

            int jobId = JobManager.RegisterForegroundJob(JobType.Bof, targetName, worker, outFileRaw);


            worker.Start();
            done.WaitOne();
            JobManager.MarkJobCompleted(jobId);
            JobManager.UnregisterForegroundJob();

            if (captured != null)
                Console.WriteLine("[!] bof: " + captured.Message);
        }

        // -------------------------
        // Helpers
        // -------------------------

        private static bool TryParseInt32(string s, out int value)
        {
            if (!string.IsNullOrEmpty(s) &&
                (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                 s.StartsWith("&h", StringComparison.OrdinalIgnoreCase)))
            {
                try { value = Convert.ToInt32(s.Substring(2), 16); return true; }
                catch { value = 0; return false; }
            }
            return int.TryParse(s, out value);
        }

        private static bool TryParseInt16(string s, out short value)
        {
            if (!string.IsNullOrEmpty(s) &&
                (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                 s.StartsWith("&h", StringComparison.OrdinalIgnoreCase)))
            {
                try { value = Convert.ToInt16(s.Substring(2), 16); return true; }
                catch { value = 0; return false; }
            }
            return short.TryParse(s, out value);
        }

        private static bool IsHttpUrl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
                   Uri.IsWellFormedUriString(s, UriKind.Absolute);
        }

        private static byte[] ReadAllFromLocation(string currentDirectory, string token)
        {
            if (IsHttpUrl(token))
            {
                string _;
                return NetUtils.DownloadDataSmart(token, out _);
            }

            string resolved = VfsLayer.ResolvePath(currentDirectory, token);
            if (!VfsLayer.FileExists(resolved))
                throw new FileNotFoundException("not found", token);

            return VfsLayer.ReadAllBytes(resolved);
        }

        private static void AppendTextResolved(string resolved, string text)
        {
            if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string existing = VfsLayer.FileExists(resolved)
                    ? Encoding.UTF8.GetString(VfsLayer.ReadAllBytes(resolved))
                    : string.Empty;

                EnsureParentDirForResolvedPath(resolved);
                VfsLayer.WriteAllText(resolved, existing + text, Encoding.UTF8);
            }
            else
            {
                string dir = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var sw = new StreamWriter(resolved, append: true, Encoding.UTF8))
                    sw.Write(text);
            }
        }

        private static void EnsureParentDirForResolvedPath(string resolved)
        {
            if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
            {
                string p = resolved.Substring("vfs://".Length).TrimEnd('/');
                int idx = p.LastIndexOf('/');
                string parent = (idx > 0) ? ("vfs://" + p.Substring(0, idx)) : "vfs://";
                VfsLayer.CreateDirectory(parent);
            }
            else
            {
                string dir = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  bof [--entrypoint <name>] [--outfile <path>] [--bg] <coffPath> [formatString] [arg1 ...]");
            Console.WriteLine();
            Console.WriteLine("Defaults:");
            Console.WriteLine("  --entrypoint defaults to \"go\"");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  bof vfs://my.bof                            (no arguments)");
            Console.WriteLine("  bof vfs://my.bof z \"hello world\"");
            Console.WriteLine("  bof --outfile vfs://out.txt whoami.o");
            Console.WriteLine("  bof --entrypoint Main C:\\tools\\payload.o bis my.bin 1337 7");
            Console.WriteLine("  bof --entrypoint run --outfile out.txt https://x/o bZ https://x/in.bin \"Wide String\"");
            Console.WriteLine("  bof --bg my.bof z \"async run in background\"");
            Console.WriteLine();
            Console.WriteLine("Format chars: b(binary file/url), i(int32), s(int16), z(utf8 cstr), Z(utf16le cstr)");
        }
    }
}
