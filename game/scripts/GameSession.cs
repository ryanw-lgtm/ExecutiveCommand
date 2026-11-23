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
    public int ContextSize { get; set; } = 6144;
    public bool Fullscreen { get; set; }
    public float UiScale { get; set; } = 1.0f;
    public bool Autosave { get; set; } = true;
    public bool SeenWelcome { get; set; }
    public bool ReduceMotion { get; set; }
    /// <summary>0 = off, 1 = fast, 2 = natural (see Core.AI.ReplyPacing).</summary>
    public int ReplyPacing { get; set; } = 2;
    /// <summary>Attendees of the last meeting, restored next time.</summary>
    public List<string> LastAttendees { get; set; } = new() { "CIO", "CISO", "EADirector" };
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
        WarmUp();
    }

    public string? Load(string slot)
    {
        var r = Saves.Load(slot);
        Engine = new SimulationEngine(r.State);
        CurrentSlot = slot == "autosave" ? "career" : slot;
        StateChanged?.Invoke();
        WarmUp();
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

    /// <summary>Advance one day without saving or notifying (the UI animates multi-day advances).</summary>
    public List<InboxMessage> StepDay() => Engine!.AdvanceDays(1);

    /// <summary>Finish a multi-day advance: autosave once and refresh the UI. Returns true if it saved.</summary>
    public bool FinishAdvance()
    {
        bool saved = false;
        if (Settings.Autosave) { Saves.Save("autosave", State); saved = true; }
        StateChanged?.Invoke();
        return saved;
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

    /// <summary>Where the downloaded model lives: %APPDATA%\ExecutiveCommand\models on Windows.</summary>
    public static string ModelsDir => ProjectSettings.GlobalizePath("user://models");

    public static IEnumerable<string> DiscoverModels()
    {
        var dirs = new[]
        {
            ModelsDir,
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "models"),
            Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "models"),
        };
        return dirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.gguf")).Select(Path.GetFullPath)
                   .Where(m => !m.Contains("test-tiny-random")).Distinct();
    }

    public enum AiPhase { NeedsModel, Downloading, Starting, Ready, Failed, Disabled }
    public AiPhase Phase { get; private set; } = AiPhase.NeedsModel;
    public string AiMessage { get; private set; } = "";
    public DownloadProgress? Download { get; private set; }
    public event Action? AiChanged;
    private CancellationTokenSource? _downloadCts;

    private void SetPhase(AiPhase p, string message)
    {
        Phase = p; AiMessage = message;
        AiChanged?.Invoke();
    }

    public string? FindModel()
    {
        if (!string.IsNullOrEmpty(Settings.ModelPath) && File.Exists(Settings.ModelPath)) return Settings.ModelPath;
        var preferred = Path.Combine(ModelsDir, ModelSpec.Default.FileName);
        if (ModelDownloader.IsInstalled(ModelsDir, ModelSpec.Default)) return preferred;
        return DiscoverModels().FirstOrDefault(m => Path.GetFileName(m) == ModelSpec.Default.FileName && new FileInfo(m).Length == ModelSpec.Default.SizeBytes)
               ?? DiscoverModels().FirstOrDefault();
    }

    /// <summary>Downloads the pinned model (once), verifies it, then starts the AI.</summary>
    public async Task DownloadModelAsync()
    {
        if (Phase == AiPhase.Downloading) return;
        _downloadCts = new CancellationTokenSource();
        SetPhase(AiPhase.Downloading, "Downloading the AI model…");
        try
        {
            var progress = new Progress<DownloadProgress>(p => { Download = p; AiChanged?.Invoke(); });
            await new ModelDownloader().DownloadAsync(ModelSpec.Default, ModelsDir, progress, _downloadCts.Token);
            Download = null;
            await StartAiAsync();
        }
        catch (OperationCanceledException) { Download = null; SetPhase(AiPhase.NeedsModel, "Download paused. It will resume where it left off."); }
        catch (Exception e) { Download = null; SetPhase(AiPhase.NeedsModel, $"Download failed: {e.Message}"); }
    }

    public void CancelDownload() => _downloadCts?.Cancel();

    public async Task StartAiAsync()
    {
        StopAi();
        if (!Settings.AiEnabled) { SetPhase(AiPhase.Disabled, "AI is turned off in Settings."); AiStatusChanged?.Invoke(LlmStatus.Disabled); return; }
        var model = FindModel();
        if (model == null)
        {
            SetPhase(AiPhase.NeedsModel, $"The AI model ({ModelSpec.Default.SizeBytes / 1_000_000_000.0:0.0} GB) needs a one-time download.");
            AiStatusChanged?.Invoke(LlmStatus.NotConfigured);
            return;
        }
        SetPhase(AiPhase.Starting, "Starting the AI…");
        Llm = new LlamaServerModel(new LlmSettings
        {
            ServerPath = BundledServerPath(), ModelPath = model, Threads = Settings.Threads, GpuLayers = Settings.GpuLayers, ContextSize = Settings.ContextSize,
        });
        Llm.StatusChanged += s => Callable.From(() => AiStatusChanged?.Invoke(s)).CallDeferred();
        bool ok = await Llm.StartAsync();
        AiStatusChanged?.Invoke(Llm.Status);
        if (!ok) { SetPhase(AiPhase.Failed, $"The AI could not start: {Llm.LastError}"); return; }
        SetPhase(AiPhase.Ready, "AI ready");
        WarmUp();
    }

    /// <summary>Pre-load every executive's persona so even their first reply is quick.</summary>
    public void WarmUp()
    {
        if (Llm?.Status != LlmStatus.Ready || !HasGame) return;
        var prompts = State.Executives.Select(x => Core.AI.MeetingService.SystemPromptFor(State, x)).ToList();
        _ = Llm.WarmupAsync(prompts);
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
