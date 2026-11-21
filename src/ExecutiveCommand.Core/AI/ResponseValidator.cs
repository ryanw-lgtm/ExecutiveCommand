using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Executives;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

/// <summary>
/// Validates untrusted model output. Dialogue that breaks character, invents figures, contradicts the
/// computed stance, or claims actions the engine did not perform is rejected and replaced by the
/// deterministic composer. Interpreter output must have grounded parameters.
/// </summary>
public static partial class ResponseValidator
{
    private static readonly string[] BreaksCharacter =
    {
        "as an ai", "language model", "system prompt", "my instructions", "i am an assistant", "i'm an assistant", "openai", "anthropic",
        "llama", "i cannot comply", "as a chatbot", "<|", "[inst]", "```",
    };

    private static readonly string[] ClaimsAuthorityActions =
    {
        "i've approved", "i have approved", "i approve the", "i'm approving", "i've authorized", "i have authorized", "i've transferred",
        "i have transferred", "i've allocated", "i have allocated", "i've added", "funds have been", "budget has been increased",
        "i've updated the budget", "consider it approved", "you're promoted", "you are promoted", "you've been promoted", "here is $", "here's $",
    };

    private static readonly string[] StrongAgreement = { "great idea", "fully support", "completely agree", "absolutely, let's", "love this", "100%", "couldn't agree more", "wholeheartedly" };

    [GeneratedRegex(@"^\s*(\*\*)?[A-Z][\w.\s]{0,40}(\([^)]*\))?(\*\*)?\s*:\s*")]
    private static partial Regex SpeakerPrefix();

    public sealed record DialogueCheck(bool Ok, string Text, string? Reason);

    public static DialogueCheck CheckDialogue(string? raw, ReplyContext ctx, Stance? stance)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new(false, "", "empty");
        string text = SpeakerPrefix().Replace(raw.Trim(), "").Trim().Trim('"').Trim();
        text = Regex.Replace(text, @"\s+", " ");
        if (text.Length < 2) return new(false, "", "too short");
        if (text.Length > 900) text = TrimToSentence(text, 900);
        string lower = text.ToLowerInvariant();
        if (BreaksCharacter.Any(lower.Contains)) return new(false, "", "breaks character");
        if (text.Contains('{') && text.Contains('}')) return new(false, "", "structured output in dialogue");
        if (ClaimsAuthorityActions.Any(lower.Contains)) return new(false, "", "claims an action the engine did not perform");
        if (stance is { Position: Position.Oppose or Position.Concerned } && StrongAgreement.Any(lower.Contains))
            return new(false, "", "contradicts computed stance");

        // Every dollar figure must come from the facts the executive was given or from the player.
        var allowed = new List<decimal>();
        foreach (var f in ctx.Facts.Concat(stance?.Facts ?? new()).Concat(stance?.Reasons ?? new()).Concat(ctx.EngineResult).Append(ctx.PlayerText))
            allowed.AddRange(TextParsing.Money(f).Select(m => m.Amount));
        foreach (var m in TextParsing.Money(text).Where(m => m.HasDollarSign || m.Amount >= 10_000))
            if (!allowed.Any(a => a != 0 && Math.Abs(a - m.Amount) / a <= 0.02m))
                return new(false, "", $"unsupported figure {m.Amount:0}");
        return new(true, text, null);
    }

    private static string TrimToSentence(string text, int max)
    {
        var cut = text[..max];
        int dot = cut.LastIndexOfAny(new[] { '.', '!', '?' });
        return dot > 40 ? cut[..(dot + 1)] : cut;
    }

    /// <summary>Parses LLM interpreter JSON into an intent. Returns null when the output is unusable.</summary>
    public static InterpretedIntent? ParseInterpreterJson(string? json, GameState s, ActionRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JsonObject? o;
        try { o = JsonNode.Parse(json) as JsonObject; } catch { return null; }
        if (o == null) return null;
        var intent = new InterpretedIntent { Source = "llm" };
        intent.Kind = (o["intent"]?.GetValue<string>() ?? "statement") switch
        {
            "action" => IntentKind.Action, "question" => IntentKind.Question, "claim" => IntentKind.Claim, _ => IntentKind.Statement,
        };
        intent.AddressedRole = Roles.Parse(o["addressed_role"]?.GetValue<string>());
        if (o["clarification"] is JsonValue cv && cv.TryGetValue<string>(out var cq) && !string.IsNullOrWhiteSpace(cq)) intent.Clarification = cq.Length > 200 ? cq[..200] : cq;
        if (intent.Kind == IntentKind.Action && o["action"] is JsonObject a && a["name"]?.GetValue<string>() is string name)
        {
            if (registry.Get(name) == null) return null;
            var p = a["params"] as JsonObject ?? new JsonObject();
            var clean = new JsonObject();
            foreach (var kv in p)
                if (kv.Value != null && registry.Get(name)!.Spec.Params.Any(ps => ps.Name == kv.Key)) clean[kv.Key] = kv.Value.DeepClone();
            intent.Action = new ActionProposal(name, clean);
        }
        return intent;
    }

    /// <summary>
    /// Grounding: numbers in an interpreted action must appear in what the player actually said (or be
    /// the cost of a proposal they referenced). Models must not "fill in" a budget or deadline.
    /// </summary>
    public static string? GroundingProblem(ActionProposal a, string playerText, GameState s)
    {
        var money = TextParsing.Money(playerText).Select(m => m.Amount).ToList();
        var days = TextParsing.DurationsDays(playerText.ToLowerInvariant());
        foreach (var key in new[] { "authorized_budget", "annual_amount" })
        {
            var v = a.Money(key);
            if (v is > 0 && !money.Any(m => m == v)) return $"I want to be sure about the amount — how much exactly?";
        }
        foreach (var key in new[] { "deadline_days", "days" })
        {
            var v = a.Int(key);
            if (v != null && !days.Any(d => Math.Abs(d - v.Value) <= 1)) return "What deadline do you want on this?";
        }
        return null;
    }
}
