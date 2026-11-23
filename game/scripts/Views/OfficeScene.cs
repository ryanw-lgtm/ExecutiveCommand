using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>
/// The CTO's office, drawn in code as a flat illustration. Objects are live: the inbox tray piles up
/// with unread mail, the corkboard shows objectives, the whiteboard shows projects, the beacon glows
/// during a crisis, and the sky outside cycles through the day as time passes. Clicking an object opens
/// the matching screen.
/// </summary>
public partial class OfficeScene : Control
{
    // Scene is authored in a 1000 × 620 space and scaled to fit.
    private const float W = 1000, H = 620;

    public sealed record Hotspot(string Id, Rect2 Rect, string Label, string Target);

    public GameState? State { get; set; }
    public Action<string>? Activate { get; set; }
    /// <summary>0 = midnight, 0.25 = dawn, 0.5 = noon, 0.75 = dusk.</summary>
    public float DayPhase { get; set; } = 0.62f;

    private readonly List<Hotspot> _spots = new();
    private string? _hover;
    private double _clock;
    private float _scale = 1;
    private Vector2 _offset;
    private readonly (Rect2 R, int Layer)[] _buildings;
    private readonly List<(Vector2 P, float Threshold)> _cityWindows = new();

    public OfficeScene()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new Vector2(600, 380);
        // Deterministic skyline.
        var rng = new Random(20270104);
        var list = new List<(Rect2, int)>();
        for (int layer = 0; layer < 3; layer++)
        {
            float x = 60;
            while (x < 520)
            {
                float w = 18 + rng.Next(0, 34) - layer * 3;
                float h = 50 + rng.Next(0, 150) - layer * 35;
                var r = new Rect2(x, 320 - h, w, h);
                list.Add((r, layer));
                if (layer == 2)
                    for (float wy = r.Position.Y + 6; wy < 314; wy += 9)
                        for (float wx = r.Position.X + 4; wx < r.End.X - 4; wx += 7)
                            _cityWindows.Add((new Vector2(wx, wy), (float)rng.NextDouble()));
                x += w + rng.Next(0, 6);
            }
        }
        _buildings = list.ToArray();
        _spots.AddRange(new[]
        {
            new Hotspot("monitor", new Rect2(410, 290, 180, 120), "Dashboard", "Dashboard"),
            new Hotspot("inbox", new Rect2(290, 350, 100, 70), "Inbox", "Inbox"),
            new Hotspot("folders", new Rect2(615, 360, 100, 60), "Decisions", "Decisions"),
            new Hotspot("door", new Rect2(905, 150, 85, 380), "Boardroom", "Meeting Room"),
            new Hotspot("cork", new Rect2(555, 55, 215, 190), "Objectives", "Goals"),
            new Hotspot("whiteboard", new Rect2(785, 55, 105, 170), "Projects", "Projects"),
            new Hotspot("cabinet", new Rect2(800, 360, 85, 170), "Finance", "Finance"),
            new Hotspot("shelf", new Rect2(70, 350, 140, 185), "Performance", "Performance"),
            new Hotspot("orgchart", new Rect2(790, 250, 95, 80), "Executives", "Executives"),
            new Hotspot("beacon", new Rect2(918, 80, 60, 55), "Risk", "Risk Register"),
            new Hotspot("calendar", new Rect2(565, 262, 70, 80), "Next event", "advance"),
        });
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        QueueRedraw(); // gentle ambient animation (monitor glow, beacon, clouds)
    }

    private Vector2 ToScene(Vector2 p) => (p - _offset) / _scale;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            var p = ToScene(mm.Position);
            var h = _spots.LastOrDefault(s => s.Rect.HasPoint(p))?.Id;
            if (h != _hover) { _hover = h; MouseDefaultCursorShape = h != null ? CursorShape.PointingHand : CursorShape.Arrow; TooltipText = h != null ? Describe(_spots.First(s => s.Id == h)) : ""; }
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var p = ToScene(mb.Position);
            var hit = _spots.LastOrDefault(s => s.Rect.HasPoint(p));
            if (hit != null) { Activate?.Invoke(hit.Target); AcceptEvent(); }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit) { _hover = null; }
    }

    /// <summary>Live description for an object (also used as its tooltip).</summary>
    public string Describe(Hotspot h)
    {
        var s = State;
        if (s == null) return h.Label;
        int unread = s.Inbox.Count(m => !m.Read);
        int pending = s.Proposals.Count(p => p.Status == ProposalStatus.Pending);
        var inc = s.Incidents.FirstOrDefault(i => i.Status == IncidentStatus.Active);
        return h.Id switch
        {
            "monitor" => "Dashboard — your organization at a glance",
            "inbox" => unread == 0 ? "Inbox — all read" : $"Inbox — {unread} unread",
            "folders" => pending == 0 ? "Decisions — nothing waiting" : $"Decisions — {pending} waiting for you",
            "door" => "Boardroom — hold a meeting",
            "cork" => $"Objectives — {s.Objectives.Count(o => o.Status == ObjectiveStatus.Active)} active · ★ {s.Career.Stars}",
            "whiteboard" => $"Projects — {s.Projects.Count(p => p.Status != ProjectStatus.Completed)} active, {s.Assignments.Count(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue)} assignments out",
            "cabinet" => "Finance — statements, budget and savings",
            "shelf" => $"Performance & trophies — {s.Career.Achievements.Count} achievements",
            "orgchart" => "Executives — who's who and how they see you",
            "beacon" => inc != null ? $"ALERT — {inc.Title}" : "Risk register — all quiet",
            "calendar" => "Skip ahead to the next event",
            _ => h.Label,
        };
    }

    // ------------------------------------------------------------------ drawing

    public override void _Draw()
    {
        var size = Size;
        _scale = Math.Min(size.X / W, size.Y / H);
        _offset = (size - new Vector2(W, H) * _scale) / 2;
        float floorY = _offset.Y + 530 * _scale;
        DrawRect(new Rect2(0, 0, size.X, floorY), C("131d33"));
        DrawRect(new Rect2(0, floorY, size.X, size.Y - floorY), C("0f1626"));
        DrawSetTransform(_offset, 0, new Vector2(_scale, _scale));
        var s = State;

        DrawWall();
        DrawWindow();
        DrawCorkboard(s);
        DrawWhiteboard(s);
        DrawCalendar(s);
        DrawOrgChart();
        DrawDoor();
        DrawBeacon(s);
        DrawFloor();
        DrawShelf(s);
        DrawCabinet();
        DrawDesk(s);
        DrawNightTint();
        DrawHover(s);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private static Color C(string hex) => new(hex);

    private void Box(Rect2 r, Color fill, float radius = 4, Color? border = null, float bw = 1)
    {
        var sb = new StyleBoxFlat { BgColor = fill, AntiAliasing = true };
        sb.SetCornerRadiusAll((int)radius);
        if (border is { } b) { sb.BorderColor = b; sb.SetBorderWidthAll((int)Math.Max(1, bw)); }
        DrawStyleBox(sb, r);
    }

    private void VGradient(Rect2 r, Color top, Color bottom) =>
        DrawPolygon(new[] { r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y) }, new[] { top, top, bottom, bottom });

    private void Text(string text, Vector2 pos, int size, Color color, Font? font = null, float width = -1, HorizontalAlignment align = HorizontalAlignment.Left) =>
        DrawString(font ?? Fonts.Sans, pos, text, align, width, size, color);

    private void Star(Vector2 c, float r, Color col)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
            float rr = i % 2 == 0 ? r : r * 0.45f;
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
        }
        DrawColoredPolygon(pts, col);
    }

    private (Color Top, Color Bottom, float Light) Sky()
    {
        // Keyframes across the day: night, dawn, day, dusk.
        (float t, Color top, Color bot, float light)[] k =
        {
            (0.00f, C("070d22"), C("14204a"), 0.0f),
            (0.22f, C("1c2a5c"), C("e08a5a"), 0.35f),
            (0.32f, C("4a86c8"), C("f2c48a"), 0.8f),
            (0.50f, C("3f86d6"), C("a8cdee"), 1.0f),
            (0.68f, C("3a5aa0"), C("f0b06a"), 0.75f),
            (0.78f, C("2a2456"), C("d0644a"), 0.35f),
            (0.88f, C("0c1430"), C("1e2450"), 0.05f),
            (1.00f, C("070d22"), C("14204a"), 0.0f),
        };
        float p = ((DayPhase % 1) + 1) % 1;
        for (int i = 0; i < k.Length - 1; i++)
            if (p >= k[i].t && p <= k[i + 1].t)
            {
                float f = (p - k[i].t) / (k[i + 1].t - k[i].t);
                return (k[i].top.Lerp(k[i + 1].top, f), k[i].bot.Lerp(k[i + 1].bot, f), Mathf.Lerp(k[i].light, k[i + 1].light, f));
            }
        return (k[0].top, k[0].bot, 0);
    }

    private void DrawWall()
    {
        VGradient(new Rect2(0, 0, W, 530), C("18243d"), C("101a2e"));
        for (float x = 0; x < W; x += 125) DrawLine(new Vector2(x, 0), new Vector2(x, 530), new Color(1, 1, 1, 0.025f), 2);
        DrawRect(new Rect2(0, 0, W, 14), C("0d1526"));            // ceiling trim
        DrawRect(new Rect2(0, 505, W, 25), C("0f182b"));          // skirting
    }

    private void DrawWindow()
    {
        var win = new Rect2(60, 50, 460, 275);
        var (top, bot, light) = Sky();
        Box(win.Grow(10), C("2a3550"), 6);
        VGradient(win, top, bot);
        // Sun or moon on an arc.
        float p = ((DayPhase % 1) + 1) % 1;
        bool day = p is > 0.22f and < 0.80f;
        float arc = day ? (p - 0.22f) / 0.58f : ((p + 0.2f) % 1) / 0.42f;
        var body = new Vector2(80 + arc * 420, 260 - Mathf.Sin(Mathf.Clamp(arc, 0, 1) * Mathf.Pi) * 180);
        if (day) { DrawCircle(body, 26, new Color(1f, 0.85f, 0.55f, 0.18f)); DrawCircle(body, 16, C("ffd98a")); }
        else if (arc <= 1) { DrawCircle(body, 12, C("e8ecf5")); DrawCircle(body + new Vector2(5, -3), 11, top); }
        // Clouds drift slowly.
        float drift = (float)(_clock * 4 % 600);
        foreach (var (cx, cy, cw) in new[] { (100f, 95f, 70f), (300f, 75f, 90f), (450f, 115f, 60f) })
        {
            float x = 60 + (cx + drift) % 520 - 40;
            var cc = new Color(1, 1, 1, 0.08f + 0.12f * light);
            DrawCircle(new Vector2(x, cy), cw * 0.22f, cc); DrawCircle(new Vector2(x + cw * 0.25f, cy - 6), cw * 0.25f, cc); DrawCircle(new Vector2(x + cw * 0.5f, cy), cw * 0.2f, cc);
        }
        // Skyline: three depth layers, farther = lighter and hazier.
        Color[] layerCol = { top.Lerp(C("223056"), 0.55f), C("18223e").Lerp(top, 0.2f), C("0e1628") };
        foreach (var (r, layer) in _buildings)
        {
            var clipped = r.Intersection(win);
            if (clipped.Size.X > 0) DrawRect(clipped, layerCol[layer]);
        }
        float lit = 1 - light; // more windows lit at night
        foreach (var (pt, th) in _cityWindows)
            if (th < 0.08f + 0.55f * lit && win.HasPoint(pt)) DrawRect(new Rect2(pt, new Vector2(3, 4)), new Color(1f, 0.82f, 0.45f, 0.55f + 0.4f * lit));
        // Mullions and sill.
        DrawRect(new Rect2(win.Position.X + win.Size.X / 2 - 3, win.Position.Y, 6, win.Size.Y), C("2a3550"));
        DrawRect(new Rect2(win.Position.X, win.Position.Y + 118, win.Size.X, 5), C("2a3550"));
        Box(new Rect2(48, 330, 484, 12), C("2e3a58"), 3);
        // Reflection sheen.
        DrawColoredPolygon(new[] { new Vector2(90, 50), new Vector2(150, 50), new Vector2(70, 325), new Vector2(60, 325), new Vector2(60, 140) }, new Color(1, 1, 1, 0.035f));
    }

    private void DrawCorkboard(GameState? s)
    {
        var r = new Rect2(555, 55, 215, 190);
        Box(r.Grow(6), C("4a3826"), 6);
        Box(r, C("a7804f"), 4);
        Text("OBJECTIVES", new Vector2(r.Position.X + 10, r.Position.Y + 18), 11, C("3a2a18"), Fonts.Caption);
        Star(new Vector2(r.End.X - 40, r.Position.Y + 13), 7, C("ffcf4a"));
        Text($"{s?.Career.Stars ?? 0}", new Vector2(r.End.X - 30, r.Position.Y + 18), 12, C("3a2a18"), Fonts.MonoMedium);
        var active = s?.Objectives.Where(o => o.Status == ObjectiveStatus.Active).OrderBy(o => o.Deadline).Take(4).ToList() ?? new();
        if (active.Count == 0)
        {
            Text("All objectives resolved", new Vector2(r.Position.X + 12, r.Position.Y + 70), 12, C("3a2a18"));
            return;
        }
        for (int i = 0; i < active.Count; i++)
        {
            var o = active[i];
            var card = new Rect2(r.Position.X + 8 + (i % 2) * 102, r.Position.Y + 28 + (i / 2) * 78, 96, 72);
            float tilt = (i % 2 == 0 ? -1 : 1) * 0.5f;
            Box(new Rect2(card.Position + new Vector2(2, 3), card.Size), new Color(0, 0, 0, 0.25f), 2);
            Box(card, i == 0 ? C("fff4c2") : C("f4f1e8"), 2);
            DrawCircle(card.Position + new Vector2(card.Size.X / 2, 5 + tilt), 4, C("d94848"));
            DrawMultilineString(Fonts.SansMedium, card.Position + new Vector2(6, 20), o.Title, HorizontalAlignment.Left, 86, 10, 2, C("26303f"));
            for (int st = 0; st < o.Stars; st++) Star(card.Position + new Vector2(10 + st * 13, 52), 5, C("e3a93b"));
            DrawRect(new Rect2(card.Position + new Vector2(6, 62), new Vector2(84, 4)), C("d8d2c2"));
            DrawRect(new Rect2(card.Position + new Vector2(6, 62), new Vector2(84 * (float)o.Progress, 4)), C("3cc48a"));
        }
    }

    private void DrawWhiteboard(GameState? s)
    {
        var r = new Rect2(785, 55, 105, 170);
        Box(r.Grow(4), C("9aa3b5"), 4);
        Box(r, C("eef1f6"), 2);
        Text("PROJECTS", new Vector2(r.Position.X + 8, r.Position.Y + 16), 10, C("2f5aa8"), Fonts.Caption);
        var items = new List<(string, double)>();
        if (s != null)
        {
            items.AddRange(s.Projects.Where(p => p.Status != ProjectStatus.Completed).Select(p => (p.Name, p.Progress)));
            items.AddRange(s.Assignments.Where(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue).Select(a => (a.Subject, Math.Min(1, a.EffortDone / a.EffortRequired))));
        }
        Color[] notes = { C("ffe17a"), C("9fe0c0"), C("ffb3a7"), C("a9c8ff") };
        for (int i = 0; i < Math.Min(4, items.Count); i++)
        {
            var n = new Rect2(r.Position.X + 8, r.Position.Y + 24 + i * 36, 89, 31);
            Box(n, notes[i % notes.Length], 2);
            DrawMultilineString(Fonts.SansMedium, n.Position + new Vector2(4, 10), items[i].Item1, HorizontalAlignment.Left, 81, 8, 1, C("26303f"));
            DrawRect(new Rect2(n.Position + new Vector2(4, 22), new Vector2(81, 3)), new Color(0, 0, 0, 0.15f));
            DrawRect(new Rect2(n.Position + new Vector2(4, 22), new Vector2(81 * (float)items[i].Item2, 3)), C("26303f"));
        }
        if (items.Count == 0) Text("Nothing on the board", new Vector2(r.Position.X + 8, r.Position.Y + 60), 9, C("6a7487"), null, 90);
    }

    private void DrawCalendar(GameState? s)
    {
        var r = new Rect2(565, 262, 70, 80);
        Box(new Rect2(r.Position + new Vector2(2, 3), r.Size), new Color(0, 0, 0, 0.3f), 4);
        Box(r, C("f4f1e8"), 4);
        Box(new Rect2(r.Position, new Vector2(r.Size.X, 20)), C("d94848"), 4);
        var d = s?.CurrentDate ?? new DateOnly(2027, 1, 4);
        Text(d.ToString("MMM").ToUpperInvariant(), r.Position + new Vector2(0, 15), 11, C("fff4f0"), Fonts.Caption, r.Size.X, HorizontalAlignment.Center);
        Text(d.Day.ToString(), r.Position + new Vector2(0, 58), 30, C("1e2638"), Fonts.SansSemi, r.Size.X, HorizontalAlignment.Center);
        Text(d.ToString("ddd").ToUpperInvariant(), r.Position + new Vector2(0, 74), 9, C("6a7487"), Fonts.Caption, r.Size.X, HorizontalAlignment.Center);
    }

    private void DrawOrgChart()
    {
        var r = new Rect2(790, 250, 95, 80);
        Box(r.Grow(4), C("6b5232"), 3);
        Box(r, C("e9e4d6"), 1);
        var top = r.Position + new Vector2(r.Size.X / 2, 16);
        DrawCircle(top, 6, C("2f5aa8"));
        DrawLine(top + new Vector2(0, 6), top + new Vector2(0, 22), C("6a7487"), 1.5f);
        DrawLine(new Vector2(r.Position.X + 18, top.Y + 22), new Vector2(r.End.X - 18, top.Y + 22), C("6a7487"), 1.5f);
        for (int i = 0; i < 4; i++)
        {
            var c = new Vector2(r.Position.X + 18 + i * (r.Size.X - 36) / 3, top.Y + 36);
            DrawLine(c - new Vector2(0, 14), c - new Vector2(0, 6), C("6a7487"), 1.5f);
            DrawCircle(c, 5, i == 1 ? C("e3a93b") : C("7a869c"));
        }
    }

    private void DrawDoor()
    {
        var r = new Rect2(905, 150, 85, 380);
        Box(r.Grow(5), C("0c1322"), 3);
        VGradient(r, C("3a2c22"), C("2c2119"));
        Box(new Rect2(r.Position.X + 14, r.Position.Y + 24, r.Size.X - 28, 150), C("27364f"), 3);
        DrawColoredPolygon(new[] { new Vector2(r.Position.X + 20, r.Position.Y + 30), new Vector2(r.Position.X + 40, r.Position.Y + 30), new Vector2(r.Position.X + 22, r.Position.Y + 168) }, new Color(1, 1, 1, 0.06f));
        Box(new Rect2(r.Position.X + 10, r.Position.Y + 190, r.Size.X - 20, 22), C("c9a24a"), 2);
        Text("BOARDROOM", new Vector2(r.Position.X + 10, r.Position.Y + 206), 9, C("2a1d0c"), Fonts.Caption, r.Size.X - 20, HorizontalAlignment.Center);
        DrawCircle(new Vector2(r.Position.X + 14, r.Position.Y + 260), 5, C("d8b45a"));
    }

    private void DrawBeacon(GameState? s)
    {
        bool alarm = s?.Incidents.Any(i => i.Status == IncidentStatus.Active) == true;
        var c = new Vector2(948, 110);
        Box(new Rect2(c.X - 16, c.Y + 8, 32, 10), C("2a3550"), 2);
        float pulse = alarm ? 0.5f + 0.5f * Mathf.Sin((float)_clock * 6f) : 0;
        if (alarm) DrawCircle(c, 30 + 8 * pulse, new Color(0.94f, 0.36f, 0.36f, 0.12f + 0.15f * pulse));
        DrawCircle(c, 14, alarm ? new Color("ef5b5b").Lerp(C("ffb0a0"), pulse * 0.5f) : C("3b2328"));
        DrawCircle(c + new Vector2(-4, -4), 4, new Color(1, 1, 1, alarm ? 0.5f : 0.12f));
    }

    private void DrawFloor()
    {
        VGradient(new Rect2(0, 530, W, 90), C("141d30"), C("0b111f"));
        DrawRect(new Rect2(0, 530, W, 2), C("26324b"));
        Box(new Rect2(230, 545, 560, 60), new Color("1c2740"), 30); // rug
    }

    private void DrawShelf(GameState? s)
    {
        var r = new Rect2(70, 350, 140, 185);
        Box(r, C("3a2c22"), 3);
        for (int i = 0; i < 3; i++) DrawRect(new Rect2(r.Position.X + 6, r.Position.Y + 58 + i * 58, r.Size.X - 12, 5), C("2a1f17"));
        Color[] books = { C("2f5aa8"), C("8a3b3b"), C("3c7a5a"), C("c9a24a"), C("5a4a7a"), C("7a869c") };
        var rng = new Random(7);
        for (int shelf = 1; shelf < 3; shelf++)
        {
            float x = r.Position.X + 10;
            while (x < r.End.X - 16)
            {
                float w = 7 + rng.Next(0, 6), h = 30 + rng.Next(0, 18);
                DrawRect(new Rect2(x, r.Position.Y + 58 + shelf * 58 - h, w, h), books[rng.Next(books.Length)]);
                x += w + 2;
            }
        }
        // Trophies: one per achievement (top shelf).
        int n = Math.Min(6, s?.Career.Achievements.Count ?? 0);
        for (int i = 0; i < n; i++)
        {
            var b = new Vector2(r.Position.X + 18 + i * 20, r.Position.Y + 56);
            DrawRect(new Rect2(b.X - 5, b.Y - 4, 10, 4), C("8a6a2a"));
            DrawRect(new Rect2(b.X - 1.5f, b.Y - 12, 3, 8), C("c9a24a"));
            DrawColoredPolygon(new[] { b + new Vector2(-7, -26), b + new Vector2(7, -26), b + new Vector2(4, -13), b + new Vector2(-4, -13) }, C("e3a93b"));
        }
        if (n == 0) Text("no trophies yet", new Vector2(r.Position.X + 10, r.Position.Y + 48), 9, new Color(1, 1, 1, 0.25f));
    }

    private void DrawCabinet()
    {
        var r = new Rect2(800, 360, 85, 170);
        Box(r, C("5d6a82"), 3);
        for (int i = 0; i < 3; i++)
        {
            var d = new Rect2(r.Position.X + 6, r.Position.Y + 8 + i * 54, r.Size.X - 12, 48);
            Box(d, C("6f7d97"), 2, C("4d5970"));
            Box(new Rect2(d.Position.X + d.Size.X / 2 - 14, d.Position.Y + 10, 28, 6), C("3d475b"), 2);
            Box(new Rect2(d.Position.X + d.Size.X / 2 - 12, d.Position.Y + 22, 24, 10), C("e8e4d8"), 1);
        }
        Text("$", new Vector2(r.Position.X + r.Size.X / 2 - 4, r.Position.Y + 39), 9, C("26303f"), Fonts.MonoMedium);
    }

    private void DrawDesk(GameState? s)
    {
        // Desk body.
        Box(new Rect2(250, 412, 520, 18), C("4a3626"), 4);
        VGradient(new Rect2(262, 428, 496, 102), C("3a2a1e"), C("2a1f16"));
        Box(new Rect2(290, 440, 150, 80), C("33261b"), 3, C("4a3626"));
        Box(new Rect2(580, 440, 150, 80), C("33261b"), 3, C("4a3626"));
        // Monitor with live mini-chart of cash.
        var mon = new Rect2(420, 298, 160, 104);
        Box(mon.Grow(5), C("1a1f2b"), 6);
        float glow = 0.85f + 0.15f * Mathf.Sin((float)_clock * 1.7f);
        VGradient(mon, C("0f2236") * glow, C("0a1626"));
        Text("DASHBOARD", mon.Position + new Vector2(8, 15), 9, C("e3a93b"), Fonts.Caption);
        var hist = s?.History.Select(h => (double)h.Cash).ToArray() ?? Array.Empty<double>();
        if (hist.Length >= 2)
        {
            double mn = hist.Min(), mx = hist.Max(); if (mx - mn < 1) { mx += 1; mn -= 1; }
            var pts = hist.Select((v, i) => mon.Position + new Vector2(10 + i * (mon.Size.X - 20) / (hist.Length - 1), mon.Size.Y - 14 - (float)((v - mn) / (mx - mn)) * (mon.Size.Y - 44))).ToArray();
            DrawPolyline(pts, C("3cc48a"), 2, true);
        }
        else
            for (int i = 0; i < 4; i++) DrawRect(new Rect2(mon.Position.X + 12 + i * 34, mon.End.Y - 16 - (i * 11 + 12), 20, i * 11 + 12), new Color("3a5a8a"));
        DrawRect(new Rect2(490, 402, 20, 12), C("1a1f2b"));
        Box(new Rect2(465, 408, 70, 6), C("1a1f2b"), 2);
        // Keyboard and mug.
        Box(new Rect2(440, 416, 120, 8), C("2b3346"), 2);
        Box(new Rect2(718, 392, 18, 20), C("e8e4d8"), 3);
        DrawArc(new Vector2(738, 401), 6, -Mathf.Pi / 2, Mathf.Pi / 2, 10, C("e8e4d8"), 2.5f);
        float steam = (float)(_clock % 2.0);
        DrawLine(new Vector2(723, 388 - steam * 6), new Vector2(726, 380 - steam * 6), new Color(1, 1, 1, 0.15f * (1 - steam / 2)), 2);
        // Inbox tray: paper piles up with unread mail.
        int unread = Math.Min(10, s?.Inbox.Count(m => !m.Read) ?? 0);
        var tray = new Rect2(298, 396, 84, 16);
        for (int i = 0; i < unread; i++) Box(new Rect2(tray.Position.X + 6 + (i % 2), tray.Position.Y - 4 - i * 3.2f, 72, 4), i % 3 == 0 ? C("fff4c2") : C("f4f1e8"), 1);
        Box(tray, C("5d6a82"), 2);
        Text("IN", tray.Position + new Vector2(0, 12), 9, C("e8e4d8"), Fonts.Caption, tray.Size.X, HorizontalAlignment.Center);
        if (unread > 0) Badge(new Vector2(tray.End.X - 2, tray.Position.Y - unread * 3.2f - 8), $"{s!.Inbox.Count(m => !m.Read)}", C("e3a93b"));
        // Decision folders with red tabs.
        int pending = s?.Proposals.Count(p => p.Status == ProposalStatus.Pending) ?? 0;
        for (int i = 0; i < Math.Min(6, pending); i++)
        {
            var f = new Rect2(625 + i * 2, 402 - i * 5, 80, 10);
            Box(f, C("c9a24a").Darkened(i * 0.06f), 2);
            Box(new Rect2(f.Position.X + 56, f.Position.Y - 3, 16, 4), C("d94848"), 1);
        }
        if (pending > 0) Badge(new Vector2(712, 392 - Math.Min(6, pending) * 5), pending.ToString(), C("e3a93b"));
        // Desk lamp.
        DrawLine(new Vector2(272, 412), new Vector2(262, 360), C("2b3346"), 4);
        DrawLine(new Vector2(262, 360), new Vector2(292, 340), C("2b3346"), 4);
        DrawColoredPolygon(new[] { new Vector2(282, 330), new Vector2(312, 342), new Vector2(300, 356), new Vector2(276, 344) }, C("e3a93b"));
        DrawColoredPolygon(new[] { new Vector2(300, 356), new Vector2(312, 342), new Vector2(380, 412), new Vector2(290, 412) }, new Color(1f, 0.85f, 0.5f, 0.06f));
    }

    private void Badge(Vector2 c, string text, Color color)
    {
        DrawCircle(c, 10, color);
        Text(text, c + new Vector2(-10, 4), 10, C("1a1206"), Fonts.MonoMedium, 20, HorizontalAlignment.Center);
    }

    private void DrawNightTint()
    {
        var (_, _, light) = Sky();
        if (light < 0.95f) DrawRect(new Rect2(0, 0, W, H), new Color(0.02f, 0.03f, 0.1f, (1 - light) * 0.22f));
    }

    private void DrawHover(GameState? s)
    {
        if (_hover == null) return;
        var h = _spots.First(x => x.Id == _hover);
        var r = h.Rect.Grow(4);
        Box(r, new Color(Palette.Accent, 0.06f), 6, Palette.Accent, 2);
        string label = Describe(h);
        var font = Fonts.SansMedium;
        var tsize = font.GetStringSize(label, HorizontalAlignment.Left, -1, 13);
        var pos = new Vector2(Math.Clamp(r.Position.X + r.Size.X / 2 - tsize.X / 2 - 10, 4, W - tsize.X - 24), r.Position.Y > 40 ? r.Position.Y - 30 : r.End.Y + 6);
        Box(new Rect2(pos, new Vector2(tsize.X + 20, 24)), C("172035"), 5, Palette.Accent, 1);
        Text(label, pos + new Vector2(10, 17), 13, Palette.TextPrimary, font);
    }
}
