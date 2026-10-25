using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// VIP theme engine: ten strong, complete visual identities. Each theme
/// defines the glass panel tints, gradient accents, chat bubbles, aurora
/// glows, the red mic halo and legacy dialog palette. Applying a theme
/// swaps a ResourceDictionary into Application.Resources; windows that
/// reference theme brushes via DynamicResource repaint instantly, and
/// dialogs opened later pick the theme up automatically (the dictionary
/// lives at app level, above every window's local lookup).
/// All brushes are frozen — cheap, cross-thread safe, cinematic.
/// </summary>
public static class ThemeEngine
{
    /// <summary>Compact theme data. Every entry is an #AARRGGBB string unless noted.</summary>
    public sealed record Theme(
        string Name,
        string Bg, string Chat, string Side, string Row, string RowHover, string Field,
        string Fg, string Muted,
        string AccA, string AccB, string AccC, string AccH, string Glow,
        string UserB1, string UserB2, string AssB, string SysB, string Stroke,
        string Aurora1, string Aurora2,
        string Danger, string Success, string Warning);

    public static readonly IReadOnlyList<Theme> Themes = new[]
    {
        new Theme("Midnight Violet",
            "#D20D1220", "#AE121832", "#9812162A", "#1E23335C", "#3329417E", "#5A19213A",
            "#F2F5FF", "#94A0BE",
            "#5E7BFF", "#8B5CF6", "#B78CFF", "#8A9BFF", "#668B5CF6",
            "#E65E7BFF", "#D98B5CF6", "#D8181F33", "#66243048", "#33FFFFFF",
            "#7F5E7BFF", "#668B5CF6",
            "#F87171", "#4ADE80", "#FBBF24"),

        new Theme("Crimson Ember",
            "#D8120B0D", "#B81A1014", "#A5160C10", "#2A372329", "#4A3D3038", "#5F2A1F26",
            "#FFF2F0F0", "#B9968FA5",
            "#FF5A5A", "#E23636", "#FFB199", "#FF6B6B", "#99E23636",
            "#E8FF5B52", "#D8D93B34", "#D41A1216", "#66391F22", "#38FFFFFF",
            "#8FFF5A4D", "#66FF7B54",
            "#FF6B6B", "#34D399", "#FBBF24"),

        new Theme("Emerald Glass",
            "#D109140F", "#B80E1D15", "#A30C1912", "#1E293526", "#33405A38", "#5A20331F",
            "#EFFFF4EC", "#8FAEA0",
            "#34D399", "#10B981", "#6EE7B7", "#4BE3A8", "#8010B981",
            "#E512B981", "#D834D399", "#D40E1B15", "#66203327", "#30FFFFFF",
            "#7F10B981", "#6610B981",
            "#F87171", "#34D399", "#FBBF24"),

        new Theme("Royal Gold",
            "#D312100A", "#B91C1710", "#A619140B", "#30362E18", "#4A5A4426", "#66201C67",
            "#FFFAF0E6", "#C4B894",
            "#FFB84D", "#D9A441", "#FFE29A", "#FFC959", "#99D9A441",
            "#E8FFB84D", "#D8D9A441", "#D41B1710", "#66373420", "#38FFFFFF",
            "#8FD9A441", "#66FFB84D",
            "#F87171", "#34D399", "#FBBF24"),

        new Theme("Neon Blade",
            "#DE07080E", "#BD0A1020", "#22090C18", "#1A284048", "#33509090", "#5A0F2A3C",
            "#E8FBFFFF", "#7FA8BA",
            "#22D3EE", "#3B82F6", "#67E8F9", "#38CFF8", "#803B82F6",
            "#E522D3EE", "#D83B82F6", "#D60D1626", "#66163548", "#3FFFFFFF",
            "#8F22D3EE", "#663B82F6",
            "#FF5C5C", "#2DD4BF", "#FACC15"),

        new Theme("Rose Noir",
            "#D3160F19", "#BC201625", "#A8181320", "#2D38242C", "#4D4A2F3F", "#65332A38",
            "#FFF0F5FA", "#C39AAE",
            "#F472B6", "#EC4899", "#FDA4AF", "#FF85C4", "#99EC4899",
            "#E8F472B6", "#D8EC4899", "#D61C1220", "#66392E36", "#34FFFFFF",
            "#8FEC4899", "#66F472B6",
            "#FB7185", "#34D399", "#FBBF24"),

        new Theme("Frost Light",
            "#E9F2F5FB", "#D8FEFEFE", "#C8F4F7FD", "#33FFFFFF", "#55FFFFFF", "#88FFFFFF",
            "#1B2438", "#5C6B87",
            "#4B6BFB", "#7C5CFF", "#B59CFF", "#3E5FF8", "#557C5CFF",
            "#E84B6BFB", "#D87C5CFF", "#E6FFFFFF", "#99E6ECF6", "#22070F1F",
            "#554B6BFB", "#447C5CFF",
            "#DC3545", "#1B9E5C", "#C77700"),

        new Theme("Carbon Mono",
            "#DB0C0D10", "#B915161A", "#22101114", "#2E26272E", "#42443C46", "#621C1D20",
            "#FAFAFB", "#9EA1A8",
            "#E8ECF5", "#9AA3B2", "#FFFFFF", "#FFFFFF", "#559AA3B2",
            "#E83E434F", "#D85A6070", "#D5151619", "#66262A2E", "#30FFFFFF",
            "#4F9AA3B2", "#33E8ECF5",
            "#FF4D4D", "#4ADE80", "#FBBF24"),

        new Theme("Synthwave",
            "#D6120B24", "#BC1A1039", "#A8160E2F", "#33402E1A", "#55602E1A", "#63232E5A",
            "#F5EAFC", "#B493C4",
            "#FF3DDB", "#FF6EC7", "#FFB86C", "#FF5FE0", "#99FF3DDB",
            "#E8FF3DDB", "#D8FF6EC7", "#D6160C34", "#66392656", "#34FFFFFF",
            "#8FFF3DDB", "#66FFB86C",
            "#FF6B8A", "#4ADE80", "#FFD166"),

        new Theme("Stealth Green",
            "#D60B120C", "#BC0E1E12", "#A80C1A0F", "#2C382618", "#4C5A3E2A", "#631D3A20",
            "#F0FFF0F0", "#8FBA9A",
            "#A3E635", "#65A30D", "#BEF264", "#BFF34A", "#9965A30D",
            "#E8A3E635", "#D865A30D", "#D60D1D11", "#66293821", "#30FFFFFF",
            "#8F65A30D", "#66A3E635",
            "#F87171", "#34D399", "#FBBF24"),
    };

