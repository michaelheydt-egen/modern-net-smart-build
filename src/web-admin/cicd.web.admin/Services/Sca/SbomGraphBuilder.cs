namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Pure builder that projects an <see cref="SbomDocument"/> into a
/// <see cref="DependencyGraph"/>. No I/O, no DI — call from anywhere.
/// Safe to memoize per build number when caching becomes worth it.
/// </summary>
public static class SbomGraphBuilder
{
    public static DependencyGraph Build(SbomDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var rootRef = doc.Metadata?.Component?.BomRef;

        // 1. Build the node index keyed by bom-ref. We add a synthesized entry for
        //    the metadata.component if it doesn't appear in components[] — that's
        //    the common case for the root, since the SBOM author keeps it in
        //    metadata rather than duplicating it into the components list.
        var nodesByRef = new Dictionary<string, NodeBuilder>(StringComparer.Ordinal);
        foreach (var c in doc.Components)
        {
            if (string.IsNullOrEmpty(c.BomRef)) continue;
            nodesByRef[c.BomRef] = new NodeBuilder(c.BomRef, c.Name, c.Version, c.Type, c.Licenses);
        }
        if (!string.IsNullOrEmpty(rootRef) && !nodesByRef.ContainsKey(rootRef))
        {
            var rc = doc.Metadata!.Component!;
            nodesByRef[rootRef] = new NodeBuilder(
                bomRef:   rootRef,
                name:     rc.Name,
                version:  rc.Version,
                type:     rc.Type,
                licenses: Array.Empty<string>());
        }

        // 2. Roll up vulnerabilities. Each vuln's AffectsRefs contributes to each
        //    affected node's VulnCount + worst-severity tracker. Vulns whose refs
        //    point at nodes we don't have are silently skipped.
        foreach (var vuln in doc.Vulnerabilities)
        {
            foreach (var affected in vuln.AffectsRefs)
            {
                if (nodesByRef.TryGetValue(affected, out var nb))
                {
                    nb.VulnCount++;
                    nb.UpdateWorstSeverity(vuln.Severity);
                }
            }
        }

        // 3. Build edges. Both endpoints must exist in the node index — otherwise
        //    the edge would dangle and confuse the BFS / D3 forces. Endpoints
        //    missing from components[] is unusual but happens; better to drop the
        //    edge than to invent a phantom node from nothing.
        var edges = new List<GraphEdge>(capacity: doc.Dependencies.Sum(d => d.DependsOn.Count));
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var dep in doc.Dependencies)
        {
            if (!nodesByRef.TryGetValue(dep.Ref, out var fromNode)) continue;

            foreach (var to in dep.DependsOn)
            {
                if (!nodesByRef.TryGetValue(to, out var toNode)) continue;
                edges.Add(new GraphEdge(dep.Ref, to));
                fromNode.OutDegree++;
                toNode.InDegree++;

                if (!adjacency.TryGetValue(dep.Ref, out var neighbors))
                {
                    neighbors = new List<string>();
                    adjacency[dep.Ref] = neighbors;
                }
                neighbors.Add(to);
            }
        }

        // 4. BFS depth from root. Nodes unreached stay at -1 (the NodeBuilder default),
        //    which surfaces them in the stats as "unreachable" — useful diagnostic for
        //    SBOMs where the dependencies graph is incomplete or the metadata.component
        //    bom-ref doesn't match a real component.
        if (!string.IsNullOrEmpty(rootRef) && nodesByRef.TryGetValue(rootRef, out var rootNb))
        {
            rootNb.DepthFromRoot = 0;
            var queue = new Queue<string>();
            queue.Enqueue(rootRef);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDepth = nodesByRef[current].DepthFromRoot;
                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (var next in neighbors)
                {
                    var nextNb = nodesByRef[next];
                    if (nextNb.DepthFromRoot >= 0) continue;   // already visited (covers cycles)
                    nextNb.DepthFromRoot = currentDepth + 1;
                    queue.Enqueue(next);
                }
            }
        }

        // 5. Materialize the immutable view + roll stats.
        var nodes = nodesByRef.Values.Select(nb => nb.Build(isRoot: nb.Ref == rootRef)).ToArray();

        var severity = CountSeverities(nodes);
        var maxDepth = nodes.Where(n => n.DepthFromRoot >= 0)
                            .Select(n => n.DepthFromRoot)
                            .DefaultIfEmpty(0)
                            .Max();
        var stats = new GraphStats(
            NodeCount:           nodes.Length,
            EdgeCount:           edges.Count,
            VulnerableNodeCount: nodes.Count(n => n.VulnCount > 0),
            UnreachableCount:    nodes.Count(n => n.DepthFromRoot < 0),
            MaxDepth:            maxDepth,
            SeverityCounts:      severity);

        return new DependencyGraph(rootRef, nodes, edges, stats);
    }

    private static GraphSeverityCounts CountSeverities(IReadOnlyList<GraphNode> nodes)
    {
        int critical = 0, high = 0, medium = 0, low = 0;
        foreach (var n in nodes)
        {
            if (n.VulnCount == 0) continue;
            switch (n.WorstSeverity)
            {
                case "critical": critical++; break;
                case "high":     high++;     break;
                case "medium":   medium++;   break;
                case "low":      low++;      break;
            }
        }
        return new GraphSeverityCounts(critical, high, medium, low);
    }

    /// <summary>
    /// Mutable buffer used while accumulating node state during the build pass.
    /// Becomes an immutable <see cref="GraphNode"/> via <see cref="Build"/>.
    /// </summary>
    private sealed class NodeBuilder
    {
        public string Ref { get; }
        public string Name { get; }
        public string? Version { get; }
        public string? Type { get; }
        public IReadOnlyList<string> Licenses { get; }

        public int VulnCount { get; set; }
        public string? WorstSeverity { get; private set; }
        public int InDegree { get; set; }
        public int OutDegree { get; set; }
        public int DepthFromRoot { get; set; } = -1;

        public NodeBuilder(string bomRef, string? name, string? version, string? type, IReadOnlyList<string> licenses)
        {
            Ref      = bomRef;
            Name     = string.IsNullOrEmpty(name) ? bomRef : name;
            Version  = version;
            Type     = type;
            Licenses = licenses;
        }

        public void UpdateWorstSeverity(string? severity)
        {
            var rank = SeverityRank(severity);
            if (rank > SeverityRank(WorstSeverity))
            {
                WorstSeverity = severity?.ToLowerInvariant();
            }
        }

        private static int SeverityRank(string? s) => s?.ToLowerInvariant() switch
        {
            "critical" => 4,
            "high"     => 3,
            "medium"   => 2,
            "low"      => 1,
            _          => 0,
        };

        public GraphNode Build(bool isRoot) => new(
            Ref:           Ref,
            Name:          Name,
            Version:       Version,
            Type:          Type,
            Licenses:      Licenses,
            VulnCount:     VulnCount,
            WorstSeverity: WorstSeverity,
            InDegree:      InDegree,
            OutDegree:     OutDegree,
            DepthFromRoot: DepthFromRoot,
            IsRoot:        isRoot);
    }
}
