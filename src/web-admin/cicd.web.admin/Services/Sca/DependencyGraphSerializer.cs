using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Canonical JSON round-trip for <see cref="DependencyGraph"/>. The shape produced
/// here is the format we'll persist (SQLite JSON column or Nexus raw artifact) —
/// keeping serialize / deserialize on one shared options instance prevents
/// silent format drift across writers + readers.
///
/// Domain names are preserved (Ref / From / To); presentation rename to D3 idioms
/// (id / source / target / links) happens at the JS boundary, not here.
/// </summary>
public static class DependencyGraphSerializer
{
    private static readonly JsonSerializerOptions SharedOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions PrettyOptions = new(SharedOptions)
    {
        WriteIndented = true,
    };

    /// <summary>Compact JSON suitable for storage / JS interop.</summary>
    public static string Serialize(DependencyGraph graph) =>
        JsonSerializer.Serialize(graph, SharedOptions);

    /// <summary>Pretty-printed JSON for ad-hoc inspection (logs, diffs, debug dumps).</summary>
    public static string SerializeIndented(DependencyGraph graph) =>
        JsonSerializer.Serialize(graph, PrettyOptions);

    /// <summary>
    /// Inverse of <see cref="Serialize"/>. Returns null on malformed JSON rather than
    /// throwing — same tolerance as <see cref="SbomReader.Parse"/> so a corrupt cached
    /// graph just regenerates from the SBOM rather than crashing the page.
    /// </summary>
    public static DependencyGraph? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DependencyGraph>(json, SharedOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
