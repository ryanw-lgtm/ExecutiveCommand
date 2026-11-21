using ExecutiveCommand.Core.AI.Llm;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Core.Persistence;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

public partial class MainMenu : Control
{
    public MainMenu() { }

    public MainMenu(Main main)
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var col = Ui.VBox(14);
        col.CustomMinimumSize = new Vector2(440, 0);
        center.AddChild(col);

        col.AddChild(Ui.Label("EXECUTIVE", 48, Palette.Text));
        col.AddChild(Ui.Label("COMMAND", 48, Palette.Accent));
        col.AddChild(Ui.Label("Leadership is measured in consequences.", 16, Palette.Muted));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        bool hasAutosave = main.Session.Saves.Exists("autosave");
        var cont = Ui.Button("Continue", () =>
        {
            var warn = main.Session.Load("autosave");
            main.ShowGame();
            if (warn != null) main.Toast(warn, Palette.Warn);
        }, primary: true);
        cont.Disabled = !hasAutosave;
        col.AddChild(cont);
        col.AddChild(Ui.Button("New Career — Healthcare CTO", main.ShowNewGame, primary: !hasAutosave));
        col.AddChild(Ui.Button("Load Game", () => main.ShowSaveLoad(false)));
        col.AddChild(Ui.Button("Settings", () => main.ShowSettings(false)));
        col.AddChild(Ui.Button("Quit", () => main.Quit()));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });
        col.AddChild(Ui.Label($"v{GameInfo.Version} · prototype · all organizations and people are fictional", 12, Palette.Muted));
        foreach (var b in col.GetChildren().OfType<Button>()) b.CustomMinimumSize = new Vector2(0, 44);
    }
}

public partial class NewGameScreen : Control
{
    public NewGameScreen() { }

    public NewGameScreen(Main main)
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var col = Ui.VBox(12);
        col.CustomMinimumSize = new Vector2(760, 0);
        center.AddChild(col);

        col.AddChild(Ui.Heading("New Career", 30));
        var scenario = Ui.VBox(6,
            Ui.Caption("Scenario"),
            Ui.Label("Meridian Crest Health Partners — Chief Technology Officer", 20),
            Ui.Label("A fictional regional health system: four hospitals, a rehabilitation center and outpatient clinics. " +
                     "The EHR platform is aging, disaster recovery has never met its target, the CFO wants savings and the EHR contract renews in April. " +
                     "Some facts are hidden until someone investigates — and not every report will be accurate.", 14, Palette.Muted, wrap: true));
        col.AddChild(Ui.Card(scenario));

        var name = new LineEdit { PlaceholderText = "Your name", Text = "Alex Morgan", CustomMinimumSize = new Vector2(320, 0) };
        var diff = new OptionButton();
        diff.AddItem("Casual — more guidance, forgiving finances, fewer incidents", (int)Difficulty.Casual);
        diff.AddItem("Professional — realistic constraints and independent executives", (int)Difficulty.Professional);
        diff.AddItem("Executive — limited information, higher board expectations", (int)Difficulty.Executive);
        diff.Selected = 1;
        var seed = new LineEdit { PlaceholderText = "Random", CustomMinimumSize = new Vector2(200, 0) };
        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(Ui.Label("Name", 15, Palette.Muted)); grid.AddChild(name);
        grid.AddChild(Ui.Label("Difficulty", 15, Palette.Muted)); grid.AddChild(diff);
        grid.AddChild(Ui.Label("World seed (optional)", 15, Palette.Muted)); grid.AddChild(seed);
        col.AddChild(Ui.Card(grid));

        col.AddChild(Ui.HBox(10,
            Ui.Button("Back", main.ShowMainMenu),
            Ui.Spacer(),
            Ui.Button("Begin", () =>
            {
                ulong? s = ulong.TryParse(seed.Text.Trim(), out var v) ? v : null;
                main.Session.NewGame(name.Text, (Difficulty)diff.GetSelectedId(), s);
                main.ShowGame();
            }, primary: true)));
    }
}

