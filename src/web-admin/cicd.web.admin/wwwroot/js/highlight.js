// Thin Blazor → Prism interop. Loaded globally from App.razor.
//
// Prism core + the autoloader plugin are loaded from CDN in App.razor head;
// autoloader fetches the per-language definitions on demand (json, yaml, xml,
// csharp, etc.) the first time we ask Prism to highlight that language.
//
// Called from BuildDetails.razor's OnAfterRenderAsync once a preview's <pre>
// has the right `language-*` class. Failures are silent: the underlying text
// is still legible as raw monospace if highlighting can't run for any reason.

window.artifactHighlight = function (element) {
    if (!element || !window.Prism) return;
    try { window.Prism.highlightElement(element); }
    catch (e) { /* swallow — preview is still readable as plain text */ }
};
