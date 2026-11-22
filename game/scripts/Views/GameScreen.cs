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
        bar.AddChild(Ui.Gap(8));
        _date = Ui.Label("", Fonts.Base, Palette.TextPrimary, font: Fonts.MonoMedium);
        var dateBox = Ui.HBox(8, Ui.Icon("calendar-plus", 18, Palette.Accent), _date);
        bar.AddChild(dateBox);
        AddTip(bar, Ui.Button("Day", () => Advance(1), icon: "play"), "Advance one day (F5). Your executives keep working while time passes.");
        AddTip(bar, Ui.Button("Week", () => Advance(7), icon: "fast-forward"), "Advance one week (F6).");
        AddTip(bar, Ui.Button("Month", () => Advance(30), icon: "fast-forward"), "Advance 30 days (F7). Month-end brings the financial close.");
        bar.AddChild(Ui.Gap(8));
        AddTip(bar, IconButton("magnifying-glass", OpenPalette), "Command palette — jump anywhere or do anything (Ctrl+K)");
        AddTip(bar, IconButton("floppy-disk", () => main.ShowSaveLoad(true)), "Save or load (the game also autosaves when you advance time)");
        AddTip(bar, IconButton("question", () => ShowHelp()), "How to play (F1)");
        AddTip(bar, IconButton("gear-six", () => main.ShowSettings(true)), "Settings: interface size, motion, local AI");
        AddTip(bar, IconButton("list", () => { Session.Save(); main.ShowMainMenu(); }), "Save and return to the main menu");

        // ---- Body: nav + content
        var body = Ui.HBox(0);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(body);
        var nav = Ui.VBox(2);
        var navCard = Ui.Card(nav, Palette.BgSunken, Palette.Border, 12);
        navCard.AddThemeStyleboxOverride("panel", NoRadius(GameTheme.Box(Palette.BgSunken, Palette.Border, 0, 12)));
        navCard.CustomMinimumSize = new Vector2(232, 0);
        body.AddChild(navCard);

        Register("Dashboard", new DashboardView(this));
        Register("Inbox", new InboxView(this));
        Register("Meeting Room", new MeetingView(this));
        Register("Decisions", new ProposalsView(this));
        Register("Projects", new ProjectsView(this));
        Register("Finance", new FinanceView(this));
        Register("Risk Register", new RiskView(this));
        Register("Executives", new PeopleView(this));
        Register("Performance", new PerformanceView(this));
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
        Navigate("Dashboard");
    }

    private void Register(string navKey, Control view) => _views[navKey] = view;

    private static readonly Dictionary<string, string> NavIcon = new()
    {
        ["Dashboard"] = "squares-four", ["Inbox"] = "tray", ["Meeting Room"] = "chats-circle", ["Decisions"] = "scales", ["Projects"] = "kanban",
        ["Finance"] = "chart-line-up", ["Risk Register"] = "shield-warning", ["Executives"] = "users-three", ["Performance"] = "medal",
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
        cmds.Add(("Advance one day", "Time", () => Advance(1)));
        cmds.Add(("Advance one week", "Time", () => Advance(7)));
        cmds.Add(("Advance one month", "Time", () => Advance(30)));
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

    private static void AddTip(Container parent, Control c, string tip) { c.TooltipText = tip; parent.AddChild(c); }

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
        else if (k.Keycode == Key.F5) Advance(1);
        else if (k.Keycode == Key.F6) Advance(7);
        else if (k.Keycode == Key.F7) Advance(30);
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
            "   Name the person, say what you want, and give a deadline. Click an [b]Idea[/b] if you're not sure what to say.\n" +
            "   Daniel (CIO), Priya (CISO) and Marcus (Architecture) report to you, so you can assign them work. Everyone else you have to ask.\n\n" +
            "[b]3. Decide.[/b] Approve, defer or reject proposals in [b]Decisions[/b]. Each executive shows whether they support it and why. Big spending goes to the CFO and CEO.\n\n" +
            "[b]4. Let time pass.[/b] Press [b]Next day / week / month[/b]. Work gets done, reports arrive, money moves, and sometimes things go wrong.\n\n" +
            "[color=#8a97ad]Your executives judge you by results and evidence — not by what you claim. Decisions backed by analysis and the right people score better.\n" +
            "Shortcuts: F1 help · F5/F6/F7 advance a day/week/month · Ctrl+1–9 switch screens.[/color]");
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
        int unread = s.Inbox.Count(m => !m.Read);
        _nav["Inbox"].Text = unread > 0 ? $"Inbox  ({unread})" : "Inbox";
        int pending = s.Proposals.Count(p => p.Status == ProposalStatus.Pending) + s.Incidents.Count(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0);
        _nav["Decisions"].Text = pending > 0 ? $"Decisions  ({pending})" : "Decisions";
        ((IGameView)_views[_current]).Refresh(Session);
    }

    private void Advance(int days)
    {
        if (Session.State.Outcome != GameOutcome.InProgress) { _main.Toast("Your tenure has ended. Start a new career from the menu.", Palette.Warn); return; }
        if (Meeting.HasPendingConfirmation) { _main.Toast("Confirm or cancel the pending decision in the meeting first.", Palette.Warn); return; }
        var msgs = Session.Advance(days);
        var urgent = msgs.FirstOrDefault(m => m.Kind is MessageKind.Incident or MessageKind.Escalation or MessageKind.Review);
        _main.Toast(urgent != null ? $"{urgent.Subject}" : $"{msgs.Count} new message(s).", urgent?.Kind == MessageKind.Incident ? Palette.Bad : urgent != null ? Palette.Warn : Palette.Accent);
        if (msgs.Any(m => m.Kind == MessageKind.Incident)) Navigate("Inbox");
    }

    public void Toast(string text, Color? c = null) => _main.Toast(text, c);
}