public partial class SettingsScreen : Control
{
    private Label _status = null!;
    private readonly Main _main = null!;

    public SettingsScreen() { }

    public SettingsScreen(Main main, bool fromGame)
    {
        _main = main;
        var st = main.Session.Settings;
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 48);
        AddChild(margin);
        var col = Ui.VBox(12);
        margin.AddChild(Ui.Scroll(col));
        col.AddChild(Ui.Heading("Settings", 30));

        // --- Local AI
        var ai = Ui.VBox(8, Ui.Caption("Local AI (optional — the game is fully playable without it)"));
        var enabled = new CheckBox { Text = "Use local AI for executive dialogue and natural-language interpretation", ButtonPressed = st.AiEnabled };
        ai.AddChild(enabled);
        var model = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var models = GameSession.DiscoverModels().ToList();
        if (!string.IsNullOrEmpty(st.ModelPath) && !models.Contains(st.ModelPath)) models.Insert(0, st.ModelPath);
        model.AddItem("(automatic)");
        foreach (var m in models) model.AddItem(Path.GetFileName(m));
        model.Selected = Math.Max(0, models.IndexOf(st.ModelPath) + 1);
        var browse = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.gguf ; GGUF models" }, Size = new Vector2I(900, 600) };
        AddChild(browse);
        browse.FileSelected += path => { models.Add(path); model.AddItem(Path.GetFileName(path)); model.Selected = model.ItemCount - 1; };
        ai.AddChild(Ui.HBox(8, Ui.Label("Model", 15, Palette.Muted), model, Ui.Button("Browse…", () => browse.PopupCentered())));
        var threads = new SpinBox { MinValue = 1, MaxValue = System.Environment.ProcessorCount, Value = st.Threads };
        var gpu = new SpinBox { MinValue = 0, MaxValue = 200, Value = st.GpuLayers };
        var ctx = new SpinBox { MinValue = 2048, MaxValue = 16384, Step = 1024, Value = st.ContextSize };
        ai.AddChild(Ui.HBox(8, Ui.Label("CPU threads", 15, Palette.Muted), threads, Ui.Label("GPU layers (0 = CPU only)", 15, Palette.Muted), gpu, Ui.Label("Context", 15, Palette.Muted), ctx));
        ai.AddChild(Ui.Label(HardwareInfo.Recommendation(), 13, Palette.Muted, wrap: true));
        _status = Ui.Label(StatusText(main.Session.Llm?.Status ?? LlmStatus.Stopped), 14, Palette.Text, wrap: true);
        ai.AddChild(_status);
        main.Session.AiStatusChanged += OnAiStatus;
        TreeExiting += () => main.Session.AiStatusChanged -= OnAiStatus;
        col.AddChild(Ui.Card(ai));

        // --- Display & accessibility
        var disp = Ui.VBox(8, Ui.Caption("Display & accessibility"));
        var full = new CheckBox { Text = "Fullscreen", ButtonPressed = st.Fullscreen };
        var scale = new OptionButton();
        foreach (var f in new[] { 0.9f, 1.0f, 1.1f, 1.25f, 1.5f }) scale.AddItem($"{f:P0} interface scale");
        scale.Selected = Array.IndexOf(new[] { 0.9f, 1.0f, 1.1f, 1.25f, 1.5f }, st.UiScale) is var i && i >= 0 ? i : 1;
        var autosave = new CheckBox { Text = "Autosave after each time advance", ButtonPressed = st.Autosave };
        disp.AddChild(full); disp.AddChild(scale); disp.AddChild(autosave);
        col.AddChild(Ui.Card(disp));

