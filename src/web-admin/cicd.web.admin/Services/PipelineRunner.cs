using System.Text;
using Jenkins.Orchestrator;

namespace Cicd.Web.Admin.Services;

public enum PipelineState
{
    Idle,
    Running,
    Completed,
    Failed
}

public sealed record PipelineSnapshot(
    PipelineState State,
    IReadOnlyList<PipelineEvent> Events,
    string? CurrentStepJob,
    IReadOnlyDictionary<string, string> Logs,
    PipelineRun? Result);

public interface IPipelineRunner
{
    PipelineSnapshot Snapshot { get; }

    /// <summary>Fired whenever state, events, or logs change. Subscribers must marshal to their own sync context.</summary>
    event Action? StateChanged;

    /// <summary>Returns false if a pipeline is already running.</summary>
    bool TryStart(IReadOnlyList<PipelineStep> steps);

    /// <summary>Best-effort cancel of the in-flight pipeline; the in-flight Jenkins build is stopped.</summary>
    void Cancel();
}

public sealed class PipelineRunner : IPipelineRunner, IDisposable
{
    // ~1M chars per step. UTF-16 string buffer so memory ~2MB/step worst case.
    private const int LogCharCap = 1_000_000;
    private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(250);

    private readonly IPipelineOrchestrator _orchestrator;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly Lock _lock = new();
    private readonly Timer _flushTimer;
    private int _flushScheduled;       // 0 = idle, 1 = pending

    private List<PipelineEvent> _events = new();
    private Dictionary<string, LogBuffer> _logsByJob = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentStepJob;
    private PipelineState _state = PipelineState.Idle;
    private PipelineRun? _result;
    private CancellationTokenSource? _cts;

    public event Action? StateChanged;

    public PipelineRunner(
        IPipelineOrchestrator orchestrator,
        IHostApplicationLifetime lifetime,
        ILogger<PipelineRunner> logger)
    {
        _orchestrator = orchestrator;
        _lifetime = lifetime;
        _logger = logger;
        _flushTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public PipelineSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                var logsSnapshot = _logsByJob.Count == 0
                    ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
                    : _logsByJob.ToDictionary(kv => kv.Key, kv => kv.Value.Snapshot(), StringComparer.OrdinalIgnoreCase);

                return new PipelineSnapshot(_state, _events.ToArray(), _currentStepJob, logsSnapshot, _result);
            }
        }
    }

    public bool TryStart(IReadOnlyList<PipelineStep> steps)
    {
        CancellationToken ct;
        lock (_lock)
        {
            if (_state == PipelineState.Running) return false;

            _events = new List<PipelineEvent>();
            _logsByJob = new Dictionary<string, LogBuffer>(StringComparer.OrdinalIgnoreCase);
            _currentStepJob = null;
            _result = null;
            _state = PipelineState.Running;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            ct = _cts.Token;
        }

        ScheduleFlush();

        var progress = new Progress<PipelineEvent>(evt =>
        {
            lock (_lock)
            {
                if (evt is PipelineStepLogChunk chunk)
                {
                    if (!_logsByJob.TryGetValue(chunk.JobName, out var buf))
                    {
                        buf = new LogBuffer(LogCharCap);
                        _logsByJob[chunk.JobName] = buf;
                    }
                    buf.Append(chunk.Text);
                }
                else
                {
                    _events.Add(evt);
                    // Track which step is "currently" being shown in the live-log panel.
                    // We set on Running but DON'T clear on Completed/Failed — keeps the
                    // last step's log visible after the pipeline finishes.
                    if (evt is PipelineStepRunning r)
                    {
                        _currentStepJob = r.JobName;
                    }
                }
            }
            ScheduleFlush();
        });

        _ = Task.Run(async () =>
        {
            PipelineRun? run = null;
            string? error = null;
            try
            {
                run = await _orchestrator.RunAsync(steps, progress, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline orchestrator threw");
                error = ex.Message;
            }
            finally
            {
                lock (_lock)
                {
                    if (run is not null)
                    {
                        _result = run;
                        _state = run.Success ? PipelineState.Completed : PipelineState.Failed;
                    }
                    else
                    {
                        _result = new PipelineRun(Array.Empty<PipelineRunStep>(), Success: false, FailureReason: error);
                        _state = PipelineState.Failed;
                    }
                }
                ScheduleFlush();
            }
        }, CancellationToken.None);

        return true;
    }

    public void Cancel()
    {
        CancellationTokenSource? cts;
        lock (_lock) cts = _cts;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* already finished */ }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        _cts?.Dispose();
    }

    // --- Flush coalescing ---

    private void ScheduleFlush()
    {
        // Coalesce: only one Timer fire pending at a time. Subsequent ScheduleFlush
        // calls during the 250ms window are no-ops; the pending fire will pick up
        // everything that accumulated.
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) == 0)
        {
            try { _flushTimer.Change(FlushDelay, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { /* shutting down */ }
        }
    }

    private void Flush()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);
        StateChanged?.Invoke();
    }

    // --- Per-job log buffer ---

    private sealed class LogBuffer
    {
        private readonly int _cap;
        private readonly StringBuilder _sb = new();
        private bool _trimmed;

        public LogBuffer(int cap) { _cap = cap; }

        public void Append(string text)
        {
            _sb.Append(text);
            if (_sb.Length > _cap)
            {
                var excess = _sb.Length - _cap;
                _sb.Remove(0, excess);
                _trimmed = true;
            }
        }

        public string Snapshot()
        {
            return _trimmed
                ? "<em>… earlier output trimmed …</em>\n" + _sb
                : _sb.ToString();
        }
    }
}
