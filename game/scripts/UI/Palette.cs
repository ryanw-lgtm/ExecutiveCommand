using Godot;

namespace ExecutiveCommand.Game.UI;

/// <summary>
/// Semantic color tokens (Rune / UI-UX Pro Max "financial dashboard" dark scheme, adapted).
/// Rules: no pure black/white; elevated surfaces are lighter; green/red only carry data meaning and are
/// always paired with a symbol (▲▼●); text ≥ 4.5:1 contrast on its surface; one brand accent (executive gold).
/// </summary>
public static class Palette
{
    // Surfaces (darkest → lightest)
    public static readonly Color BgBase = new("070b14");
    public static readonly Color BgSunken = new("0a101d");
    public static readonly Color BgSurface = new("0f1627");
    public static readonly Color BgElevated = new("172035");
    public static readonly Color BgHover = new("1e2a44");
    public static readonly Color Border = new("26324b");
    public static readonly Color BorderStrong = new("3a4868");

    // Text
    public static readonly Color TextPrimary = new("eef2f7");
    public static readonly Color TextSecondary = new("a3afc2");
    public static readonly Color TextMuted = new("75829a");

    // Brand accent: executive gold (deliberately not the default blue/indigo).
    public static readonly Color Accent = new("e3a93b");
    public static readonly Color AccentHover = new("f0bb55");
    public static readonly Color AccentPressed = new("c48d27");
    public static readonly Color AccentSoft = new(0.89f, 0.66f, 0.23f, 0.14f);
    public static readonly Color OnAccent = new("1a1206");

    // Data semantics
    public static readonly Color Success = new("3cc48a");
    public static readonly Color Danger = new("ef5b5b");
    public static readonly Color Warning = new("f0883a");
    public static readonly Color Info = new("62a0f2");

    // Legacy aliases used across views.
    public static Color Background => BgBase;
    public static Color Surface => BgSurface;
    public static Color SurfaceRaised => BgElevated;
    public static Color Charcoal => BgSunken;
    public static Color Text => TextPrimary;
    public static Color Muted => TextSecondary;
    public static Color AccentDim => AccentSoft;
    public static Color Good => Success;
    public static Color Bad => Danger;
    public static Color Warn => Warning;

    public static Color ForScore(double v, double good = 65, double bad = 45) => v >= good ? Success : v <= bad ? Danger : Warning;

    public static string Hex(Color c) => c.ToHtml(false);
}

/// <summary>Type scale 12 / 14 / 16 / 18 / 24 / 32 on IBM Plex Sans; all numbers in IBM Plex Mono.</summary>
public static class Fonts
{
    private static FontFile Load(string file) => GD.Load<FontFile>($"res://assets/fonts/{file}.woff");
    public static readonly Font Sans = Load("IBMPlexSans-Regular");
    public static readonly Font SansMedium = Load("IBMPlexSans-Medium");
    public static readonly Font SansSemi = Load("IBMPlexSans-SemiBold");
    public static readonly Font Mono = Load("IBMPlexMono-Regular");
    public static readonly Font MonoMedium = Load("IBMPlexMono-Medium");
    public static readonly Font Caption = new FontVariation { BaseFont = SansSemi, SpacingGlyph = 1 };

    public const int Xs = 12, Sm = 14, Base = 16, Lg = 18, Xl = 24, Xxl = 32;
}

/// <summary>Motion tokens: short ease-out entrances, faster exits, none for keyboard-driven or high-frequency actions.</summary>
public static class Motion
{
    public static bool Reduced { get; set; }
    public const double Press = 0.12, Fade = 0.18, Exit = 0.14;

    public static void FadeIn(Control c, double duration = Fade, float rise = 6)
    {
        if (Reduced || !c.IsInsideTree()) return;
        c.Modulate = new Color(1, 1, 1, 0);
        var t = c.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        t.TweenProperty(c, "modulate:a", 1.0, duration);
        if (rise != 0 && c.GetParent() is not Container)
        {
            var p = c.Position;
            c.Position = p + new Vector2(0, rise);
            t.TweenProperty(c, "position", p, duration);
        }
    }
}

