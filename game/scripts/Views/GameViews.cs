using ExecutiveCommand.Core.AI;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>The home screen: your office. A greeting, the most pressing item, and the live scene.</summary>
public partial class OfficeView : VBoxContainer, IGameView
{
    public string Title => "Office";
    private readonly GameScreen _game = null!;
    public OfficeScene Scene { get; } = new();
    private Label _greeting = null!, _sub = null!;
    private HBoxContainer _nudge = null!;

    public OfficeView() { }

    public OfficeView(GameScreen game)
    {
        _game = game;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        _greeting = Ui.Heading("", 28);
        _sub = Ui.Label("", Fonts.Base, Palette.TextSecondary);
        _nudge = Ui.HBox(10);
        AddChild(Ui.HBox(16, Ui.VBox(2, _greeting, _sub), Ui.Spacer(), _nudge));
        var frame = Ui.Card(Scene, Palette.BgSunken, Palette.Border, 0);
        frame.SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(frame);
        AddChild(Ui.Label("Click anything in the office. The calendar skips to the next event; the boardroom door starts a meeting.", Fonts.Sm, Palette.TextMuted));
        Scene.Activate = target =>
        {
            if (target == "advance") _ = _game.AdvanceAsync(60, untilEvent: true);
            else _game.Navigate(target);
        };
    }

    public void Refresh(GameSession session)
    {
        var s = session.State;
        Scene.State = s;
        string first = s.Player.Name.Split(' ')[0];
        _greeting.Text = $"{(s.CurrentDate.DayOfWeek == DayOfWeek.Monday ? "New week" : "Welcome back")}, {first}.";
        int unread = s.Inbox.Count(m => !m.Read);
        int active = s.Objectives.Count(o => o.Status == ObjectiveStatus.Active);
        _sub.Text = $"{s.CurrentDate:dddd, MMMM d} · {unread} unread · {active} objectives in play · rank: {ProgressionEngine.RankTitle(s.Career.Xp)}";
        Ui.Clear(_nudge);
        var top = Guidance.Attention(s).FirstOrDefault();
        if (top != null)
        {
            var col = top.Level == AttentionLevel.Urgent ? Palette.Danger : top.Level == AttentionLevel.Important ? Palette.Warning : Palette.Info;
            var text = Ui.Label(top.Text, Fonts.Sm, Palette.TextPrimary);
            text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            text.CustomMinimumSize = new Vector2(380, 0);
            _nudge.AddChild(Ui.Icon("warning-circle", 18, col));
            _nudge.AddChild(text);
            _nudge.AddChild(Ui.Button(top.ButtonLabel, () =>
            {
                if (top.NavigateTo == "Meeting Room" || (top.MeetWith != null && top.NavigateTo != "Decisions")) _game.OpenMeeting(top.MeetWith ?? new[] { ExecRole.CIO }, top.Prefill);
                else if (top.NavigateTo == "Inbox") _game.OpenInbox(top.RelatedId);
                else _game.Navigate(top.NavigateTo);
            }, primary: top.Level == AttentionLevel.Urgent));
        }
        Scene.QueueRedraw();
    }
}

/// <summary>Objectives, rank, achievements and report cards — what you're playing for.</summary>
public partial class GoalsView : StateView
{
    public override string Title => "Goals & Career";
    public GoalsView() { }
    public GoalsView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        var c = s.Career;
        var (into, needed) = ProgressionEngine.LevelProgress(c.Xp);
        int level = ProgressionEngine.Level(c.Xp);
        var rank = Ui.VBox(6, Ui.Caption("Executive rank"),
            Ui.HBox(12, Ui.Label($"{ProgressionEngine.RankTitle(c.Xp)}", 28, Palette.Accent, font: Fonts.SansSemi), Ui.Label($"Level {level}", Fonts.Base, Palette.TextSecondary)),
            Ui.Bar(into, needed, Palette.Accent),
            Ui.Label(level >= ProgressionEngine.Ranks.Length ? $"{c.Xp} XP — top rank" : $"{c.Xp} XP · {needed - into} XP to {ProgressionEngine.Ranks[level].Title}", Fonts.Sm, Palette.TextSecondary, font: Fonts.Mono));
        var stars = Ui.VBox(6, Ui.Caption("Stars"), Ui.Num($"★ {c.Stars}", 32, Palette.Accent), Ui.Label($"of {s.Objectives.Sum(o => o.Stars)} available in this campaign", Fonts.Sm, Palette.TextSecondary));
        var trophies = Ui.VBox(6, Ui.Caption("Achievements"), Ui.Num($"{c.Achievements.Count} / {ProgressionEngine.AllAchievements.Count}", 32, Palette.TextPrimary), Ui.Label("Earned from results, never from words", Fonts.Sm, Palette.TextSecondary));
        var head = Ui.HBox(16, Ui.Card(rank, null, null, 20), Ui.Card(stars, null, null, 20), Ui.Card(trophies, null, null, 20));
        head.GetChild<Control>(0).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.GetChild<Control>(0).SizeFlagsStretchRatio = 2;
        head.GetChild<Control>(1).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.GetChild<Control>(2).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Body.AddChild(head);

