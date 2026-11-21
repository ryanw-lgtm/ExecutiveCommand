using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExecutiveCommand.Core.AI.Llm;

public sealed class LlmSettings
{
    /// <summary>Path to the bundled llama-server executable (shipped with the game, not installed by the player).</summary>
    public string ServerPath { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public int ContextSize { get; set; } = 4096;
    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int GpuLayers { get; set; }
    public int StartupTimeoutSeconds { get; set; } = 120;
    public int InterpretTimeoutSeconds { get; set; } = 20;
    public int DialogueTimeoutSeconds { get; set; } = 30;
    public int MaxDialogueTokens { get; set; } = 180;
    public double DialogueTemperature { get; set; } = 0.7;
    public bool Enabled { get; set; } = true;
}

public enum LlmStatus { Disabled, NotConfigured, Starting, Loading, Ready, Failed, Stopped }

public sealed record ChatMessage(string Role, string Content);

/// <summary>Abstraction the game uses for language generation. Implementations must never throw for
/// ordinary failures; they return null so callers fall back to deterministic behavior.</summary>
public interface ILanguageModel
{
    LlmStatus Status { get; }
    Task<string?> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, JsonObject schema, CancellationToken ct);
    Task<string?> CompleteTextAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct);
}

public static class HardwareInfo
{
    public static int LogicalCores => Environment.ProcessorCount;
    public static long TotalMemoryBytes => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    /// <summary>Rough check: a 4-bit 7–8B model needs ~5–6 GB RAM plus context.</summary>
    public static string Recommendation()
    {
        double gb = TotalMemoryBytes / 1024.0 / 1024 / 1024;
        return gb >= 12 ? $"{gb:0} GB RAM, {LogicalCores} threads: a quantized 7–8B model should run on CPU (expect a few seconds per reply)."
             : gb >= 8 ? $"{gb:0} GB RAM: use a small (3–4B) quantized model or rely on scripted dialogue."
             : $"{gb:0} GB RAM: local AI is not recommended; scripted dialogue will be used.";
    }
}

/// <summary>
/// Manages a llama-server child process bound to 127.0.0.1 on a free port, and talks to its
/// OpenAI-compatible API. The process is started on demand and killed when the game exits.
/// Grammar-constrained JSON (response_format + schema) is used for action interpretation.
/// </summary>
public sealed class LlamaServerModel : ILanguageModel, IDisposable
{
    private readonly LlmSettings _settings;
    private readonly HttpClient _http;
    private Process? _process;
    private int _port;
    private readonly StringBuilder _log = new();
    private readonly object _lock = new();

    public LlmStatus Status { get; private set; } = LlmStatus.Stopped;
    public string? LastError { get; private set; }
    public event Action<LlmStatus>? StatusChanged;
    public string RecentLog { get { lock (_lock) return _log.ToString(); } }

    public LlamaServerModel(LlmSettings settings)
    {
        _settings = settings;
        _http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private void SetStatus(LlmStatus s) { Status = s; StatusChanged?.Invoke(s); }

    public async Task<bool> StartAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled) { SetStatus(LlmStatus.Disabled); return false; }
        if (!File.Exists(_settings.ServerPath) || !File.Exists(_settings.ModelPath))
        {
            LastError = !File.Exists(_settings.ServerPath) ? $"Inference runtime not found at '{_settings.ServerPath}'." : $"Model file not found at '{_settings.ModelPath}'.";
            SetStatus(LlmStatus.NotConfigured);
            return false;
        }
        _port = FreePort();
        var psi = new ProcessStartInfo(_settings.ServerPath)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_settings.ServerPath)!,
        };
        foreach (var a in new[]
                 {
                     "-m", _settings.ModelPath, "--host", "127.0.0.1", "--port", _port.ToString(), "-c", _settings.ContextSize.ToString(),
                     "-t", _settings.Threads.ToString(), "-ngl", _settings.GpuLayers.ToString(), "-np", "1", "--jinja", "--no-webui",
                 })
            psi.ArgumentList.Add(a);
        try
        {
            SetStatus(LlmStatus.Starting);
            _process = Process.Start(psi) ?? throw new InvalidOperationException("Process failed to start.");
            _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
            _process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            SetStatus(LlmStatus.Failed);
            return false;
        }

        var deadline = DateTime.UtcNow.AddSeconds(_settings.StartupTimeoutSeconds);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (_process.HasExited) { LastError = $"Inference process exited with code {_process.ExitCode}."; SetStatus(LlmStatus.Failed); return false; }
            try
            {
                using var resp = await _http.GetAsync($"http://127.0.0.1:{_port}/health", ct);
                if (resp.StatusCode == HttpStatusCode.OK) { SetStatus(LlmStatus.Ready); return true; }
                if (resp.StatusCode == HttpStatusCode.ServiceUnavailable && Status != LlmStatus.Loading) SetStatus(LlmStatus.Loading);
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            await Task.Delay(250, ct).ContinueWith(_ => { });
        }
        LastError = ct.IsCancellationRequested ? "Startup cancelled." : "Model did not finish loading in time.";
        Stop();
        SetStatus(LlmStatus.Failed);
        return false;
    }

    private void AppendLog(string? line)
    {
        if (line == null) return;
        lock (_lock)
        {
            _log.AppendLine(line);
            if (_log.Length > 20_000) _log.Remove(0, _log.Length - 20_000);
        }
    }

    public Task<string?> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, JsonObject schema, CancellationToken ct) =>
        ChatAsync(messages, new JsonObject { ["type"] = "json_schema", ["json_schema"] = new JsonObject { ["name"] = "interpretation", ["schema"] = schema.DeepClone() } },
            temperature: 0.1, maxTokens: 300, _settings.InterpretTimeoutSeconds, ct);

    public Task<string?> CompleteTextAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct) =>
        ChatAsync(messages, null, _settings.DialogueTemperature, _settings.MaxDialogueTokens, _settings.DialogueTimeoutSeconds, ct);

    private async Task<string?> ChatAsync(IReadOnlyList<ChatMessage> messages, JsonObject? responseFormat, double temperature, int maxTokens, int timeoutSeconds, CancellationToken ct)
    {
        if (Status != LlmStatus.Ready || _process == null || _process.HasExited) return null;
        var body = new JsonObject
        {
            ["model"] = "local",
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role, ["content"] = m.Content }).ToArray()),
            ["temperature"] = temperature,
            ["max_tokens"] = maxTokens,
            ["stream"] = false,
        };
        if (responseFormat != null) body["response_format"] = responseFormat;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds)); // cancelling closes the connection; llama-server aborts generation
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"http://127.0.0.1:{_port}/v1/chat/completions", content, cts.Token);
            if (!resp.IsSuccessStatusCode) { LastError = $"Inference HTTP {(int)resp.StatusCode}"; return null; }
            var json = await resp.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cts.Token);
            return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        }
        catch (OperationCanceledException) { LastError = ct.IsCancellationRequested ? "Cancelled." : "Timed out."; return null; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException) { LastError = ex.Message; return null; }
    }

    public void Stop()
    {
        try { if (_process is { HasExited: false }) { _process.Kill(entireProcessTree: true); _process.WaitForExit(5000); } }
        catch (Exception ex) { LastError = ex.Message; }
        _process?.Dispose();
        _process = null;
        if (Status != LlmStatus.Failed) SetStatus(LlmStatus.Stopped);
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
