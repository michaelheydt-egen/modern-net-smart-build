namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Component-and-dependency graph derived from an <see cref="SbomDocument"/>.
/// Designed as a serialization-friendly value type: persists cleanly to SQLite
/// or Nexus (via System.Text.Json) and survives round-tripping back into the
/// same shape. Property names are kept domain-natural here; any presentation
/// rename to D3 conventions (id / source / target / links) happens at the JS
/// boundary, not in the stored format.
/// </summary>
public sealed record DependencyGraph(
    /// <summary>bom-ref of the metadata.component, or null if the source SBOM didn't declare a root.</summary>
    string? RootRef,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    GraphStats Stats);

public sealed record GraphNode(
    /// <summary>The component's bom-ref — the stable id used as both the dictionary key and the D3 node id.</summary>
    string Ref,
    string Name,
    string? Version,
    string? Type,
    IReadOnlyList<string> Licenses,
    /// <summary>Count of vulnerabilities whose <c>affects[]</c> list contains this node's ref.</summary>
    int VulnCount,
    /// <summary>Worst severity across this node's vulnerabilities (lowercased: critical/high/medium/low) or null.</summary>
    string? WorstSeverity,
    /// <summary>How many other components depend on this one — the load-bearing signal that drives node size in D3.</summary>
    int InDegree,
    /// <summary>How many components this one depends on directly.</summary>
    int OutDegree,
    /// <summary>BFS distance from <see cref="DependencyGraph.RootRef"/>. -1 means unreachable from root.</summary>
    int DepthFromRoot,
    bool IsRoot);

public sealed record GraphEdge(
    /// <summary>Source bom-ref (the depending component).</summary>
    string From,
    /// <summary>Target bom-ref (the dependency).</summary>
    string To);

public sealed record GraphStats(
    int NodeCount,
    int EdgeCount,
    int VulnerableNodeCount,
    /// <summary>Nodes the BFS couldn't reach from the root — orphans / metadata gaps / dead-code-equivalent.</summary>
    int UnreachableCount,
    int MaxDepth,
    GraphSeverityCounts SeverityCounts);

/// <summary>
/// Per-severity vulnerable-node counts (NOT vulnerability counts — a node with three high
/// vulns still counts once here under <see cref="High"/>). The visualization uses this
/// for at-a-glance summary chips.
/// </summary>
public sealed record GraphSeverityCounts(
    int Critical,
    int High,
    int Medium,
    int Low);