public static class GameTheme
{
    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 8, int pad = 16, int borderWidth = 1)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad * 0.75f, ContentMarginBottom = pad * 0.75f,
            AntiAliasing = true,
        };
        if (border is { } b) { sb.BorderColor = b; sb.SetBorderWidthAll(borderWidth); }
        return sb;
    }

    /// <summary>2px accent focus ring drawn outside the control (keyboard users must always see focus).</summary>
    public static StyleBoxFlat FocusRing(int radius = 8)
    {
        var sb = Box(new Color(0, 0, 0, 0), Palette.Accent, radius, 0, 2);
        sb.ExpandMarginLeft = sb.ExpandMarginRight = sb.ExpandMarginTop = sb.ExpandMarginBottom = 2;
        return sb;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFont = Fonts.Sans, DefaultFontSize = Fonts.Base };
        foreach (var type in new[] { "Label", "Button", "LineEdit", "CheckBox", "OptionButton", "ItemList", "RichTextLabel", "TextEdit", "SpinBox", "PopupMenu", "TooltipLabel" })
            t.SetColor("font_color", type, Palette.TextPrimary);
        t.SetColor("default_color", "RichTextLabel", Palette.TextPrimary);
        t.SetFont("bold_font", "RichTextLabel", Fonts.SansSemi);
        t.SetFont("mono_font", "RichTextLabel", Fonts.Mono);
        t.SetColor("font_placeholder_color", "LineEdit", Palette.TextMuted);
        t.SetColor("caret_color", "LineEdit", Palette.Accent);
        t.SetColor("selection_color", "LineEdit", Palette.AccentSoft);

        t.SetStylebox("panel", "PanelContainer", Box(Palette.BgSurface, Palette.Border));
        t.SetStylebox("panel", "Panel", Box(Palette.BgBase, null, 0, 0));

        // Buttons: quiet by default, gold border on hover, gold ring on focus.
        t.SetStylebox("normal", "Button", Box(Palette.BgElevated, Palette.Border, 6, 14));
        t.SetStylebox("hover", "Button", Box(Palette.BgHover, Palette.BorderStrong, 6, 14));
        t.SetStylebox("pressed", "Button", Box(Palette.BgSunken, Palette.Accent, 6, 14));
        t.SetStylebox("hover_pressed", "Button", Box(Palette.BgHover, Palette.Accent, 6, 14));
        t.SetStylebox("disabled", "Button", Box(Palette.BgSunken, Palette.Border, 6, 14));
        t.SetStylebox("focus", "Button", FocusRing(6));
        t.SetFont("font", "Button", Fonts.SansMedium);
        t.SetFontSize("font_size", "Button", Fonts.Sm + 1);
        t.SetColor("font_hover_color", "Button", Palette.TextPrimary);
        t.SetColor("font_pressed_color", "Button", Palette.TextPrimary);
        t.SetColor("font_focus_color", "Button", Palette.TextPrimary);
        t.SetColor("font_disabled_color", "Button", Palette.TextMuted);
        t.SetColor("icon_normal_color", "Button", Palette.TextSecondary);
        t.SetColor("icon_hover_color", "Button", Palette.TextPrimary);
        t.SetColor("icon_pressed_color", "Button", Palette.Accent);
        t.SetColor("icon_focus_color", "Button", Palette.TextPrimary);
        t.SetConstant("h_separation", "Button", 10);

        t.SetStylebox("normal", "LineEdit", Box(Palette.BgSunken, Palette.Border, 6, 12));
        t.SetStylebox("focus", "LineEdit", Box(Palette.BgSunken, Palette.Accent, 6, 12, 2));
        t.SetStylebox("normal", "TextEdit", Box(Palette.BgSunken, Palette.Border, 6, 12));
        t.SetStylebox("normal", "SpinBox", Box(Palette.BgSunken, Palette.Border, 6, 8));
        t.SetStylebox("panel", "ItemList", Box(Palette.BgSunken, Palette.Border, 8, 6));
        t.SetStylebox("focus", "ItemList", FocusRing(8));
        t.SetColor("font_selected_color", "ItemList", Palette.TextPrimary);
        t.SetColor("font_hovered_color", "ItemList", Palette.TextPrimary);
        t.SetStylebox("selected", "ItemList", Box(Palette.AccentSoft, null, 6, 6));
        t.SetStylebox("selected_focus", "ItemList", Box(Palette.AccentSoft, Palette.Accent, 6, 6));
        t.SetStylebox("hovered", "ItemList", Box(Palette.BgHover, null, 6, 6));
        t.SetConstant("v_separation", "ItemList", 8);
        t.SetStylebox("normal", "RichTextLabel", Box(Palette.BgSunken, Palette.Border, 8, 16));
        t.SetStylebox("focus", "RichTextLabel", Box(Palette.BgSunken, Palette.Border, 8, 16));

        t.SetColor("font_color", "CheckBox", Palette.TextSecondary);
        t.SetColor("font_pressed_color", "CheckBox", Palette.TextPrimary);
        t.SetColor("font_hover_color", "CheckBox", Palette.TextPrimary);
        t.SetStylebox("focus", "CheckBox", FocusRing(6));
        t.SetStylebox("normal", "OptionButton", Box(Palette.BgSunken, Palette.Border, 6, 12));
        t.SetStylebox("hover", "OptionButton", Box(Palette.BgHover, Palette.BorderStrong, 6, 12));
        t.SetStylebox("focus", "OptionButton", FocusRing(6));

        t.SetStylebox("background", "ProgressBar", Box(Palette.BgSunken, null, 4, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Palette.Accent, null, 4, 0));

        t.SetStylebox("panel", "PopupMenu", Box(Palette.BgElevated, Palette.BorderStrong, 8, 8));
        t.SetStylebox("panel", "PopupPanel", Box(Palette.BgElevated, Palette.BorderStrong, 10, 12));
        t.SetStylebox("panel", "TooltipPanel", Box(Palette.BgElevated, Palette.BorderStrong, 6, 10));
        t.SetFontSize("font_size", "TooltipLabel", Fonts.Sm);
        t.SetStylebox("panel", "AcceptDialog", Box(Palette.BgSurface, null, 10, 20));
        t.SetStylebox("embedded_border", "Window", Box(Palette.BgSurface, Palette.BorderStrong, 10, 4));
        t.SetStylebox("embedded_unfocused_border", "Window", Box(Palette.BgSurface, Palette.Border, 10, 4));
        t.SetColor("title_color", "Window", Palette.TextSecondary);
        t.SetFont("title_font", "Window", Fonts.SansMedium);

        t.SetStylebox("grabber", "VScrollBar", Box(Palette.BorderStrong, null, 4, 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", Box(Palette.TextMuted, null, 4, 0));
        t.SetStylebox("scroll", "VScrollBar", Box(new Color(0, 0, 0, 0), null, 4, 0));
        t.SetConstant("separation", "VBoxContainer", 8);
        t.SetConstant("separation", "HBoxContainer", 8);
        t.SetConstant("h_separation", "GridContainer", 16);
        t.SetConstant("v_separation", "GridContainer", 8);
        return t;
    }
}

