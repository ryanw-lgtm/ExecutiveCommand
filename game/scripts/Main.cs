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
        _ = Session.StartAiAsync(); // finds the model; if missing, the AI panel offers the one-time download
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

    private readonly List<Control> _toasts = new();
    public IReadOnlyList<Control> ActiveToasts => _toasts;
    private readonly Dictionary<Control, Tween> _toastMoves = new();

    /// <summary>One movement tween per toast: a new layout cancels the old one so they never fight.</summary>
    private Tween MoveTween(Control t)
    {
        if (_toastMoves.TryGetValue(t, out var old) && old.IsValid()) old.Kill();
        var tw = t.CreateTween().SetParallel().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _toastMoves[t] = tw;
        return tw;
    }
    private const float ToastWidth = 440, ToastGap = 10, ToastMargin = 24;

    /// <summary>Toasts stack in the bottom-right, slide in, and reflow as older ones leave (max 4 visible).</summary>
    public void Toast(string text, Color? color = null)
    {
        var c = color ?? Palette.Accent;
        var label = Ui.Label(text, Fonts.Sm + 1, Palette.TextPrimary, wrap: true);
        label.CustomMinimumSize = new Vector2(ToastWidth - 64, 0); // wrap at the toast's real width so its height is right
        var card = Ui.AccentCard(label, c);
        card.AddThemeStyleboxOverride("panel", GameTheme.Box(Palette.BgElevated, Palette.BorderStrong, 8, 14));
        card.CustomMinimumSize = new Vector2(ToastWidth, 0);
        card.Size = new Vector2(ToastWidth, 0);
        card.MouseFilter = MouseFilterEnum.Stop;
        card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) DismissToast(card); }; // click to dismiss
        AddChild(card);
        _toasts.Add(card);
        while (_toasts.Count > 4) DismissToast(_toasts[0]);
        LayoutToasts(card);
        var life = CreateTween();
        life.TweenInterval(4.0);
        life.TweenCallback(Callable.From(() => DismissToast(card)));
    }

    private void LayoutToasts(Control? entering = null)
    {
        var vp = GetViewportRect().Size;
        float y = vp.Y - ToastMargin;
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            float h = t.GetCombinedMinimumSize().Y;
            y -= h;
            var target = new Vector2(vp.X - ToastWidth - ToastMargin, y);
            if (t == entering)
            {
                t.Position = target + new Vector2(Motion.Reduced ? 0 : 24, 0);
                t.Modulate = new Color(1, 1, 1, Motion.Reduced ? 1 : 0);
                if (!Motion.Reduced)
                {
                    var tw = MoveTween(t);
                    tw.TweenProperty(t, "position", target, 0.22);
                    tw.TweenProperty(t, "modulate:a", 1.0, 0.18);
                }
            }
            else if (Motion.Reduced) t.Position = target;
            else MoveTween(t).TweenProperty(t, "position", target, 0.18);
            y -= ToastGap;
        }
    }

    private void DismissToast(Control card)
    {
        if (!_toasts.Remove(card) || !IsInstanceValid(card)) return;
        if (_toastMoves.Remove(card, out var mv) && mv.IsValid()) mv.Kill();
        if (Motion.Reduced) card.QueueFree();
        else
        {
            var t = card.CreateTween().SetParallel().SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
            t.TweenProperty(card, "modulate:a", 0.0, Motion.Exit);
            t.TweenProperty(card, "position:x", card.Position.X + 16, Motion.Exit);
            t.Chain().TweenCallback(Callable.From(card.QueueFree));
        }
        LayoutToasts();
    }

    public void ToggleFullscreen()
    {
        Session.Settings.Fullscreen = !Session.Settings.Fullscreen;
        try { Session.SaveSettings(); } catch (Exception e) { GD.PushWarning(e.Message); }
        ApplyDisplaySettings();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 }) { ToggleFullscreen(); GetViewport().SetInputAsHandled(); }
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
