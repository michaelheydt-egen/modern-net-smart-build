using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Deployment.Contracts.Catalog;
using Deployment.Contracts.Configuration;
using Deployment.Contracts.Deployments;
using Deployment.Contracts.Environments;
using Deployment.Contracts.Releases;

namespace Cicd.Web.Admin.Services.Deployment;

/// <summary>
/// Typed HttpClient over the Deployment.Api HTTP surface. All deserialization
/// uses the project's <see cref="JsonSerializerOptions"/> with camelCase + enum
/// string conversion enabled so the wire shape matches ASP.NET Core defaults.
///
/// 409 / 400 responses are surfaced as <see cref="DeploymentApiException"/>
/// with the server-provided detail so callers can show a useful Snackbar.
/// </summary>
public sealed class DeploymentApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public DeploymentApiClient(HttpClient http)
    {
        _http = http;
    }

    // ---- Services ----

    public async Task<IReadOnlyList<ServiceDto>> ListServicesAsync(
        bool? onlyActive = null, CancellationToken ct = default)
    {
        var url = onlyActive is null ? "api/deployment/services" : $"api/deployment/services?onlyActive={onlyActive}";
        var list = await _http.GetFromJsonAsync<List<ServiceDto>>(url, Json, ct).ConfigureAwait(false);
        return list ?? new List<ServiceDto>();
    }

    public async Task<ServiceDto?> GetServiceAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/deployment/services/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ServiceDto>(Json, ct).ConfigureAwait(false);
    }

    public Task<ServiceDto> RegisterServiceAsync(RegisterServiceRequest body, CancellationToken ct = default)
        => PostJsonAsync<RegisterServiceRequest, ServiceDto>("api/deployment/services", body, ct);

    public Task RenameServiceAsync(Guid id, RenameServiceRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/services/{id}/rename", body, ct);

    public Task UpdateServiceRepositoryInfoAsync(Guid id, UpdateServiceRepositoryInfoRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/services/{id}/repository", body, ct);

    public Task DeactivateServiceAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/deployment/services/{id}/deactivate", ct);

    public Task ReactivateServiceAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/deployment/services/{id}/reactivate", ct);

    // ---- Applications ----

    public async Task<IReadOnlyList<ApplicationDto>> ListApplicationsAsync(
        bool? onlyActive = null, CancellationToken ct = default)
    {
        var url = onlyActive is null ? "api/deployment/applications" : $"api/deployment/applications?onlyActive={onlyActive}";
        var list = await _http.GetFromJsonAsync<List<ApplicationDto>>(url, Json, ct).ConfigureAwait(false);
        return list ?? new List<ApplicationDto>();
    }

    public async Task<ApplicationDto?> GetApplicationAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/deployment/applications/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ApplicationDto>(Json, ct).ConfigureAwait(false);
    }

    public Task<ApplicationDto> RegisterApplicationAsync(RegisterApplicationRequest body, CancellationToken ct = default)
        => PostJsonAsync<RegisterApplicationRequest, ApplicationDto>("api/deployment/applications", body, ct);

    public Task RenameApplicationAsync(Guid id, RenameApplicationRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/applications/{id}/rename", body, ct);

    public Task ChangeApplicationDescriptionAsync(Guid id, ChangeApplicationDescriptionRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/applications/{id}/description", body, ct);

    public Task DeactivateApplicationAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/deployment/applications/{id}/deactivate", ct);

    public Task ReactivateApplicationAsync(Guid id, CancellationToken ct = default)
        => PostAsync($"api/deployment/applications/{id}/reactivate", ct);

    public Task AddApplicationMemberAsync(Guid id, AddApplicationMemberRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/applications/{id}/services", body, ct);

    public Task UpdateApplicationMemberAsync(Guid id, Guid serviceId, UpdateApplicationMemberRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/applications/{id}/services/{serviceId}", body, ct);

    public async Task RemoveApplicationMemberAsync(Guid id, Guid serviceId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/deployment/applications/{id}/services/{serviceId}", ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    // ---- Releases ----

    public async Task<IReadOnlyList<ReleaseDto>> ListReleasesAsync(Guid deployableUnitId, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<ReleaseDto>>(
            $"api/deployment/releases?deployableUnitId={deployableUnitId}", Json, ct).ConfigureAwait(false);
        return list ?? new List<ReleaseDto>();
    }

    public async Task<ReleaseDto?> GetReleaseAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/deployment/releases/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ReleaseDto>(Json, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReleaseStatusChangeDto>> GetReleaseStatusHistoryAsync(Guid id, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<ReleaseStatusChangeDto>>(
            $"api/deployment/releases/{id}/status-history", Json, ct).ConfigureAwait(false);
        return list ?? new List<ReleaseStatusChangeDto>();
    }

    public async Task<Guid> PublishReleaseAsync(PublishReleaseRequest body, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/deployment/releases", body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        var payload = await resp.Content.ReadFromJsonAsync<PublishReleaseResponse>(Json, ct).ConfigureAwait(false);
        return payload?.Id ?? throw new DeploymentApiException(resp.StatusCode, "Empty publish response.");
    }

    public Task AttachProvenanceAsync(Guid id, AttachProvenanceRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/releases/{id}/provenance", body, ct);

    public Task ChangeReleaseStatusAsync(Guid id, ChangeReleaseStatusRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/releases/{id}/status", body, ct);

    public Task AddCompositionEntryAsync(Guid id, AddCompositionEntryRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/releases/{id}/compositions", body, ct);

    public Task UpdateCompositionEntryAsync(Guid id, Guid serviceId, UpdateCompositionEntryRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/releases/{id}/compositions/{serviceId}", body, ct);

    public async Task RemoveCompositionEntryAsync(Guid id, Guid serviceId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/deployment/releases/{id}/compositions/{serviceId}", ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    private sealed record PublishReleaseResponse(Guid Id);

    // ---- Environments ----

    public async Task<IReadOnlyList<EnvironmentDto>> ListEnvironmentsAsync(CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<EnvironmentDto>>(
            "api/deployment/environments", Json, ct).ConfigureAwait(false);
        return list ?? new List<EnvironmentDto>();
    }

    public async Task<EnvironmentDto?> GetEnvironmentAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/deployment/environments/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EnvironmentDto>(Json, ct).ConfigureAwait(false);
    }

    public Task<EnvironmentDto> RegisterEnvironmentAsync(RegisterEnvironmentRequest body, CancellationToken ct = default)
        => PostJsonAsync<RegisterEnvironmentRequest, EnvironmentDto>("api/deployment/environments", body, ct);

    public Task RenameEnvironmentAsync(Guid id, RenameEnvironmentRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/rename", body, ct);

    public Task ChangePromotionRankAsync(Guid id, ChangePromotionRankRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/promotion-rank", body, ct);

    public Task SetApprovalRequirementAsync(Guid id, SetApprovalRequirementRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/approval-requirement", body, ct);

    public Task SetProductionFlagAsync(Guid id, SetProductionFlagRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/production-flag", body, ct);

    public Task AddTargetAsync(Guid id, AddTargetRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/targets", body, ct);

    public Task UpdateTargetAsync(Guid id, Guid targetId, UpdateTargetRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/targets/{targetId}", body, ct);

    public async Task RemoveTargetAsync(Guid id, Guid targetId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/deployment/environments/{id}/targets/{targetId}", ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    public Task ScheduleFreezeWindowAsync(Guid id, ScheduleFreezeWindowRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/environments/{id}/freeze-windows", body, ct);

    public async Task CancelFreezeWindowAsync(Guid id, Guid freezeWindowId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/deployment/environments/{id}/freeze-windows/{freezeWindowId}", ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    // ---- Configuration ----

    public async Task<IReadOnlyList<ConfigurationSettingDto>> ListConfigurationSettingsAsync(
        Guid deployableUnitId, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<ConfigurationSettingDto>>(
            $"api/deployment/configuration/settings?deployableUnitId={deployableUnitId}", Json, ct).ConfigureAwait(false);
        return list ?? new List<ConfigurationSettingDto>();
    }

    public async Task<ConfigurationSettingDto?> GetConfigurationSettingAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/deployment/configuration/settings/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ConfigurationSettingDto>(Json, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConfigurationSettingHistoryDto>> GetConfigurationSettingHistoryAsync(
        Guid id, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<ConfigurationSettingHistoryDto>>(
            $"api/deployment/configuration/settings/{id}/history", Json, ct).ConfigureAwait(false);
        return list ?? new List<ConfigurationSettingHistoryDto>();
    }

    public Task CreateConfigurationSettingAsync(CreateConfigurationSettingRequest body, CancellationToken ct = default)
        => PostJsonAsync("api/deployment/configuration/settings", body, ct);

    public Task UpdateConfigurationSettingAsync(Guid id, UpdateConfigurationSettingRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/configuration/settings/{id}", body, ct);

    public Task DeleteConfigurationSettingAsync(Guid id, DeleteConfigurationSettingRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/configuration/settings/{id}/delete", body, ct);

    // ---- Deployments ----

    public async Task<IReadOnlyList<DeploymentSummaryDto>> ListDeploymentsAsync(
        Guid? environmentId = null,
        DeploymentStatusDto? status = null,
        Guid? releaseId = null,
        bool onlyParents = true,
        int take = 100,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"onlyParents={onlyParents}", $"take={take}" };
        if (environmentId is { } eid) qs.Add($"environmentId={eid}");
        if (releaseId is { } rid) qs.Add($"releaseId={rid}");
        if (status is { } s) qs.Add($"status={s}");
        var url = $"api/deployment/deployments?{string.Join("&", qs)}";
        var list = await _http.GetFromJsonAsync<List<DeploymentSummaryDto>>(url, Json, ct).ConfigureAwait(false);
        return list ?? new List<DeploymentSummaryDto>();
    }

    public async Task<DeploymentDetailDto?> GetDeploymentAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/deployment/deployments/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<DeploymentDetailDto>(Json, ct).ConfigureAwait(false);
    }

    public async Task<StartedDeploymentDto> StartDeploymentAsync(StartDeploymentRequest body, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync("api/deployment/deployments", body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        var payload = await resp.Content.ReadFromJsonAsync<StartedDeploymentDto>(Json, ct).ConfigureAwait(false);
        return payload ?? throw new DeploymentApiException(resp.StatusCode, "Empty StartDeployment response.");
    }

    public Task ApproveDeploymentAsync(Guid id, ApproveDeploymentRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/deployments/{id}/approve", body, ct);

    public Task CancelDeploymentAsync(Guid id, CancelDeploymentRequest body, CancellationToken ct = default)
        => PostJsonAsync($"api/deployment/deployments/{id}/cancel", body, ct);

    public async Task<IReadOnlyList<EffectiveVersionRow>> GetEffectiveVersionsAsync(
        Guid applicationId, Guid environmentId, CancellationToken ct = default)
    {
        var url = $"api/deployment/dashboards/effective-versions?applicationId={applicationId}&environmentId={environmentId}";
        var list = await _http.GetFromJsonAsync<List<EffectiveVersionRow>>(url, Json, ct).ConfigureAwait(false);
        return list ?? new List<EffectiveVersionRow>();
    }

    // ---- Plumbing ----

    private async Task PostAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task PostJsonAsync<TBody>(string url, TBody body, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task<TResp> PostJsonAsync<TBody, TResp>(string url, TBody body, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        var parsed = await resp.Content.ReadFromJsonAsync<TResp>(Json, ct).ConfigureAwait(false);
        return parsed ?? throw new DeploymentApiException(resp.StatusCode, $"Empty body from {url}");
    }

    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new DeploymentApiException(resp.StatusCode, ExtractMessage(body) ?? resp.ReasonPhrase ?? "Unknown error");
    }

    private static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            // ProblemDetails uses "detail"; ValidationProblem uses "title" + "errors".
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString();
            if (doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        catch (JsonException) { /* fall through */ }
        return body.Length > 500 ? body[..500] : body;
    }
}

public sealed class DeploymentApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public DeploymentApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
