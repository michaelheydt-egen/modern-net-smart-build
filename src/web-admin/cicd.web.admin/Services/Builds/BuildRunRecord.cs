namespace Cicd.Web.Admin.Services.Builds;

/// <summary>
/// One row in the local SQLite mirror of Jenkins build history. Schema is
/// intentionally denormalized — causes, artifacts, and the build-info.json
/// blob live as JSON columns so the only secondary indexes we need are
/// <c>(JobName, Number)</c> for upsert and <c>(JobName, Building)</c> for
/// the "what's still in-flight" sweep.
/// </summary>
public sealed class BuildRunRecord
{
    public int Id { get; set; }

    public string JobName { get; set; } = string.Empty;
    public int Number { get; set; }

    /// <summary>String form of <see cref="Jenkins.Client.BuildResult"/>, null for in-progress / unknown.</summary>
    public string? Result { get; set; }
    public bool Building { get; set; }

    /// <summary>Unix milliseconds (matches Jenkins's own field shape).</summary>
    public long Timestamp { get; set; }

    /// <summary>Build duration in milliseconds. 0 for in-flight builds.</summary>
    public long Duration { get; set; }

    public string? Description { get; set; }

    /// <summary>JSON array of <c>shortDescription</c> strings from Jenkins's causes.</summary>
    public string? CausesJson { get; set; }

    /// <summary>JSON array of <c>{fileName, relativePath}</c>.</summary>
    public string? ArtifactsJson { get; set; }

    /// <summary>Verbatim content of the build's <c>build-info.json</c> artifact when present.</summary>
    public string? BuildInfoJson { get; set; }

    /// <summary>Unix milliseconds when this row was last upserted by the sync service.</summary>
    public long SyncedAt { get; set; }
}
