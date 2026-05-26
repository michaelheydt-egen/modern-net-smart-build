using Jenkins.Orchestrator;

namespace Jenkins.WebUI.Services;

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
    PipelineRun? Result);

public interface IPipelineRunner
{
    PipelineSnapshot Snapshot { get; }

    /// <summary>Fired whenever state or events change. Subscribers must marshal to their own sync context.</summary>
    event Action? StateChanged;

    /// <summary>Returns false if a pipeline is already running.</summary>
    bool TryStart(IReadOnlyList<PipelineStep> steps);

    /// <summary>Best-effort cancel of the in-flight pipeline; the in-flight Jenkins build is stopped.</summary>
    void Cancel();
}

public sealed class PipelineRunner : IPipelineRunner, IDisposable
{
    private readonly IPipelineOrchestrator _orchestrator;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<PipelineRunner> _logger;
    private readonly Lock _lock = new();

    private List<PipelineEvent> _events = new();
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
    }

    public PipelineSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                return new PipelineSnapshot(_state, _events.ToArray(), _result);
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
            _result = null;
            _state = PipelineState.Running;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            ct = _cts.Token;
        }

        StateChanged?.Invoke();

        var progress = new Progress<PipelineEvent>(evt =>
        {
            lock (_lock) _events.Add(evt);
            StateChanged?.Invoke();
        });

        // Detach onto the thread pool so the caller (UI thread / SignalR dispatcher) returns immediately.
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
                StateChanged?.Invoke();
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
        _cts?.Dispose();
    }
}
