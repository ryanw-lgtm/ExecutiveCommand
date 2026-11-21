using System.Text.Json;
using System.Text.Json.Nodes;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.AI.Llm;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Core.Persistence;
using ExecutiveCommand.Core.Scenarios;

namespace ExecutiveCommand.Core.Tests;

internal static class TestUtil
{
    public static SimulationEngine NewGame(ulong seed = 42, Difficulty d = Difficulty.Professional) =>
        new(HealthcareCtoScenario.Create(seed, d));

    /// <summary>Everything that matters for money, authority and outcomes — excludes dialogue logs and memories.</summary>
    public static string MaterialHash(GameState s)
    {
        var o = new
        {
            s.Finance, s.Departments, s.Proposals, s.Projects, s.Assignments, s.Savings, s.Player, s.Metrics, s.Technology,
            s.Contracts, s.Risks, s.Incidents, s.Decisions, Exec = s.Executives.Select(e => new { e.Role, e.Competence, e.Workload, e.Morale }),
        };
        return StateSerializer.Checksum(JsonSerializer.Serialize(o, StateSerializer.Options));
    }

    public static ActionProposal A(string name, object p) =>
        new(name, JsonNode.Parse(JsonSerializer.Serialize(p))!.AsObject());

    public static string TempDb() => Path.Combine(Path.GetTempPath(), "ec-tests", Guid.NewGuid().ToString("N") + ".db");
}

/// <summary>Scriptable stand-in for the local model, including hostile behavior.</summary>
internal sealed class FakeModel : ILanguageModel
{
    public LlmStatus Status { get; set; } = LlmStatus.Ready;
    public Func<string, string?> Json { get; set; } = _ => null;
    public Func<string, string?> Text { get; set; } = _ => null;
    public int JsonCalls, TextCalls;

    public Task<string?> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, JsonObject schema, CancellationToken ct)
    { JsonCalls++; return Task.FromResult(Json(messages[^1].Content)); }

    public Task<string?> CompleteTextAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    { TextCalls++; return Task.FromResult(Text(messages[^1].Content)); }
}
