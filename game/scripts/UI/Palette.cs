using Godot;

namespace ExecutiveCommand.Game.UI;

/// <summary>Dark navy / charcoal visual language. All colors come from here.</summary>
public static class Palette
{
    public static readonly Color Background = new("0e1626");
    public static readonly Color Surface = new("152238");
    public static readonly Color SurfaceRaised = new("1c2c47");
    public static readonly Color Charcoal = new("1b1f27");
    public static readonly Color Border = new("2a3b5a");
    public static readonly Color Text = new("dce3ee");
    public static readonly Color Muted = new("8a97ad");
    public static readonly Color Accent = new("4c8dff");
    public static readonly Color AccentDim = new("2f5aa8");
    public static readonly Color Good = new("3fb68b");
    public static readonly Color Warn = new("e0a33a");
    public static readonly Color Bad = new("e5534b");

    public static Color ForScore(double v, double good = 65, double bad = 45) => v >= good ? Good : v <= bad ? Bad : Warn;

    public static string Hex(Color c) => c.ToHtml(false);
}

public static class GameTheme
{
    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 6, int pad = 12, int borderWidth = 1)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = bg, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad * 0.75f, ContentMarginBottom = pad * 0.75f,
        };
        if (border is { } b) { sb.BorderColor = b; sb.SetBorderWidthAll(borderWidth); }
        return sb;
    }

    public static Theme Build()
    {
        var t = new Theme { DefaultFontSize = 15 };
        foreach (var type in new[] { "Label", "Button", "LineEdit", "CheckBox", "OptionButton", "ItemList", "RichTextLabel", "TextEdit", "SpinBox" })
            t.SetColor("font_color", type, Palette.Text);
        t.SetColor("default_color", "RichTextLabel", Palette.Text);
        t.SetColor("font_placeholder_color", "LineEdit", Palette.Muted);

        t.SetStylebox("panel", "PanelContainer", Box(Palette.Surface, Palette.Border));
        t.SetStylebox("panel", "Panel", Box(Palette.Background, null, 0, 0));

        t.SetStylebox("normal", "Button", Box(Palette.SurfaceRaised, Palette.Border, 5, 10));
        t.SetStylebox("hover", "Button", Box(new Color("23385c"), Palette.Accent, 5, 10));
        t.SetStylebox("pressed", "Button", Box(Palette.AccentDim, Palette.Accent, 5, 10));
        t.SetStylebox("disabled", "Button", Box(Palette.Charcoal, Palette.Border, 5, 10));
        t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Palette.Accent, 5, 10, 2));
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", Palette.Muted);

        t.SetStylebox("normal", "LineEdit", Box(Palette.Charcoal, Palette.Border, 5, 10));
        t.SetStylebox("focus", "LineEdit", Box(Palette.Charcoal, Palette.Accent, 5, 10));
        t.SetStylebox("normal", "TextEdit", Box(Palette.Charcoal, Palette.Border, 5, 10));
        t.SetStylebox("panel", "ItemList", Box(Palette.Charcoal, Palette.Border, 5, 6));
        t.SetColor("font_selected_color", "ItemList", Colors.White);
        t.SetStylebox("selected", "ItemList", Box(Palette.AccentDim, null, 4, 4));
        t.SetStylebox("selected_focus", "ItemList", Box(Palette.AccentDim, Palette.Accent, 4, 4));
        t.SetStylebox("normal", "RichTextLabel", Box(Palette.Charcoal, Palette.Border, 5, 12));

        t.SetStylebox("background", "ProgressBar", Box(Palette.Charcoal, Palette.Border, 4, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Palette.Accent, null, 4, 0));
        t.SetColor("font_color", "ProgressBar", Palette.Text);

        t.SetStylebox("panel", "PopupMenu", Box(Palette.SurfaceRaised, Palette.Border, 5, 6));
        t.SetStylebox("panel", "AcceptDialog", Box(Palette.Surface, Palette.Accent, 6, 16));
        t.SetStylebox("embedded_border", "Window", Box(Palette.Surface, Palette.Accent, 6, 4));
        t.SetColor("title_color", "Window", Palette.Text);
        t.SetConstant("separation", "VBoxContainer", 8);
        t.SetConstant("separation", "HBoxContainer", 8);
        t.SetConstant("h_separation", "GridContainer", 16);
        t.SetConstant("v_separation", "GridContainer", 6);
        return t;
    }
}

/// <summary>Small factory helpers so screens read like layout descriptions.</summary>
public static class Ui
{
    public static Label Label(string text, int size = 15, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        if (size != 15) l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.CustomMinimumSize = new Vector2(50, 0); }
        return l;
    }

    public static Label Heading(string text, int size = 22) => Label(text, size, Palette.Text);
    public static Label Caption(string text) => Label(text.ToUpperInvariant(), 12, Palette.Muted);

    public static Button Button(string text, Action onPressed, bool primary = false)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All };
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", GameTheme.Box(Palette.AccentDim, Palette.Accent, 5, 12));
            b.AddThemeStyleboxOverride("hover", GameTheme.Box(Palette.Accent, Palette.Accent, 5, 12));
        }
        b.Pressed += onPressed;
        return b;
    }

    public static PanelContainer Card(Control child, Color? bg = null, Color? border = null)
    {
        var p = new PanelContainer();
        if (bg != null || border != null) p.AddThemeStyleboxOverride("panel", GameTheme.Box(bg ?? Palette.Surface, border ?? Palette.Border));
        p.AddChild(child);
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

    /// <summary>KPI tile: caption, large value, supporting line.</summary>
    public static PanelContainer Tile(string caption, string value, string sub = "", Color? accent = null)
    {
        var v = VBox(2, Caption(caption), Label(value, 26, accent ?? Palette.Text));
        if (sub.Length > 0) v.AddChild(Label(sub, 13, Palette.Muted, wrap: true));
        var card = Card(v);
        card.CustomMinimumSize = new Vector2(220, 104);
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return card;
    }

    public static ProgressBar Bar(double value, double max = 100, Color? fill = null)
    {
        var b = new ProgressBar { MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(120, 10), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (fill is { } f) b.AddThemeStyleboxOverride("fill", GameTheme.Box(f, null, 4, 0));
        return b;
    }

    /// <summary>Simple table: header row + rows of strings. Column 0 expands.</summary>
    public static GridContainer Table(string[] headers, IEnumerable<string[]> rows, Func<int, int, Color?>? colorAt = null)
    {
        var g = new GridContainer { Columns = headers.Length, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var h in headers) g.AddChild(Caption(h));
        int r = 0;
        foreach (var row in rows)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                var l = Label(c < row.Length ? row[c] : "", 14, colorAt?.Invoke(r, c));
                if (c == 0) { l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(160, 0); }
                else l.HorizontalAlignment = HorizontalAlignment.Right;
                g.AddChild(l);
            }
            r++;
        }
        return g;
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    public static string Esc(string s) => s.Replace("[", "[lb]");
}
