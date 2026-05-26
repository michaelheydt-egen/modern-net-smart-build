using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Util.Commands.Nexus;

public sealed class NexusClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;

    public NexusClient(NexusOptions options)
    {
        _http = new HttpClient { BaseAddress = new Uri(options.Url.TrimEnd('/') + "/") };
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.User}:{options.Password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async IAsyncEnumerable<NexusComponent> GetComponentsAsync(
        string repository,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? token = null;
        do
        {
            var url = $"service/rest/v1/components?repository={Uri.EscapeDataString(repository)}";
            if (token is not null)
            {
                url += $"&continuationToken={Uri.EscapeDataString(token)}";
            }

            var page = await _http.GetFromJsonAsync<NexusComponentsPage>(url, JsonOpts, cancellationToken)
                ?? throw new InvalidOperationException("Empty response from Nexus components API");

            foreach (var item in page.Items)
            {
                yield return item;
            }

            token = page.ContinuationToken;
        }
        while (token is not null);
    }

    public async Task DeleteComponentAsync(string id, CancellationToken cancellationToken = default)
    {
        using var resp = await _http.DeleteAsync(
            $"service/rest/v1/components/{Uri.EscapeDataString(id)}",
            cancellationToken);
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
