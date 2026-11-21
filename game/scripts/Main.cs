using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using ExecutiveCommand.Game.Views;
using Godot;

namespace ExecutiveCommand.Game;

/// <summary>Root node: owns the session and switches between top-level screens.</summary>
public partial class Main : Control
{
    public GameSession Session { get; private set; } = null!;
    private Control? _screen;

    public override void _Ready()
    {
        Theme = GameTheme.Build();
        var bg = new Panel();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        Session = new GameSession();
        ApplyDisplaySettings();
        GetTree().AutoAcceptQuit = false; // stop the inference process cleanly before exiting

        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--smoke-test")) { _ = SmokeTest.RunAsync(this); return; }
        ShowMainMenu();
        if (Session.Settings.AiEnabled) _ = Session.StartAiAsync();
    }

    public void ApplyDisplaySettings()
    {
        GetTree().Root.ContentScaleFactor = Math.Clamp(Session.Settings.UiScale, 0.75f, 2f);
        if (DisplayServer.GetName() != "headless")
            DisplayServer.WindowSetMode(Session.Settings.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
    }

    private void Show(Control c)
    {
        if (_screen != null) { RemoveChild(_screen); _screen.QueueFree(); }
        _screen = c;
        c.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(c);
    }

    public void ShowMainMenu() => Show(new MainMenu(this));
    public void ShowNewGame() => Show(new NewGameScreen(this));
    public GameScreen ShowGame() { var g = new GameScreen(this); Show(g); return g; }
    public void ShowSettings(bool fromGame) => Show(new SettingsScreen(this, fromGame));
    public void ShowSaveLoad(bool fromGame) => Show(new SaveLoadScreen(this, fromGame));

    public void Toast(string text, Color? color = null)
    {
        var l = Ui.Label(text, 15, color ?? Palette.Text);
        var card = Ui.Card(l, Palette.SurfaceRaised, color ?? Palette.Accent);
        card.SetAnchorsPreset(LayoutPreset.CenterBottom);
        card.Position = new Vector2(GetViewportRect().Size.X / 2 - 260, GetViewportRect().Size.Y - 90);
        card.CustomMinimumSize = new Vector2(520, 0);
        AddChild(card);
        var tween = CreateTween();
        tween.TweenInterval(2.8);
        tween.TweenProperty(card, "modulate:a", 0.0, 0.6);
        tween.TweenCallback(Callable.From(card.QueueFree));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Quit();
    }

    public void Quit(int code = 0)
    {
        try { if (Session.HasGame && Session.Settings.Autosave && Session.State.Outcome == GameOutcome.InProgress) Session.Saves.Save("autosave", Session.State); }
        catch (Exception e) { GD.PushError($"Autosave on exit failed: {e.Message}"); }
        Session.Dispose();
        GetTree().Quit(code);
    }
}
