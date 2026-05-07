using System;
using System.Threading;
using System.Threading.Tasks;

namespace LensHH.Mcp
{
    /// <summary>
    /// Status of a job-pattern optimization. Returned by optimize_status
    /// so the LLM can either keep polling (Running) or report completion
    /// to the user (Completed / Cancelled / Faulted).
    /// </summary>
    public enum JobStatus
    {
        Running,
        Completed,
        Cancelled,
        Faulted,
    }

    /// <summary>
    /// Snapshot of a long-running optimization that the LLM polls via
    /// optimize_status. Lives on McpSession until cleared. The worker
    /// Task updates the progress fields directly; the status tool reads
    /// them. Atomicity is best-effort — a single status read may show
    /// a torn merit/trial pair, but the LLM is asking ~every 10s so
    /// torn reads aren't a real problem in practice.
    /// </summary>
    public class RunningJob
    {
        public string JobId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Kind { get; }
        public DateTime StartedUtc { get; } = DateTime.UtcNow;

        public CancellationTokenSource Cts { get; } = new CancellationTokenSource();
        public Task? Task { get; set; }

        // Progress snapshot — written by the worker, read by the status tool.
        public volatile string Phase = "Starting...";
        public double InitialMerit = double.NaN;
        public double CurrentMerit = double.NaN;
        public double BestMerit = double.NaN;
        public int Trial;
        public int MaxTrials;
        public int Accepted;
        public int Rejected;
        public int GlassSwaps;

        // Terminal state — set when the worker finishes.
        public volatile string StatusValue = nameof(JobStatus.Running);
        public string? Result;
        public string? Error;
        public DateTime? CompletedUtc;

        public JobStatus Status =>
            Enum.TryParse<JobStatus>(StatusValue, out var s) ? s : JobStatus.Running;

        public TimeSpan Elapsed => (CompletedUtc ?? DateTime.UtcNow) - StartedUtc;

        public RunningJob(string kind)
        {
            Kind = kind;
        }

        public void Complete(string result)
        {
            Result = result;
            CompletedUtc = DateTime.UtcNow;
            StatusValue = nameof(JobStatus.Completed);
        }

        public void Cancel()
        {
            CompletedUtc ??= DateTime.UtcNow;
            StatusValue = nameof(JobStatus.Cancelled);
        }

        public void Fault(Exception ex)
        {
            Error = ex.Message;
            CompletedUtc = DateTime.UtcNow;
            StatusValue = nameof(JobStatus.Faulted);
        }
    }
}
