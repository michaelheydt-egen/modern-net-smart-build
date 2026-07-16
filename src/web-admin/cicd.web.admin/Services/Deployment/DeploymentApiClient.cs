using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deployment.Contracts.AspireApps;
using Deployment.Contracts.Catalog;
using Deployment.Contracts.Kubernetes;
using Deployment.Contracts.Mappings;
using Deployment.Contracts.Previews;
using Deployment.Contracts.Runs;

namespace Cicd.Web.Admin.Services.Deployment;

/// <summary>Outcome of a manual deploy trigger.</summary>
public sealed record DeployResponse(Guid? RunId, string? Outcome);

/// <summary>Typed HttpClient over Deployment.Api (services, environments, mappings, runs, inventory).</summary>
public sealed class DeploymentApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;
    public DeploymentApiClient(HttpClient http) => _http = http;

    /// <summary>Base address of deployment-api — used to build the SignalR hub URL for completion toasts.</summary>
    public Uri BaseAddress => _http.BaseAddress!;

    // ---- Services ----
    public async Task<IReadOnlyList<ServiceDto>> ListServicesAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<ServiceDto>>("api/deployment/services", Json, ct).ConfigureAwait(false) ?? new();
    public Task<ServiceDto> CreateServiceAsync(CreateServiceRequest body, CancellationToken ct = default)
        => PostJsonAsync<CreateServiceRequest, ServiceDto>("api/deployment/services", body, ct);
    public Task UpdateServiceAsync(Guid id, UpdateServiceRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/deployment/services/{id}", body, ct);
    public Task ActivateServiceAsync(Guid id, CancellationToken ct = default) => PostAsync($"api/deployment/services/{id}/activate", ct);
    public Task DeactivateServiceAsync(Guid id, CancellationToken ct = default) => PostAsync($"api/deployment/services/{id}/deactivate", ct);
    public Task DeleteServiceAsync(Guid id, CancellationToken ct = default) => DeleteAsync($"api/deployment/services/{id}", ct);

    // ---- Environments ----
    public async Task<IReadOnlyList<EnvironmentDto>> ListEnvironmentsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<EnvironmentDto>>("api/deployment/environments", Json, ct).ConfigureAwait(false) ?? new();
    public Task<EnvironmentDto> CreateEnvironmentAsync(CreateEnvironmentRequest body, CancellationToken ct = default)
        => PostJsonAsync<CreateEnvironmentRequest, EnvironmentDto>("api/deployment/environments", body, ct);
    public Task UpdateEnvironmentAsync(Guid id, UpdateEnvironmentRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/deployment/environments/{id}", body, ct);
    public Task DeleteEnvironmentAsync(Guid id, CancellationToken ct = default) => DeleteAsync($"api/deployment/environments/{id}", ct);

    // ---- Mappings ----
    public async Task<IReadOnlyList<DeploymentMappingDto>> ListMappingsAsync(Guid? serviceId = null, CancellationToken ct = default)
    {
        var url = serviceId is { } s ? $"api/deployment/mappings?serviceId={s}" : "api/deployment/mappings";
        return await _http.GetFromJsonAsync<List<DeploymentMappingDto>>(url, Json, ct).ConfigureAwait(false) ?? new();
    }
    public Task CreateMappingAsync(CreateMappingRequest body, CancellationToken ct = default)
        => PostJsonNoBodyAsync("api/deployment/mappings", body, ct);
    public Task UpdateMappingAsync(Guid id, UpdateMappingRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/deployment/mappings/{id}", body, ct);
    public Task SetAutoDeployAsync(Guid id, bool autoDeploy, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/mappings/{id}/auto", new SetAutoDeployRequest(autoDeploy), ct);
    public Task DeleteMappingAsync(Guid id, CancellationToken ct = default) => DeleteAsync($"api/deployment/mappings/{id}", ct);
    public Task<DeployResponse> DeployAsync(Guid mappingId, TriggerDeploymentRequest body, CancellationToken ct = default)
        => PostJsonAsync<TriggerDeploymentRequest, DeployResponse>($"api/deployment/mappings/{mappingId}/deploy", body, ct);

    // ---- Runs + inventory ----
    public Task PromoteRunAsync(Guid runId, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/runs/{runId}/promote", new PromoteDeploymentRunRequest("ui"), ct);
    public Task RollbackRunAsync(Guid runId, string? reason = null, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/runs/{runId}/rollback", new RollbackDeploymentRunRequest("ui", reason), ct);

    public async Task<IReadOnlyList<DeploymentRunDto>> ListRunsAsync(Guid? serviceId = null, Guid? mappingId = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (serviceId is { } s) q.Add($"serviceId={s}");
        if (mappingId is { } m) q.Add($"mappingId={m}");
        var url = "api/deployment/runs" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return await _http.GetFromJsonAsync<List<DeploymentRunDto>>(url, Json, ct).ConfigureAwait(false) ?? new();
    }
    public async Task<IReadOnlyList<KnownContainerDto>> ListKnownContainersAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<KnownContainerDto>>("api/deployment/containers", Json, ct).ConfigureAwait(false) ?? new();
    public Task<KnownContainerDto> AddKnownContainerAsync(AddKnownContainerRequest body, CancellationToken ct = default)
        => PostJsonAsync<AddKnownContainerRequest, KnownContainerDto>("api/deployment/containers", body, ct);

    // ---- Aspire applications (Aspir8 → Kubernetes) ----
    public async Task<IReadOnlyList<AspireApplicationDto>> ListAspireAppsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<AspireApplicationDto>>("api/deployment/aspire-apps", Json, ct).ConfigureAwait(false) ?? new();
    public Task<AspireApplicationDto> CreateAspireAppAsync(CreateAspireApplicationRequest body, CancellationToken ct = default)
        => PostJsonAsync<CreateAspireApplicationRequest, AspireApplicationDto>("api/deployment/aspire-apps", body, ct);
    public Task UpdateAspireAppAsync(Guid id, UpdateAspireApplicationRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/deployment/aspire-apps/{id}", body, ct);
    public Task DeleteAspireAppAsync(Guid id, CancellationToken ct = default) => DeleteAsync($"api/deployment/aspire-apps/{id}", ct);
    public Task SetAspireAutoDeployAsync(Guid id, bool autoDeploy, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/aspire-apps/{id}/auto-deploy", new SetAspireAutoDeployRequest(autoDeploy), ct);
    public Task<DeployResponse> DeployAspireAppAsync(Guid id, TriggerAspireDeploymentRequest body, CancellationToken ct = default)
        => PostJsonAsync<TriggerAspireDeploymentRequest, DeployResponse>($"api/deployment/aspire-apps/{id}/deploy", body, ct);
    public Task<DeployResponse> RollbackAspireAppAsync(Guid id, Guid targetRunId, CancellationToken ct = default)
        => PostJsonAsync<RollbackAspireDeploymentRequest, DeployResponse>($"api/deployment/aspire-apps/{id}/rollback", new RollbackAspireDeploymentRequest(targetRunId, "ui"), ct);
    public Task<DeployResponse> PromoteAspireAppAsync(Guid id, Guid targetEnvironmentId, CancellationToken ct = default)
        => PostJsonAsync<PromoteAspireDeploymentRequest, DeployResponse>($"api/deployment/aspire-apps/{id}/promote", new PromoteAspireDeploymentRequest(targetEnvironmentId, "ui"), ct);
    public Task ApproveAspireRunAsync(Guid runId, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/aspire-runs/{runId}/approve", new ApproveAspireRunRequest("ui"), ct);
    public Task RejectAspireRunAsync(Guid runId, string? reason = null, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/aspire-runs/{runId}/reject", new RejectAspireRunRequest("ui", reason), ct);
    public Task PromoteAspireRunAsync(Guid runId, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/aspire-runs/{runId}/promote", new PromoteAspireRunRequest("ui"), ct);
    public Task RollbackAspireRunAsync(Guid runId, string? reason = null, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/aspire-runs/{runId}/rollback", new RollbackAspireRunRequest("ui", reason), ct);
    public Task<AspireAppStatusDto?> GetAspireAppStatusAsync(Guid id, CancellationToken ct = default)
        => _http.GetFromJsonAsync<AspireAppStatusDto>($"api/deployment/aspire-apps/{id}/status", Json, ct);

    // ---- Preview environments ----
    public async Task<IReadOnlyList<PreviewEnvironmentDto>> ListPreviewsAsync(bool includeTornDown = false, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<PreviewEnvironmentDto>>($"api/deployment/previews?includeTornDown={includeTornDown.ToString().ToLowerInvariant()}", Json, ct).ConfigureAwait(false) ?? new();
    public Task CreatePreviewAsync(CreatePreviewEnvironmentRequest body, CancellationToken ct = default)
        => PostJsonNoBodyAsync("api/deployment/previews", body, ct);
    public Task TeardownPreviewAsync(Guid id, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/previews/{id}/teardown", new { }, ct);
    public async Task<IReadOnlyList<AspireApplicationRunDto>> ListAspireRunsAsync(Guid? applicationId = null, CancellationToken ct = default)
    {
        var url = applicationId is { } a ? $"api/deployment/aspire-runs?applicationId={a}" : "api/deployment/aspire-runs";
        return await _http.GetFromJsonAsync<List<AspireApplicationRunDto>>(url, Json, ct).ConfigureAwait(false) ?? new();
    }

    // ---- Kubernetes (read-only cluster browsing) ----
    public async Task<IReadOnlyList<K8sContextDto>> ListK8sContextsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<K8sContextDto>>("api/deployment/k8s/contexts", Json, ct).ConfigureAwait(false) ?? new();
    public async Task<IReadOnlyList<K8sNamespaceDto>> ListK8sNamespacesAsync(string? context = null, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<K8sNamespaceDto>>($"api/deployment/k8s/namespaces{ContextQuery(context)}", Json, ct).ConfigureAwait(false) ?? new();
    public Task<K8sNamespaceDetailDto?> GetK8sNamespaceAsync(string ns, string? context = null, CancellationToken ct = default)
        => _http.GetFromJsonAsync<K8sNamespaceDetailDto>($"api/deployment/k8s/namespaces/{Uri.EscapeDataString(ns)}{ContextQuery(context)}", Json, ct);
    public Task<PodLogDto?> GetK8sPodLogAsync(string ns, string pod, string? container = null, int tail = 500, string? context = null, CancellationToken ct = default)
    {
        var q = $"?tail={tail}";
        if (!string.IsNullOrWhiteSpace(container)) q += $"&container={Uri.EscapeDataString(container)}";
        if (!string.IsNullOrWhiteSpace(context)) q += $"&context={Uri.EscapeDataString(context)}";
        return _http.GetFromJsonAsync<PodLogDto>($"api/deployment/k8s/namespaces/{Uri.EscapeDataString(ns)}/pods/{Uri.EscapeDataString(pod)}/logs{q}", Json, ct);
    }
    private static string ContextQuery(string? context)
        => string.IsNullOrWhiteSpace(context) ? "" : $"?context={Uri.EscapeDataString(context)}";

    public Task RestartK8sDeploymentAsync(string ns, string name, string? context = null, CancellationToken ct = default)
        => PostAsync($"api/deployment/k8s/namespaces/{Uri.EscapeDataString(ns)}/deployments/{Uri.EscapeDataString(name)}/restart{ContextQuery(context)}", ct);
    public Task ScaleK8sDeploymentAsync(string ns, string name, int replicas, string? context = null, CancellationToken ct = default)
        => PostJsonNoBodyAsync($"api/deployment/k8s/namespaces/{Uri.EscapeDataString(ns)}/deployments/{Uri.EscapeDataString(name)}/scale{ContextQuery(context)}", new ScaleDeploymentRequest(replicas), ct);
    public Task DeleteK8sPodAsync(string ns, string pod, string? context = null, CancellationToken ct = default)
        => DeleteAsync($"api/deployment/k8s/namespaces/{Uri.EscapeDataString(ns)}/pods/{Uri.EscapeDataString(pod)}{ContextQuery(context)}", ct);

    // ---- Plumbing ----
    private async Task PostAsync(string url, CancellationToken ct)
    { using var r = await _http.PostAsync(url, null, ct).ConfigureAwait(false); await EnsureOk(r, ct).ConfigureAwait(false); }
    private async Task DeleteAsync(string url, CancellationToken ct)
    { using var r = await _http.DeleteAsync(url, ct).ConfigureAwait(false); await EnsureOk(r, ct).ConfigureAwait(false); }
    private async Task PutJsonAsync<TBody>(string url, TBody body, CancellationToken ct)
    { using var r = await _http.PutAsJsonAsync(url, body, Json, ct).ConfigureAwait(false); await EnsureOk(r, ct).ConfigureAwait(false); }
    private async Task PostJsonNoBodyAsync<TBody>(string url, TBody body, CancellationToken ct)
    { using var r = await _http.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false); await EnsureOk(r, ct).ConfigureAwait(false); }
    private async Task<TResp> PostJsonAsync<TBody, TResp>(string url, TBody body, CancellationToken ct)
    {
        using var r = await _http.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureOk(r, ct).ConfigureAwait(false);
        return await r.Content.ReadFromJsonAsync<TResp>(Json, ct).ConfigureAwait(false)
            ?? throw new DeploymentApiException(r.StatusCode, $"Empty body from {url}");
    }
    private static async Task EnsureOk(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new DeploymentApiException(resp.StatusCode, Extract(body) ?? resp.ReasonPhrase ?? "Unknown error");
    }
    private static string? Extract(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) return d.GetString();
            if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String) return t.GetString();
        }
        catch (JsonException) { }
        return body.Length > 500 ? body[..500] : body;
    }
}

public sealed class DeploymentApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public DeploymentApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}
