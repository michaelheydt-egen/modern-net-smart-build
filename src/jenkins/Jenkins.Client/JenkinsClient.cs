using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jenkins.Client;

public sealed class JenkinsClient : IJenkinsClient, IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultTimeout      = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private (string Field, string Value)? _crumb;       // null = not yet fetched; (null,null) sentinel handled below
    private bool _crumbFetched;
    private readonly SemaphoreSlim _crumbLock = new(1, 1);

    public JenkinsClient(JenkinsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = new HttpClient { BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/") };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.User}:{options.ApiToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        _ownsHttp = true;
    }

    /// <summary>For advanced callers (DI, HttpClientFactory). Caller owns the HttpClient.</summary>
    public JenkinsClient(HttpClient httpClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttp = false;
    }

    // --- Public surface ---

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        // ?tree=mode trims the response to a single field so Jenkins doesn't serialize the
        // whole root metadata blob for what's effectively a heartbeat.
        using var resp = await _http.GetAsync("api/json?tree=mode", cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<long> StartBuildAsync(
        string jobName,
        IDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var path = (parameters is { Count: > 0 })
            ? $"{JobPath(jobName)}/buildWithParameters?{BuildQuery(parameters)}"
            : $"{JobPath(jobName)}/build";

        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        using var resp = await SendWithCrumbAsync(req, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var location = resp.Headers.Location
            ?? throw new InvalidOperationException("Jenkins did not return a Location header for the queued build.");
        // Location is typically: <jenkinsUrl>/queue/item/<id>/
        var segments = location.AbsolutePath.TrimEnd('/').Split('/');
        if (segments.Length < 2 || !long.TryParse(segments[^1], out var queueId))
        {
            throw new InvalidOperationException($"Unexpected queue Location: {location}");
        }
        return queueId;
    }

    public async Task<QueueItem> GetQueueItemAsync(long queueId, CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<QueueItem>($"queue/item/{queueId}/api/json", cancellationToken);
    }

    public async Task<int> WaitForBuildToStartAsync(
        long queueId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? DefaultPollInterval;
        using var timer = NewTimeoutCts(timeout, cancellationToken);

        while (true)
        {
            timer.Token.ThrowIfCancellationRequested();
            var item = await GetQueueItemAsync(queueId, timer.Token);
            if (item.Cancelled)
            {
                throw new InvalidOperationException($"Queue item {queueId} was cancelled before starting (reason: {item.Why ?? "n/a"}).");
            }
            if (item.Executable is { Number: > 0 })
            {
                return item.Executable.Number;
            }
            await Task.Delay(interval, timer.Token);
        }
    }

    public async Task<Build> GetBuildAsync(string jobName, int buildNumber, CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<Build>($"{JobPath(jobName)}/{buildNumber}/api/json", cancellationToken);
    }

    public async Task<Build> GetLastBuildAsync(string jobName, CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<Build>($"{JobPath(jobName)}/lastBuild/api/json", cancellationToken);
    }

    public async Task<Build> WaitForBuildToFinishAsync(
        string jobName,
        int buildNumber,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? DefaultPollInterval;
        using var timer = NewTimeoutCts(timeout, cancellationToken);

        while (true)
        {
            timer.Token.ThrowIfCancellationRequested();
            var build = await GetBuildAsync(jobName, buildNumber, timer.Token);
            if (!build.Building)
            {
                return build;
            }
            await Task.Delay(interval, timer.Token);
        }
    }

    public async Task StopBuildAsync(string jobName, int buildNumber, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{JobPath(jobName)}/{buildNumber}/stop");
        using var resp = await SendWithCrumbAsync(req, cancellationToken);
        // Jenkins returns 302 even on success — treat anything < 400 as ok.
        if ((int)resp.StatusCode >= 400)
        {
            resp.EnsureSuccessStatusCode();
        }
    }

    public async Task<string> GetConsoleLogAsync(string jobName, int buildNumber, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.GetAsync($"{JobPath(jobName)}/{buildNumber}/consoleText", cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<byte[]> GetArtifactAsync(
        string jobName,
        int buildNumber,
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        var path = $"{JobPath(jobName)}/{buildNumber}/artifact/{artifactPath.TrimStart('/')}";
        using var resp = await _http.GetAsync(path, cancellationToken);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<Build> RunBuildAsync(
        string jobName,
        IDictionary<string, string>? parameters = null,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var queueId = await StartBuildAsync(jobName, parameters, cancellationToken);
        var buildNumber = await WaitForBuildToStartAsync(queueId, pollInterval, timeout, cancellationToken);
        return await WaitForBuildToFinishAsync(jobName, buildNumber, pollInterval, timeout, cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
        _crumbLock.Dispose();
    }

    // --- Internals ---

    private static string JobPath(string jobName)
    {
        // Jenkins folder convention: "folderA/folderB/jobName" -> "/job/folderA/job/folderB/job/jobName"
        var parts = jobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "job/" + string.Join("/job/", parts);
    }

    private static string BuildQuery(IDictionary<string, string> parameters)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var kv in parameters)
        {
            if (!first) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value ?? string.Empty));
            first = false;
        }
        return sb.ToString();
    }

    private static CancellationTokenSource NewTimeoutCts(TimeSpan? timeout, CancellationToken outer)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(timeout ?? DefaultTimeout);
        return cts;
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        var result = await _http.GetFromJsonAsync<T>(path, JsonOpts, ct);
        return result ?? throw new InvalidOperationException($"Jenkins returned empty body for {path}");
    }

    /// <summary>
    /// Sends a POST and transparently attaches a CSRF crumb if Jenkins requires one. The crumb is
    /// fetched lazily on first POST and cached for the lifetime of this client. Cluster-restart or
    /// crumb-rotation will surface as a 403 on the next POST — recreate the client to recover.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCrumbAsync(HttpRequestMessage req, CancellationToken ct)
    {
        await EnsureCrumbAsync(ct);
        if (_crumb is { } c)
        {
            req.Headers.TryAddWithoutValidation(c.Field, c.Value);
        }
        return await _http.SendAsync(req, ct);
    }

    private async Task EnsureCrumbAsync(CancellationToken ct)
    {
        if (_crumbFetched) return;
        await _crumbLock.WaitAsync(ct);
        try
        {
            if (_crumbFetched) return;
            try
            {
                using var resp = await _http.GetAsync("crumbIssuer/api/json", ct);
                if (resp.StatusCode == HttpStatusCode.NotFound)
                {
                    // CSRF disabled on this Jenkins; nothing to send.
                    _crumb = null;
                }
                else
                {
                    resp.EnsureSuccessStatusCode();
                    var doc = await resp.Content.ReadFromJsonAsync<CrumbResponse>(JsonOpts, ct);
                    if (doc is { Crumb: { Length: > 0 } crumb, CrumbRequestField: { Length: > 0 } field })
                    {
                        _crumb = (field, crumb);
                    }
                }
            }
            catch (HttpRequestException) when (!ct.IsCancellationRequested)
            {
                // Treat any crumb fetch failure as "no crumb" — Jenkins will return 403 on the actual
                // POST if it really needed one, and that error is more diagnostic than failing here.
                _crumb = null;
            }
            _crumbFetched = true;
        }
        finally
        {
            _crumbLock.Release();
        }
    }

    private sealed record CrumbResponse(string Crumb, string CrumbRequestField);
}
