namespace Util.Commands.Nexus;

public sealed record NexusComponent(string Id, string Name, string Version);

internal sealed record NexusComponentsPage(NexusComponent[] Items, string? ContinuationToken);
