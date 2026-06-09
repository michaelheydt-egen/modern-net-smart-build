using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Projects a <see cref="DependencyGraph"/> into the shape D3's force-directed
/// layout expects ( <c>{ nodes:[{id,…}], links:[{source,target}] }</c> ).
/// Kept separate from the domain records and from <see cref="DependencyGraphSerializer"/>
/// so the storage format isn't tied to the visualization library — swapping D3
/// for Cytoscape or vis-network later means rewriting this file only.
/// </summary>
public static class D3GraphAdapter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToD3Json(DependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var payload = new D3Payload(
            RootRef: graph.RootRef,
            Nodes: graph.Nodes.Select(n => new D3Node(
                Id:            n.Ref,
                Name:          n.Name,
                Version:       n.Version,
                VulnCount:     n.VulnCount,
                WorstSeverity: n.WorstSeverity,
                InDegree:      n.InDegree,
                Depth:         n.DepthFromRoot,
                IsRoot:        n.IsRoot)).ToArray(),
            Links: graph.Edges.Select(e => new D3Link(e.From, e.To)).ToArray(),
            Stats: graph.Stats);

        return JsonSerializer.Serialize(payload, Options);
    }

    // --- D3 wire shape. CamelCase naming policy yields id / name / source / target,
    //     which is exactly what d3-force expects. Internal because nothing outside
    //     the visualization pipeline should be reaching for this representation. ---

    private sealed record D3Payload(
        string? RootRef,
        IReadOnlyList<D3Node> Nodes,
        IReadOnlyList<D3Link> Links,
        GraphStats Stats);

    private sealed record D3Node(
        string Id,
        string Name,
        string? Version,
        int VulnCount,
        string? WorstSeverity,
        int InDegree,
        int Depth,
        bool IsRoot);

    private sealed record D3Link(string Source, string Target);
}
