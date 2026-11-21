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

        var pick = Ui.HBox(10, Ui.Label("Attendees:", 14, Palette.Muted));
        foreach (var r in Roles.Addressable)
        {
            var cb = new CheckBox { Text = Roles.Label(r) == "Enterprise Architecture Director" ? "EA Director" : Roles.Label(r), ButtonPressed = r is ExecRole.CIO or ExecRole.CISO or ExecRole.EADirector };
            _attendees[r] = cb;
            pick.AddChild(cb);
        }
        pick.AddChild(Ui.Spacer());
        pick.AddChild(Ui.Button("Start new meeting", StartMeeting, primary: true));
        AddChild(Ui.Card(pick));

        _who = Ui.Label("", 13, Palette.Muted, wrap: true);
        AddChild(_who);

        _log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill, SelectionEnabled = true, CustomMinimumSize = new Vector2(0, 300) };
        AddChild(_log);

        _confirmText = Ui.Label("", 15, Palette.Warn, wrap: true);
        _confirmBar = Ui.HBox(10, _confirmText, Ui.Button("Cancel", () => _ = Confirm(false)), Ui.Button("Confirm decision", () => _ = Confirm(true), primary: true));
        _confirmCard = Ui.Card(_confirmBar, Palette.SurfaceRaised, Palette.Warn);
        _confirmCard.Visible = false;
        AddChild(_confirmCard);

        _thinking = Ui.Label("", 13, Palette.Muted);
        _input = new LineEdit { PlaceholderText = "Speak to the room — e.g. \"Daniel, investigate alternatives before we spend another $180,000. Three weeks.\"", SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 1000 };
        _input.TextSubmitted += _text => _ = Send();
        _send = Ui.Button("Say", () => _ = Send(), primary: true);
        _cancelGen = Ui.Button("Stop", () => _cts?.Cancel());
        _cancelGen.Visible = false;
        AddChild(Ui.HBox(8, _input, _send, _cancelGen));
        AddChild(_thinking);
    }

    public void Refresh(GameSession session)
    {
        // A loaded game has a different state object: meetings do not carry across.
        if (_boundState != session.State) { _meeting = null; _boundState = session.State; _log.Clear(); }
        if (_meeting == null) StartMeeting();
        _confirmCard.Visible = _meeting?.HasPendingConfirmation == true;
        _confirmText.Text = _meeting?.PendingConfirmationPrompt ?? "";
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
            Render(r);
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
        foreach (var reply in r.Replies)
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

    /// <summary>Used by the smoke test.</summary>
    public Task<MeetingResponse> SayForTest(string text) => _meeting!.SayAsync(text);
    public Task<MeetingResponse> ConfirmForTest(bool yes) => _meeting!.ConfirmAsync(yes);
    public void RenderForTest(MeetingResponse r) => Render(r);
}
