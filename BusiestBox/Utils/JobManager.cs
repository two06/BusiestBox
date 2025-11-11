using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace BusiestBox.Utils
{
    internal enum JobStatus
    {
        Running,
        Complete,
        Canceled,
        Error
    }

    internal enum JobType
    {
        Assembly,
        Bof,
        Shell,
        Other
    }

    internal enum JobMode
    {
        Foreground,
        Background
    }

    internal class JobInfo
    {
        public int JobId { get; set; }
        public JobType Type { get; set; }
        public JobMode Mode { get; set; }
        public string TargetName { get; set; }
        public string OutputPath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public JobStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public Thread WorkerThread { get; set; }
        public int ManagedThreadId { get; set; }
        public Process BoundProcess; // Optional: for Shell jobs

        public TimeSpan Runtime
        {
            get { return (EndTime ?? DateTime.UtcNow) - StartTime; }
        }
    }

    internal static class JobManager
    {
        private static readonly object _lock = new object();
        private static readonly Dictionary<int, JobInfo> _jobs = new Dictionary<int, JobInfo>();
        private static int _nextJobId = 0;

        // Track current foreground job for Ctrl+C
        private static int _foregroundJobId = -1;

        // -----------------------
        // Job creation
        // -----------------------

        /// <summary>
        /// Starts a background job on a new thread. Optionally binds a process for later cancellation.
        /// </summary>
        public static int StartBackgroundJob(
            JobType type,
            string targetName,
            string outputPath,
            Action action,
            Process boundProcess = null)
        {
            int jobId = Interlocked.Increment(ref _nextJobId);

            var thread = new Thread(() =>
            {
                JobInfo job;
                lock (_lock) job = _jobs[jobId];

                try
                {
                    action();
                    job.Status = JobStatus.Complete;
                }
                catch (ThreadAbortException)
                {
                    try { Thread.ResetAbort(); } catch { }
                    job.Status = JobStatus.Canceled;
                    job.EndTime = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    job.Status = JobStatus.Error;
                    job.ErrorMessage = ex.Message;
                    job.EndTime = DateTime.UtcNow;
                }
                finally
                {
                    if (!job.EndTime.HasValue)
                        job.EndTime = DateTime.UtcNow;
                }
            });

            thread.IsBackground = true;
            try { thread.SetApartmentState(ApartmentState.MTA); } catch { }

            var jobInfo = new JobInfo
            {
                JobId = jobId,
                Type = type,
                Mode = JobMode.Background,
                TargetName = targetName,
                OutputPath = outputPath,
                StartTime = DateTime.UtcNow,
                Status = JobStatus.Running,
                WorkerThread = thread,
                ManagedThreadId = thread.ManagedThreadId,
                BoundProcess = boundProcess   // ✅ NEW
            };

            lock (_lock)
            {
                _jobs[jobId] = jobInfo;
            }

            thread.Start();
            return jobId;
        }

        /// <summary>
        /// Registers a foreground job (e.g. Invoke without --bg). Allows Ctrl+C cancellation.
        /// </summary>
        public static int RegisterForegroundJob(
            JobType type,
            string targetName,
            Thread thread,
            string outputPath = null,
            Process boundProcess = null)
        {
            int jobId = Interlocked.Increment(ref _nextJobId);

            var jobInfo = new JobInfo
            {
                JobId = jobId,
                Type = type,
                Mode = JobMode.Foreground,
                TargetName = targetName,
                OutputPath = outputPath,
                StartTime = DateTime.UtcNow,
                Status = JobStatus.Running,
                WorkerThread = thread,
                ManagedThreadId = thread?.ManagedThreadId ?? -1,
                BoundProcess = boundProcess   // ✅ NEW
            };

            lock (_lock)
            {
                _foregroundJobId = jobId;
                _jobs[jobId] = jobInfo;
            }

            return jobId;
        }

        /// <summary>
        /// Marks the current foreground job as finished (success or error).
        /// Should be called once the foreground thread exits.
        /// </summary>
        public static void UnregisterForegroundJob()
        {
            lock (_lock)
            {
                _foregroundJobId = -1;
            }
        }

        // -----------------------
        // Cancellation
        // -----------------------

        /// <summary>
        /// Cancels the currently registered foreground job (Ctrl+C handler).
        /// </summary>
        public static bool CancelForeground()
        {
            lock (_lock)
            {
                if (_foregroundJobId == -1) return false;
                JobInfo job;
                if (!_jobs.TryGetValue(_foregroundJobId, out job)) return false;
                return AbortThread(job);
            }
        }

        /// <summary>
        /// Attempts to kill any job (foreground or background) by ID.
        /// </summary>
        public static bool KillJob(int jobId)
        {
            lock (_lock)
            {
                JobInfo job;
                if (!_jobs.TryGetValue(jobId, out job)) return false;
                return AbortThread(job);
            }
        }

        public static void MarkJobCompleted(int jobId)
        {
            lock (_lock)
            {
                JobInfo job;
                if (_jobs.TryGetValue(jobId, out job))
                {
                    if (job.Status == JobStatus.Running)
                    {
                        job.Status = JobStatus.Complete;
                        job.EndTime = DateTime.UtcNow;
                    }
                }
            }
        }

        private static bool AbortThread(JobInfo job)
        {
            if (job == null)
                return false;

            // Kill associated process first (if any)
            try
            {
                if (job.BoundProcess != null && !job.BoundProcess.HasExited)
                {
                    job.BoundProcess.Kill();
                    job.BoundProcess.Dispose();
                    job.BoundProcess = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Failed to kill process for job {0}: {1}", job.JobId, ex.Message);
            }

            // Then abort the worker thread if still running
            if (job.Status == JobStatus.Running && job.WorkerThread != null && job.WorkerThread.IsAlive)
            {
                try
                {
#pragma warning disable 618
                    job.WorkerThread.Abort();
#pragma warning restore 618
                    job.Status = JobStatus.Canceled;
                    job.EndTime = DateTime.UtcNow;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        // -----------------------
        // Listing & reporting
        // -----------------------

        public static JobInfo GetJobInfo(int jobId)
        {
            lock (_lock)
            {
                JobInfo job;
                return _jobs.TryGetValue(jobId, out job) ? job : null;
            }
        }

        public static void SetJobProcess(int jobId, Process proc)
        {
            lock (_lock)
            {
                JobInfo job;
                if (_jobs.TryGetValue(jobId, out job))
                {
                    job.BoundProcess = proc;
                }
            }
        }

        public static List<JobInfo> ListJobs()
        {
            lock (_lock)
            {
                return new List<JobInfo>(_jobs.Values);
            }
        }

        public static void PrintJobsTable()
        {
            var jobs = ListJobs();
            if (jobs.Count == 0)
            {
                Console.WriteLine("[*] No active or historical jobs.");
                return;
            }

            Console.WriteLine("ID   Status   Type       Mode       Target                    Runtime  Outfile");
            Console.WriteLine("==== ======== ========== ========== ========================= ======== =======");

            foreach (var job in jobs)
            {
                var runtimeStr = string.Format("{0}s", (int)job.Runtime.TotalSeconds);
                Console.WriteLine(
                    "{0,-4} {1,-8} {2,-10} {3,-10} {4,-25} {5,-8} {6,-25}",
                    job.JobId,
                    job.Status,
                    job.Type,
                    job.Mode,
                    Truncate(job.TargetName, 25),
                    runtimeStr,
                    Truncate(job.OutputPath ?? "(none)", 25)
                );
            }
        }

        private static string Truncate(string s, int len)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= len) return s;
            return s.Substring(0, len - 3) + "...";
        }
    }
}
