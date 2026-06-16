using Deployment.Domain.Common;

namespace Deployment.Domain.Containers;

/// <summary>
/// Light inventory of containers seen on the bus (from CI ContainerPublished). One row per
/// container name, holding the latest-seen version/digest/Nexus ref — the source a manual or
/// auto deployment promotes. Keyed by container name (the latest push wins).
/// </summary>
public sealed class KnownContainer : AggregateRoot<Guid>
{
    public string ContainerName { get; private set; }
    public string Version { get; private set; }
    public string? ImageDigest { get; private set; }

    /// <summary>The Nexus pull reference (digest-pinned when available) — the GarPush source.</summary>
    public string NexusRef { get; private set; }

    public DateTimeOffset FirstSeenAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    private KnownContainer()
    {
        ContainerName = string.Empty;
        Version = string.Empty;
        NexusRef = string.Empty;
    }

    public KnownContainer(Guid id, string containerName, string version, string nexusRef, DateTimeOffset seenAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("ContainerName cannot be empty.", nameof(containerName));

        Id = id;
        ContainerName = containerName.Trim();
        Version = version?.Trim() ?? string.Empty;
        NexusRef = nexusRef?.Trim() ?? string.Empty;
        ImageDigest = ParseDigest(NexusRef);
        FirstSeenAtUtc = seenAtUtc;
        LastSeenAtUtc = seenAtUtc;
    }

    /// <summary>A newer push of the same container name — refresh version/ref.</summary>
    public void Observe(string version, string nexusRef, DateTimeOffset seenAtUtc)
    {
        Version = version?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(nexusRef))
        {
            NexusRef = nexusRef.Trim();
            ImageDigest = ParseDigest(NexusRef);
        }
        LastSeenAtUtc = seenAtUtc;
    }

    private static string? ParseDigest(string nexusRef)
    {
        var at = nexusRef.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        return at >= 0 ? nexusRef[(at + 1)..] : null;
    }
}
