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

        // ---- Top bar
        var bar = Ui.HBox(12);
        var barCard = Ui.Card(bar, Palette.Charcoal, Palette.Border);
        root.AddChild(barCard);
        _company = Ui.Label("", 16, Palette.Text);
        _date = Ui.Label("", 16, Palette.Accent);
        _ai = Ui.Label("", 13, Palette.Muted);
        // The company line shrinks with "…" so the bar never forces the layout wider than the window.
        _company.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _company.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _company.CustomMinimumSize = new Vector2(120, 0);
        bar.AddChild(_company);
        bar.AddChild(_ai);
        bar.AddChild(_date);
        AddTip(bar, Ui.Button("Next day", () => Advance(1)), "Move the calendar forward one day (F5). Your executives keep working while time passes.");
        AddTip(bar, Ui.Button("Next week", () => Advance(7)), "Move forward one week (F6).");
        AddTip(bar, Ui.Button("Next month", () => Advance(30)), "Move forward 30 days (F7). Month-end brings the financial close.");
        AddTip(bar, Ui.Button("Save", () => main.ShowSaveLoad(true)), "Save or load a game. The game also autosaves every time you advance.");
        AddTip(bar, Ui.Button("Settings", () => main.ShowSettings(true)), "Interface size, fullscreen, local AI.");
        AddTip(bar, Ui.Button("? Help", () => ShowHelp()), "How to play (F1)");
        AddTip(bar, Ui.Button("Menu", () => { Session.Save(); main.ShowMainMenu(); }), "Save and return to the main menu.");

        // ---- Body: nav + content
        var body = Ui.HBox(0);
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(body);
        var nav = Ui.VBox(4);
        var navCard = Ui.Card(nav, Palette.Charcoal, Palette.Border);
        navCard.CustomMinimumSize = new Vector2(220, 0);
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
        int n = 1;
        foreach (var (title, _) in _views)
        {
            var b = Ui.Button(title, () => Navigate(title));
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(0, 40);
            b.TooltipText = (NavHelp.TryGetValue(title, out var h) ? h : title) + $"  (Ctrl+{n++})";
            _nav[title] = b;
            nav.AddChild(b);
        }
        nav.AddChild(Ui.Spacer(false));
        nav.AddChild(Ui.Label(Session.State.Company.FictionNotice, 11, Palette.Muted, wrap: true));

        _content = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var side in new[] { "left", "right", "top", "bottom" }) _content.AddThemeConstantOverride($"margin_{side}", 20);
        body.AddChild(_content);

        Session.StateChanged += OnStateChanged;
        Session.AiStatusChanged += OnAiStatus;
        TreeExiting += () => { Session.StateChanged -= OnStateChanged; Session.AiStatusChanged -= OnAiStatus; };
        OnAiStatus(Session.Llm?.Status ?? Core.AI.Llm.LlmStatus.Stopped);
        Navigate("Dashboard");
    }

    private void Register(string navKey, Control view) => _views[navKey] = view;

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
            if (i < _views.Count) Navigate(_views.Keys.ElementAt(i));
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

    public void Navigate(string title)
    {
        _current = title;
        foreach (var c in _content.GetChildren()) _content.RemoveChild(c);
        var v = _views[title];
        _content.AddChild(v);
        foreach (var (t, b) in _nav)
            b.AddThemeStyleboxOverride("normal", t == title ? GameTheme.Box(Palette.AccentDim, Palette.Accent, 5, 10) : GameTheme.Box(Palette.SurfaceRaised, Palette.Border, 5, 10));
        RefreshAll();
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
        _ai.AddThemeColorOverride("font_color", s == Core.AI.Llm.LlmStatus.Ready ? Palette.Good : Palette.Muted);
    }

    public void RefreshAll()
    {
        var s = Session.State;
        _company.Text = $"{s.Company.Name}  ·  {s.Player.Name}, {s.Player.Title}  ·  {s.Difficulty}";
        _company.TooltipText = _company.Text;
        _date.Text = s.CurrentDate.ToString("dddd, MMMM d, yyyy");
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
