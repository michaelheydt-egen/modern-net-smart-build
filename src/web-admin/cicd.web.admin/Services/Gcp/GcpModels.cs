namespace Cicd.Web.Admin.Services.Gcp;

/// <summary>One image+tag combination from Google Artifact Registry.</summary>
public sealed record GarImage(
    string Name,                        // e.g. "web-apphost"
    IReadOnlyList<string> Tags,         // ["v1", "ci-42", "abc1234"]
    DateTimeOffset UpdatedAt,
    long? SizeBytes,
    string Digest,                      // sha256:...
    string FullUri);                    // us-west1-docker.pkg.dev/.../web-apphost@sha256:...

/// <summary>One service running in Cloud Run.</summary>
public sealed record CloudRunService(
    string Name,
    string Url,                         // https://...run.app
    string LatestRevision,
    string Image,                       // last-resolved container image
    string Status,                      // Ready / Reconciling / Failed
    DateTimeOffset UpdatedAt);
