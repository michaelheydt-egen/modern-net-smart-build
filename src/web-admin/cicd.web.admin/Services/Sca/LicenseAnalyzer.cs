namespace Cicd.Web.Admin.Services.Sca;

/// <summary>
/// Categorizes the licenses of every node in a <see cref="DependencyGraph"/>
/// and surfaces compatibility issues. Pure function — no I/O, safe to memoize.
///
/// What this is NOT: legal advice. The rules below capture the common-sense
/// cases (root project's license vs transitive copyleft, presence of AGPL,
/// undeclared licenses) and intentionally don't model edge cases like
/// classpath-exception, LGPL static-vs-dynamic linking, or commercial
/// dual-licensing. Treat results as a triage signal that should prompt
/// review by someone qualified to make license calls.
/// </summary>
public static class LicenseAnalyzer
{
    public static LicenseAnalysis Analyze(DependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Per-node categorization. A node with multiple licenses takes the
        // most permissive interpretation: dual-licensed "MIT OR GPL-3.0" can
        // be consumed as MIT, so we classify as Permissive.
        var categoryByRef = new Dictionary<string, LicenseCategory>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            categoryByRef[node.Ref] = CategorizeLicenses(node.Licenses);
        }

        // Resolve the root's category — drives whether transitive copyleft is a problem.
        // (No root → treat as Permissive so we still flag pure-copyleft transitives;
        // the conflict description names this explicitly.)
        var rootCategory = !string.IsNullOrEmpty(graph.RootRef) && categoryByRef.TryGetValue(graph.RootRef, out var rc)
            ? rc
            : LicenseCategory.Permissive;

        var conflicts = new List<LicenseConflict>();
        foreach (var node in graph.Nodes)
        {
            if (node.IsRoot) continue;   // we evaluate root through its children
            var cat = categoryByRef[node.Ref];

            // Rule 1: AGPL transitive — always raise, regardless of root category.
            //         The network-use clause has serious implications even in
            //         strong-copyleft projects; everyone should be aware.
            if (cat == LicenseCategory.NetworkCopyleft)
            {
                conflicts.Add(new LicenseConflict(
                    SourceRef:      node.Ref,
                    SourceCategory: cat,
                    Reason:         "AGPL-licensed dependency — network-use clause requires source disclosure for any reachable service.",
                    Severity:       LicenseConflictSeverity.High));
                continue;
            }

            // Rule 2: Strong copyleft (GPL) in a non-copyleft root.
            //         The whole work effectively becomes GPL-licensed if shipped.
            if (cat == LicenseCategory.StrongCopyleft
                && rootCategory is LicenseCategory.Permissive or LicenseCategory.WeakCopyleft or LicenseCategory.PublicDomain)
            {
                conflicts.Add(new LicenseConflict(
                    SourceRef:      node.Ref,
                    SourceCategory: cat,
                    Reason:         $"GPL-style dependency under a {Display(rootCategory).ToLowerInvariant()} root — shipping the combined work may require the whole project to adopt GPL terms.",
                    Severity:       LicenseConflictSeverity.High));
                continue;
            }

            // Rule 3: Weak copyleft (LGPL/MPL) in a permissive root.
            //         Usually safe with dynamic linking, but worth flagging
            //         because static linking changes the picture.
            if (cat == LicenseCategory.WeakCopyleft && rootCategory == LicenseCategory.Permissive)
            {
                conflicts.Add(new LicenseConflict(
                    SourceRef:      node.Ref,
                    SourceCategory: cat,
                    Reason:         "LGPL/MPL-style dependency under a permissive root — generally safe via dynamic linking; static linking has additional obligations.",
                    Severity:       LicenseConflictSeverity.Medium));
                continue;
            }

            // Rule 4: Proprietary / commercial — needs license review for distribution.
            if (cat == LicenseCategory.Proprietary)
            {
                conflicts.Add(new LicenseConflict(
                    SourceRef:      node.Ref,
                    SourceCategory: cat,
                    Reason:         "Proprietary / commercial license declared — confirm the use case is covered by the purchased terms.",
                    Severity:       LicenseConflictSeverity.Medium));
                continue;
            }

            // Rule 5: No license info at all.
            if (cat == LicenseCategory.Unknown)
            {
                conflicts.Add(new LicenseConflict(
                    SourceRef:      node.Ref,
                    SourceCategory: cat,
                    Reason:         "No license metadata declared in the SBOM — legally redistributing this dependency is risky until the license is confirmed.",
                    Severity:       LicenseConflictSeverity.Low));
            }
        }

