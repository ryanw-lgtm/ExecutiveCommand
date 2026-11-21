using System.Globalization;
using System.Text.Json.Nodes;

namespace ExecutiveCommand.Core.Actions;

/// <summary>
/// A structured request to change game state. Produced by the natural-language interpreter (LLM or
/// rule-based) or by UI buttons. It is untrusted until <see cref="ActionValidator"/> accepts it.
/// </summary>
public sealed class ActionProposal
{
    public string Name { get; set; } = "";
    public JsonObject Params { get; set; } = new();

    public ActionProposal() { }
    public ActionProposal(string name, JsonObject? p = null) { Name = name; Params = p ?? new JsonObject(); }

    public string? Str(string key) => Params.TryGetPropertyValue(key, out var n) && n is JsonValue v
        ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString().Trim('"'))
        : null;

    public long? Int(string key)
    {
        if (!Params.TryGetPropertyValue(key, out var n) || n is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d) && Math.Abs(d - Math.Round(d)) < 1e-9) return (long)Math.Round(d);
        if (v.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    public decimal? Money(string key)
    {
        if (!Params.TryGetPropertyValue(key, out var n) || n is not JsonValue v) return null;
        if (v.TryGetValue<decimal>(out var m)) return m;
        if (v.TryGetValue<double>(out var d)) return (decimal)d;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<string>(out var s) && decimal.TryParse(s.Replace("$", "").Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    public bool? Bool(string key)
    {
        if (!Params.TryGetPropertyValue(key, out var n) || n is not JsonValue v) return null;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var p)) return p;
        return null;
    }

    public override string ToString() => $"{Name} {Params.ToJsonString()}";
}

public enum ParamType { String, Integer, Money, Boolean, Enum }

public sealed record ParamSpec(
    string Name,
    ParamType Type,
    bool Required,
    string Description,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<string>? EnumValues = null,
    int MaxLength = 200);

public sealed record ActionSpec(string Name, string Description, IReadOnlyList<ParamSpec> Params);

public enum ValidationStatus { Valid, Invalid, NeedsClarification, NeedsConfirmation }

public sealed class ValidationResult
{
    public ValidationStatus Status { get; init; }
    public List<string> Errors { get; init; } = new();
    public string? Question { get; init; }
    public string? ConfirmationPrompt { get; init; }
    /// <summary>A suggested alternative (e.g. "request_assessment" instead of "delegate_assignment").</summary>
    public string? Suggestion { get; init; }

    public bool IsValid => Status == ValidationStatus.Valid;

    public static ValidationResult Ok() => new() { Status = ValidationStatus.Valid };
    public static ValidationResult Fail(string error, string? suggestion = null) =>
        new() { Status = ValidationStatus.Invalid, Errors = { error }, Suggestion = suggestion };
    public static ValidationResult Clarify(string question) =>
        new() { Status = ValidationStatus.NeedsClarification, Question = question };
    public static ValidationResult Confirm(string prompt) =>
        new() { Status = ValidationStatus.NeedsConfirmation, ConfirmationPrompt = prompt };
}

public sealed class ActionOutcome
{
    public bool Executed { get; init; }
    public ValidationResult Validation { get; init; } = ValidationResult.Ok();
    public string? DecisionId { get; init; }
    public string Summary { get; init; } = "";
    public List<string> Effects { get; init; } = new();
}