    private static ResourceDictionary? _current;
    private static string _currentName = string.Empty;

    public static string CurrentName => _currentName;

    public static string[] ThemeNames
    {
        get
        {
            var names = new string[Themes.Count];
            for (var i = 0; i < Themes.Count; i++) names[i] = Themes[i].Name;
            return names;
        }
    }

    /// <summary>Maps legacy/unknown theme names onto the current catalog.</summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Themes[0].Name;
        if (string.Equals(name, "Dark", StringComparison.OrdinalIgnoreCase)) return Themes[0].Name;
        foreach (var t in Themes)
        {
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t.Name;
        }
        return Themes[0].Name;
    }

    /// <summary>
    /// Applies a theme (idempotent). Live for DynamicResource consumers;
    /// new windows pick it up even if they use StaticResource.
    /// </summary>
    public static void Apply(string? name)
    {
        var theme = Find(Normalize(name));
        if (_currentName == theme.Name && _current is not null) return;

        var dict = Build(theme);

        var app = Application.Current;
        if (app is null)
        {
            return; // designer / safety
        }

        var merged = app.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }
        merged.Add(dict);
        _current = dict;
        _currentName = theme.Name;
    }

    private static Theme Find(string name)
    {
        foreach (var t in Themes)
        {
            if (string.Equals(t.Name, name, StringComparison.Ordinal)) return t;
        }
        return Themes[0];
    }

    private static ResourceDictionary Build(Theme t)
    {
        var d = new ResourceDictionary();

        // ---- Glass surfaces ----
        d["GlassBackdrop"] = Solid(t.Bg);
        d["ChatGlass"] = Solid(t.Chat);
        d["SideGlass"] = Solid(t.Side);
        d["RowGlass"] = Solid(t.Row);
        d["RowHoverGlass"] = Solid(t.RowHover);
        d["FieldGlass"] = Solid(t.Field);
        d["CardGlass"] = Solid(t.Chat);
        d["HoverGlass"] = Solid(Boost(t.RowHover, 0.35));
        d["PressGlass"] = Solid(Boost(t.RowHover, 0.25));
        d["ThumbGlass"] = Solid("#30FFFFFF");
        if (string.Equals(t.Name, "Frost Light", StringComparison.Ordinal))
        {
            d["ThumbGlass"] = Solid("#44070F1F");
            d["ThumbHover"] = Solid("#77070F1F");
        }
        else
        {
            d["ThumbHover"] = Solid("#66FFFFFF");
        }

        // ---- Strokes ----
        if (string.Equals(t.Name, "Frost Light", StringComparison.Ordinal))
        {
            d["GlassStroke"] = Solid("#1C0A1020");
            d["GlassStrokeStrong"] = Solid("#330A1020");
            d["AssistantNameColor"] = Solid("#4059C8");
            d["BubbleStroke"] = Solid("#22070F1F");
        }
        else
        {
            d["GlassStroke"] = Solid("#1CFFFFFF");
            d["GlassStrokeStrong"] = Solid("#33FFFFFF");
            d["AssistantNameColor"] = SolidFrom(Lighten(t.AccB, 0.35));
            d["BubbleStroke"] = Solid(t.Stroke);
        }

        // ---- Text + states ----
        d["Foreground"] = Solid(t.Fg);
        d["MutedForeground"] = Solid(t.Muted);
        d["StateNeutral"] = Solid(t.Muted);
        d["Success"] = Solid(t.Success);
        d["Danger"] = Solid(t.Danger);
        d["Warning"] = Solid(t.Warning);

        // ---- Accents ----
        d["Accent"] = Solid(t.AccB);
        d["AccentHover"] = Solid(t.AccH);
        d["AccentGlow"] = Solid(t.Glow);
        d["AccentDeep"] = Solid(t.AccC);
        d["AccentGradientBrush"] = Gradient(t.AccA, t.AccB, t.AccC, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        d["BrandBrush"] = Gradient(Lighten(t.AccA, 0.28), Lighten(t.AccB, 0.22), Lighten(t.AccC, 0.25), new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));

        // ---- Bubbles ----
        d["UserBubble"] = Gradient(t.UserB1, t.UserB2, new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        d["AssistantBubble"] = Solid(t.AssB);
        d["SystemBubble"] = Solid(t.SysB);

        // ---- Halo (user request: always RED) ----
        d["HaloBrush"] = Solid("#E6FF4D4D");

        // ---- Aurora glows ----
        d["AuroraBrushA"] = Radial(t.Aurora1);
        d["AuroraBrushB"] = Radial(t.Aurora2);

        // ---- Legacy solids for dialogs (opaque so they stay readable) ----
        d["WindowBackground"] = SolidOpaque(t.Bg);
        d["PanelBackground"] = SolidOpaque(t.Chat);
        d["SidebarBackground"] = SolidOpaque(t.Side);

        // Freeze everything: performance + thread safety.
        foreach (var key in d.Keys)
        {
            if (d[key] is Freezable f && f.CanFreeze) f.Freeze();
        }

        return d;
    }

    // ---------- helpers ----------

    private static Color C(string hex)
    {
        // Accepts #AARRGGBB or #RRGGBB.
        var s = hex.TrimStart('#');
        byte a = 0xFF;
        if (s.Length == 8)
        {
            a = Convert.ToByte(s.Substring(0, 2), 16);
            s = s.Substring(2);
        }
        var r = Convert.ToByte(s.Substring(0, 2), 16);
        var g = Convert.ToByte(s.Substring(2, 2), 16);
        var b = Convert.ToByte(s.Substring(4, 2), 16);
        return Color.FromArgb(a, r, g, b);
    }

    private static SolidColorBrush Solid(string hex)
    {
        var b = new SolidColorBrush(C(hex));
        return b;
    }

    private static SolidColorBrush SolidOpaque(string hex)
    {
        var c = C(hex);
        var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        return b;
    }

    /// <summary>Lightens toward white by factor [0..1] — used to derive "lite" brand stops.</summary>
    private static Color Lighten(string hex, double factor)
    {
        var c = C(hex);
        byte L(byte v) => (byte)Math.Min(255, v + (255 - v) * factor);
        return Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B));
    }

    /// <summary>Boosts alpha of an ARGB string (for hover/press states).</summary>
    private static string Boost(string hex, double deltaAlpha)
    {
        var s = hex.TrimStart('#');
        var a = Convert.ToInt32(s.Substring(0, 2), 16);
        var boosted = Math.Min(255, (int)(a + 255 * deltaAlpha));
        // Regenerate alpha from the boosted value; rest unchanged.
        var rgb = s.Length == 8 ? s.Substring(2) : s;
        return $"#{boosted:X2}{rgb}";
    }

    private static SolidColorBrush SolidFrom(Color c) => new(c);

    private static LinearGradientBrush Gradient(string from, string to, System.Windows.Point start, System.Windows.Point end)
    {
        var g = new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end,
        };
        g.GradientStops.Add(new GradientStop(C(from), 0));
        g.GradientStops.Add(new GradientStop(C(to), 1));
        return g;
    }

    private static LinearGradientBrush Gradient(string a, string b, string c, System.Windows.Point start, System.Windows.Point end)
        => Gradient(C(a), C(b), C(c), start, end);

    private static LinearGradientBrush Gradient(Color a, Color b, Color c, System.Windows.Point start, System.Windows.Point end)
    {
        var g = new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end,
        };
        g.GradientStops.Add(new GradientStop(a, 0));
        g.GradientStops.Add(new GradientStop(b, 0.55));
        g.GradientStops.Add(new GradientStop(c, 1));
        return g;
    }

    private static RadialGradientBrush Radial(string hex)
    {
        var c = C(hex);
        var transparent = Color.FromArgb(0, c.R, c.G, c.B);
        var b = new RadialGradientBrush
        {
            Center = new System.Windows.Point(0.5, 0.5),
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        b.GradientStops.Add(new GradientStop(c, 0));
        b.GradientStops.Add(new GradientStop(transparent, 1));
        return b;
    }
}
