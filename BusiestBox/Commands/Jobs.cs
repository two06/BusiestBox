using System;
using BusiestBox.Utils;

namespace BusiestBox.Commands
{
    internal static class Jobs
    {
        public static void Execute(string[] args)
        {
            // No arguments → list all jobs
            if (args == null || args.Length == 0)
            {
                JobManager.PrintJobsTable();
                return;
            }

            // jobs kill <id>
            if (args.Length == 2 && args[0].Equals("kill", StringComparison.OrdinalIgnoreCase))
            {
                int jobId;
                if (!int.TryParse(args[1], out jobId))
                {
                    Console.WriteLine("[!] jobs kill: invalid job ID");
                    return;
                }

                if (JobManager.KillJob(jobId))
                {
                    Console.WriteLine("[*] Killed job {0}", jobId);
                }
                else
                {
                    Console.WriteLine("[!] Failed to kill job {0} (maybe it's not running)", jobId);
                }
                return;
            }

            // Unrecognized usage
            PrintUsage();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  jobs            - list all background and foreground jobs");
            Console.WriteLine("  jobs kill <id>  - kill a running job by ID");
        }
    }
}