        Body.AddChild(Ui.Caption("Objectives"));
        foreach (var o in s.Objectives.OrderBy(o => o.Status != ObjectiveStatus.Active).ThenBy(o => o.Deadline))
        {
            int daysLeft = o.Deadline.DayNumber - s.CurrentDate.DayNumber;
            var (status, col) = o.Status switch
            {
                ObjectiveStatus.Completed => ($"✓ Completed {o.ResolvedOn:MMM d}", Palette.Success),
                ObjectiveStatus.Failed => ($"✕ Missed — deadline was {o.Deadline:MMM d}", Palette.Danger),
                _ => (daysLeft <= 14 ? $"{daysLeft} days left" : $"Due {o.Deadline:MMM d}", daysLeft <= 14 ? Palette.Warning : Palette.TextSecondary),
            };
            var starRow = Ui.HBox(2);
            for (int i = 0; i < o.Stars; i++) starRow.AddChild(Ui.Label("★", Fonts.Lg, o.Status == ObjectiveStatus.Completed ? Palette.Accent : Palette.TextMuted));
            var v = Ui.VBox(6,
                Ui.HBox(12, starRow, Ui.Label(o.Title, Fonts.Lg, o.Status == ObjectiveStatus.Failed ? Palette.TextMuted : Palette.TextPrimary, font: Fonts.SansSemi), Ui.Spacer(), Ui.Label(status, Fonts.Sm, col)),
                Ui.Label(o.Description, Fonts.Sm, Palette.TextSecondary, wrap: true));
            if (o.Status == ObjectiveStatus.Active)
            {
                var bar = Ui.Bar(o.Progress, 1, Palette.Success);
                v.AddChild(Ui.HBox(12, bar, Ui.Num($"{o.Progress:P0}", Fonts.Sm, Palette.TextSecondary)));
                v.AddChild(Ui.Label("Tip: " + o.Hint, Fonts.Sm, Palette.TextMuted, wrap: true));
            }
            Body.AddChild(Ui.AccentCard(v, col == Palette.TextSecondary ? Palette.BorderStrong : col));
        }

        Body.AddChild(Ui.Caption("Achievements"));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 12);
        foreach (var (id, title, desc) in ProgressionEngine.AllAchievements)
        {
            var got = c.Achievements.FirstOrDefault(a => a.Id == id);
            var card = Ui.Card(Ui.HBox(14, Ui.Icon("medal", 28, got != null ? Palette.Accent : Palette.TextMuted),
                Ui.VBox(2, Ui.Label(got != null ? title : "???  " + title, Fonts.Base, got != null ? Palette.TextPrimary : Palette.TextMuted, font: Fonts.SansSemi),
                    Ui.Label(got != null ? $"{desc} · {got.UnlockedOn:MMM d}" : desc, Fonts.Sm, Palette.TextSecondary, wrap: true))),
                got != null ? Palette.BgElevated : Palette.BgSurface, got != null ? new Color(Palette.Accent, 0.5f) : Palette.Border, 16);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(card);
        }
        Body.AddChild(grid);

        if (c.ReportCards.Count > 0)
        {
            Body.AddChild(Ui.Caption("Report cards"));
            foreach (var r in c.ReportCards.AsEnumerable().Reverse())
            {
                var row = Ui.HBox(16, Ui.Label(r.Period, Fonts.Lg, Palette.TextPrimary, font: Fonts.SansSemi), Ui.Label(r.Overall, 28, GradeColor(r.Overall), font: Fonts.SansSemi),
                    Ui.Label($"Decisions {r.Decisions} · Delivery {r.Delivery} · Finance {r.Finance} · Risk {r.Risk} · ★ {r.StarsEarned}/{r.StarsPossible} · +{r.XpGained} XP", Fonts.Sm, Palette.TextSecondary),
                    Ui.Spacer(), Ui.Button("Open", () => Game.ShowReportCard(r)));
                Body.AddChild(Ui.Card(row));
            }
        }
    }

    public static Color GradeColor(string g) => g switch { "A" => Palette.Success, "B" => Palette.Info, "C" => Palette.Accent, "D" => Palette.Warning, _ => Palette.Danger };
}

