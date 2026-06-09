// SBOM dependency-graph visualization (d3-force, SVG). Entry points exposed on window:
//
//   sbomGraphRender(containerEl, graphJson, dotNetRef)
//     containerEl : a <div> ElementReference handed in from Razor
//     graphJson   : compact JSON of shape { rootRef, nodes:[{id,name,version,
//                                              vulnCount,worstSeverity,inDegree,
//                                              depth,isRoot}], links:[{source,target}], stats }
//                   produced by D3GraphAdapter.ToD3Json server-side.
//     dotNetRef   : Blazor DotNetObjectReference — JS calls
//                   dotNetRef.invokeMethodAsync('OnNodeClicked', nodeId) on node click.
//
//   sbomGraphDestroy(containerEl)
//     Stops the running simulation (if any) and removes the SVG. Called on view-toggle
//     back to JSON and on page navigation.
//
//   sbomGraphFilter(containerEl, { searchText, minSeverity, vulnOnly })
//     Updates visibility of nodes + links based on filter state. Filtered-out
//     elements dim to 0.15 opacity (still in layout). Idempotent.
//
//   sbomGraphReset(containerEl)
//     Clears hover state, re-heats the simulation, and recenters pan/zoom.
//
// Interaction model:
//   - Click a node → fires OnNodeClicked Blazor callback (Razor opens side panel).
//   - Hover a node → that node + its direct neighbors stay opaque; everything
//     else dims. On mouse-out, dimming reverts to the filter state.
//   - Drag a node → re-heats simulation, releases pin on drop (root stays pinned).
//   - Pan / zoom via mouse wheel + click-drag on the background.