/// <summary>Phosphor icons rasterized at their exact on-screen size (crisp at any UI scale).</summary>
public static class Icons
{
    private static readonly Dictionary<(string, int), Texture2D> Cache = new();

    public static Texture2D? Get(string name, int px = 20)
    {
        if (Cache.TryGetValue((name, px), out var t)) return t;
        if (!IconData.Svg.TryGetValue(name, out var svg)) return null;
        var img = new Image();
        if (img.LoadSvgFromString(svg, px / 256f) != Error.Ok) return null;
        return Cache[(name, px)] = ImageTexture.CreateFromImage(img);
    }
}

/// <summary>Draws a small trend line; used inside KPI tiles.</summary>
public partial class Sparkline : Control
{
    public double[] Values { get; set; } = Array.Empty<double>();
    public Color LineColor { get; set; } = Palette.TextSecondary;

    public Sparkline() { CustomMinimumSize = new Vector2(96, 28); MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        if (Values.Length < 2) return;
        double min = Values.Min(), max = Values.Max();
        if (Math.Abs(max - min) < 1e-9) { max += 1; min -= 1; }
        var size = Size;
        var pts = Values.Select((v, i) => new Vector2(
            (float)(i / (double)(Values.Length - 1) * (size.X - 4) + 2),
            (float)(size.Y - 3 - (v - min) / (max - min) * (size.Y - 6)))).ToArray();
        DrawPolyline(pts, LineColor, 1.6f, true);
        DrawCircle(pts[^1], 2.6f, LineColor);
    }
}

