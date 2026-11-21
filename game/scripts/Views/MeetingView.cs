using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.AI;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>
/// The meeting room: choose attendees, speak freely, see how the organization interpreted you, and
/// confirm high-impact decisions. Dialogue is labeled when AI-generated; engine results are shown
/// separately so the player can always tell what actually happened.
/// </summary>
public partial class MeetingView : VBoxContainer, IGameView
{
    public string Title => "Meeting Room";
    private readonly GameScreen _game = null!;
    private MeetingService? _meeting;
    private readonly Dictionary<ExecRole, CheckBox> _attendees = new();
    private RichTextLabel _log = null!;
    private LineEdit _input = null!;
    private Button _send = null!, _cancelGen = null!;
    private HBoxContainer _confirmBar = null!;
    private PanelContainer _confirmCard = null!;
    private Label _confirmText = null!, _thinking = null!, _who = null!;
    private CancellationTokenSource? _cts;
    private GameState? _boundState;

    public bool HasPendingConfirmation => _meeting?.HasPendingConfirmation == true;

    public MeetingView() { }

    public MeetingView(GameScreen game)
    {
        _game = game;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(Ui.Heading("Meeting Room"));
        AddChild(Ui.Label("Talk naturally. Say who should do what, by when, and with how much money. Nothing happens until you see \u25b6 RECORDED.", 14, Palette.Muted, wrap: true));

        // Who is in the room: one-click presets, or fine-tune with the checkboxes.
        var presets = Ui.HBox(8, Ui.Label("Quick meetings:", 14, Palette.Muted));
        foreach (var (label, roles, tip) in Presets)
        {
            var b = Ui.Button(label, () => OpenWith(roles, null));
            b.TooltipText = tip;
            presets.AddChild(b);
        }
        var pick = Ui.HBox(10, Ui.Label("Or choose:", 14, Palette.Muted));
        foreach (var r in Roles.Addressable)
        {
            var cb = new CheckBox { Text = Roles.Label(r) == "Enterprise Architecture Director" ? "EA Director" : Roles.Label(r), ButtonPressed = r is ExecRole.CIO or ExecRole.CISO or ExecRole.EADirector };
            _attendees[r] = cb;
            pick.AddChild(cb);
        }
        pick.AddChild(Ui.Spacer());
        pick.AddChild(Ui.Button("Start meeting", StartMeeting, primary: true));
        AddChild(Ui.Card(Ui.VBox(8, presets, pick)));

        _who = Ui.Label("", 13, Palette.Muted, wrap: true);
        AddChild(_who);

        _log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true, CustomMinimumSize = new Vector2(0, 260) };
        _log.AddThemeFontSizeOverride("normal_font_size", 16);
        _log.AddThemeFontSizeOverride("bold_font_size", 16);
        AddChild(_log);

        // "Raymond is typing…" — replies arrive one at a time at each executive's own pace.
        _typing = Ui.Label("", 14, Palette.Muted);
        _skip = Ui.Button("Skip", () => _skipRequested = true);
        _skip.TooltipText = "Show all replies now. Reply pacing can be changed in Settings.";
        _typingRow = Ui.HBox(10, _typing, Ui.Spacer(), _skip);
        _typingRow.Visible = false;
        AddChild(_typingRow);

        _confirmText = Ui.Label("", 15, Palette.Warn, wrap: true);
        _confirmBar = Ui.HBox(10, _confirmText, Ui.Button("Cancel", () => _ = Confirm(false)), Ui.Button("Confirm decision", () => _ = Confirm(true), primary: true));
        _confirmCard = Ui.Card(_confirmBar, Palette.SurfaceRaised, Palette.Warn);
        _confirmCard.Visible = false;
        AddChild(_confirmCard);

        // Clickable ideas based on the current situation. Clicking fills the box so you can edit before sending.
        _ideas = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _ideas.AddThemeConstantOverride("h_separation", 6);
        _ideas.AddThemeConstantOverride("v_separation", 6);
        AddChild(Ui.HBox(8, Ui.Label("Ideas:", 13, Palette.Muted), _ideas));

