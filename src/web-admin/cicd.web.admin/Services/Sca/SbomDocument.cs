namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Strongly-typed view of a CycloneDX BOM (the bom-vex.json variant Trivy emits).
/// Only the fields the SCA pages actually render are surfaced — keeps the shape
/// small and lets us extend incrementally. Empty collections, never null, so the
/// UI can branch on .Count without null checks every time.
/// </summary>
public sealed record SbomDocument(
    string? BomFormat,
    string? SpecVersion,
    SbomMetadata? Metadata,
    IReadOnlyList<SbomComponentEntry> Components,
    IReadOnlyList<SbomVulnerability> Vulnerabilities,
    // True when the source JSON had a `vulnerabilities` array at all (even if empty);
    // false when the key was absent entirely. Lets the UI distinguish
    // "Trivy says clean" from "Trivy didn't enrich this SBOM".
    bool HasVulnerabilitiesSection,
    IReadOnlyList<SbomDependency> Dependencies);

// --- Metadata block ---

public sealed record SbomMetadata(
    string? Timestamp,
    SbomComponent? Component,
    IReadOnlyList<SbomTool> Tools,
    IReadOnlyList<SbomProperty> Properties);

public sealed record SbomComponent(
    string? Name,
    string? Type,
    string? Version,
    string? BomRef);

public sealed record SbomTool(
    string? Vendor,
    string? Name,
    string? Version);

public sealed record SbomProperty(
    string Name,
    string? Value);

// --- Top-level components[] block ---

/// <summary>
/// One entry from the BOM's <c>components[]</c> array — typically a NuGet package.
/// Distinct from <see cref="SbomComponent"/> (the metadata root) so the components
/// table can show fuller fields (purl, licenses) without bloating the metadata view.
/// </summary>
public sealed record SbomComponentEntry(
    string? BomRef,
    string? Name,
    string? Version,
    string? Type,
    string? Purl,
    IReadOnlyList<string> Licenses);

// --- vulnerabilities[] block (CycloneDX VEX extension, populated by Trivy) ---

public sealed record SbomVulnerability(
    string? Id,
    string? Severity,         // critical / high / medium / low / info / none / unknown
    string? Source,           // ghsa / nvd / redhat / ... whoever issued the rating
    // URL on the issuing source's site (e.g., the GHSA advisory page). Trivy
    // populates this on `source.url`; we surface it as a per-row open-in-new-tab
    // button so users can jump to the authoritative write-up.
    string? SourceUrl,
    string? Description,
    IReadOnlyList<string> AffectsRefs,
    IReadOnlyList<SbomVulnReference> References);

public sealed record SbomVulnReference(
    string? Source,
    string? Url);

// --- dependencies[] block (graph as adjacency list) ---

public sealed record SbomDependency(
    string Ref,
    IReadOnlyList<string> DependsOn);
