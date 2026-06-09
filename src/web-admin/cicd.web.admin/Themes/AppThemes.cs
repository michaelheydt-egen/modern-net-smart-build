using MudBlazor;
using MudBlazor.Utilities;

namespace Cicd.Web.Admin.Themes;

/// <summary>
/// Named MudBlazor themes used by the app. Each theme is a self-contained
/// <see cref="MudTheme"/>; the active one is swapped wholesale and exposed
/// via <c>&lt;MudThemeProvider Theme="..." IsDarkMode="..." /&gt;</c>.
/// </summary>
public static class AppThemes
{
    public const string SolarizedDarkId  = "solarized-dark";
    public const string SolarizedLightId = "solarized-light";
    public const string NordId           = "nord";
    public const string OneDarkId        = "one-dark";
    public const string MonokaiId        = "monokai";
    public const string MaterialDarkId   = "material-dark";
    public const string DefaultId        = "default";

    public sealed record Entry(string Id, string Label, MudTheme Theme, bool IsDarkMode);

    public static IReadOnlyList<Entry> All { get; } = new[]
    {
        new Entry(SolarizedDarkId,  "Solarized Dark",  CreateSolarizedDark(),  IsDarkMode: true),
        new Entry(SolarizedLightId, "Solarized Light", CreateSolarizedLight(), IsDarkMode: false),
        new Entry(NordId,           "Nord",            CreateNord(),           IsDarkMode: true),
        new Entry(OneDarkId,        "One Dark",        CreateOneDark(),        IsDarkMode: true),
        new Entry(MonokaiId,        "Monokai",         CreateMonokai(),        IsDarkMode: true),
        new Entry(MaterialDarkId,   "Material Dark",   CreateMaterialDark(),   IsDarkMode: true),
        new Entry(DefaultId,        "Default",         CreateDefault(),        IsDarkMode: false),
    };

    public static Entry Resolve(string? id) =>
        All.FirstOrDefault(e => e.Id == id) ?? All[0];

    // --- Solarized palette ---
    //   base03  #002b36   base02  #073642   base01  #586e75   base00  #657b83
    //   base0   #839496   base1   #93a1a1   base2   #eee8d5   base3   #fdf6e3
    //   yellow  #b58900   orange  #cb4b16   red     #dc322f   magenta #d33682
    //   violet  #6c71c4   blue    #268bd2   cyan    #2aa198   green   #859900

