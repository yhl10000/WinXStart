namespace WinXStart.Models;

/// <summary>
/// Visual and behavior settings loaded from settings.json.
/// Edit the file and restart the app to apply changes.
/// </summary>
public class AppSettings
{
    // ── Window ──────────────────────────────────────────
    /// <summary>Window size as percentage of screen work area (10-100).</summary>
    public int WindowSizePercent { get; set; } = 70;

    /// <summary>Border corner radius in pixels.</summary>
    public double CornerRadius { get; set; } = 8;

    // ── Background ─────────────────────────────────────
    /// <summary>Background opacity percentage (0 = fully transparent, 100 = fully opaque).</summary>
    public int Opacity { get; set; } = 80;

    /// <summary>
    /// Gradient colors from top-left to bottom-right.
    /// Use standard hex: "#RRGGBB". Alpha is controlled by Opacity above.
    /// Any number of stops supported (evenly distributed).
    /// </summary>
    public string[] GradientColors { get; set; } =
    [
        "#FF0000",  // Red
        "#FF7700",  // Orange
        "#FFFF00",  // Yellow
        "#00CC00",  // Green
        "#0000FF",  // Blue
        "#4B0082",  // Indigo
        "#8B00FF"   // Violet
    ];

    /// <summary>Gradient direction: "Diagonal", "Horizontal", or "Vertical".</summary>
    public string GradientDirection { get; set; } = "Diagonal";

    // ── Border ──────────────────────────────────────────
    /// <summary>Border color in "#AARRGGBB" or "#RRGGBB" hex.</summary>
    public string BorderColor { get; set; } = "#66FFFFFF";

    /// <summary>Border thickness in pixels.</summary>
    public double BorderThickness { get; set; } = 1;

    // ── Search Box ─────────────────────────────────────
    /// <summary>Search box background in "#AARRGGBB" or "#RRGGBB" hex.</summary>
    public string SearchBoxBackground { get; set; } = "#44FFFFFF";

    /// <summary>Search box border in "#AARRGGBB" or "#RRGGBB" hex.</summary>
    public string SearchBoxBorder { get; set; } = "#88FFFFFF";

    // ── Bottom Bar ─────────────────────────────────────
    /// <summary>Bottom bar background in "#AARRGGBB" or "#RRGGBB" hex.</summary>
    public string BottomBarBackground { get; set; } = "#33000000";

    // ── Separator ──────────────────────────────────────
    /// <summary>Vertical separator between app list and tiles.</summary>
    public string SeparatorColor { get; set; } = "#44FFFFFF";

    // ── Fonts ───────────────────────────────────────────
    /// <summary>Pinned tile app name font size.</summary>
    public double TileFontSize { get; set; } = 12;

    /// <summary>Group header font size.</summary>
    public double GroupFontSize { get; set; } = 13;
}
