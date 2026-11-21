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
        bar.AddChild(_company);
        bar.AddChild(Ui.Spacer());
        bar.AddChild(_ai);
        bar.AddChild(_date);
        bar.AddChild(Ui.Button("+1 day", () => Advance(1)));
        bar.AddChild(Ui.Button("+1 week", () => Advance(7)));
        bar.AddChild(Ui.Button("+1 month", () => Advance(30)));
        bar.AddChild(Ui.Button("Save", () => main.ShowSaveLoad(true)));
        bar.AddChild(Ui.Button("Settings", () => main.ShowSettings(true)));
        bar.AddChild(Ui.Button("Menu", () => { Session.Save(); main.ShowMainMenu(); }));

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
        foreach (var (title, _) in _views)
        {
            var b = Ui.Button(title, () => Navigate(title));
            b.Alignment = HorizontalAlignment.Left;
            b.CustomMinimumSize = new Vector2(0, 38);
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
