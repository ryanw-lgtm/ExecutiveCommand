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
        string text = Clean(raw);
        if (text.Length < 2) return new(false, "", "too short");
        if (text.Length > 900) text = TrimToSentence(text, 900);
        string lower = text.ToLowerInvariant();
        if (!LooksLikeEnglishSpeech(text)) return new(false, "", "implausible text (model may be broken or misconfigured)");
        if (BreaksCharacter.Any(lower.Contains)) return new(false, "", "breaks character");
        if (text.Contains('{') && text.Contains('}')) return new(false, "", "structured output in dialogue");
        if (ClaimsAuthorityActions.Any(lower.Contains) || Regex.IsMatch(lower, @"^(approved|denied|rejected|granted)\b"))
            return new(false, "", "speaks as if they made the decision; only the player decides here");
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

    private static readonly HashSet<string> FunctionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "to", "of", "we", "i", "is", "it", "that", "this", "you", "be", "for", "on", "in", "not", "but", "our",
        "with", "have", "are", "if", "can", "will", "would", "my", "me", "at", "as", "do", "so", "what", "there", "need", "want", "think",
    };

    /// <summary>Cheap guard against a broken, wrong or corrupted model emitting fluent-looking garbage.</summary>
    public static bool LooksLikeEnglishSpeech(string text)
    {
        int nonAscii = text.Count(ch => ch > 127 && ch != '—' && ch != '–' && ch != '’' && ch != '“' && ch != '”' && ch != '…');
        if (nonAscii > text.Length * 0.05) return false;
        var words = Regex.Matches(text, @"[A-Za-z']+").Select(m => m.Value).ToList();
        if (words.Count < 2) return false;
        int letters = text.Count(char.IsLetter);
        if (letters < text.Length * 0.6) return false;
        int fn = words.Count(w => FunctionWords.Contains(w));
        if (words.Count >= 6 && fn < Math.Max(2, words.Count / 10)) return false;
        double avgLen = words.Average(w => w.Length);
        return avgLen is >= 2.5 and <= 9;
    }

    /// <summary>Turns model output into plain spoken text: no markdown, no filler opener, at most three sentences.</summary>
    public static string Clean(string raw)
    {
        string t = raw.Trim();
        t = Regex.Replace(t, @"(?m)^\s*(#+|[-*•]|\d+\.)\s+", "");   // headings, bullets, numbered lists
        t = t.Replace("**", "").Replace("__", "");
        t = Regex.Replace(t, @"(?<!\w)\*(?!\s)([^*]+)(?<!\s)\*(?!\w)", "$1"); // *emphasis*
        t = Regex.Replace(t, @"\s+", " ").Trim();
        t = SpeakerPrefix().Replace(t, "").Trim().Trim('"', '“', '”').Trim();
        t = Regex.Replace(t, @"^(great|good|excellent|fair) (question|point)[^.!?]*[.!?—-]\s*", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"^(thank you|thanks) for (asking|raising|bringing)[^.!?]*[.!?—-]\s*", "", RegexOptions.IgnoreCase);
        var sentences = Regex.Split(t, @"(?<=[.!?])\s+(?=[A-Z""'“])").Where(x => x.Length > 0).ToList();
        if (sentences.Count > 3) t = string.Join(" ", sentences.Take(3));
        return t.Length > 0 ? char.ToUpperInvariant(t[0]) + t[1..] : t;
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
