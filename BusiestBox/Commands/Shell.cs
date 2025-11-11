using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using BusiestBox.Utils;
using BusiestBox.Vfs;

namespace BusiestBox.Commands
{
    internal class Shell
    {
        public static void Execute(string currentDirectory, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                PrintUsage();
                return;
            }

            bool runInBackground = false;
            string outFileRaw = null;

            int i = 0;
            while (i < args.Length && args[i].StartsWith("--", StringComparison.Ordinal))
            {
                string tok = args[i];

                if (tok.Equals("--bg", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("--background", StringComparison.OrdinalIgnoreCase))
                {
                    runInBackground = true;
                    i++;
                    continue;
                }

                if (tok.Equals("--outfile", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.WriteLine("[!] shell: --outfile requires a path");
                        return;
                    }
                    outFileRaw = args[++i];
                    i++;
                    continue;
                }

                if (tok.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    PrintUsage();
                    return;
                }

                Console.WriteLine("[!] shell: unknown option '{0}'", tok);
                return;
            }

            if (i >= args.Length)
            {
                PrintUsage();
                return;
            }

            string cmd = args[i];
            string[] cmdArgs = (i + 1 < args.Length) ? args.Skip(i + 1).ToArray() : Array.Empty<string>();
            string targetDisplay = cmd + (cmdArgs.Length > 0 ? " " + string.Join(" ", cmdArgs) : "");

            if (runInBackground)
            {
                Process proc = BuildProcess(cmd, cmdArgs);
                int jobId = 0;

                jobId = JobManager.StartBackgroundJob(
                    JobType.Shell,
                    targetDisplay,
                    outFileRaw,
                    () =>
                    {
                        var jobInfo = JobManager.GetJobInfo(jobId);
                        RunShellCommand(proc, outFileRaw, "[bg " + jobId + "] ", jobInfo, currentDirectory);
                    },
                    boundProcess: proc
                );

                Console.WriteLine("[*] shell scheduled in background (job {0})", jobId);
                return;
            }

            RunForegroundShell(cmd, cmdArgs, currentDirectory, outFileRaw, targetDisplay);
        }

        public static void ExecuteShorthand(string line, string currentDirectory)
        {
            string cmdLine = line.TrimStart('!', ' ');
            if (string.IsNullOrWhiteSpace(cmdLine)) return;

            string[] parts = SplitArgs(cmdLine);
            string cmd = parts[0];
            string[] cmdArgs = parts.Skip(1).ToArray();

            RunForegroundShell(cmd, cmdArgs, currentDirectory, null, cmdLine);
        }

        // ---------- Foreground ----------

        private static void RunForegroundShell(
            string cmd,
            string[] args,
            string currentDirectory,
            string outFileRaw,
            string targetDisplay)
        {
            var done = new ManualResetEvent(false);
            Exception captured = null;

            int jobId = JobManager.RegisterForegroundJob(JobType.Shell, targetDisplay, null, outFileRaw);
            var jobInfo = JobManager.GetJobInfo(jobId);

            Process proc = BuildProcess(cmd, args);
            if (jobInfo != null) jobInfo.BoundProcess = proc;

            var worker = new Thread(() =>
            {
                try
                {
                    RunShellCommand(proc, outFileRaw, "", jobInfo, currentDirectory);
                }
                catch (Exception ex)
                {
                    captured = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            if (jobInfo != null)
            {
                jobInfo.WorkerThread = worker;
                try { jobInfo.ManagedThreadId = worker.ManagedThreadId; } catch { }
            }

            worker.IsBackground = true;
            try { worker.SetApartmentState(ApartmentState.MTA); } catch { }

            worker.Start();
            done.WaitOne();
            JobManager.UnregisterForegroundJob();

            if (captured != null)
                Console.WriteLine("[!] shell: " + captured.Message);
        }

        // ---------- Core runner ----------

        private static void RunShellCommand(
            Process proc,
            string outFileRaw,
            string prefix,
            JobInfo jobInfo,
            string currentDirectory)
        {
            TextWriter fileWriter = null;
            StringWriter vfsBuffer = null;
            string vfsTarget = null;

            // Set up file or VFS output writer
            if (!string.IsNullOrEmpty(outFileRaw))
            {
                try
                {
                    string resolved = VfsLayer.ResolvePath(currentDirectory, outFileRaw);
                    if (resolved.StartsWith("vfs://", StringComparison.OrdinalIgnoreCase))
                    {
                        vfsTarget = resolved;
                        vfsBuffer = new StringWriter(new StringBuilder());
                        EnsureParentDirForResolvedPath(vfsTarget);
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(resolved);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        fileWriter = new StreamWriter(resolved, false, Encoding.UTF8) { AutoFlush = true };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] shell: failed to open output file: " + ex.Message);
                }
            }

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                Console.WriteLine(prefix + e.Data);
                if (fileWriter != null) fileWriter.WriteLine(e.Data);
                if (vfsBuffer != null) vfsBuffer.WriteLine(e.Data);
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                Console.WriteLine(prefix + e.Data);
                if (fileWriter != null) fileWriter.WriteLine(e.Data);
                if (vfsBuffer != null) vfsBuffer.WriteLine(e.Data);
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            fileWriter?.Dispose();

            if (vfsBuffer != null && vfsTarget != null)
            {
                VfsLayer.WriteAllText(vfsTarget, vfsBuffer.ToString(), Encoding.UTF8);
                vfsBuffer.Dispose();
            }
        }

        private static Process BuildProcess(string cmd, string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (args != null && args.Length > 0)
                psi.Arguments = string.Join(" ", args);

            var proc = new Process();
            proc.StartInfo = psi;
            proc.EnableRaisingEvents = true;
            return proc;
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

        private static string[] SplitArgs(string input)
        {
            return TabComplete.SplitArgsPreservingQuotes(input);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  shell [--bg] [--outfile <path>] <command> [args...]");
            Console.WriteLine("Examples:");
            Console.WriteLine("  shell whoami /all");
            Console.WriteLine("  shell --bg --outfile vfs://out.txt ipconfig /all");
            Console.WriteLine();
            Console.WriteLine("Short form:");
            Console.WriteLine("  !hostname");
        }
    }
}