        var counts = CountCategories(categoryByRef);
        return new LicenseAnalysis(
            CategoryByNodeRef: categoryByRef,
            RootCategory:      rootCategory,
            Conflicts:         conflicts,
            Counts:            counts);
    }

    /// <summary>
    /// Combines a node's license list into a single category. Empty list = Unknown.
    /// Multiple licenses → most permissive wins (dual-licensed "MIT OR GPL"
    /// effectively grants the MIT route).
    /// </summary>
    private static LicenseCategory CategorizeLicenses(IReadOnlyList<string> licenses)
    {
        if (licenses.Count == 0) return LicenseCategory.Unknown;

        var most = LicenseCategory.Unknown;
        var mostRank = -1;
        foreach (var lic in licenses)
        {
            foreach (var single in SplitExpression(lic))
            {
                var cat = CategorizeSingle(single);
                // "Most permissive" = lowest restrictiveness rank.
                var rank = PermissivenessRank(cat);
                if (rank > mostRank)
                {
                    mostRank = rank;
                    most = cat;
                }
            }
        }
        return most;
    }

    /// <summary>
    /// CycloneDX licenses can be SPDX expressions like "MIT OR Apache-2.0" or
    /// "(LGPL-2.1-only OR MIT) AND BSD-3-Clause". We do a naive split on "OR"
    /// since that's the only operator where the picker-wins rule applies; AND
    /// expressions are conservatively treated as their most-permissive
    /// member, which under-flags compared to a strict reading. Acceptable for
    /// a triage signal.
    /// </summary>
    private static IEnumerable<string> SplitExpression(string expression)
    {
        var cleaned = expression.Replace("(", " ").Replace(")", " ");
        // Split on whitespace-delimited OR / AND / WITH so we get a token per atomic id.
        var tokens = cleaned.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var t in tokens)
        {
            if (string.Equals(t, "OR", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(t, "AND", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(t, "WITH", StringComparison.OrdinalIgnoreCase)) continue;
            yield return t.Trim();
        }
    }

    /// <summary>
    /// SPDX ID + common-alias → category. Comparison is invariant + case-insensitive
    /// against a curated table; anything we don't recognize falls into Unknown.
    /// </summary>
    private static LicenseCategory CategorizeSingle(string licenseId)
    {
        var id = licenseId.Trim().ToLowerInvariant();
        if (id.Length == 0) return LicenseCategory.Unknown;
        if (Permissive.Contains(id))      return LicenseCategory.Permissive;
        if (WeakCopyleft.Contains(id))    return LicenseCategory.WeakCopyleft;
        if (StrongCopyleft.Contains(id))  return LicenseCategory.StrongCopyleft;
        if (NetworkCopyleft.Contains(id)) return LicenseCategory.NetworkCopyleft;
        if (PublicDomain.Contains(id))    return LicenseCategory.PublicDomain;
        if (Proprietary.Contains(id))     return LicenseCategory.Proprietary;
        // Prefix match for LicenseRef-* (CycloneDX uses this for custom licenses).
        if (id.StartsWith("licenseref-", StringComparison.Ordinal)) return LicenseCategory.Proprietary;
        return LicenseCategory.Unknown;
    }

    // --- Curated SPDX tables ---
    // Source: SPDX License List (most common identifiers as of 2025).
    // Bias toward inclusion: misclassifying an obscure license as Unknown is a
    // better failure mode than calling it permissive when it isn't.

    private static readonly HashSet<string> Permissive = new(StringComparer.OrdinalIgnoreCase)
    {
        "mit", "mit-0", "mit license",
        "apache-2.0", "apache 2.0", "apache-1.1", "apache-1.0",
        "bsd-2-clause", "bsd-3-clause", "bsd-4-clause", "0bsd",
        "isc",
        "zlib", "libpng",
        "bsl-1.0",
        "x11",
        "ms-pl",                      // Microsoft Public License — permissive enough
        "ncsa", "ntp",
        "postgresql",
    };

    private static readonly HashSet<string> WeakCopyleft = new(StringComparer.OrdinalIgnoreCase)
    {
        "lgpl-2.0-only", "lgpl-2.0-or-later", "lgpl-2.0+",
        "lgpl-2.1-only", "lgpl-2.1-or-later", "lgpl-2.1+",
        "lgpl-3.0-only", "lgpl-3.0-or-later", "lgpl-3.0+",
        "mpl-1.0", "mpl-1.1", "mpl-2.0",
        "epl-1.0", "epl-2.0",
        "cddl-1.0", "cddl-1.1",
        "eupl-1.0", "eupl-1.1", "eupl-1.2",
    };

    private static readonly HashSet<string> StrongCopyleft = new(StringComparer.OrdinalIgnoreCase)
    {
        "gpl-2.0-only", "gpl-2.0-or-later", "gpl-2.0+",
        "gpl-3.0-only", "gpl-3.0-or-later", "gpl-3.0+",
        "gpl-1.0-only", "gpl-1.0-or-later",
    };

    private static readonly HashSet<string> NetworkCopyleft = new(StringComparer.OrdinalIgnoreCase)
    {
        "agpl-3.0-only", "agpl-3.0-or-later", "agpl-3.0+", "agpl-3.0",
    };

    private static readonly HashSet<string> PublicDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        "cc0-1.0", "unlicense", "wtfpl", "pddl-1.0", "0bsd",
    };

    private static readonly HashSet<string> Proprietary = new(StringComparer.OrdinalIgnoreCase)
    {
        "proprietary", "commercial", "ms-eula",
    };

    /// <summary>
    /// Higher rank = more permissive. Used to pick the "best" license out of
    /// an OR-expression (e.g., MIT OR GPL → caller picks MIT).
    /// </summary>
    private static int PermissivenessRank(LicenseCategory c) => c switch
    {
        LicenseCategory.PublicDomain    => 5,
        LicenseCategory.Permissive      => 4,
        LicenseCategory.WeakCopyleft    => 3,
        LicenseCategory.StrongCopyleft  => 2,
        LicenseCategory.NetworkCopyleft => 1,
        LicenseCategory.Proprietary     => 0,
        LicenseCategory.Unknown         => -1,
        _                               => -1,
    };

    private static LicenseCategoryCounts CountCategories(IReadOnlyDictionary<string, LicenseCategory> map)
    {
        int p = 0, w = 0, s = 0, n = 0, pd = 0, pr = 0, u = 0;
        foreach (var c in map.Values)
        {
            switch (c)
            {
                case LicenseCategory.Permissive:      p++;  break;
                case LicenseCategory.WeakCopyleft:    w++;  break;
                case LicenseCategory.StrongCopyleft:  s++;  break;
                case LicenseCategory.NetworkCopyleft: n++;  break;
                case LicenseCategory.PublicDomain:    pd++; break;
                case LicenseCategory.Proprietary:     pr++; break;
                case LicenseCategory.Unknown:         u++;  break;
            }
        }
        return new LicenseCategoryCounts(p, w, s, n, pd, pr, u);
    }

    public static string Display(LicenseCategory c) => c switch
    {
        LicenseCategory.Permissive      => "Permissive",
        LicenseCategory.WeakCopyleft    => "Weak copyleft",
        LicenseCategory.StrongCopyleft  => "Strong copyleft",
        LicenseCategory.NetworkCopyleft => "Network copyleft (AGPL)",
        LicenseCategory.PublicDomain    => "Public domain",
        LicenseCategory.Proprietary     => "Proprietary",
        LicenseCategory.Unknown         => "Unknown",
        _                               => c.ToString(),
    };
}

/// <summary>
/// Categories the analyzer buckets licenses into. Order roughly tracks
/// restrictiveness; <see cref="LicenseAnalyzer.Display"/> renders human labels.
/// </summary>
public enum LicenseCategory
{
    Unknown,
    Permissive,
    WeakCopyleft,
    StrongCopyleft,
    NetworkCopyleft,
    PublicDomain,
    Proprietary,
}

public enum LicenseConflictSeverity
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One flagged finding. <see cref="SourceRef"/> is the dependency whose license
/// raises the issue — the UI uses it to highlight the matching node on the
/// dependency graph.
/// </summary>
public sealed record LicenseConflict(
    string SourceRef,
    LicenseCategory SourceCategory,
    string Reason,
    LicenseConflictSeverity Severity);

public sealed record LicenseCategoryCounts(
    int Permissive,
    int WeakCopyleft,
    int StrongCopyleft,
    int NetworkCopyleft,
    int PublicDomain,
    int Proprietary,
    int Unknown);

public sealed record LicenseAnalysis(
    IReadOnlyDictionary<string, LicenseCategory> CategoryByNodeRef,
    LicenseCategory RootCategory,
    IReadOnlyList<LicenseConflict> Conflicts,
    LicenseCategoryCounts Counts);