    private static MudTheme CreateSolarizedDark() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary           = "#268bd2",
            Secondary         = "#2aa198",
            Tertiary          = "#6c71c4",
            Info              = "#2aa198",
            Success           = "#859900",
            Warning           = "#b58900",
            Error             = "#dc322f",
            Black             = "#002b36",
            Background        = "#002b36",
            BackgroundGray    = "#073642",
            Surface           = "#073642",
            AppbarBackground  = "#073642",
            AppbarText        = "#93a1a1",
            DrawerBackground  = "#073642",
            DrawerText        = "#839496",
            DrawerIcon        = "#93a1a1",
            TextPrimary       = "#93a1a1",
            TextSecondary     = "#839496",
            TextDisabled      = "#586e75",
            ActionDefault     = "#839496",
            ActionDisabled    = "#586e75",
            ActionDisabledBackground = "#073642",
            LinesDefault      = "#073642",
            LinesInputs       = "#586e75",
            TableLines        = "#073642",
            TableStriped      = new MudColor("#073642").SetAlpha(0.40).ToString(),
            TableHover        = new MudColor("#586e75").SetAlpha(0.10).ToString(),
            Divider           = "#073642",
            DividerLight      = "#586e75",
        },
    };

    private static MudTheme CreateSolarizedLight() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary          = "#268bd2",
            Secondary        = "#2aa198",
            Tertiary         = "#6c71c4",
            Info             = "#2aa198",
            Success          = "#586e0d",
            Warning          = "#7d5e00",
            Error            = "#b32026",
            Background       = "#fdf6e3",
            BackgroundGray   = "#eee8d5",
            Surface          = "#eee8d5",
            AppbarBackground = "#eee8d5",
            AppbarText       = "#586e75",
            DrawerBackground = "#eee8d5",
            DrawerText       = "#657b83",
            DrawerIcon       = "#586e75",
            TextPrimary      = "#586e75",
            TextSecondary    = "#657b83",
            TextDisabled     = "#93a1a1",
            ActionDefault    = "#657b83",
            ActionDisabled   = "#93a1a1",
            ActionDisabledBackground = "#eee8d5",
            LinesDefault     = "#eee8d5",
            LinesInputs      = "#93a1a1",
            TableLines       = "#eee8d5",
            TableStriped     = new MudColor("#eee8d5").SetAlpha(0.50).ToString(),
            TableHover       = new MudColor("#93a1a1").SetAlpha(0.10).ToString(),
            Divider          = "#eee8d5",
            DividerLight     = "#93a1a1",
        },
    };

    // --- Nord palette (Arctic Ice Studio) ---
    //   Polar Night : nord0 #2e3440  nord1 #3b4252  nord2 #434c5e  nord3 #4c566a
    //   Snow Storm  : nord4 #d8dee9  nord5 #e5e9f0  nord6 #eceff4
    //   Frost       : nord7 #8fbcbb  nord8 #88c0d0  nord9 #81a1c1  nord10 #5e81ac
    //   Aurora      : nord11 #bf616a (red)  nord12 #d08770 (orange)
    //                 nord13 #ebcb8b (yellow)  nord14 #a3be8c (green)  nord15 #b48ead (purple)

    private static MudTheme CreateNord() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary           = "#88c0d0",   // nord8 — frost
            Secondary         = "#5e81ac",   // nord10 — frost deep
            Tertiary          = "#b48ead",   // nord15 — aurora purple
            Info              = "#8fbcbb",   // nord7
            Success           = "#a3be8c",   // nord14
            Warning           = "#ebcb8b",   // nord13
            Error             = "#bf616a",   // nord11
            Black             = "#2e3440",
            Background        = "#2e3440",   // nord0
            BackgroundGray    = "#3b4252",   // nord1
            Surface           = "#3b4252",
            AppbarBackground  = "#3b4252",
            AppbarText        = "#eceff4",   // nord6
            DrawerBackground  = "#3b4252",
            DrawerText        = "#d8dee9",   // nord4
            DrawerIcon        = "#eceff4",
            TextPrimary       = "#eceff4",
            TextSecondary     = "#d8dee9",
            TextDisabled      = "#4c566a",   // nord3
            ActionDefault     = "#d8dee9",
            ActionDisabled    = "#4c566a",
            ActionDisabledBackground = "#3b4252",
            LinesDefault      = "#434c5e",   // nord2
            LinesInputs       = "#4c566a",
            TableLines        = "#434c5e",
            TableStriped      = new MudColor("#434c5e").SetAlpha(0.40).ToString(),
            TableHover        = new MudColor("#4c566a").SetAlpha(0.15).ToString(),
            Divider           = "#434c5e",
            DividerLight      = "#4c566a",
        },
    };

    // --- One Dark palette (Atom) ---
    //   bg          #282c34   bg-darker   #21252b   surface     #3a3f4b
    //   fg          #abb2bf   fg-mid      #828997   fg-dim      #5c6370
    //   red #e06c75  orange #d19a66  yellow #e5c07b  green #98c379
    //   cyan #56b6c2  blue #61afef  purple #c678dd

    private static MudTheme CreateOneDark() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary           = "#61afef",   // blue
            Secondary         = "#56b6c2",   // cyan
            Tertiary          = "#c678dd",   // purple
            Info              = "#56b6c2",
            Success           = "#98c379",
            Warning           = "#e5c07b",
            Error             = "#e06c75",
            Black             = "#21252b",
            Background        = "#282c34",
            BackgroundGray    = "#21252b",
            Surface           = "#3a3f4b",
            AppbarBackground  = "#21252b",
            AppbarText        = "#abb2bf",
            DrawerBackground  = "#21252b",
            DrawerText        = "#abb2bf",
            DrawerIcon        = "#abb2bf",
            TextPrimary       = "#abb2bf",
            TextSecondary     = "#828997",
            TextDisabled      = "#5c6370",
            ActionDefault     = "#abb2bf",
            ActionDisabled    = "#5c6370",
            ActionDisabledBackground = "#21252b",
            LinesDefault      = "#3e4451",
            LinesInputs       = "#5c6370",
            TableLines        = "#3e4451",
            TableStriped      = new MudColor("#3e4451").SetAlpha(0.40).ToString(),
            TableHover        = new MudColor("#5c6370").SetAlpha(0.15).ToString(),
            Divider           = "#3e4451",
            DividerLight      = "#5c6370",
        },
    };

    // --- Monokai palette ---
    //   bg #272822  bg-lighter #3e3d32  selection #49483e
    //   fg #f8f8f2  fg-dim #75715e
    //   pink #f92672  orange #fd971f  yellow #e6db74  green #a6e22e
    //   cyan #66d9ef  purple #ae81ff

    private static MudTheme CreateMonokai() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary           = "#66d9ef",   // cyan — calm accent for buttons/links
            Secondary         = "#f92672",   // signature pink
            Tertiary          = "#ae81ff",   // purple
            Info              = "#66d9ef",
            Success           = "#a6e22e",
            Warning           = "#fd971f",   // orange — distinct from yellow #e6db74 to leave room for it
            Error             = "#f92672",
            Black             = "#272822",
            Background        = "#272822",
            BackgroundGray    = "#3e3d32",
            Surface            = "#3e3d32",
            AppbarBackground  = "#3e3d32",
            AppbarText        = "#f8f8f2",
            DrawerBackground  = "#3e3d32",
            DrawerText        = "#f8f8f2",
            DrawerIcon        = "#f8f8f2",
            TextPrimary       = "#f8f8f2",
            TextSecondary     = "#cfcfc2",
            TextDisabled      = "#75715e",
            ActionDefault     = "#f8f8f2",
            ActionDisabled    = "#75715e",
            ActionDisabledBackground = "#3e3d32",
            LinesDefault      = "#49483e",
            LinesInputs       = "#75715e",
            TableLines        = "#49483e",
            TableStriped      = new MudColor("#49483e").SetAlpha(0.40).ToString(),
            TableHover        = new MudColor("#75715e").SetAlpha(0.15).ToString(),
            Divider           = "#49483e",
            DividerLight      = "#75715e",
        },
    };

    // --- Material Dark palette (Material Design 2 dark spec) ---
    //   Background  #121212  Elevated surfaces ramp by overlay opacity
    //   Surface     #1e1e1e (≈ 1dp)   Surface higher #242424 (≈ 8dp)
    //   Primary     #bb86fc (purple 200)   Primary variant #3700b3
    //   Secondary   #03dac6 (teal 200)
    //   Error       #cf6679
    //   On-surface  #ffffff at 87% (primary text), 60% (secondary), 38% (disabled)

    private static MudTheme CreateMaterialDark() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary           = "#bb86fc",
            Secondary         = "#03dac6",
            Tertiary          = "#3700b3",
            Info              = "#03dac6",
            Success           = "#00c853",
            Warning           = "#ffb74d",
            Error             = "#cf6679",
            Black             = "#000000",
            Background        = "#121212",
            BackgroundGray    = "#1e1e1e",
            Surface           = "#1e1e1e",
            AppbarBackground  = "#242424",
            AppbarText        = new MudColor("#ffffff").SetAlpha(0.87).ToString(),
            DrawerBackground  = "#1e1e1e",
            DrawerText        = new MudColor("#ffffff").SetAlpha(0.87).ToString(),
            DrawerIcon        = new MudColor("#ffffff").SetAlpha(0.87).ToString(),
            TextPrimary       = new MudColor("#ffffff").SetAlpha(0.87).ToString(),
            TextSecondary     = new MudColor("#ffffff").SetAlpha(0.60).ToString(),
            TextDisabled      = new MudColor("#ffffff").SetAlpha(0.38).ToString(),
            ActionDefault     = new MudColor("#ffffff").SetAlpha(0.87).ToString(),
            ActionDisabled    = new MudColor("#ffffff").SetAlpha(0.38).ToString(),
            ActionDisabledBackground = "#1e1e1e",
            LinesDefault      = new MudColor("#ffffff").SetAlpha(0.12).ToString(),
            LinesInputs       = new MudColor("#ffffff").SetAlpha(0.38).ToString(),
            TableLines        = new MudColor("#ffffff").SetAlpha(0.12).ToString(),
            TableStriped      = new MudColor("#ffffff").SetAlpha(0.04).ToString(),
            TableHover        = new MudColor("#ffffff").SetAlpha(0.08).ToString(),
            Divider           = new MudColor("#ffffff").SetAlpha(0.12).ToString(),
            DividerLight      = new MudColor("#ffffff").SetAlpha(0.06).ToString(),
        },
    };

    private static MudTheme CreateDefault() => new();
}
