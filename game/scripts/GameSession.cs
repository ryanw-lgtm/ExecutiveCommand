using System.Text.Json;
using ExecutiveCommand.Core.AI.Llm;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Core.Persistence;
using ExecutiveCommand.Core.Scenarios;
using Godot;

namespace ExecutiveCommand.Game;

/// <summary>Player-facing settings persisted to user://settings.json.</summary>
public sealed class PlayerSettings
{
    public bool AiEnabled { get; set; } = true;
    public string ModelPath { get; set; } = "";
    public int Threads { get; set; } = Math.Max(1, System.Environment.ProcessorCount / 2);
    public int GpuLayers { get; set; }
    public int ContextSize { get; set; } = 4096;
    public bool Fullscreen { get; set; }
    public float UiScale { get; set; } = 1.0f;
    public bool Autosave { get; set; } = true;
    public bool SeenWelcome { get; set; }
}

/// <summary>
/// Owns the running game: the authoritative engine, the save store and the optional local AI process.
/// UI code reads <see cref="State"/> and submits actions only through <see cref="Engine"/>.
/// </summary>
public sealed class GameSession : IDisposable
{
    public SimulationEngine? Engine { get; private set; }
    public GameState State => Engine?.State ?? throw new InvalidOperationException("No game in progress.");
    public bool HasGame => Engine != null;
    public SaveRepository Saves { get; }
    public PlayerSettings Settings { get; private set; } = new();
    public LlamaServerModel? Llm { get; private set; }
    public string CurrentSlot { get; private set; } = "career";
    public event Action? StateChanged;
    public event Action<LlmStatus>? AiStatusChanged;

    private static string SettingsPath => ProjectSettings.GlobalizePath("user://settings.json");

    public GameSession()
    {
        Saves = new SaveRepository(ProjectSettings.GlobalizePath("user://saves.db"));
        LoadSettings();
    }

    public void NewGame(string playerName, Difficulty difficulty, ulong? seed = null)
    {
        ulong s = seed ?? (ulong)DateTime.UtcNow.Ticks; // the only non-deterministic input: chosen once, then stored
        Engine = new SimulationEngine(HealthcareCtoScenario.Create(s, difficulty, string.IsNullOrWhiteSpace(playerName) ? "Alex Morgan" : playerName.Trim()));
        CurrentSlot = "career";
        if (Settings.Autosave) Saves.Save("autosave", State);
        StateChanged?.Invoke();
    }

    public string? Load(string slot)
    {
        var r = Saves.Load(slot);
        Engine = new SimulationEngine(r.State);
        CurrentSlot = slot == "autosave" ? "career" : slot;
        StateChanged?.Invoke();
        return r.Warning;
    }

    public void Save(string? slot = null)
    {
        if (!HasGame) return;
        CurrentSlot = slot ?? CurrentSlot;
        Saves.Save(CurrentSlot, State);
    }

    public List<InboxMessage> Advance(int days)
    {
        var msgs = Engine!.AdvanceDays(days);
        if (Settings.Autosave) Saves.Save("autosave", State);
        StateChanged?.Invoke();
        return msgs;
    }

    public void NotifyChanged() => StateChanged?.Invoke();

    // ------------------------------------------------------------------ AI runtime

    /// <summary>The runtime ships next to the executable (or in the repo's runtime/ folder during development).</summary>
    public static string BundledServerPath()
    {
        string exe = OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
        var candidates = new List<string>
        {
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "runtime", "llama", exe),
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "runtime", "llama", exe),
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? candidates[0];
    }

    public static IEnumerable<string> DiscoverModels()
    {
        var dirs = new[]
        {
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "models"),
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "models"),
            ProjectSettings.GlobalizePath("user://models"),
        };
        return dirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.gguf")).Select(Path.GetFullPath).Distinct();
    }

    public async Task StartAiAsync()
    {
        StopAi();
        if (!Settings.AiEnabled) { AiStatusChanged?.Invoke(LlmStatus.Disabled); return; }
        string model = Settings.ModelPath;
        if (string.IsNullOrEmpty(model) || !File.Exists(model)) model = DiscoverModels().FirstOrDefault(m => !m.Contains("test-tiny-random")) ?? "";
        Llm = new LlamaServerModel(new LlmSettings
        {
            ServerPath = BundledServerPath(), ModelPath = model, Threads = Settings.Threads, GpuLayers = Settings.GpuLayers, ContextSize = Settings.ContextSize,
        });
        Llm.StatusChanged += s => Callable.From(() => AiStatusChanged?.Invoke(s)).CallDeferred();
        await Llm.StartAsync();
        AiStatusChanged?.Invoke(Llm.Status);
    }

    public void StopAi()
    {
        Llm?.Dispose();
        Llm = null;
    }

    public void LoadSettings()
    {
        try { if (File.Exists(SettingsPath)) Settings = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception e) { GD.PushWarning($"Settings unreadable, using defaults: {e.Message}"); Settings = new(); }
    }

    public void SaveSettings() => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));

    public void Dispose() => StopAi();
}
