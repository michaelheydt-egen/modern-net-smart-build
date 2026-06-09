using System.Diagnostics;
using System.Net;
using Jenkins.Client;

namespace Cicd.Web.Admin.Services;

public enum JenkinsHealthStatus
{
    Unknown,        // before the first probe
    Healthy,        // 2xx + creds OK
    Degraded,       // server up but bad creds / 5xx
    Unreachable     // network error / timeout
}

public sealed record JenkinsHealthSnapshot(
    JenkinsHealthStatus Status,
    DateTimeOffset? LastCheckedAt,
    string? Detail,
    TimeSpan? Latency);

public interface IJenkinsHealth
{
    JenkinsHealthSnapshot Snapshot { get; }
    event Action? StateChanged;
}

public sealed class JenkinsHealthService : BackgroundService, IJenkinsHealth
{
    private readonly JenkinsClient _client;    // owned + disposed by this service
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _probeTimeout;
    private readonly ILogger<JenkinsHealthService> _logger;
    private readonly Lock _lock = new();

    private JenkinsHealthSnapshot _snapshot = new(JenkinsHealthStatus.Unknown, null, null, null);

    public event Action? StateChanged;

    public JenkinsHealthService(
        JenkinsOptions jenkinsOptions,
        JenkinsHealthOptions healthOptions,
        ILogger<JenkinsHealthService> logger)
    {
        _client = new JenkinsClient(jenkinsOptions);
        _pollInterval = TimeSpan.FromSeconds(healthOptions.PollIntervalSeconds);
        _probeTimeout = TimeSpan.FromSeconds(healthOptions.ProbeTimeoutSeconds);
        _logger = logger;
    }

    public JenkinsHealthSnapshot Snapshot
    {
        get { lock (_lock) return _snapshot; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First probe runs immediately so the UI doesn't sit on "Unknown" for the full interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProbeOnceAsync(CancellationToken stoppingToken)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        probeCts.CancelAfter(_probeTimeout);

        JenkinsHealthStatus status;
        string? detail;
        TimeSpan? latency = null;

        var sw = Stopwatch.StartNew();
        try
        {
            await _client.PingAsync(probeCts.Token);
            sw.Stop();
            latency = sw.Elapsed;
            status = JenkinsHealthStatus.Healthy;
            detail = $"200 OK ({sw.ElapsedMilliseconds} ms)";
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // App is shutting down — don't update state, don't log noise.
            return;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            status = JenkinsHealthStatus.Unreachable;
            detail = $"Timed out after {_probeTimeout.TotalSeconds:F0}s";
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            sw.Stop();
            status = JenkinsHealthStatus.Degraded;
            detail = $"{(int)ex.StatusCode.Value} {ex.StatusCode}";
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            sw.Stop();
            status = JenkinsHealthStatus.Degraded;
            detail = $"{(int)ex.StatusCode.Value} {ex.StatusCode}";
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            status = JenkinsHealthStatus.Unreachable;
            detail = ex.Message;
        }
        catch (Exception ex)
        {
            sw.Stop();
            status = JenkinsHealthStatus.Unreachable;
            detail = ex.Message;
        }

        UpdateSnapshot(new JenkinsHealthSnapshot(status, DateTimeOffset.UtcNow, detail, latency));
    }

    private void UpdateSnapshot(JenkinsHealthSnapshot next)
    {
        JenkinsHealthStatus prevStatus;
        lock (_lock)
        {
            prevStatus = _snapshot.Status;
            _snapshot = next;
        }

        // Log only when the status changes, so we don't spam the log on every poll tick.
        if (prevStatus != next.Status)
        {
            switch (next.Status)
            {
                case JenkinsHealthStatus.Healthy:
                    _logger.LogInformation("Jenkins is healthy: {Detail}", next.Detail);
                    break;
                case JenkinsHealthStatus.Degraded:
                    _logger.LogWarning("Jenkins is degraded: {Detail}", next.Detail);
                    break;
                case JenkinsHealthStatus.Unreachable:
                    _logger.LogWarning("Jenkins is unreachable: {Detail}", next.Detail);
                    break;
            }
        }

        StateChanged?.Invoke();
    }

    public override void Dispose()
    {
        _client.Dispose();
        base.Dispose();
    }
}
