using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jenkins.Contracts.Builds;
using Jenkins.Contracts.Handoffs;
using Jenkins.Contracts.Pipelines;
using Jenkins.Contracts.Repositories;

namespace Cicd.Web.Admin.Services.Ci;

/// <summary>
/// Typed HttpClient over the Jenkins CI service (Jenkins.Api). Mirrors
/// <c>DeploymentApiClient</c>: camelCase + enum-string JSON, and 4xx responses
/// surfaced as <see cref="JenkinsApiException"/> with the server detail.
/// </summary>
public sealed class JenkinsApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    public JenkinsApiClient(HttpClient http)
    {
        _http = http;
    }

    // ---- Repositories ----

    public async Task<IReadOnlyList<RepositoryDto>> ListRepositoriesAsync(bool? onlyActive = null, CancellationToken ct = default)
    {
        var url = onlyActive is null ? "api/jenkins/repositories" : $"api/jenkins/repositories?onlyActive={onlyActive}";
        var list = await _http.GetFromJsonAsync<List<RepositoryDto>>(url, Json, ct).ConfigureAwait(false);
        return list ?? new List<RepositoryDto>();
    }

    public async Task<RepositoryDto?> GetRepositoryAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/jenkins/repositories/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<RepositoryDto>(Json, ct).ConfigureAwait(false);
    }

    public Task<RepositoryDto> RegisterRepositoryAsync(RegisterRepositoryRequest body, CancellationToken ct = default)
        => PostJsonAsync<RegisterRepositoryRequest, RepositoryDto>("api/jenkins/repositories", body, ct);

    public Task<DeployableComponentDto> MapComponentAsync(Guid repositoryId, MapComponentRequest body, CancellationToken ct = default)
        => PostJsonAsync<MapComponentRequest, DeployableComponentDto>($"api/jenkins/repositories/{repositoryId}/components", body, ct);

    // ---- Builds ----

    public async Task<IReadOnlyList<BuildSummaryDto>> ListBuildsAsync(Guid repositoryId, int take = 50, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<BuildSummaryDto>>(
            $"api/jenkins/builds?repositoryId={repositoryId}&take={take}", Json, ct).ConfigureAwait(false);
        return list ?? new List<BuildSummaryDto>();
    }

    public async Task<BuildDetailDto?> GetBuildAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/jenkins/builds/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<BuildDetailDto>(Json, ct).ConfigureAwait(false);
    }

    // ---- Handoffs ----

    public async Task<IReadOnlyList<ContainerReleaseHandoffDto>> ListHandoffsByBuildAsync(Guid buildId, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<ContainerReleaseHandoffDto>>(
            $"api/jenkins/handoffs?buildId={buildId}", Json, ct).ConfigureAwait(false);
        return list ?? new List<ContainerReleaseHandoffDto>();
    }

    public async Task<ContainerReleaseHandoffDto?> GetHandoffAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/jenkins/handoffs/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ContainerReleaseHandoffDto>(Json, ct).ConfigureAwait(false);
    }

    public Task<ContainerReleaseHandoffDto> PromoteAsync(PromoteBuildRequest body, CancellationToken ct = default)
        => PostJsonAsync<PromoteBuildRequest, ContainerReleaseHandoffDto>("api/jenkins/handoffs", body, ct);

    // ---- Pipelines ----

    public async Task<IReadOnlyList<PipelineSummaryDto>> ListPipelinesAsync(CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<PipelineSummaryDto>>("api/jenkins/pipelines", Json, ct).ConfigureAwait(false);
        return list ?? new List<PipelineSummaryDto>();
    }

    public async Task<PipelineDto?> GetPipelineAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/jenkins/pipelines/{id}", ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<PipelineDto>(Json, ct).ConfigureAwait(false);
    }

    public Task<PipelineDto> CreatePipelineAsync(CreatePipelineRequest body, CancellationToken ct = default)
        => PostJsonAsync<CreatePipelineRequest, PipelineDto>("api/jenkins/pipelines", body, ct);

    public Task<PipelineDto> UpdatePipelineAsync(Guid id, UpdatePipelineRequest body, CancellationToken ct = default)
        => PostJsonAsync<UpdatePipelineRequest, PipelineDto>($"api/jenkins/pipelines/{id}", body, ct);

    public Task<PipelineDto> SetPipelineActiveAsync(Guid id, SetPipelineActiveRequest body, CancellationToken ct = default)
        => PostJsonAsync<SetPipelineActiveRequest, PipelineDto>($"api/jenkins/pipelines/{id}/active", body, ct);

    public async Task DeletePipelineAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"api/jenkins/pipelines/{id}", ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
    }

    public Task<PipelineDto> AddStageAsync(Guid id, AddStageRequest body, CancellationToken ct = default)
        => PostJsonAsync<AddStageRequest, PipelineDto>($"api/jenkins/pipelines/{id}/stages", body, ct);

    public Task<PipelineDto> UpdateStageAsync(Guid id, Guid stageId, UpdateStageRequest body, CancellationToken ct = default)
        => PostJsonAsync<UpdateStageRequest, PipelineDto>($"api/jenkins/pipelines/{id}/stages/{stageId}", body, ct);

    public async Task<PipelineDto> RemoveStageAsync(Guid id, Guid stageId, CancellationToken ct = default)
    {
        using var resp = await _http.DeleteAsync($"api/jenkins/pipelines/{id}/stages/{stageId}", ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        return (await resp.Content.ReadFromJsonAsync<PipelineDto>(Json, ct).ConfigureAwait(false))!;
    }

    public Task<PipelineDto> ReorderStagesAsync(Guid id, ReorderStagesRequest body, CancellationToken ct = default)
        => PostJsonAsync<ReorderStagesRequest, PipelineDto>($"api/jenkins/pipelines/{id}/stages/reorder", body, ct);

    // ---- Plumbing ----

    private async Task<TResp> PostJsonAsync<TBody, TResp>(string url, TBody body, CancellationToken ct)
    {
        using var resp = await _http.PostAsJsonAsync(url, body, Json, ct).ConfigureAwait(false);
        await EnsureOkAsync(resp, ct).ConfigureAwait(false);
        var parsed = await resp.Content.ReadFromJsonAsync<TResp>(Json, ct).ConfigureAwait(false);
        return parsed ?? throw new JenkinsApiException(resp.StatusCode, $"Empty body from {url}");
    }

    private static async Task EnsureOkAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new JenkinsApiException(resp.StatusCode, ExtractMessage(body) ?? resp.ReasonPhrase ?? "Unknown error");
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

public sealed class JenkinsApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public JenkinsApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
