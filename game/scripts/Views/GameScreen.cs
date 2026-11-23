using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>Interface every in-game view implements.</summary>
public interface IGameView
{
    string Title { get; }
    void Refresh(GameSession session);
}

public partial class GameScreen : Control
{
    private readonly Main _main = null!;
    private readonly Dictionary<string, Control> _views = new();
    private readonly Dictionary<string, Button> _nav = new();
    private MarginContainer _content = null!;
    private Label _date = null!, _company = null!, _ai = null!;
    private string _current = "Dashboard";
    private readonly List<Button> _timeButtons = new();
    private readonly Dictionary<string, PanelContainer> _badges = new();
    private readonly Dictionary<string, int> _badgeCounts = new();
    private ProgressBar _timeStrip = null!;
    private Label _saved = null!, _rank = null!, _stars = null!;
    private ProgressBar _xpBar = null!;
    private int _shownXp = -1;
    private bool _advancing;
    /// <summary>Messages that arrived during the last time advance, shown on the dashboard.</summary>
    public List<InboxMessage> Digest { get; } = new();
    public string? DigestNote { get; private set; }

    public GameSession Session => _main.Session;
    public MeetingView Meeting => (MeetingView)_views["Meeting Room"];

    public GameScreen() { }

    public GameScreen(Main main)
    {
        _main = main;
        var root = Ui.VBox(0);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        // ---- Top bar: where you are on the left, time controls in the middle, utilities on the right.
        var bar = Ui.HBox(8);
        var barCard = Ui.Card(bar, Palette.BgSunken, Palette.Border, 12);
        barCard.AddThemeStyleboxOverride("panel", NoRadius(GameTheme.Box(Palette.BgSunken, Palette.Border, 0, 12)));
        root.AddChild(barCard);
        var brand = Ui.Label("EXECUTIVE COMMAND", Fonts.Xs, Palette.Accent, font: Fonts.Caption);
        _company = Ui.Label("", Fonts.Sm, Palette.TextSecondary);
        _company.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _company.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _company.CustomMinimumSize = new Vector2(120, 0);
        bar.AddChild(Ui.Gap(4));
        var who = Ui.VBox(0, brand, _company);
        who.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bar.AddChild(who);
        _ai = Ui.Label("", Fonts.Xs, Palette.TextMuted);
        bar.AddChild(_ai);
        _rank = Ui.Label("", Fonts.Xs, Palette.Accent, font: Fonts.Caption);
        _xpBar = Ui.Bar(0, 1, Palette.Accent);
        _xpBar.CustomMinimumSize = new Vector2(110, 6);
        _stars = Ui.Num("", Fonts.Sm, Palette.Accent);
        var career = Ui.HBox(10, Ui.VBox(3, _rank, _xpBar), _stars);
        career.MouseFilter = MouseFilterEnum.Ignore;
        var careerBtn = new Button { Flat = true, FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = "Goals & career" };
        careerBtn.AddChild(career);
        careerBtn.Pressed += () => Navigate("Goals");
        career.Resized += () => careerBtn.CustomMinimumSize = career.Size;
        bar.AddChild(careerBtn);
        bar.AddChild(Ui.Gap(8));
        _date = Ui.Label("", Fonts.Base, Palette.TextPrimary, font: Fonts.MonoMedium);
        var dateBox = Ui.HBox(8, Ui.Icon("calendar-plus", 18, Palette.Accent), _date);
        bar.AddChild(dateBox);
        _timeButtons.Add(AddTip(bar, Ui.Button("Day", () => _ = AdvanceAsync(1), icon: "play"), "Advance one day (F5). Your executives keep working while time passes."));
        _timeButtons.Add(AddTip(bar, Ui.Button("Week", () => _ = AdvanceAsync(7), icon: "fast-forward"), "Advance one week (F6). Stops early if something urgent happens."));
        _timeButtons.Add(AddTip(bar, Ui.Button("Month", () => _ = AdvanceAsync(30), icon: "fast-forward"), "Advance 30 days (F7). Stops early if something urgent happens."));
        _timeButtons.Add(AddTip(bar, Ui.Button("Next event", () => _ = AdvanceAsync(60, untilEvent: true), icon: "lightning"), "Skip ahead until something happens — a report, escalation, incident or review (F8)."));
        _saved = Ui.Label("Saved ✓", Fonts.Xs, Palette.Success);
        _saved.Modulate = new Color(1, 1, 1, 0);
        bar.AddChild(_saved);
        bar.AddChild(Ui.Gap(8));
        AddTip(bar, IconButton("magnifying-glass", OpenPalette), "Command palette — jump anywhere or do anything (Ctrl+K)");
        AddTip(bar, IconButton("floppy-disk", () => main.ShowSaveLoad(true)), "Save or load (the game also autosaves when you advance time)");
        AddTip(bar, IconButton("question", () => ShowHelp()), "How to play (F1)");
        AddTip(bar, IconButton("gear-six", () => main.ShowSettings(true)), "Settings: interface size, motion, local AI");
        AddTip(bar, IconButton("list", () => { Session.Save(); main.ShowMainMenu(); }), "Save and return to the main menu");

        // Slim progress strip that fills while time passes.
        _timeStrip = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 3), Modulate = new Color(1, 1, 1, 0) };
        _timeStrip.AddThemeStyleboxOverride("background", GameTheme.Box(Palette.BgSunken, null, 0, 0));
        _timeStrip.AddThemeStyleboxOverride("fill", GameTheme.Box(Palette.Accent, null, 0, 0));
        root.AddChild(_timeStrip);

        // ---- Body: nav + content
        var body = Ui.HBox(0);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(body);
        var nav = Ui.VBox(2);
        var navCard = Ui.Card(nav, Palette.BgSunken, Palette.Border, 12);
        navCard.AddThemeStyleboxOverride("panel", NoRadius(GameTheme.Box(Palette.BgSunken, Palette.Border, 0, 12)));
        navCard.CustomMinimumSize = new Vector2(232, 0);
        body.AddChild(navCard);

        Register("Office", new OfficeView(this));
        Register("Dashboard", new DashboardView(this));
        Register("Inbox", new InboxView(this));
        Register("Meeting Room", new MeetingView(this));
        Register("Decisions", new ProposalsView(this));
        Register("Projects", new ProjectsView(this));
        Register("Finance", new FinanceView(this));
        Register("Risk Register", new RiskView(this));
        Register("Executives", new PeopleView(this));
        Register("Performance", new PerformanceView(this));
        Register("Goals", new GoalsView(this));
        nav.AddChild(Ui.Gap(4));
        int n = 1;
        foreach (var (title, _) in _views)
        {
            var b = new Button { Text = title, Alignment = HorizontalAlignment.Left, Icon = Icons.Get(NavIcon[title], 20), CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.All, MouseDefaultCursorShape = CursorShape.PointingHand };
            b.AddThemeStyleboxOverride("normal", NavStyle(false));
            b.AddThemeStyleboxOverride("hover", GameTheme.Box(Palette.BgHover, null, 6, 12));
            b.AddThemeStyleboxOverride("pressed", NavStyle(true));
            b.AddThemeFontOverride("font", Fonts.SansMedium);
            b.Pressed += () => Navigate(title);
            b.TooltipText = (NavHelp.TryGetValue(title, out var h) ? h : title) + $"  (Ctrl+{n++})";
            _nav[title] = b;
            nav.AddChild(b);
            if (title is "Inbox" or "Decisions")
            {
                var badge = Ui.Pill("0", Palette.Accent);
                badge.SetAnchorsPreset(LayoutPreset.CenterRight);
                badge.Position = new Vector2(0, 0);
                badge.Visible = false;
                b.AddChild(badge);
                b.Resized += () => badge.Position = new Vector2(b.Size.X - badge.Size.X - 10, (b.Size.Y - badge.Size.Y) / 2);
                _badges[title] = badge;
            }
        }
        nav.AddChild(Ui.Spacer(false));
        nav.AddChild(Ui.Label(Session.State.Company.FictionNotice, 11, Palette.TextMuted, wrap: true));

        _content = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right" }) _content.AddThemeConstantOverride($"margin_{side}", 32);
        _content.AddThemeConstantOverride("margin_top", 24);
        _content.AddThemeConstantOverride("margin_bottom", 16);
        body.AddChild(_content);

        Session.StateChanged += OnStateChanged;
        Session.AiStatusChanged += OnAiStatus;
        TreeExiting += () => { Session.StateChanged -= OnStateChanged; Session.AiStatusChanged -= OnAiStatus; };
        OnAiStatus(Session.Llm?.Status ?? Core.AI.Llm.LlmStatus.Stopped);
        Navigate("Office");
    }

    private void Register(string navKey, Control view) => _views[navKey] = view;

    private static readonly Dictionary<string, string> NavIcon = new()
    {
        ["Office"] = "lightning", ["Goals"] = "medal", ["Dashboard"] = "squares-four", ["Inbox"] = "tray", ["Meeting Room"] = "chats-circle", ["Decisions"] = "scales", ["Projects"] = "kanban",
        ["Finance"] = "chart-line-up", ["Risk Register"] = "shield-warning", ["Executives"] = "users-three", ["Performance"] = "chart-line-up",
    };

    private static StyleBoxFlat NoRadius(StyleBoxFlat sb) { sb.SetCornerRadiusAll(0); sb.BorderWidthTop = sb.BorderWidthLeft = 0; return sb; }

    private static StyleBoxFlat NavStyle(bool active)
    {
        var sb = GameTheme.Box(active ? Palette.AccentSoft : new Color(0, 0, 0, 0), null, 6, 12);
        if (active) { sb.BorderColor = Palette.Accent; sb.BorderWidthLeft = 3; }
        return sb;
    }

    private static Button IconButton(string icon, Action onPressed)
    {
        var b = Ui.Button("", onPressed, icon: icon);
        b.Icon = Icons.Get(icon, 20);
        b.CustomMinimumSize = new Vector2(38, 36);
        return b;
    }

    // ---- Command palette (Ctrl+K): keyboard-first, so it opens instantly with no animation.
    private PopupPanel? _palette;

    public void OpenPalette()
    {
        _palette?.QueueFree();
        var cmds = new List<(string Label, string Group, Action Run)>();
        foreach (var k in _views.Keys) cmds.Add(($"Go to {k}", "Navigate", () => Navigate(k)));
        cmds.Add(("Advance one day", "Time", () => _ = AdvanceAsync(1, animate: false)));
        cmds.Add(("Advance one week", "Time", () => _ = AdvanceAsync(7, animate: false)));
        cmds.Add(("Advance one month", "Time", () => _ = AdvanceAsync(30, animate: false)));
        cmds.Add(("Skip to next event", "Time", () => _ = AdvanceAsync(60, untilEvent: true, animate: false)));
        cmds.Add(("Toggle fullscreen", "Display", () => _main.ToggleFullscreen()));
        cmds.Add(("Save game", "Game", () => _main.ShowSaveLoad(true)));
        cmds.Add(("How to play", "Game", () => ShowHelp()));
        cmds.Add(("Settings", "Game", () => _main.ShowSettings(true)));
        foreach (var sug in Core.AI.Guidance.Suggestions(Session.State))
            cmds.Add(($"Say: {sug.Text}", "Meeting", () => OpenMeeting(sug.Attendees, sug.Text)));
        foreach (var x in Session.State.Executives.Where(x => x.Role != ExecRole.Vendor))
            cmds.Add(($"Meet with {x.Name} ({x.Role})", "Meeting", () => OpenMeeting(new[] { x.Role }, null)));

        var input = new LineEdit { PlaceholderText = "Type to search screens, actions, people, things to say…", CustomMinimumSize = new Vector2(640, 44) };
        var list = new ItemList { CustomMinimumSize = new Vector2(640, 360) };
        List<(string Label, string Group, Action Run)> shown = new();
        void Filter()
        {
            list.Clear();
            var q = input.Text.Trim().ToLowerInvariant();
            shown = cmds.Where(c => q.Length == 0 || q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => c.Label.ToLowerInvariant().Contains(w))).Take(40).ToList();
            foreach (var c in shown) { int i = list.AddItem($"{c.Label}"); list.SetItemTooltip(i, c.Group); }
            if (shown.Count > 0) list.Select(0);
        }
        void RunAt(int i) { if (i < 0 || i >= shown.Count) return; var run = shown[i].Run; _palette!.Hide(); run(); }
        input.TextChanged += _ => Filter();
        input.TextSubmitted += _ => RunAt(list.GetSelectedItems().FirstOrDefault(0));
        input.GuiInput += ev =>
        {
            if (ev is not InputEventKey { Pressed: true } key || shown.Count == 0) return;
            int cur = list.GetSelectedItems().FirstOrDefault(0);
            if (key.Keycode == Key.Down) { list.Select(Math.Min(shown.Count - 1, cur + 1)); list.EnsureCurrentIsVisible(); input.AcceptEvent(); }
            if (key.Keycode == Key.Up) { list.Select(Math.Max(0, cur - 1)); list.EnsureCurrentIsVisible(); input.AcceptEvent(); }
        };
        list.ItemActivated += i => RunAt((int)i);
        _palette = new PopupPanel();
        _palette.AddChild(Ui.VBox(10, Ui.Caption("Command palette"), input, list, Ui.Label("↑↓ to choose · Enter to run · Esc to close", Fonts.Xs, Palette.TextMuted)));
        AddChild(_palette);
        Filter();
        _palette.PopupCentered();
        input.GrabFocus();
    }

    private static readonly Dictionary<string, string> NavHelp = new()
    {
        ["Office"] = "Your office — click objects to get around",
        ["Goals"] = "Objectives, stars, rank, achievements and report cards",
        ["Dashboard"] = "Your organization at a glance, and what needs your attention",
        ["Inbox"] = "Reports, escalations and notices from your executives",
        ["Meeting Room"] = "Talk to executives in your own words: ask, assign, decide",
        ["Decisions"] = "Proposals waiting for you, with who supports or opposes them and why",
        ["Projects"] = "Approved projects and the work you have delegated",
        ["Finance"] = "Monthly results, your technology budget, savings and contracts",
        ["Risk Register"] = "Known risks and past incidents",
        ["Executives"] = "Who's who, how much they trust you and how busy they are",
        ["Performance"] = "How you are being judged, and every decision on record",
    };

    private static T AddTip<T>(Container parent, T c, string tip) where T : Control { c.TooltipText = tip; parent.AddChild(c); return c; }

    /// <summary>Jump to the meeting room with chosen attendees and an optional sentence ready to send.</summary>
    public void OpenMeeting(IEnumerable<ExecRole> roles, string? prefill)
    {
        if (Meeting.HasPendingConfirmation) { Navigate("Meeting Room"); Toast("Confirm or cancel the pending decision first.", Palette.Warn); return; }
        Navigate("Meeting Room");
        Meeting.OpenWith(roles, prefill);
    }

    public void OpenInbox(string? messageId)
    {
        ((InboxView)_views["Inbox"]).Select(messageId);
        Navigate("Inbox");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.F1) ShowHelp();
        else if (k.Keycode == Key.F5) _ = AdvanceAsync(1, animate: false);
        else if (k.Keycode == Key.F6) _ = AdvanceAsync(7, animate: false);
        else if (k.Keycode == Key.F7) _ = AdvanceAsync(30, animate: false);
        else if (k.Keycode == Key.F8) _ = AdvanceAsync(60, untilEvent: true, animate: false);
        else if (k.CtrlPressed && k.Keycode >= Key.Key1 && k.Keycode <= Key.Key9)
        {
            int i = (int)(k.Keycode - Key.Key1);
            if (i < _views.Count) Navigate(_views.Keys.ElementAt(i), animate: false); // keyboard: no animation
        }
        else if (k.CtrlPressed && k.Keycode == Key.K)
        {
            OpenPalette();
        }
        else return;
        GetViewport().SetInputAsHandled();
    }

    public void ShowHelp(bool welcome = false)
    {
        var s = Session.State;
        var text = new RichTextLabel { BbcodeEnabled = true, FitContent = true, CustomMinimumSize = new Vector2(640, 0) };
        text.AddThemeFontSizeOverride("normal_font_size", 16);
        text.AppendText(
            (welcome ? $"[font_size=22]Welcome, {Ui.Esc(s.Player.Name)}.[/font_size]\nYou are the new {s.Player.Title} of {s.Company.Name}, a fictional regional health system.\n\n" : "[font_size=22]How to play[/font_size]\n\n") +
            "[b]1. Read your situation.[/b] The [b]Dashboard[/b] shows what needs attention. Start with the CEO's welcome note in the [b]Inbox[/b].\n\n" +
            "[b]2. Talk to your executives.[/b] In the [b]Meeting Room[/b], type in plain English — for example:\n" +
            "   [i]\"Daniel, assess our real disaster recovery time. Four weeks.\"[/i]\n" +
            "   You don't have to name anyone: the person whose job it is picks it up. Give a deadline for tasks. Click an [b]Idea[/b] if you're not sure what to say.\n" +
            "   Daniel (CIO), Priya (CISO) and Marcus (Architecture) report to you, so you can assign them work. Everyone else you have to ask.\n\n" +
            "[b]3. Decide.[/b] Approve, defer or reject proposals in [b]Decisions[/b]. Each executive shows whether they support it and why. Big spending goes to the CFO and CEO.\n\n" +
            "[b]4. Let time pass.[/b] Press [b]Next day / week / month[/b]. Work gets done, reports arrive, money moves, and sometimes things go wrong.\n\n" +
            "[color=#8a97ad]Your executives judge you by results and evidence — not by what you claim. Decisions backed by analysis and the right people score better.\n" +
            "Shortcuts: F1 help · F5/F6/F7 advance a day/week/month · F8 next event · Ctrl+K command palette · Ctrl+1–9 screens · F11 fullscreen.\n" +
            "In meetings: ↑/↓ previous lines · Esc clear or cancel · Ctrl+Enter confirm a decision.[/color]");
        var dlg = new AcceptDialog { Title = welcome ? "Your first day" : "How to play", OkButtonText = welcome ? "Let's begin" : "Got it" };
        dlg.AddChild(text);
        AddChild(dlg);
        dlg.Confirmed += dlg.QueueFree;
        dlg.Canceled += dlg.QueueFree;
        dlg.PopupCentered(new Vector2I(720, 520));
    }

    public override void _Ready()
    {
        if (!Session.Settings.SeenWelcome)
        {
            Session.Settings.SeenWelcome = true;
            try { Session.SaveSettings(); } catch (Exception ex) { GD.PushWarning(ex.Message); }
            if (!OS.GetCmdlineUserArgs().Contains("--smoke-test")) CallDeferred(MethodName.ShowWelcome);
        }
    }

    private void ShowWelcome() => ShowHelp(welcome: true);

    public override void _ExitTree()
    {
        // Cached views that are not currently displayed are not in the tree; free them explicitly.
        foreach (var v in _views.Values) if (IsInstanceValid(v) && !v.IsInsideTree()) v.QueueFree();
    }

    public IReadOnlyCollection<string> ViewKeys => _views.Keys;

    public void Navigate(string title) => Navigate(title, animate: true);

    public void Navigate(string title, bool animate)
    {
        _current = title;
        foreach (var c in _content.GetChildren()) _content.RemoveChild(c);
        var v = _views[title];
        _content.AddChild(v);
        foreach (var (t, b) in _nav)
        {
            b.AddThemeStyleboxOverride("normal", NavStyle(t == title));
            b.AddThemeColorOverride("font_color", t == title ? Palette.TextPrimary : Palette.TextSecondary);
            b.AddThemeColorOverride("icon_normal_color", t == title ? Palette.Accent : Palette.TextMuted);
        }
        RefreshAll();
        if (animate) Motion.FadeIn(v, Motion.Fade, 0);
    }

    private void OnStateChanged() => RefreshAll();

    private void OnAiStatus(Core.AI.Llm.LlmStatus s)
    {
        if (!IsInstanceValid(_ai)) return;
        _ai.Text = s switch
        {
            Core.AI.Llm.LlmStatus.Ready => "● Local AI",
            Core.AI.Llm.LlmStatus.Loading or Core.AI.Llm.LlmStatus.Starting => "◌ AI loading",
            _ => "○ Scripted dialogue",
        };
        _ai.AddThemeColorOverride("font_color", s == Core.AI.Llm.LlmStatus.Ready ? Palette.Success : Palette.TextMuted);
    }

    public void RefreshAll()
    {
        var s = Session.State;
        _company.Text = $"{s.Player.Name}, {s.Player.Title} · {s.Company.Name} · {s.Difficulty}";
        _company.TooltipText = _company.Text;
        _date.Text = s.CurrentDate.ToString("ddd MMM d, yyyy");
        var (into, needed) = ProgressionEngine.LevelProgress(s.Career.Xp);
        _rank.Text = $"{ProgressionEngine.RankTitle(s.Career.Xp).ToUpperInvariant()} · LV {ProgressionEngine.Level(s.Career.Xp)}";
        _xpBar.MaxValue = needed;
        if (_shownXp >= 0 && s.Career.Xp > _shownXp && ProgressionEngine.Level(_shownXp) == ProgressionEngine.Level(s.Career.Xp)) Ui.GlideTo(_xpBar, _xpBar.Value, into, 0.6);
        else _xpBar.Value = into;
        _shownXp = s.Career.Xp;
        _stars.Text = $"★ {s.Career.Stars}";
        SetBadge("Inbox", s.Inbox.Count(m => !m.Read));
        SetBadge("Decisions", s.Proposals.Count(p => p.Status == ProposalStatus.Pending) + s.Incidents.Count(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0));
        ((IGameView)_views[_current]).Refresh(Session);
        if (!_advancing) Callable.From(ShowCelebrations).CallDeferred();
    }

    /// <summary>Shows queued moments (achievements, objectives, rank-ups, report cards) one after another.</summary>
    private void ShowCelebrations()
    {
        if (!Session.HasGame || !IsInsideTree()) return;
        var s = Session.State;
        var queue = s.Career.PendingCelebrations.ToList();
        if (queue.Count == 0) return;
        s.Career.PendingCelebrations.Clear();
        // The report card is the big moment: show it first. Several rank-ups at once collapse into one.
        int topRank = queue.Where(q => q.StartsWith("rank:")).Select(q => int.Parse(q[5..])).DefaultIfEmpty(0).Max();
        queue = queue.Where(q => !q.StartsWith("rank:")).OrderBy(q => q.StartsWith("report:") ? 0 : q.StartsWith("failed:") ? 1 : 2).ToList();
        if (topRank > 0) queue.Add($"rank:{topRank}");
        var report = queue.FirstOrDefault(q => q.StartsWith("report:"));
        if (report != null && s.Career.ReportCards.FirstOrDefault(r => r.Id == report[7..]) is { } card)
        {
            var rest = queue.Where(q => q != report).ToList();
            card.Seen = true;
            Celebrations.ReportCard(_main, card, s, onClose: () => { s.Career.PendingCelebrations.InsertRange(0, rest); ShowCelebrations(); });
            return;
        }
        double delay = 0;
        foreach (var item in queue)
        {
            var parts = item.Split(':', 2);
            Action? show = parts[0] switch
            {
                "achievement" when s.Career.Achievements.FirstOrDefault(a => a.Id == parts[1]) is { } a =>
                    () => Celebrations.Banner(_main, "Achievement unlocked · +50 XP", a.Title, a.Description, "medal"),
                "objective" when s.Objectives.FirstOrDefault(o => o.Id == parts[1]) is { } o =>
                    () => Celebrations.Banner(_main, $"Objective complete · {new string('★', o.Stars)} · +{o.Stars * 100} XP", o.Title, o.Description, "check-circle"),
                "failed" when s.Objectives.FirstOrDefault(o => o.Id == parts[1]) is { } o =>
                    () => _main.Toast($"Objective missed: {o.Title}", Palette.Danger),
                "rank" => () => Celebrations.Banner(_main, "Your standing has grown", ProgressionEngine.Ranks[int.Parse(parts[1]) - 1].Title, $"You've reached level {parts[1]}. Your executives notice.", "lightning"),
                "report" when s.Career.ReportCards.FirstOrDefault(r => r.Id == parts[1]) is { } r => () => ShowReportCard(r),
                _ => null,
            };
            if (show == null) continue;
            if (delay == 0) show();
            else GetTree().CreateTimer(delay).Timeout += show;
            if (parts[0] is "achievement" or "objective" or "rank") delay += 3.8; // banners take turns; toasts and the report card don't wait
        }
    }

    public void ShowReportCard(QuarterReport r)
    {
        r.Seen = true;
        Celebrations.ReportCard(_main, r, Session.State, null);
    }

    private void SetBadge(string nav, int count)
    {
        if (!_badges.TryGetValue(nav, out var badge)) return;
        int before = _badgeCounts.GetValueOrDefault(nav);
        _badgeCounts[nav] = count;
        badge.Visible = count > 0;
        ((Label)badge.GetChild(0)).Text = count > 99 ? "99+" : count.ToString();
        var b = _nav[nav];
        badge.Position = new Vector2(b.Size.X - badge.GetCombinedMinimumSize().X - 10, (b.Size.Y - badge.GetCombinedMinimumSize().Y) / 2);
        if (count > before && before >= 0 && !Motion.Reduced && IsInsideTree())
        {
            // One gentle pulse when something new arrives.
            badge.PivotOffset = badge.GetCombinedMinimumSize() / 2;
            var t = badge.CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            t.TweenProperty(badge, "scale", new Vector2(1.25f, 1.25f), 0.12);
            t.TweenProperty(badge, "scale", Vector2.One, 0.18);
        }
    }

    private static bool IsUrgent(InboxMessage m) => m.Kind is MessageKind.Incident or MessageKind.Escalation;
    private static bool IsEvent(InboxMessage m) => m.Kind is MessageKind.Incident or MessageKind.Escalation or MessageKind.Review or MessageKind.Report;

    /// <summary>
    /// Advance time day by day. The date visibly ticks (mouse-initiated only), and the advance pauses as
    /// soon as something urgent happens — or, for "Next event", as soon as anything noteworthy arrives.
    /// </summary>
    public async Task AdvanceAsync(int maxDays, bool untilEvent = false, bool animate = true)
    {
        if (_advancing) return;
        if (Session.State.Outcome != GameOutcome.InProgress) { _main.Toast("Your tenure has ended. Start a new career from the menu.", Palette.Warning); return; }
        if (Meeting.HasPendingConfirmation) { _main.Toast("Confirm or cancel the pending decision in the meeting first.", Palette.Warning); return; }
        _advancing = true;
        foreach (var b in _timeButtons) b.Disabled = true;
        var office = (OfficeView)_views["Office"];
        bool onOffice = _current == "Office";
        animate &= !Motion.Reduced && (maxDays > 1 || onOffice);
        if (animate) { _timeStrip.Value = 0; _timeStrip.Modulate = Colors.White; }
        var got = new List<InboxMessage>();
        string? stopReason = null;
        int days = 0;
        double perDay = maxDays == 1 ? (onOffice ? 1.6 : 0) : Math.Clamp((onOffice ? 1.8 : 0.7) / maxDays, 0.012, onOffice ? 0.26 : 0.06);
        float startPhase = office.Scene.DayPhase;
        try
        {
            while (days < maxDays && Session.State.Outcome == GameOutcome.InProgress)
            {
                var msgs = Session.StepDay();
                days++;
                got.AddRange(msgs);
                _date.Text = Session.State.CurrentDate.ToString("ddd MMM d, yyyy");
                if (animate)
                {
                    _timeStrip.Value = days / (double)maxDays;
                    if (onOffice)
                    {
                        // Sweep the sky smoothly: a full day per day for short advances, a quarter per day for long ones.
                        office.Scene.State = Session.State;
                        // One full evening → night → morning cycle across the whole advance, however long.
                        float from = startPhase + (days - 1) / (float)maxDays, to = startPhase + days / (float)maxDays;
                        office.Scene.CreateTween().TweenMethod(Callable.From<float>(v => office.Scene.DayPhase = v), from, to, perDay);
                    }
                    await ToSignal(GetTree().CreateTimer(perDay), SceneTreeTimer.SignalName.Timeout);
                }
                var urgent = msgs.FirstOrDefault(IsUrgent);
                if (urgent != null && days < maxDays) { stopReason = $"Paused: {urgent.Subject}"; break; }
                if (untilEvent && msgs.FirstOrDefault(IsEvent) is { } ev) { stopReason = $"{ev.Subject}"; break; }
            }
        }
        finally
        {
            bool saved = Session.FinishAdvance();
            _advancing = false;
            foreach (var b in _timeButtons) b.Disabled = false;
            if (animate) _timeStrip.CreateTween().TweenProperty(_timeStrip, "modulate:a", 0.0, 0.25);
            if (onOffice) office.Scene.DayPhase = startPhase; // settle back to the resting light
            if (saved && !Motion.Reduced) { var t = _saved.CreateTween(); t.TweenProperty(_saved, "modulate:a", 1.0, 0.15); t.TweenInterval(1.2); t.TweenProperty(_saved, "modulate:a", 0.0, 0.3); }
        }

        Digest.Clear();
        Digest.AddRange(got);
        DigestNote = $"{days} day{(days == 1 ? "" : "s")} passed" + (stopReason != null && untilEvent == false ? " — paused early" : "");
        var urgentMsg = got.FirstOrDefault(IsUrgent);
        string text = stopReason ?? (got.Count == 0 ? $"{days} day{(days == 1 ? "" : "s")} passed. Nothing new." : $"{days} day{(days == 1 ? "" : "s")} passed · {got.Count} new message{(got.Count == 1 ? "" : "s")}");
        _main.Toast(text, got.Any(m => m.Kind == MessageKind.Incident) ? Palette.Danger : urgentMsg != null ? Palette.Warning : Palette.Accent);
        if (got.Any(m => m.Kind == MessageKind.Incident)) OpenInbox(got.First(m => m.Kind == MessageKind.Incident).Id);
        else RefreshAll();
    }

    public void DismissDigest() { Digest.Clear(); RefreshAll(); }

    public void Toast(string text, Color? c = null) => _main.Toast(text, c);
}