/// <summary>Factory helpers so screens read like layout descriptions.</summary>
public static class Ui
{
    public static Label Label(string text, int size = Fonts.Base, Color? color = null, bool wrap = false, Font? font = null)
    {
        var l = new Label { Text = text };
        if (size != Fonts.Base) l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (font != null) l.AddThemeFontOverride("font", font);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.CustomMinimumSize = new Vector2(50, 0); }
        return l;
    }

    public static Label Heading(string text, int size = Fonts.Xl) => Label(text, size, Palette.TextPrimary, font: Fonts.SansSemi);
    public static Label Caption(string text) => Label(text.ToUpperInvariant(), Fonts.Xs, Palette.TextMuted, font: Fonts.Caption);
    /// <summary>Numbers are always monospaced so columns and changes line up.</summary>
    public static Label Num(string text, int size = Fonts.Base, Color? color = null) => Label(text, size, color, font: size >= Fonts.Lg ? Fonts.MonoMedium : Fonts.Mono);

    public static TextureRect Icon(string name, int px = 20, Color? tint = null)
    {
        var r = new TextureRect { Texture = Icons.Get(name, px), StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(px, px), MouseFilter = Control.MouseFilterEnum.Ignore };
        r.Modulate = tint ?? Palette.TextSecondary;
        return r;
    }

    public static Button Button(string text, Action onPressed, bool primary = false, string? icon = null)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 36), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        if (icon != null) b.Icon = Icons.Get(icon, 18);
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", GameTheme.Box(Palette.Accent, null, 6, 14));
            b.AddThemeStyleboxOverride("hover", GameTheme.Box(Palette.AccentHover, null, 6, 14));
            b.AddThemeStyleboxOverride("pressed", GameTheme.Box(Palette.AccentPressed, null, 6, 14));
            b.AddThemeStyleboxOverride("hover_pressed", GameTheme.Box(Palette.AccentPressed, null, 6, 14));
            foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
                b.AddThemeColorOverride(k, Palette.OnAccent);
            b.AddThemeFontOverride("font", Fonts.SansSemi);
        }
        b.Pressed += onPressed;
        // Tactile press: scale to 0.97 and back (mouse only — keyboard activations are not animated).
        b.Resized += () => b.PivotOffset = b.Size / 2;
        b.ButtonDown += () => { if (!Motion.Reduced) b.CreateTween().TweenProperty(b, "scale", new Vector2(0.97f, 0.97f), Motion.Press).SetEase(Tween.EaseType.Out); };
        b.ButtonUp += () => { if (!Motion.Reduced) b.CreateTween().TweenProperty(b, "scale", Vector2.One, Motion.Press).SetEase(Tween.EaseType.Out); else b.Scale = Vector2.One; };
        return b;
    }

    public static PanelContainer Card(Control child, Color? bg = null, Color? border = null, int pad = 16)
    {
        var p = new PanelContainer();
        if (bg != null || border != null || pad != 16) p.AddThemeStyleboxOverride("panel", GameTheme.Box(bg ?? Palette.BgSurface, border ?? Palette.Border, 8, pad));
        p.AddChild(child);
        return p;
    }

    /// <summary>Card with a colored left edge — used for alerts and the attention list.</summary>
    public static PanelContainer AccentCard(Control child, Color edge)
    {
        var sb = GameTheme.Box(Palette.BgSurface, Palette.Border, 8, 16);
        sb.BorderWidthLeft = 4;
        sb.BorderColor = Palette.Border;
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", sb);
        // Godot borders are single-colored; draw the edge with an inner strip instead.
        var strip = new ColorRect { Color = edge, CustomMinimumSize = new Vector2(3, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var row = HBox(14, strip, child);
        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sb.BorderWidthLeft = 1;
        p.AddChild(row);
        return p;
    }

    public static VBoxContainer VBox(int sep = 8, params Control[] children)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", sep);
        foreach (var c in children) v.AddChild(c);
        return v;
    }

    public static HBoxContainer HBox(int sep = 8, params Control[] children)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", sep);
        foreach (var c in children) h.AddChild(c);
        return h;
    }

    public static Control Spacer(bool horizontal = true)
    {
        var c = new Control();
        if (horizontal) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; else c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static ScrollContainer Scroll(Control child)
    {
        var s = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        child.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        s.AddChild(child);
        return s;
    }

    public static Control Gap(int px) => new Control { CustomMinimumSize = new Vector2(px, px) };

    /// <summary>Trend marker: ▲/▼ with value, colored by whether the change is good — never color alone.</summary>
    public static Label Trend(double delta, bool higherIsBetter, string format = "+0.0;-0.0", string suffix = "")
    {
        if (Math.Abs(delta) < 0.05) return Label("— no change", Fonts.Xs, Palette.TextMuted, font: Fonts.Mono);
        bool good = delta > 0 == higherIsBetter;
        return Label($"{(delta > 0 ? "▲" : "▼")} {delta.ToString(format)}{suffix}", Fonts.Xs, good ? Palette.Success : Palette.Danger, font: Fonts.Mono);
    }

    /// <summary>KPI tile: caption, one big monospaced number, optional trend and sparkline, supporting line.</summary>
    public static PanelContainer Tile(string caption, string value, string sub = "", Color? accent = null, Control? trend = null, double[]? spark = null, string? tooltip = null)
    {
        var top = HBox(8, Caption(caption), Spacer());
        if (trend != null) top.AddChild(trend);
        var valueRow = HBox(12, Num(value, 28, accent ?? Palette.TextPrimary), Spacer());
        // A trend line needs at least three months of data to say anything.
        if (spark is { Length: >= 3 } && spark.Max() - spark.Min() > 1e-6) { var sl = new Sparkline { Values = spark, LineColor = Palette.TextSecondary, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; valueRow.AddChild(sl); }
        var v = VBox(6, top, valueRow);
        if (sub.Length > 0) v.AddChild(Label(sub, Fonts.Sm, Palette.TextSecondary, wrap: true));
        var card = Card(v);
        card.CustomMinimumSize = new Vector2(200, 0);
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (tooltip != null) card.TooltipText = tooltip;
        return card;
    }

    /// <summary>Empty state: what's missing + how to fill it.</summary>
    public static Control Empty(string what, string how, string icon = "info")
    {
        var v = VBox(4, Label(what, Fonts.Base, Palette.TextSecondary, font: Fonts.SansMedium), Label(how, Fonts.Sm, Palette.TextMuted, wrap: true));
        return HBox(12, Icon(icon, 22, Palette.TextMuted), v);
    }

    public static ProgressBar Bar(double value, double max = 100, Color? fill = null)
    {
        var b = new ProgressBar { MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(120, 8), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        if (fill is { } f) b.AddThemeStyleboxOverride("fill", GameTheme.Box(f, null, 4, 0));
        return b;
    }

    /// <summary>Pill badge, e.g. a position or a status.</summary>
    public static PanelContainer Pill(string text, Color color)
    {
        var sb = GameTheme.Box(new Color(color, 0.14f), new Color(color, 0.45f), 10, 8);
        sb.ContentMarginTop = 1; sb.ContentMarginBottom = 1;
        var p = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        p.AddThemeStyleboxOverride("panel", sb);
        p.AddChild(Label(text, Fonts.Xs, color, font: Fonts.SansMedium));
        return p;
    }

    /// <summary>Table: header row + rows. Column 0 is text (expands); other columns are right-aligned monospaced numbers.</summary>
    public static GridContainer Table(string[] headers, IEnumerable<string[]> rows, Func<int, int, Color?>? colorAt = null)
    {
        var g = new GridContainer { Columns = headers.Length, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        g.AddThemeConstantOverride("v_separation", 10);
        foreach (var (h, i) in headers.Select((h, i) => (h, i)))
        {
            var c = Caption(h);
            if (i > 0) c.HorizontalAlignment = HorizontalAlignment.Right;
            g.AddChild(c);
        }
        int r = 0;
        foreach (var row in rows)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                string text = c < row.Length ? row[c] : "";
                bool numeric = c > 0 && text.Length > 0 && (char.IsDigit(text[0]) || text[0] is '$' or '-' or '+' or '▲' or '▼');
                var l = Label(text, Fonts.Sm, colorAt?.Invoke(r, c), font: numeric ? Fonts.Mono : null);
                if (c == 0) { l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(160, 0); }
                else l.HorizontalAlignment = HorizontalAlignment.Right;
                g.AddChild(l);
            }
            r++;
        }
        return g;
    }

    public static Control Avatar(string name, int px = 40, Color? color = null)
    {
        var initials = string.Concat(name.Replace("Dr. ", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0]));
        var sb = GameTheme.Box(color ?? Palette.BgHover, Palette.BorderStrong, px / 2, 0);
        var p = new PanelContainer { CustomMinimumSize = new Vector2(px, px), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        p.AddThemeStyleboxOverride("panel", sb);
        var l = Label(initials, px >= 48 ? Fonts.Lg : Fonts.Sm, Palette.TextPrimary, font: Fonts.SansSemi);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.VerticalAlignment = VerticalAlignment.Center;
        p.AddChild(l);
        return p;
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    public static string Esc(string s) => s.Replace("[", "[lb]");
}
