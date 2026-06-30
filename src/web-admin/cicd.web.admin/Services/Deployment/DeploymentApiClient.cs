using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deployment.Contracts.Catalog;
using Deployment.Contracts.Mappings;
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
