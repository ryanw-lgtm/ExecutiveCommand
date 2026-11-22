using System.Text;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.AI;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>
/// The meeting room as a chat thread. You speak freely; executives answer one at a time at their own
/// pace; what the organization actually recorded appears as a distinct card, separate from dialogue.
/// AI-generated lines are labeled.
/// </summary>
public partial class MeetingView : VBoxContainer, IGameView
{
    public string Title => "Meeting Room";
    private readonly GameScreen _game = null!;
    private MeetingService? _meeting;
    private readonly Dictionary<ExecRole, CheckBox> _attendees = new();
    private ScrollContainer _scroll = null!;
    private VBoxContainer _thread = null!;
    private HBoxContainer _participants = null!;
    private LineEdit _input = null!;
    private Button _send = null!, _cancelGen = null!;
    private PanelContainer _confirmCard = null!;
    private Label _confirmText = null!, _thinking = null!;
    private HFlowContainer _ideas = null!;
    private Control? _typingBubble;
    private Label? _typingLabel;
    private Button _skip = null!;
    private bool _skipRequested;
    private string _typingName = "";
    private double _dotClock;
    private CancellationTokenSource? _cts;
    private GameState? _boundState;
    private readonly StringBuilder _transcript = new();

    public bool HasPendingConfirmation => _meeting?.HasPendingConfirmation == true;

    private static readonly (string Label, ExecRole[] Roles, string Tip)[] Presets =
    {
        ("Technology team", new[] { ExecRole.CIO, ExecRole.CISO, ExecRole.EADirector }, "Your direct reports: you can assign them work"),
        ("Executive committee", new[] { ExecRole.CEO, ExecRole.CFO, ExecRole.CMO, ExecRole.CNO }, "Your peers and your boss: ask, persuade, report"),
        ("CFO one-on-one", new[] { ExecRole.CFO }, "Money, savings and capital approvals"),
        ("Clinical leaders", new[] { ExecRole.CMO, ExecRole.CNO, ExecRole.CIO }, "Patient safety and anything touching the EHR"),
        ("Vendor meeting", new[] { ExecRole.Vendor, ExecRole.CIO }, "The EHR vendor's account executive"),
    };

    public MeetingView() { }

    public MeetingView(GameScreen game)
    {
        _game = game;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);

        AddChild(Ui.HBox(16, Ui.Heading("Meeting Room", 28), Ui.Spacer(),
            Ui.Label("Say who should do what, by when, with how much money.", Fonts.Sm, Palette.TextSecondary)));

        // Who is in the room.
        var presets = Ui.HBox(8, Ui.Caption("Quick meetings"));
        foreach (var (label, roles, tip) in Presets)
        {
            var b = Ui.Button(label, () => OpenWith(roles, null));
            b.TooltipText = tip;
            presets.AddChild(b);
        }
        var pick = Ui.HBox(6, Ui.Caption("Or choose"));
        foreach (var r in Roles.Addressable)
        {
            var cb = new CheckBox { Text = r == ExecRole.EADirector ? "EA Director" : r == ExecRole.Vendor ? "Vendor" : r.ToString(), ButtonPressed = r is ExecRole.CIO or ExecRole.CISO or ExecRole.EADirector };
            _attendees[r] = cb;
            pick.AddChild(cb);
        }
        pick.AddChild(Ui.Spacer());
        pick.AddChild(Ui.Button("Start meeting", StartMeeting, icon: "arrow-right"));
        AddChild(Ui.Card(Ui.VBox(10, presets, pick), null, null, 14));

        _participants = Ui.HBox(20);
        AddChild(_participants);

        // The thread.
        _thread = Ui.VBox(14);
        _scroll = Ui.Scroll(_thread);
        var threadCard = Ui.Card(_scroll, Palette.BgSunken, Palette.Border, 20);
        threadCard.SizeFlagsVertical = SizeFlags.ExpandFill;
        threadCard.CustomMinimumSize = new Vector2(0, 240);
        AddChild(threadCard);

        // Confirmation for high-impact decisions.
        _confirmText = Ui.Label("", Fonts.Base, Palette.TextPrimary, wrap: true);
        var confirmRow = Ui.HBox(12, Ui.Icon("warning-circle", 22, Palette.Warning), _confirmText,
            Ui.Button("Cancel", () => _ = Confirm(false)), Ui.Button("Confirm decision", () => _ = Confirm(true), primary: true, icon: "check-circle"));
        _confirmCard = Ui.AccentCard(confirmRow, Palette.Warning);
        _confirmCard.Visible = false;
        AddChild(_confirmCard);

