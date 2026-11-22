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
        Motion.Reduced = Session.Settings.ReduceMotion;
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
        // Toast: bottom-right, colored edge + text (never color alone), ~3s, exit faster than entrance.
        var c = color ?? Palette.Accent;
        var l = Ui.Label(text, Fonts.Sm + 1, Palette.TextPrimary, wrap: true);
        var card = Ui.AccentCard(l, c);
        card.AddThemeStyleboxOverride("panel", GameTheme.Box(Palette.BgElevated, Palette.BorderStrong, 8, 14));
        card.CustomMinimumSize = new Vector2(440, 0);
        card.Size = new Vector2(440, 0);
        AddChild(card);
        card.Position = new Vector2(GetViewportRect().Size.X - 440 - 24, GetViewportRect().Size.Y - 96);
        Motion.FadeIn(card, 0.2, 8);
        var tween = CreateTween();
        tween.TweenInterval(3.0);
        tween.TweenProperty(card, "modulate:a", 0.0, Motion.Exit);
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
