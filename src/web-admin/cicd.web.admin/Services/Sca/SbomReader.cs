using System.Text.Json;

namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Parses bom-vex.json bytes into <see cref="SbomDocument"/>. Tolerant of CycloneDX
/// 1.4 vs 1.5 schema variance — most relevantly, the <c>metadata.tools</c> shape
/// changed from a flat array to an object with <c>components</c>/<c>services</c>
/// sub-arrays. Trivy 0.55 still emits the 1.4 shape inside what's nominally a 1.5
/// document; we accept either.
/// </summary>
public static class SbomReader
{
    public static SbomDocument? Parse(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes));
            return ParseDocument(doc.RootElement);
        }
        catch (JsonException)
        {
            // Corrupted SBOM — let the caller hide the panels rather than blow up.
            return null;
        }
    }

    private static SbomDocument ParseDocument(JsonElement root)
    {
        var (vulnsPresent, vulns) = ParseVulnerabilities(root);
        return new SbomDocument(
            BomFormat:                 TryGetString(root, "bomFormat"),
            SpecVersion:               TryGetString(root, "specVersion"),
            Metadata:                  TryGetObject(root, "metadata") is { } meta ? ParseMetadata(meta) : null,
            Components:                ParseComponents(root),
            Vulnerabilities:           vulns,
            HasVulnerabilitiesSection: vulnsPresent,
            Dependencies:              ParseDependencies(root));
    }

    // --- Metadata ---

    private static SbomMetadata ParseMetadata(JsonElement meta) => new(
        Timestamp:  TryGetString(meta, "timestamp"),
        Component:  TryGetObject(meta, "component") is { } c ? ParseRootComponent(c) : null,
        Tools:      ParseTools(meta),
        Properties: ParseProperties(meta));

    private static SbomComponent ParseRootComponent(JsonElement c) => new(
        Name:    TryGetString(c, "name"),
        Type:    TryGetString(c, "type"),
        Version: TryGetString(c, "version"),
        BomRef:  TryGetString(c, "bom-ref"));

    private static IReadOnlyList<SbomTool> ParseTools(JsonElement meta)
    {
        if (!meta.TryGetProperty("tools", out var tools)) return Array.Empty<SbomTool>();

        // CycloneDX 1.4 — tools is an array of {vendor, name, version}.
        if (tools.ValueKind == JsonValueKind.Array)
        {
            var list = new List<SbomTool>();
            foreach (var t in tools.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                list.Add(new SbomTool(
                    Vendor:  TryGetString(t, "vendor"),
                    Name:    TryGetString(t, "name"),
                    Version: TryGetString(t, "version")));
            }
            return list;
        }

        // CycloneDX 1.5 — tools is {components: [...], services: [...]}, each entry
        // a full component record. manufacturer (preferred) or publisher → vendor.
        if (tools.ValueKind == JsonValueKind.Object)
        {
            var list = new List<SbomTool>();
            if (tools.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in components.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.Object) continue;
                    list.Add(new SbomTool(
                        Vendor:  TryGetString(c, "manufacturer") ?? TryGetString(c, "publisher"),
                        Name:    TryGetString(c, "name"),
                        Version: TryGetString(c, "version")));
                }
            }
            return list;
        }

        return Array.Empty<SbomTool>();
    }

    private static IReadOnlyList<SbomProperty> ParseProperties(JsonElement meta)
    {
        if (!meta.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Array)
            return Array.Empty<SbomProperty>();

        var list = new List<SbomProperty>();
        foreach (var p in props.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var name = TryGetString(p, "name");
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new SbomProperty(name, TryGetString(p, "value")));
        }
        return list;
    }

    // --- Top-level components[] ---

    private static IReadOnlyList<SbomComponentEntry> ParseComponents(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<SbomComponentEntry>();

        var list = new List<SbomComponentEntry>();
        foreach (var c in arr.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object) continue;
            list.Add(new SbomComponentEntry(
                BomRef:   TryGetString(c, "bom-ref"),
                Name:     TryGetString(c, "name"),
                Version:  TryGetString(c, "version"),
                Type:     TryGetString(c, "type"),
                Purl:     TryGetString(c, "purl"),
                Licenses: ParseLicenses(c)));
        }
        return list;
    }

    /// <summary>
    /// CycloneDX licenses[] entries are union-shaped: each one is either
    /// <c>{license:{id:"..."}}</c>, <c>{license:{name:"..."}}</c>, or <c>{expression:"..."}</c>.
    /// We flatten all three into a display-string list — duplicates filtered with a Set.
    /// </summary>
    private static IReadOnlyList<string> ParseLicenses(JsonElement component)
    {
        if (!component.TryGetProperty("licenses", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            string? value = null;
            if (entry.TryGetProperty("expression", out var expr) && expr.ValueKind == JsonValueKind.String)
            {
                value = expr.GetString();
            }
            else if (entry.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object)
            {
                value = TryGetString(lic, "id") ?? TryGetString(lic, "name");
            }

            if (!string.IsNullOrEmpty(value) && seen.Add(value))
            {
                list.Add(value);
            }
        }
        return list;
    }

    // --- vulnerabilities[] (VEX extension) ---

    /// <summary>
    /// Returns <c>(present, items)</c>. <c>present=false</c> when the source had no
    /// <c>vulnerabilities</c> key at all (unenriched); <c>present=true</c> with an
    /// empty list means the scanner found nothing (clean). The UI uses the distinction.
    /// </summary>
    private static (bool Present, IReadOnlyList<SbomVulnerability> Items) ParseVulnerabilities(JsonElement root)
    {
        if (!root.TryGetProperty("vulnerabilities", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return (false, Array.Empty<SbomVulnerability>());

        var list = new List<SbomVulnerability>();
        foreach (var v in arr.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Object) continue;

            // Pull name + url out of source[] in one walk rather than twice.
            string? sourceName = null;
            string? sourceUrl  = null;
            if (TryGetObject(v, "source") is { } s)
            {
                sourceName = TryGetString(s, "name");
                sourceUrl  = TryGetString(s, "url");
            }

            list.Add(new SbomVulnerability(
                Id:          TryGetString(v, "id"),
                Severity:    ExtractSeverity(v),
                Source:      sourceName,
                SourceUrl:   sourceUrl,
                Description: TryGetString(v, "description") ?? TryGetString(v, "detail"),
                AffectsRefs: ExtractAffectsRefs(v),
                References:  ExtractReferences(v)));
        }
        return (true, list);
    }

    /// <summary>
    /// Severity lives in <c>ratings[]</c> — multiple ratings (different scoring methods
    /// from different sources) are common. Pick the highest-ranked severity we recognise.
    /// </summary>
    private static string? ExtractSeverity(JsonElement v)
    {
        if (!v.TryGetProperty("ratings", out var ratings) || ratings.ValueKind != JsonValueKind.Array)
            return null;

        string? best = null;
        var bestRank = int.MinValue;
        foreach (var r in ratings.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            var s = TryGetString(r, "severity");
            if (s is null) continue;

            var rank = SeverityRank(s);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = s.ToLowerInvariant();
            }
        }
        return best;
    }

    private static int SeverityRank(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" => 4,
        "high"     => 3,
        "medium"   => 2,
        "low"      => 1,
        "info"     => 0,
        "none"     => 0,
        _          => -1,
    };

    private static IReadOnlyList<string> ExtractAffectsRefs(JsonElement v)
    {
        if (!v.TryGetProperty("affects", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var r = TryGetString(entry, "ref");
            if (!string.IsNullOrEmpty(r)) list.Add(r);
        }
        return list;
    }

    private static IReadOnlyList<SbomVulnReference> ExtractReferences(JsonElement v)
    {
        var list = new List<SbomVulnReference>();

        // references[] — CycloneDX's primary place for cross-source pointers.
        if (v.TryGetProperty("references", out var refs) && refs.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in refs.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var source = TryGetObject(entry, "source") is { } s ? TryGetString(s, "name") : null;
                var url    = TryGetString(entry, "url");
                // CycloneDX sometimes nests the URL under source.url — fall back.
                if (string.IsNullOrEmpty(url) && TryGetObject(entry, "source") is { } src2)
                {
                    url = TryGetString(src2, "url");
                }
                list.Add(new SbomVulnReference(source, url));
            }
        }

        // advisories[] — Trivy populates this separately from references[]; entries
        // are typically {title, url} pairs. Without this, vulns Trivy emits often
        // appear "linkless" in the UI even though there's a clear advisory page.
        if (v.TryGetProperty("advisories", out var advisories) && advisories.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in advisories.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var url = TryGetString(entry, "url");
                if (string.IsNullOrEmpty(url)) continue;
                list.Add(new SbomVulnReference(
                    Source: TryGetString(entry, "title"),
                    Url:    url));
            }
        }

        return list;
    }

    // --- dependencies[] ---

    private static IReadOnlyList<SbomDependency> ParseDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<SbomDependency>();

        var list = new List<SbomDependency>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var refStr = TryGetString(entry, "ref");
            if (string.IsNullOrEmpty(refStr)) continue;

            var deps = new List<string>();
            if (entry.TryGetProperty("dependsOn", out var depsArr) && depsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in depsArr.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.String && d.GetString() is { Length: > 0 } str)
                    {
                        deps.Add(str);
                    }
                }
            }
            list.Add(new SbomDependency(refStr, deps));
        }
        return list;
    }

    // --- Helpers ---

    private static string? TryGetString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? TryGetObject(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
}
