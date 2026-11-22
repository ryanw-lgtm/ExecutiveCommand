using ExecutiveCommand.Core.AI.Llm;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>
/// Shows the state of the local AI and lets the player install it: one-time model download with
/// progress, then automatic start. Used on the main menu, in Settings and in the meeting room.
/// </summary>
public partial class AiPanel : PanelContainer
{
    private readonly GameSession _session = null!;
    private readonly bool _compact;
    private Label _title = null!, _detail = null!;
    private TextureRect _icon = null!;
    private ProgressBar _bar = null!;
    private Button _action = null!, _cancel = null!;

    public AiPanel() { }

    public AiPanel(GameSession session, bool compact = false)
    {
        _session = session;
        _compact = compact;
        AddThemeStyleboxOverride("panel", GameTheme.Box(Palette.BgSurface, Palette.Border, 8, compact ? 12 : 16));
        _icon = Ui.Icon("lightning", 22, Palette.Accent);
        _title = Ui.Label("", Fonts.Base, Palette.TextPrimary, font: Fonts.SansSemi);
        _detail = Ui.Label("", Fonts.Sm, Palette.TextSecondary, wrap: true);
        _bar = Ui.Bar(0, 1);
        _bar.Visible = false;
        _action = Ui.Button("", OnAction, primary: true, icon: "arrow-right");
        _cancel = Ui.Button("Pause", () => _session.CancelDownload());
        var text = Ui.VBox(4, _title, _detail, _bar);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var row = Ui.HBox(14, _icon, text, _cancel, _action);
        _action.SizeFlagsVertical = _cancel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        AddChild(row);
        _session.AiChanged += OnChanged;
        TreeExiting += () => _session.AiChanged -= OnChanged;
        Update();
    }

    private void OnChanged() => Callable.From(Update).CallDeferred();

    private void OnAction()
    {
        switch (_session.Phase)
        {
            case GameSession.AiPhase.NeedsModel: _ = _session.DownloadModelAsync(); break;
            case GameSession.AiPhase.Failed: _ = _session.StartAiAsync(); break;
        }
    }

    private void Update()
    {
        if (!IsInstanceValid(this)) return;
        var spec = ModelSpec.Default;
        _bar.Visible = false;
        _cancel.Visible = false;
        _action.Visible = true;
        switch (_session.Phase)
        {
            case GameSession.AiPhase.NeedsModel:
                _title.Text = "Install the AI (one time)";
                _detail.Text = $"Executives are written by a local AI that runs on your PC — private, offline, no account. " +
                               $"It needs a one-time {spec.SizeBytes / 1e9:0.0} GB download ({spec.DisplayName}, {spec.License} license)." +
                               (_session.AiMessage.StartsWith("Download") ? "  " + _session.AiMessage : "");
                _action.Text = "Download AI model";
                break;
            case GameSession.AiPhase.Downloading:
                var d = _session.Download;
                _title.Text = d?.Stage == "Verifying" ? "Verifying download…" : "Downloading the AI model…";
                if (d != null)
                {
                    double remaining = d.BytesPerSecond > 0 ? (d.Total - d.Received) / d.BytesPerSecond : 0;
                    _detail.Text = $"{d.Received / 1e9:0.00} of {d.Total / 1e9:0.00} GB · {d.BytesPerSecond / 1e6:0.0} MB/s" +
                                   (remaining > 0 ? $" · about {TimeSpan.FromSeconds(remaining):h\\:mm\\:ss} left" : "") + " · you can keep playing";
                    _bar.MaxValue = 1; _bar.Value = d.Fraction; _bar.Visible = true;
                }
                _cancel.Visible = true;
                _action.Visible = false;
                break;
            case GameSession.AiPhase.Starting:
                _title.Text = "Starting the AI…";
                _detail.Text = "Loading the model into memory. This takes a few seconds.";
                _action.Visible = false;
                break;
            case GameSession.AiPhase.Ready:
                _title.Text = "AI ready";
                _detail.Text = _compact ? "Every executive reply is written live by the local AI." : $"{spec.DisplayName} running locally. Every executive reply is written live; the simulation still decides what is true and what happens.";
                _icon.Modulate = Palette.Success;
                _action.Visible = false;
                break;
            case GameSession.AiPhase.Failed:
                _title.Text = "The AI could not start";
                _detail.Text = _session.AiMessage + " Until it runs, executives use short scripted lines (labeled).";
                _action.Text = "Try again";
                _icon.Modulate = Palette.Danger;
                break;
            default:
                _title.Text = "AI turned off";
                _detail.Text = "Executives use short scripted lines. Turn the AI on in Settings.";
                _action.Visible = false;
                break;
        }
        if (_session.Phase != GameSession.AiPhase.Ready && _session.Phase != GameSession.AiPhase.Failed) _icon.Modulate = Palette.Accent;
    }
}