        _thinking = Ui.Label("", 13, Palette.Muted);
        _input = new LineEdit { PlaceholderText = "Type what you want to say, then press Enter — or click an idea above", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 1000, CustomMinimumSize = new Vector2(0, 42) };
        _input.AddThemeFontSizeOverride("font_size", 16);
        _input.TextSubmitted += _text => _ = Send();
        _send = Ui.Button("Say  \u23ce", () => _ = Send(), primary: true);
        _cancelGen = Ui.Button("Stop", () => _cts?.Cancel());
        _cancelGen.Visible = false;
        AddChild(Ui.HBox(8, _input, _send, _cancelGen));
        AddChild(_thinking);
    }

    private static readonly (string Label, ExecRole[] Roles, string Tip)[] Presets =
    {
        ("Technology team", new[] { ExecRole.CIO, ExecRole.CISO, ExecRole.EADirector }, "Your direct reports: you can assign them work"),
        ("Executive committee", new[] { ExecRole.CEO, ExecRole.CFO, ExecRole.CMO, ExecRole.CNO }, "Your peers and your boss: ask, persuade, report"),
        ("CFO one-on-one", new[] { ExecRole.CFO }, "Money, savings and capital approvals"),
        ("Clinical leaders", new[] { ExecRole.CMO, ExecRole.CNO, ExecRole.CIO }, "Patient safety and anything touching the EHR"),
        ("Vendor meeting", new[] { ExecRole.Vendor, ExecRole.CIO }, "The EHR vendor's account executive"),
    };

    private HFlowContainer _ideas = null!;
    private Label _typing = null!;
    private Button _skip = null!;
    private HBoxContainer _typingRow = null!;
    private bool _skipRequested;
    private string _typingName = "";
    private double _dotClock;

    public override void _Process(double delta)
    {
        if (!_typingRow.Visible) return;
        _dotClock += delta;
        int dots = (int)(_dotClock * 3) % 4;
        _typing.Text = $"{_typingName} is typing{new string('.', dots)}";
    }

    private async Task WaitAsync(double seconds)
    {
        double end = Time.GetTicksMsec() / 1000.0 + seconds;
        while (!_skipRequested && Time.GetTicksMsec() / 1000.0 < end && IsInsideTree())
            await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>Reveal replies one by one with a typing indicator, then the recorded outcome.</summary>
    private async Task RenderAsync(MeetingResponse r)
    {
        var pacing = (ReplyPacing)_game.Session.Settings.ReplyPacing;
        _skipRequested = false;
        var s = _game.Session.State;
        foreach (var reply in r.Replies)
        {
            if (pacing != ReplyPacing.Off && !_skipRequested && IsInsideTree() && s.TryExec(reply.Speaker) is { } x)
            {
                var t = TypingPace.For(x, reply.Text, pacing);
                _typingName = reply.Name.StartsWith("Dr. ") ? "Dr. " + reply.Name.Split(' ')[^1] : reply.Name.Split(' ')[0];
                _dotClock = 0;
                await WaitAsync(t.ThinkSeconds);          // thinking: no indicator yet
                _typingRow.Visible = true;
                await WaitAsync(t.TypeSeconds);           // typing
                _typingRow.Visible = false;
            }
            AppendReply(reply);
        }
        _typingRow.Visible = false;
        AppendOutcome(r);
    }

    /// <summary>Open a meeting with specific people, optionally with a sentence ready to send.</summary>
    public void OpenWith(IEnumerable<ExecRole> roles, string? prefill)
    {
        var set = roles.ToHashSet();
        foreach (var (r, cb) in _attendees) cb.ButtonPressed = set.Contains(r);
        StartMeeting();
        if (prefill != null) { _input.Text = prefill; _input.CaretColumn = prefill.Length; }
        if (IsInsideTree()) _input.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void RefreshIdeas()
    {
        Ui.Clear(_ideas);
        if (!_game.Session.HasGame) return;
        foreach (var sug in Guidance.Suggestions(_game.Session.State).Take(7))
        {
            var chip = Ui.Button(sug.Text, () =>
            {
                // If the right people aren't in the room, bring them in.
                if (_meeting != null && !sug.Attendees.Any(r => _meeting.Attendees.Contains(r))) OpenWith(sug.Attendees, sug.Text);
                else { _input.Text = sug.Text; _input.CaretColumn = sug.Text.Length; _input.GrabFocus(); }
            });
            chip.TooltipText = sug.Why + " — click to put it in the box, then press Enter";
            chip.AddThemeFontSizeOverride("font_size", 13);
            _ideas.AddChild(chip);
        }
    }

    public void Refresh(GameSession session)
    {
        // A loaded game has a different state object: meetings do not carry across.
        if (_boundState != session.State) { _meeting = null; _boundState = session.State; _log.Clear(); }
        if (_meeting == null) StartMeeting();
        _confirmCard.Visible = _meeting?.HasPendingConfirmation == true;
        _confirmText.Text = _meeting?.PendingConfirmationPrompt ?? "";
        RefreshIdeas();
    }

    private void StartMeeting()
    {
        var s = _game.Session.State;
        var roles = _attendees.Where(kv => kv.Value.ButtonPressed).Select(kv => kv.Key).ToList();
        if (roles.Count == 0) { _game.Toast("Choose at least one attendee.", Palette.Warn); return; }
        _meeting = new MeetingService(_game.Session.Engine!, roles, _game.Session.Llm);
        _log.Clear();
        _log.AppendText($"[color={Palette.Hex(Palette.Muted)}]{s.CurrentDate:dddd, MMMM d} — meeting with {string.Join(", ", roles.Select(r => s.Exec(r).Name))}[/color]\n\n");
        _who.Text = string.Join("   ·   ", roles.Select(r => { var x = s.Exec(r); return $"{x.Name} — {x.Title}{(x.ReportsTo == s.Player.Role ? " (reports to you)" : "")}"; }));
        _confirmCard.Visible = false;
    }

    private async Task Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _meeting == null) return;
        if (_game.Session.State.Outcome != GameOutcome.InProgress) { _game.Toast("Your tenure has ended.", Palette.Warn); return; }
        _input.Text = "";
        _log.AppendText($"[color={Palette.Hex(Palette.Accent)}][b]You:[/b][/color] {Ui.Esc(text)}\n");
        await Run(ct => _meeting.SayAsync(text, ExecRole.None, ct));
    }

    private async Task Confirm(bool yes)
    {
        if (_meeting == null) return;
        _log.AppendText($"[color={Palette.Hex(Palette.Accent)}][b]You:[/b][/color] {(yes ? "Confirmed." : "Let's not proceed.")}\n");
        await Run(ct => _meeting.ConfirmAsync(yes, ct));
    }

    private async Task Run(Func<CancellationToken, Task<MeetingResponse>> call)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var r = await call(_cts.Token);
            await RenderAsync(r);
        }
        catch (Exception e)
        {
            // Defensive: a failure here must never take the game down.
            _log.AppendText($"[color={Palette.Hex(Palette.Bad)}]Meeting error: {Ui.Esc(e.Message)}[/color]\n");
            GD.PushError(e.ToString());
        }
        finally
        {
            SetBusy(false);
            _confirmCard.Visible = _meeting?.HasPendingConfirmation == true;
            _confirmText.Text = _meeting?.PendingConfirmationPrompt ?? "";
            _game.Session.NotifyChanged();
            _input.GrabFocus();
        }
    }

    private void SetBusy(bool busy)
    {
        _send.Disabled = busy;
        _input.Editable = !busy;
        _cancelGen.Visible = busy && _game.Session.Llm?.Status == Core.AI.Llm.LlmStatus.Ready;
        _thinking.Text = busy ? (_game.Session.Llm?.Status == Core.AI.Llm.LlmStatus.Ready ? "Executives are considering your words…" : "") : "";
    }

    private void Render(MeetingResponse r)
    {
        foreach (var reply in r.Replies) AppendReply(reply);
        AppendOutcome(r);
    }

    private void AppendReply(MeetingReply reply)
    {
        {
            string pos = reply.Position switch
            {
                Core.Executives.Position.Support => $" [color={Palette.Hex(Palette.Good)}]supports[/color]",
                Core.Executives.Position.Oppose => $" [color={Palette.Hex(Palette.Bad)}]opposes[/color]",
                Core.Executives.Position.Concerned => $" [color={Palette.Hex(Palette.Warn)}]concerned[/color]",
                Core.Executives.Position.Conditional => $" [color={Palette.Hex(Palette.Muted)}]conditional[/color]",
                _ => "",
            };
            string tag = reply.AiGenerated ? $" [color={Palette.Hex(Palette.Muted)}][font_size=11]AI[/font_size][/color]" : "";
            _log.AppendText($"[b]{Ui.Esc(reply.Name)}[/b] [color={Palette.Hex(Palette.Muted)}]({Roles.Label(reply.Speaker)})[/color]{pos}{tag}\n{Ui.Esc(reply.Text)}\n\n");
        }
    }

    private void AppendOutcome(MeetingResponse r)
    {
        var muted = Palette.Hex(Palette.Muted);
        if (r.Intent?.Action is { } a && r.Outcome == null && r.ConfirmationPrompt == null)
            _log.AppendText($"[color={muted}][i]Interpreted as {a.Name.Replace('_', ' ')} — not carried out.[/i][/color]\n");
        if (r.Outcome?.Executed == true)
        {
            _log.AppendText($"[color={Palette.Hex(Palette.Good)}]▶ RECORDED: {Ui.Esc(r.Outcome.Summary)}[/color]\n");
            foreach (var e in r.Outcome.Effects) _log.AppendText($"[color={muted}]   • {Ui.Esc(e)}[/color]\n");
            var dec = _game.Session.State.Decisions.LastOrDefault(d => d.Id == r.Outcome.DecisionId);
            if (dec != null) _log.AppendText($"[color={muted}]   • Decision {dec.Id} logged for review.[/color]\n");
            _log.AppendText("\n");
        }
        foreach (var n in r.SystemNotes.Where(n => r.Outcome == null || (n != r.Outcome.Summary && !r.Outcome.Effects.Contains(n))))
            _log.AppendText($"[color={muted}][i]{Ui.Esc(n)}[/i][/color]\n");
    }

    /// <summary>Used by the smoke test: goes through the real input → paced rendering path.</summary>
    public Task SendForTest(string text) { _input.Text = text; return Send(); }
    public bool TypingVisible => _typingRow.Visible;
    public string LogText => _log.GetParsedText();

    /// <summary>Used by the smoke test.</summary>
    public Task<MeetingResponse> SayForTest(string text) => _meeting!.SayAsync(text);
    public Task<MeetingResponse> ConfirmForTest(bool yes) => _meeting!.ConfirmAsync(yes);
    public void RenderForTest(MeetingResponse r) => Render(r);
}