/// <summary>Full-screen overlays for big moments: report cards, achievements, rank-ups.</summary>
public static class Celebrations
{
    /// <summary>Gold banner that drops in from the top, holds, and leaves.</summary>
    public static void Banner(Control host, string kicker, string title, string body, string icon = "medal")
    {
        var bodyLabel = Ui.Label(body, Fonts.Sm, new Color(Palette.OnAccent, 0.8f), wrap: true);
        bodyLabel.CustomMinimumSize = new Vector2(420, 0); // wrap at the banner's real width
        var v = Ui.VBox(2, Ui.Label(kicker.ToUpperInvariant(), Fonts.Xs, Palette.OnAccent, font: Fonts.Caption), Ui.Label(title, Fonts.Xl, Palette.OnAccent, font: Fonts.SansSemi), bodyLabel);
        v.CustomMinimumSize = new Vector2(420, 0);
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", GameTheme.Box(Palette.Accent, Palette.AccentHover, 12, 20, 2));
        var iconRect = Ui.Icon(icon, 40, Palette.OnAccent);
        iconRect.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        card.AddChild(Ui.HBox(16, iconRect, v));
        host.AddChild(card);
        var vp = host.GetViewportRect().Size;
        float x = vp.X / 2 - 270;
        card.Position = new Vector2(x, Motion.Reduced ? 84 : -140);
        card.CustomMinimumSize = new Vector2(540, 0);
        var t = card.CreateTween();
        if (!Motion.Reduced) t.TweenProperty(card, "position:y", 84f, 0.35).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        t.TweenInterval(3.2);
        t.TweenProperty(card, "modulate:a", 0.0, Motion.Exit);
        t.TweenCallback(Callable.From(card.QueueFree));
    }

    private static Control Verdict(QuarterReport r, GameState s)
    {
        var quote = Ui.Label($"“{r.Verdict}”", Fonts.Lg, Palette.TextPrimary, wrap: true);
        quote.CustomMinimumSize = new Vector2(480, 0);
        var col = Ui.VBox(6, Ui.Label("Overall grade", Fonts.Base, Palette.TextSecondary), quote, Ui.Label($"— {s.Exec(ExecRole.CEO).Name}, CEO", Fonts.Sm, Palette.TextMuted));
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        return col;
    }

    public static void ReportCard(Control host, QuarterReport r, GameState s, Action? onClose)
    {
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(center);

        var grades = Ui.HBox(18);
        foreach (var (label, g) in new[] { ("Decisions", r.Decisions), ("Delivery", r.Delivery), ("Finance", r.Finance), ("Risk", r.Risk) })
        {
            var cell = Ui.VBox(2, Ui.Caption(label), Ui.Label(g, 40, GoalsView.GradeColor(g), font: Fonts.SansSemi));
            grades.AddChild(Ui.Card(cell, Palette.BgElevated, Palette.Border, 16));
        }
        var starRow = Ui.HBox(4);
        for (int i = 0; i < r.StarsPossible; i++) starRow.AddChild(Ui.Label("★", 28, i < r.StarsEarned ? Palette.Accent : Palette.BorderStrong));
        var v = Ui.VBox(14,
            Ui.Caption($"Quarterly report card · {r.Period}"),
            Ui.HBox(20, Ui.Label(r.Overall, 96, GoalsView.GradeColor(r.Overall), font: Fonts.SansSemi), Verdict(r, s)),
            grades,
            Ui.HBox(12, starRow, Ui.Label($"{r.StarsEarned} of {r.StarsPossible} objective stars", Fonts.Base, Palette.TextSecondary), Ui.Spacer(), Ui.Num($"+{r.XpGained} XP", Fonts.Xl, Palette.Accent)));
        foreach (var h in r.Highlights) v.AddChild(Ui.Label("• " + h, Fonts.Sm, Palette.TextSecondary, wrap: true));
        v.AddChild(Ui.HBox(0, Ui.Spacer(), Ui.Button("Continue", () => { dim.QueueFree(); onClose?.Invoke(); }, primary: true, icon: "arrow-right")));
        var card = Ui.Card(v, Palette.BgSurface, Palette.Accent, 28);
        card.CustomMinimumSize = new Vector2(720, 0);
        center.AddChild(card);
        Motion.FadeIn(dim, 0.25, 0);
    }
}
