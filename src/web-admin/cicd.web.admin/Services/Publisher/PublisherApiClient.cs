using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Publisher.Contracts.Channels;
using Publisher.Contracts.Containers;
using Publisher.Contracts.Promotions;
using Publisher.Contracts.Registries;
using Publisher.Contracts.Rules;

namespace Cicd.Web.Admin.Services.Publisher;

/// <summary>
/// Outcome of a promote/publish request: a <c>PromotionId</c> when a push was queued, or null
/// (with an <c>Outcome</c> like <c>already-promoted</c>) when it was a no-op.
/// </summary>
public sealed record PromoteResponse(Guid? PromotionId, string? Outcome);

/// <summary>
/// Typed HttpClient over the Publisher.Api HTTP surface (registries, rules, container inventory,
/// publishable channels, promotions). Mirrors the Deployment/Jenkins API clients: web-defaults
/// JSON with string enums, and 4xx/5xx surfaced as <see cref="PublisherApiException"/> carrying
/// the server's problem-detail message.
/// </summary>
public sealed class PublisherApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public PublisherApiClient(HttpClient http) => _http = http;

    // ---- Registries ----

    public async Task<IReadOnlyList<RemoteRegistryDto>> ListRegistriesAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<RemoteRegistryDto>>("api/publisher/registries", Json, ct).ConfigureAwait(false)
           ?? new List<RemoteRegistryDto>();

    public async Task<RemoteRegistryDto?> GetRegistryAsync(Guid id, CancellationToken ct = default)
        => await GetOrNullAsync<RemoteRegistryDto>($"api/publisher/registries/{id}", ct).ConfigureAwait(false);

    public Task<RemoteRegistryDto> CreateRegistryAsync(CreateRegistryRequest body, CancellationToken ct = default)
        => PostJsonAsync<CreateRegistryRequest, RemoteRegistryDto>("api/publisher/registries", body, ct);

    public Task UpdateRegistryAsync(Guid id, UpdateRegistryRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/publisher/registries/{id}", body, ct);

    public Task SetDefaultRegistryAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/registries/{id}/default", ct);

    public Task EnableRegistryAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/registries/{id}/enable", ct);

    public Task DisableRegistryAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/registries/{id}/disable", ct);

    public Task DeleteRegistryAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"api/publisher/registries/{id}", ct);

    // ---- Rules ----

    public async Task<IReadOnlyList<AutomationRuleDto>> ListRulesAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<AutomationRuleDto>>("api/publisher/rules", Json, ct).ConfigureAwait(false)
           ?? new List<AutomationRuleDto>();

    public async Task<AutomationRuleDto?> GetRuleAsync(Guid id, CancellationToken ct = default)
        => await GetOrNullAsync<AutomationRuleDto>($"api/publisher/rules/{id}", ct).ConfigureAwait(false);

    public Task<AutomationRuleDto> CreateRuleAsync(CreateRuleRequest body, CancellationToken ct = default)
        => PostJsonAsync<CreateRuleRequest, AutomationRuleDto>("api/publisher/rules", body, ct);

    public Task UpdateRuleAsync(Guid id, UpdateRuleRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/publisher/rules/{id}", body, ct);

    public Task EnableRuleAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/rules/{id}/enable", ct);

    public Task DisableRuleAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/rules/{id}/disable", ct);

    public Task DeleteRuleAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"api/publisher/rules/{id}", ct);

    // ---- Containers (inventory) ----

    public async Task<IReadOnlyList<PublishableContainerDto>> ListContainersAsync(
        Guid? repositoryId = null, string? containerName = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (repositoryId is { } rid) query.Add($"repositoryId={rid}");
        if (!string.IsNullOrWhiteSpace(containerName)) query.Add($"containerName={Uri.EscapeDataString(containerName)}");
        var url = "api/publisher/containers" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return await _http.GetFromJsonAsync<List<PublishableContainerDto>>(url, Json, ct).ConfigureAwait(false)
               ?? new List<PublishableContainerDto>();
    }

    public Task<PublishableContainerDto> AddContainerAsync(AddContainerRequest body, CancellationToken ct = default)
        => PostJsonAsync<AddContainerRequest, PublishableContainerDto>("api/publisher/containers", body, ct);

    public Task ActivateContainerAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/containers/{id}/activate", ct);

    public Task DeactivateContainerAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/publisher/containers/{id}/deactivate", ct);

    public Task DeleteContainerAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"api/publisher/containers/{id}", ct);

    // ---- Channels ----

    public async Task<IReadOnlyList<PublishChannelDto>> ListChannelsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<PublishChannelDto>>("api/publisher/channels", Json, ct).ConfigureAwait(false)
           ?? new List<PublishChannelDto>();

    public Task TagPublishableAsync(string channelName, TagPublishableRequest body, CancellationToken ct = default)
        => PutJsonAsync($"api/publisher/channels/{Uri.EscapeDataString(channelName)}", body, ct);

    public Task<PromoteResponse> PublishChannelAsync(string channelName, PromoteContainerRequest body, CancellationToken ct = default)
        => PostJsonAsync<PromoteContainerRequest, PromoteResponse>(
            $"api/publisher/channels/{Uri.EscapeDataString(channelName)}/publish", body, ct);

    // ---- Promotions ----

    public async Task<IReadOnlyList<PromotionDto>> ListPromotionsAsync(
        Guid? containerId = null, Guid? registryId = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (containerId is { } cid) query.Add($"containerId={cid}");
        if (registryId is { } rid) query.Add($"registryId={rid}");
        var url = "api/publisher/promotions" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        return await _http.GetFromJsonAsync<List<PromotionDto>>(url, Json, ct).ConfigureAwait(false)
               ?? new List<PromotionDto>();
    }

    public Task<PromoteResponse> PromoteContainerAsync(Guid containerId, PromoteContainerRequest body, CancellationToken ct = default)
        => PostJsonAsync<PromoteContainerRequest, PromoteResponse>(
            $"api/publisher/containers/{containerId}/promote", body, ct);

    // ---- Plumbing ----

    private async Task<T?> GetOrNullAsync<T>(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return default;
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        return await resp.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
    }

    private async Task PostAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task<TResp> PostJsonAsync<TBody, TResp>(string url, TBody body, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        var parsed = await resp.Content.ReadFromJsonAsync<TResp>(Json, ct).ConfigureAwait(false);
        return parsed ?? throw new PublisherApiException(resp.StatusCode, $"Empty body from {url}");
    }

    private async Task PutJsonAsync<TBody>(string url, TBody body, CancellationToken ct)
    {
        using var resp = await _http.PutAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task DeleteAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.DeleteAsync(url, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new PublisherApiException(resp.StatusCode, ExtractMessage(body) ?? resp.ReasonPhrase ?? "Unknown error");
    }

    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString();
            if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        catch (JsonException) { /* fall through */ }
        return body.Length > 500 ? body[..500] : body;
    }
}

public sealed class PublisherApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public PublisherApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
}