(function () {
    'use strict';

    const DIM_OPACITY  = 0.15;
    const FULL_OPACITY = 1.0;

    // Per-container instance so view-toggles + re-renders don't double-allocate.
    const instances = new WeakMap();

    function render(container, graphJson, dotNetRef) {
        if (!container || !window.d3) return;
        destroy(container);

        let data;
        try { data = JSON.parse(graphJson); }
        catch (e) { console.error('sbom-graph: invalid graphJson', e); return; }

        const nodes = (data.nodes || []).map(n => Object.assign({}, n));
        const links = (data.links || []).map(l => Object.assign({}, l));

        // Precompute neighbor adjacency for fast hover-highlight lookup.
        // Built once at render time (links carry d3-mutated source/target objects
        // by tick #1, so we capture ids now before that mutation kicks in).
        const neighborsByNode = new Map();
        nodes.forEach(n => neighborsByNode.set(n.id, new Set()));
        links.forEach(l => {
            const s = typeof l.source === 'object' ? l.source.id : l.source;
            const t = typeof l.target === 'object' ? l.target.id : l.target;
            neighborsByNode.get(s)?.add(t);
            neighborsByNode.get(t)?.add(s);
        });

        // Track container size for reflow. CSS controls the actual height
        // (drag-handle resize + fullscreen toggle on Visualize.razor); we just
        // read whatever it currently is and feed it to the simulation forces.
        const measure = () => ({
            width:  Math.max(400, container.clientWidth  || 800),
            height: Math.max(300, container.clientHeight || 600),
        });
        const initial = measure();
        const width  = initial.width;
        const height = initial.height;

        const svg = d3.select(container)
            .append('svg')
            .attr('viewBox', `0 0 ${width} ${height}`)
            .attr('preserveAspectRatio', 'xMidYMid meet')
            .style('width', '100%')
            .style('height', '100%')        // follow the container — CSS owns the actual size
            .style('display', 'block')
            .style('background', '#1a1a1a')
            .style('border-radius', '4px');

        const g = svg.append('g');

        const zoom = d3.zoom()
            .scaleExtent([0.1, 8])
            .on('zoom', (event) => g.attr('transform', event.transform));
        svg.call(zoom);

        // Concentric-ring depth seeding — fast convergence + recognizable structure
        // from tick 1. Root pinned at center.
        const maxDepth = Math.max(1, ...nodes.map(n => n.depth >= 0 ? n.depth : 0));
        const cx = width / 2, cy = height / 2;
        const usableRadius = Math.min(width, height) / 2 - 40;
        nodes.forEach((n, i) => {
            if (n.isRoot) {
                n.x = cx; n.y = cy;
                n.fx = cx; n.fy = cy;
            } else if (n.depth > 0) {
                const sameRing = nodes.filter(m => m.depth === n.depth);
                const idxInRing = sameRing.indexOf(n);
                const angle = (idxInRing / Math.max(1, sameRing.length)) * 2 * Math.PI;
                const radius = (n.depth / maxDepth) * usableRadius;
                n.x = cx + Math.cos(angle) * radius;
                n.y = cy + Math.sin(angle) * radius;
            } else {
                const angle = (i / nodes.length) * 2 * Math.PI;
                n.x = cx + Math.cos(angle) * usableRadius;
                n.y = cy + Math.sin(angle) * usableRadius;
            }
        });

        const simulation = d3.forceSimulation(nodes)
            .force('link',    d3.forceLink(links).id(d => d.id).distance(60).strength(0.6))
            .force('charge',  d3.forceManyBody().strength(-180))
            .force('center',  d3.forceCenter(cx, cy).strength(0.05))
            .force('collide', d3.forceCollide().radius(d => nodeRadius(d) + 2));

        const linkSel = g.append('g')
            .attr('stroke', '#555')
            .attr('stroke-opacity', 0.55)
            .selectAll('line')
            .data(links)
            .join('line')
            .attr('stroke-width', 1);

        const nodeSel = g.append('g')
            .selectAll('circle')
            .data(nodes)
            .join('circle')
            .attr('r', nodeRadius)
            .attr('fill', nodeColor)
            .attr('stroke', d => d.isRoot ? '#ffffff' : '#1a1a1a')
            .attr('stroke-width', d => d.isRoot ? 2 : 1)
            .style('cursor', 'pointer')
            .call(makeDrag(simulation));

        // (No native <title> tooltip — the Blazor hover-card overlay takes over;
        // leaving both in would cause a double-tooltip flash on slow hovers.)

        // Labels: root + load-bearing + vulnerable only (everything else is noise).
        const labelData = nodes.filter(d => d.isRoot || d.inDegree >= 5 || d.vulnCount > 0);
        const labelSel = g.append('g')
            .attr('font-family', 'ui-monospace, monospace')
            .attr('font-size', 10)
            .attr('fill', '#d4d4d4')
            .attr('pointer-events', 'none')
            .selectAll('text')
            .data(labelData)
            .join('text')
            .attr('dy', d => -(nodeRadius(d) + 4))
            .attr('text-anchor', 'middle')
            .text(d => d.name);

        simulation.on('tick', () => {
            linkSel
                .attr('x1', d => d.source.x)
                .attr('y1', d => d.source.y)
                .attr('x2', d => d.target.x)
                .attr('y2', d => d.target.y);
            nodeSel.attr('cx', d => d.x).attr('cy', d => d.y);
            labelSel.attr('x', d => d.x).attr('y', d => d.y);
        });

        const freezeTimer = setTimeout(() => simulation.stop(), 4000);

        // ResizeObserver: when CSS resizes the container (drag handle,
        // fullscreen toggle, viewport change), update the viewBox + center
        // force + root pin and re-heat the simulation so the graph reflows
        // into the new space rather than just scaling. Debounced because
        // drag-resize fires the callback continuously.
        let resizeDebounceTimer = null;
        const resizeObserver = new ResizeObserver(() => {
            clearTimeout(resizeDebounceTimer);
            resizeDebounceTimer = setTimeout(() => {
                const next = measure();
                svg.attr('viewBox', `0 0 ${next.width} ${next.height}`);
                const centerForce = simulation.force('center');
                if (centerForce) {
                    centerForce.x(next.width / 2).y(next.height / 2);
                }
                // Root stays pinned to the new center.
                const root = nodes.find(n => n.isRoot);
                if (root) {
                    root.fx = next.width / 2;
                    root.fy = next.height / 2;
                }
                simulation.alpha(0.3).restart();
                clearTimeout(instance.freezeTimer);
                instance.freezeTimer = setTimeout(() => simulation.stop(), 4000);
            }, 100);
        });
        resizeObserver.observe(container);

        // --- Interactivity wiring ---

        nodeSel
            .on('click', (event, d) => {
                event.stopPropagation();
                if (dotNetRef) {
                    dotNetRef.invokeMethodAsync('OnNodeClicked', d.id)
                        .catch(err => console.warn('sbom-graph: OnNodeClicked callback failed', err));
                }
            })
            .on('mouseover', (event, d) => {
                instance.hoveredId = d.id;
                applyVisibility(instance);
                if (instance.dotNetRef) {
                    instance.dotNetRef.invokeMethodAsync('OnNodeHoverEnter', d.id, computeHoverSide(instance, d))
                        .catch(err => console.warn('sbom-graph: OnNodeHoverEnter failed', err));
                }
            })
            .on('mouseout', () => {
                instance.hoveredId = null;
                applyVisibility(instance);
                if (instance.dotNetRef) {
                    instance.dotNetRef.invokeMethodAsync('OnNodeHoverLeave')
                        .catch(err => console.warn('sbom-graph: OnNodeHoverLeave failed', err));
                }
            });

        const instance = {
            svg, g, simulation, freezeTimer, zoom,
            nodes, links, nodeSel, linkSel, labelSel,
            neighborsByNode,
            dotNetRef,
            filter: { searchText: '', minSeverity: 'all', vulnOnly: false },
            hoveredId: null,
            resizeObserver,
        };
        instances.set(container, instance);
    }

    function destroy(container) {
        if (!container) return;
        const inst = instances.get(container);
        if (inst) {
            clearTimeout(inst.freezeTimer);
            try { inst.resizeObserver?.disconnect(); } catch (_) { /* ignore */ }
            try { inst.simulation.stop(); }            catch (_) { /* ignore */ }
            try { inst.svg.remove(); }                 catch (_) { /* ignore */ }
            instances.delete(container);
        }
        d3.select(container).selectAll('svg').remove();
    }

    function filter(container, filterState) {
        const inst = instances.get(container);
        if (!inst) return;
        inst.filter = Object.assign({}, inst.filter, filterState || {});
        // highlightRefs is a ref-list overlay: when non-empty, those nodes (plus
        // their direct neighbors, for context) pass; search / severity / vulnOnly
        // are ignored. Lets callers like the license analyzer pin attention
        // on a small set of nodes without fighting other filter state.
        if (filterState && Array.isArray(filterState.highlightRefs)) {
            inst.highlightSet = filterState.highlightRefs.length > 0
                ? new Set(filterState.highlightRefs)
                : null;
        }
        applyVisibility(inst);
    }

    function reset(container) {
        const inst = instances.get(container);
        if (!inst) return;
        inst.hoveredId = null;
        inst.highlightSet = null;
        applyVisibility(inst);
        // Re-heat the simulation so users get a fresh layout, and recenter zoom.
        try {
            inst.simulation.alpha(0.6).restart();
            inst.svg.transition().duration(400).call(inst.zoom.transform, d3.zoomIdentity);
        } catch (_) { /* ignore */ }
        clearTimeout(inst.freezeTimer);
        inst.freezeTimer = setTimeout(() => inst.simulation.stop(), 4000);
    }

    // --- Visibility computation ---
    //
    // A node is "passes filter" if it matches search (or no search), meets the min
    // severity threshold (or no threshold), and matches the vuln-only toggle.
    // A node is "in hover focus" if no hover is active, OR it's the hovered node,
    // OR it's a direct neighbor of the hovered node. Final opacity is full only
    // when both checks succeed; otherwise the node dims.

    function applyVisibility(inst) {
        const f = inst.filter;
        const searchLower = (f.searchText || '').toLowerCase();
        const minSev = severityRank(f.minSeverity || 'all');
        const hoverId = inst.hoveredId;
        const hoverNeighbors = hoverId ? inst.neighborsByNode.get(hoverId) : null;

        const highlightSet = inst.highlightSet;
        const passesFilter = (d) => {
            // Root always visible — anchors the layout, hiding it would be confusing.
            if (d.isRoot) return true;
            // highlight-list mode: only members of the set (and their direct
            // neighbors, so the context is visible) pass. Other filter knobs are ignored.
            if (highlightSet) {
                if (highlightSet.has(d.id)) return true;
                const neigh = inst.neighborsByNode.get(d.id);
                if (neigh) {
                    for (const refId of highlightSet) {
                        if (neigh.has(refId)) return true;
                    }
                }
                return false;
            }
            if (searchLower && !(d.name || '').toLowerCase().includes(searchLower)) return false;
            if (f.vulnOnly && (!d.vulnCount || d.vulnCount === 0)) return false;
            if (minSev > 0) {
                const r = severityRank(d.worstSeverity);
                if (r < minSev) return false;
            }
            return true;
        };
        const inHoverFocus = (d) => {
            if (!hoverId) return true;
            if (d.id === hoverId) return true;
            return hoverNeighbors && hoverNeighbors.has(d.id);
        };

        inst.nodeSel.transition().duration(150)
            .style('opacity', d => (passesFilter(d) && inHoverFocus(d)) ? FULL_OPACITY : DIM_OPACITY);

        inst.labelSel.transition().duration(150)
            .style('opacity', d => (passesFilter(d) && inHoverFocus(d)) ? FULL_OPACITY : DIM_OPACITY);

        // Edge is visible iff both endpoints pass filter AND both are in hover focus.
        inst.linkSel.transition().duration(150)
            .style('opacity', d => {
                const s = typeof d.source === 'object' ? d.source : { id: d.source };
                const t = typeof d.target === 'object' ? d.target : { id: d.target };
                const sNode = inst.nodes.find(n => n.id === s.id);
                const tNode = inst.nodes.find(n => n.id === t.id);
                if (!sNode || !tNode) return DIM_OPACITY;
                const visible = passesFilter(sNode) && passesFilter(tNode)
                             && inHoverFocus(sNode) && inHoverFocus(tNode);
                return visible ? 0.55 : DIM_OPACITY;
            });
    }

    /**
     * Picks which side of the graph the hover overlay should anchor to —
     * opposite the hovered node so it never covers what the user is pointing at.
     * Hysteresis (±60px around the centerline) keeps the panel stable when
     * the user grazes nodes near the middle.
     */
    function computeHoverSide(instance, node) {
        // Read viewBox to know the simulation's logical width regardless of zoom.
        const viewBox = instance.svg.attr('viewBox');
        const vbWidth = viewBox
            ? parseFloat(viewBox.split(/\s+/)[2]) || 800
            : 800;
        const centerX = vbWidth / 2;
        const HYSTERESIS = 60;
        const last = instance.lastHoverSide;
        let side;
        if (last === 'left' && node.x < centerX + HYSTERESIS) {
            side = 'left';
        } else if (last === 'right' && node.x > centerX - HYSTERESIS) {
            side = 'right';
        } else {
            // No prior side (first hover) or node clearly across the centerline.
            // Panel goes opposite the node — node on right half → panel on left.
            side = node.x > centerX ? 'left' : 'right';
        }
        instance.lastHoverSide = side;
        return side;
    }

    function nodeRadius(d) {
        return Math.sqrt(Math.max(d.inDegree || 0, 1)) * 4 + 5;
    }

    function nodeColor(d) {
        if (d.isRoot) return '#10b981';
        if (!d.vulnCount) return '#6b7280';
        switch ((d.worstSeverity || '').toLowerCase()) {
            case 'critical': return '#dc2626';
            case 'high':     return '#ea580c';
            case 'medium':   return '#d97706';
            case 'low':      return '#3b82f6';
            default:         return '#6b7280';
        }
    }

    function severityRank(severity) {
        switch ((severity || '').toLowerCase()) {
            case 'critical': return 4;
            case 'high':     return 3;
            case 'medium':   return 2;
            case 'low':      return 1;
            case 'all':      return 0;
            default:         return 0;
        }
    }

    function formatTooltip(d) {
        const lines = [
            d.name + (d.version ? '@' + d.version : ''),
            d.vulnCount
                ? d.vulnCount + ' vuln(s) — worst: ' + (d.worstSeverity || 'unrated')
                : 'no known vulns',
            'depended on by ' + (d.inDegree || 0) + ' component(s)',
            d.depth >= 0 ? 'depth from root: ' + d.depth : 'unreachable from root',
        ];
        return lines.join('\n');
    }

    function makeDrag(simulation) {
        return d3.drag()
            .on('start', (event, d) => {
                if (!event.active) simulation.alphaTarget(0.3).restart();
                d.fx = d.x;
                d.fy = d.y;
            })
            .on('drag', (event, d) => {
                d.fx = event.x;
                d.fy = event.y;
            })
            .on('end', (event, d) => {
                if (!event.active) simulation.alphaTarget(0);
                if (!d.isRoot) {
                    d.fx = null;
                    d.fy = null;
                }
            });
    }

    // Uses the Browser Fullscreen API — the previous CSS-only approach (position:
    // fixed + z-index) didn't work inside MudBlazor's MudMainContent because that
    // ancestor establishes a containing block (CSS transform on the drawer
    // animation), and position:fixed is positioned relative to it, not the
    // viewport. The Fullscreen API sidesteps the whole issue, plus Escape exits
    // for free.
    function toggleFullscreen(container) {
        if (!container) return;
        try {
            if (document.fullscreenElement) {
                document.exitFullscreen().catch(() => { /* user cancelled or already exiting */ });
            } else if (container.requestFullscreen) {
                container.requestFullscreen().catch(err => {
                    console.warn('sbom-graph: fullscreen request rejected', err);
                });
            }
        } catch (e) {
            console.warn('sbom-graph: fullscreen toggle failed', e);
        }
    }

    window.sbomGraphRender           = render;
    window.sbomGraphDestroy          = destroy;
    window.sbomGraphFilter           = filter;
    window.sbomGraphReset            = reset;
    window.sbomGraphToggleFullscreen = toggleFullscreen;
})();