        // Ideas: clicking fills the box so you can edit before sending.
        _ideas = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _ideas.AddThemeConstantOverride("h_separation", 8);
        _ideas.AddThemeConstantOverride("v_separation", 8);
        AddChild(Ui.HBox(12, Ui.Caption("Ideas"), _ideas));

        _thinking = Ui.Label("", Fonts.Xs, Palette.TextMuted);
        _input = new LineEdit { PlaceholderText = "Type what you want to say and press Enter — or pick an idea above", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 1000, CustomMinimumSize = new Vector2(0, 48) };
        _input.AddThemeFontSizeOverride("font_size", Fonts.Base);
        _input.TextSubmitted += _text => _ = Send();
        _send = Ui.Button("Say", () => _ = Send(), primary: true, icon: "arrow-right");
        _send.CustomMinimumSize = new Vector2(96, 48);
        _skip = Ui.Button("Skip", () => _skipRequested = true);
        _skip.TooltipText = "Show all replies now (reply pacing can be changed in Settings)";
        _skip.Visible = false;
        _cancelGen = Ui.Button("Stop", () => _cts?.Cancel());
        _cancelGen.Visible = false;
        AddChild(Ui.HBox(8, _input, _skip, _cancelGen, _send));
        AddChild(_thinking);
    }

    public override void _Process(double delta)
    {
        if (_typingLabel == null || !IsInstanceValid(_typingLabel)) return;
        _dotClock += delta;
        _typingLabel.Text = $"{_typingName} is typing" + new string('.', (int)(_dotClock * 3) % 4);
    }

    public void Refresh(GameSession session)
    {
        if (_boundState != session.State) { _meeting = null; _boundState = session.State; Ui.Clear(_thread); }
        if (_meeting == null) StartMeeting();
        _confirmCard.Visible = _meeting?.HasPendingConfirmation == true;
        _confirmText.Text = _meeting?.PendingConfirmationPrompt ?? "";
        RefreshIdeas();
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
        foreach (var sug in Guidance.Suggestions(_game.Session.State).Take(6))
        {
            var chip = Ui.Button(sug.Text, () =>
            {
                if (_meeting != null && !sug.Attendees.Any(r => _meeting.Attendees.Contains(r))) OpenWith(sug.Attendees, sug.Text);
                else { _input.Text = sug.Text; _input.CaretColumn = sug.Text.Length; _input.GrabFocus(); }
            });
            chip.TooltipText = sug.Why + " — click to put it in the box, then press Enter";
            chip.AddThemeFontOverride("font", Fonts.Sans);
            chip.AddThemeFontSizeOverride("font_size", Fonts.Sm);
            chip.CustomMinimumSize = new Vector2(0, 32);
            chip.AddThemeStyleboxOverride("normal", GameTheme.Box(Palette.BgElevated, Palette.Border, 16, 12));
            chip.AddThemeStyleboxOverride("hover", GameTheme.Box(Palette.BgHover, Palette.Accent, 16, 12));
            _ideas.AddChild(chip);
        }
    }

    private void StartMeeting()
    {
        var s = _game.Session.State;
        var roles = _attendees.Where(kv => kv.Value.ButtonPressed).Select(kv => kv.Key).ToList();
        if (roles.Count == 0) { _game.Toast("Choose at least one attendee.", Palette.Warning); return; }
        _meeting = new MeetingService(_game.Session.Engine!, roles, _game.Session.Llm);
        Ui.Clear(_thread);
        _transcript.Clear();
        Ui.Clear(_participants);
        foreach (var r in roles)
        {
            var x = s.Exec(r);
            _participants.AddChild(Ui.HBox(10, Ui.Avatar(x.Name, 32, RoleColor(r)),
                Ui.VBox(0, Ui.Label(x.Name, Fonts.Sm, Palette.TextPrimary, font: Fonts.SansMedium),
                    Ui.Label(x.ReportsTo == s.Player.Role ? $"{r} · reports to you" : r == ExecRole.Vendor ? "External vendor" : r.ToString(), Fonts.Xs, Palette.TextMuted))));
        }
        var divider = Ui.Label($"{s.CurrentDate:dddd, MMMM d} · meeting started", Fonts.Xs, Palette.TextMuted);
        divider.HorizontalAlignment = HorizontalAlignment.Center;
        _thread.AddChild(divider);
        var hint = Ui.Empty("The room is listening.", "Ask a question, give an instruction with a deadline, or pick one of the ideas below.", "chats-circle");
        hint.SetMeta("empty", true);
        _thread.AddChild(hint);
        _confirmCard.Visible = false;
    }

    private static Color RoleColor(ExecRole r) => r switch
    {
        ExecRole.CEO => new Color("3b2f12"), ExecRole.CFO => new Color("12313a"), ExecRole.CMO or ExecRole.CNO => new Color("153526"),
        ExecRole.CISO => new Color("3a1a1f"), ExecRole.Vendor => new Color("2a2a2a"), _ => new Color("1c2b4a"),
    };

    // ------------------------------------------------------------------ thread items

    private void AddToThread(Control c)
    {
        // The "room is listening" hint goes away once the conversation starts.
        if (_thread.GetChildCount() >= 2 && _thread.GetChild(1) is HBoxContainer first && first.GetMeta("empty", false).AsBool()) { _thread.RemoveChild(first); first.QueueFree(); }
        _thread.AddChild(c);
        Motion.FadeIn(c, 0.2, 0);
        ScrollToEnd();
    }

    private async void ScrollToEnd()
    {
        if (!IsInsideTree()) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }

    private void AppendPlayer(string text)
    {
        _transcript.AppendLine($"You: {text}");
        var bubble = Ui.Card(Ui.Label(text, Fonts.Base, Palette.TextPrimary, wrap: true), Palette.AccentSoft, new Color(Palette.Accent, 0.45f), 14);
        bubble.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        bubble.SizeFlagsStretchRatio = 7;
        var spacer = Ui.Spacer();
        spacer.SizeFlagsStretchRatio = 3;
        AddToThread(Ui.VBox(4, Ui.HBox(0, Ui.Spacer(), Ui.Caption("You")), Ui.HBox(0, spacer, bubble)));
    }

    private void AppendReply(MeetingReply reply)
    {
        _transcript.AppendLine($"{reply.Name}: {reply.Text}");
        var header = Ui.HBox(8, Ui.Label(reply.Name, Fonts.Sm, Palette.TextPrimary, font: Fonts.SansSemi), Ui.Label(Roles.Label(reply.Speaker), Fonts.Xs, Palette.TextMuted));
        var (word, color) = reply.Position switch
        {
            Core.Executives.Position.Support => ("● supports", Palette.Success),
            Core.Executives.Position.Oppose => ("● opposes", Palette.Danger),
            Core.Executives.Position.Concerned => ("● concerned", Palette.Warning),
            Core.Executives.Position.Conditional => ("● it depends", Palette.TextSecondary),
            Core.Executives.Position.Neutral => ("● neutral", Palette.TextSecondary),
            _ => ("", Palette.TextSecondary),
        };
        if (word.Length > 0) header.AddChild(Ui.Pill(word, color));
        if (reply.AiGenerated) { var ai = Ui.Pill("AI", Palette.Info); ai.TooltipText = "Worded by the local language model; the position itself comes from the simulation."; header.AddChild(ai); }
        var bubble = Ui.Card(Ui.Label(reply.Text, Fonts.Base, Palette.TextPrimary, wrap: true), Palette.BgElevated, Palette.Border, 14);
        bubble.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var body = Ui.VBox(6, header, bubble);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.SizeFlagsStretchRatio = 8;
        var spacer = Ui.Spacer();
        spacer.SizeFlagsStretchRatio = 2;
        AddToThread(Ui.HBox(12, Ui.Avatar(reply.Name, 36, RoleColor(reply.Speaker)), body, spacer));
    }

    private void AppendOutcome(MeetingResponse r)
    {
        if (r.Intent?.Action is { } a && r.Outcome == null && r.ConfirmationPrompt == null)
            AppendNote($"Understood as \"{a.Name.Replace('_', ' ')}\" — not carried out.");
        if (r.Outcome?.Executed == true)
        {
            _transcript.AppendLine($"RECORDED: {r.Outcome.Summary}");
            var v = Ui.VBox(4, Ui.HBox(8, Ui.Icon("check-circle", 18, Palette.Success), Ui.Label("RECORDED", Fonts.Xs, Palette.Success, font: Fonts.Caption)),
                Ui.Label(r.Outcome.Summary, Fonts.Base, Palette.TextPrimary, wrap: true, font: Fonts.SansMedium));
            foreach (var e in r.Outcome.Effects) v.AddChild(Ui.Label("• " + e, Fonts.Sm, Palette.TextSecondary, wrap: true));
            if (r.Outcome.DecisionId is { } id) v.AddChild(Ui.Label($"Decision {id} · logged for your performance review", Fonts.Xs, Palette.TextMuted, font: Fonts.Mono));
            AddToThread(Ui.AccentCard(v, Palette.Success));
        }
        foreach (var n in r.SystemNotes.Where(n => r.Outcome == null || (n != r.Outcome.Summary && !r.Outcome.Effects.Contains(n))))
            AppendNote(n);
    }

    private void AppendNote(string text)
    {
        _transcript.AppendLine($"note: {text}");
        AddToThread(Ui.HBox(8, Ui.Gap(48), Ui.Icon("info", 16, Palette.TextMuted), Ui.Label(text, Fonts.Sm, Palette.TextMuted, wrap: true)));
    }

    private void ShowTyping(string name, ExecRole role, string fullName)
    {
        HideTyping();
        _typingName = name;
        _dotClock = 0;
        _typingLabel = Ui.Label($"{name} is typing", Fonts.Sm, Palette.TextMuted);
        var bubble = Ui.Card(_typingLabel, Palette.BgElevated, Palette.Border, 12);
        _typingBubble = Ui.HBox(12, Ui.Avatar(fullName, 36, RoleColor(role)), bubble);
        _thread.AddChild(_typingBubble);
        ScrollToEnd();
    }

    private void HideTyping()
    {
        if (_typingBubble != null && IsInstanceValid(_typingBubble)) { _thread.RemoveChild(_typingBubble); _typingBubble.QueueFree(); }
        _typingBubble = null;
        _typingLabel = null;
    }

    // ------------------------------------------------------------------ flow

    private async Task Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0 || _meeting == null) return;
        if (_game.Session.State.Outcome != GameOutcome.InProgress) { _game.Toast("Your tenure has ended.", Palette.Warning); return; }
        _input.Text = "";
        AppendPlayer(text);
        await Run(ct => _meeting.SayAsync(text, ExecRole.None, ct));
    }

    private async Task Confirm(bool yes)
    {
        if (_meeting == null) return;
        AppendPlayer(yes ? "Confirmed — go ahead." : "Let's not proceed.");
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
            AppendNote($"Something went wrong in the meeting: {e.Message}. Nothing was changed.");
            GD.PushError(e.ToString());
        }
        finally
        {
            HideTyping();
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
        _skip.Visible = busy && _game.Session.Settings.ReplyPacing != 0;
        _cancelGen.Visible = busy && _game.Session.Llm?.Status == Core.AI.Llm.LlmStatus.Ready;
        _thinking.Text = busy && _game.Session.Llm?.Status == Core.AI.Llm.LlmStatus.Ready ? "Executives are considering your words…" : "";
    }

    private async Task WaitAsync(double seconds)
    {
        double end = Time.GetTicksMsec() / 1000.0 + seconds;
        while (!_skipRequested && Time.GetTicksMsec() / 1000.0 < end && IsInsideTree())
            await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
    }

    /// <summary>Reveal replies one by one at each executive's pace, then the recorded outcome.</summary>
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
                await WaitAsync(t.ThinkSeconds);
                ShowTyping(reply.Name.StartsWith("Dr. ") ? "Dr. " + reply.Name.Split(' ')[^1] : reply.Name.Split(' ')[0], reply.Speaker, reply.Name);
                await WaitAsync(t.TypeSeconds);
                HideTyping();
            }
            AppendReply(reply);
        }
        AppendOutcome(r);
    }

    private void Render(MeetingResponse r)
    {
        foreach (var reply in r.Replies) AppendReply(reply);
        AppendOutcome(r);
    }

    // ------------------------------------------------------------------ test hooks

    public Task SendForTest(string text) { _input.Text = text; return Send(); }
    public bool TypingVisible => _typingBubble != null && IsInstanceValid(_typingBubble);
    public string LogText => _transcript.ToString();
    public Task<MeetingResponse> SayForTest(string text) { AppendPlayer(text); return _meeting!.SayAsync(text); }
    public Task<MeetingResponse> ConfirmForTest(bool yes) => _meeting!.ConfirmAsync(yes);
    public void RenderForTest(MeetingResponse r) => Render(r);
}