        col.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Privacy"),
            Ui.Label("Executive Command runs entirely on this computer. No account is required, nothing you type is sent anywhere, and no telemetry is collected.", 14, Palette.Muted, wrap: true))));

        col.AddChild(Ui.HBox(10,
            Ui.Button("Back", () => { if (fromGame && main.Session.HasGame) main.ShowGame(); else main.ShowMainMenu(); }),
            Ui.Spacer(),
            Ui.Button("Apply & restart AI", async () =>
            {
                st.AiEnabled = enabled.ButtonPressed;
                st.ModelPath = model.Selected <= 0 ? "" : models[model.Selected - 1];
                st.Threads = (int)threads.Value; st.GpuLayers = (int)gpu.Value; st.ContextSize = (int)ctx.Value;
                st.Fullscreen = full.ButtonPressed; st.UiScale = new[] { 0.9f, 1.0f, 1.1f, 1.25f, 1.5f }[scale.Selected]; st.Autosave = autosave.ButtonPressed;
                main.Session.SaveSettings();
                main.ApplyDisplaySettings();
                _status.Text = "Starting local AI…";
                await main.Session.StartAiAsync();
            }, primary: true)));
    }

    private void OnAiStatus(LlmStatus s) { if (IsInstanceValid(_status)) _status.Text = StatusText(s); }

    private string StatusText(LlmStatus s)
    {
        var err = _main.Session.Llm?.LastError;
        return s switch
        {
            LlmStatus.Ready => "● Local AI ready.",
            LlmStatus.Loading or LlmStatus.Starting => "◌ Loading model…",
            LlmStatus.NotConfigured => $"○ Local AI not set up: {err} Executives will use scripted dialogue.",
            LlmStatus.Failed => $"✕ Local AI failed to start: {err} Executives will use scripted dialogue.",
            LlmStatus.Disabled => "○ Local AI disabled. Executives use scripted dialogue.",
            _ => "○ Local AI stopped.",
        };
    }
}

public partial class SaveLoadScreen : Control
{
    public SaveLoadScreen() { }

    public SaveLoadScreen(Main main, bool fromGame)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 48);
        AddChild(margin);
        var col = Ui.VBox(12);
        margin.AddChild(col);
        col.AddChild(Ui.Heading(fromGame ? "Save / Load" : "Load Game", 30));

        var list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 360) };
        var slots = main.Session.Saves.ListSlots().ToList();
        void Fill()
        {
            list.Clear();
            slots = main.Session.Saves.ListSlots().ToList();
            foreach (var s in slots) list.AddItem($"{s.Slot,-14}  {s.Summary}   ·   saved {s.SavedUtc.ToLocalTime():g}");
        }
        Fill();
        col.AddChild(list);

        var name = new LineEdit { PlaceholderText = "slot name (letters, digits, - and _)", Text = main.Session.HasGame ? main.Session.CurrentSlot : "", CustomMinimumSize = new Vector2(320, 0) };
        var row = Ui.HBox(10, Ui.Button("Back", () => { if (fromGame && main.Session.HasGame) main.ShowGame(); else main.ShowMainMenu(); }), Ui.Spacer());
        if (fromGame && main.Session.HasGame)
        {
            row.AddChild(name);
            row.AddChild(Ui.Button("Save", () =>
            {
                try { main.Session.Save(name.Text.Trim()); Fill(); main.Toast($"Saved to '{name.Text.Trim()}'.", Palette.Good); }
                catch (ArgumentException e) { main.Toast(e.Message, Palette.Bad); }
            }, primary: true));
        }
        row.AddChild(Ui.Button("Delete", () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0) return;
            main.Session.Saves.Delete(slots[sel[0]].Slot);
            Fill();
        }));
        row.AddChild(Ui.Button("Load", () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length == 0) { main.Toast("Select a save first.", Palette.Warn); return; }
            try
            {
                var warn = main.Session.Load(slots[sel[0]].Slot);
                main.ShowGame();
                if (warn != null) main.Toast(warn, Palette.Warn);
            }
            catch (SaveException e) { main.Toast(e.Message, Palette.Bad); }
        }, primary: !fromGame));
        col.AddChild(row);
    }
}
