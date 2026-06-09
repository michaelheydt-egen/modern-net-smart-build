namespace Cicd.Web.Admin.Services.Gcp;

/// <summary>
/// A single selectable target in the Google page dropdown. Bundles the GCP project,
/// the GAR location/repository to list images from, and the Cloud Run region to list
/// services from. Multi-region support is achieved by listing the same ProjectId
/// multiple times with different Region/ArtifactRegistry values.
/// </summary>
public sealed record GcpEnvironment
{
    /// <summary>Display label shown in the project dropdown. Should be unique.</summary>
    public string Label { get; init; } = string.Empty;

    public string ProjectId { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;

    /// <summary>GAR repository name within the project/region.</summary>
    public string ArtifactRegistry { get; init; } = string.Empty;

    /// <summary>Stable key built from ProjectId+Region+Repo — used as the dropdown value.</summary>
    public string Key => $"{ProjectId}/{Region}/{ArtifactRegistry}";
}

public sealed record GcpOptions
{
    public IReadOnlyList<GcpEnvironment> Projects { get; init; } = Array.Empty<GcpEnvironment>();

    public GcpEnvironment? Default => Projects.FirstOrDefault();
}
