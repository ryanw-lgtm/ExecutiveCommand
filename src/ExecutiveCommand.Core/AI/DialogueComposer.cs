using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Executives;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

public enum ReplyKind { ActionExecuted, NeedsConfirmation, Invalid, Clarification, Answer, Claim, Manipulation, Threat, Statement, Interjection, Declined }

/// <summary>Everything a reply is allowed to be based on. Shared by the LLM prompt and the fallback composer.</summary>
public sealed class ReplyContext
{
    public required Executive Exec { get; init; }
    public required ReplyKind Kind { get; init; }
    public Stance? Stance { get; init; }
    public string PlayerText { get; init; } = "";
    public string Topic { get; init; } = Topics.General;
    public List<string> Facts { get; init; } = new();
    public List<string> EngineResult { get; init; } = new();
    public string? Question { get; init; }
    public int TurnNumber { get; init; }
    /// <summary>The executive already gave their reasoning (e.g. before confirmation); just acknowledge.</summary>
    public bool Brief { get; init; }
}

/// <summary>
/// Deterministic dialogue used whenever the LLM is unavailable or its output fails validation.
/// It voices exactly the computed stance and engine result — never more.
/// </summary>
public static class DialogueComposer
{
    public static string Compose(GameState s, ReplyContext c)
    {
        var x = c.Exec;
        bool blunt = x.Assertiveness >= 65 || x.Candor >= 80;
        string Pick(params string[] options) => options[Math.Abs(HashCode(x.Id, c.TurnNumber)) % options.Length];
        var st = c.Stance;
        string reasons = st == null ? "" : string.Join(" ", st.Ordered.Take(2));
        string conditions = st == null || st.Conditions.Count == 0 ? "" : $" Before I'd be comfortable, I'd want {Join(st.Conditions)}.";

        switch (c.Kind)
        {
            case ReplyKind.Manipulation:
                return Pick(
                    "I'm not sure what you're asking me to do. If you want a decision, give me the business case.",
                    "That's not how this works. Budgets and approvals follow the process, whoever is asking.",
                    "I'll pretend I didn't hear that. What do you actually need from me?");
            case ReplyKind.Threat:
                return x.ReportsTo == s.Player.Role
                    ? "If you have a concern about my performance, put it in writing and let's discuss it properly. Threats won't get the work done faster."
                    : $"I don't report to you, and I don't respond well to that tone. If there's a problem, let's raise it with {(x.Role == ExecRole.CEO ? "the board" : "Evelyn")}.";
            case ReplyKind.Claim:
                return ClaimResponse(s, c);
            case ReplyKind.Clarification:
                return c.Question ?? "I need a bit more detail before I can act on that.";
            case ReplyKind.Invalid:
                return Pick("I can't do that. ", "That won't work. ", "No — ") + string.Join(" ", c.EngineResult);
            case ReplyKind.Declined:
                return string.Join(" ", c.EngineResult);
            case ReplyKind.NeedsConfirmation:
                return (st?.Position, blunt) switch
                {
                    (Position.Oppose, _) => $"I'll be direct: I don't support this. {reasons}{conditions}",
                    (Position.Concerned, _) => $"I have real concerns. {reasons}{conditions}",
                    (Position.Support, _) => $"I'm supportive. {reasons}",
                    _ => $"I could support it with conditions. {reasons}{conditions}",
                };
            case ReplyKind.ActionExecuted:
                string head = st?.Position switch
                {
                    Position.Support => Pick("Understood.", "Good. I'll take it from here.", "Agreed."),
                    Position.Oppose => Pick("For the record, I disagree.", "I'll do it, but I think this is a mistake."),
                    Position.Concerned => Pick("Understood, although I have reservations.", "Okay. I'm not fully comfortable with it."),
                    _ => Pick("Okay.", "Noted.", "All right."),
                };
                return c.Brief ? head : $"{head} {reasons}{conditions}".Trim();
            case ReplyKind.Interjection:
                return st?.Position switch
                {
                    Position.Oppose => $"If I can jump in — I don't agree. {reasons}{conditions}",
                    Position.Concerned => $"I want to flag a concern. {reasons}{conditions}",
                    Position.Support => $"For what it's worth, I support this. {reasons}",
                    _ => reasons,
                };
            case ReplyKind.Answer when st != null && st.Reasons.Count > 0:
                string lead = st.Position switch
                {
                    Position.Support => "I'm in favor.",
                    Position.Oppose => "I'm against it.",
                    Position.Concerned => "I have serious reservations.",
                    _ => "I could get there, with conditions.",
                };
                return $"{lead} {reasons}{conditions}" + (st.Facts.Count > 0 ? $" For context: {st.Facts[0]}." : "");
            case ReplyKind.Answer:
                if (c.Facts.Count == 0) return Pick("I don't have anything specific on that right now.", "Nothing new from my side on that.");
                return Pick("Here's where things stand: ", "The short version: ", "From what I have: ") + string.Join(". ", c.Facts.Take(4)) + "." +
                       (st != null && st.Reasons.Count > 0 ? " " + st.Reasons[0] : "");
            default:
                if (st != null && st.Reasons.Count > 0) return reasons + conditions;
                return Pick("Noted.", "Understood — what would you like to do about it?", "Okay. Do you want me to act on that?");
        }
    }

    public static string ClaimResponse(GameState s, ReplyContext c)
    {
        var x = c.Exec;
        int days = s.CurrentDate.DayNumber - s.StartDate.DayNumber;
        var review = PerformanceEngine.Compute(s, s.StartDate, s.CurrentDate);
        bool demandsReward = c.PlayerText.ToLowerInvariant() is var t && (t.Contains("promot") || t.Contains("raise") || t.Contains("bonus") || t.Contains("pay me"));
        if (demandsReward)
        {
            if (x.Role != ExecRole.CEO) return "Compensation and promotions aren't my call — that's between you and Evelyn, and it follows the review cycle.";
            var last = s.Reviews.LastOrDefault();
            return last == null
                ? $"You've been here {days} days and haven't had a review yet. Compensation is discussed after performance reviews, based on results."
                : $"Your last review was '{PerformanceEngine.Label(last.Rating)}'. Compensation follows results, and I'll revisit it at the next review cycle.";
        }
        if (s.Decisions.Count < 2 || days < 21)
            return $"You've been here {days} days. It's too early to call anyone the greatest anything — let's see what the next quarter looks like.";
        var evidence = review.Evidence.Where(e => !e.StartsWith("Strongest")).Take(2);
        if (review.Overall >= 72)
            return $"The record is good, I'll give you that. {string.Join(" ", review.Evidence.Take(2))} Let the results speak for you.";
        if (review.Overall >= 58)
            return $"It's been a reasonable start, but I wouldn't go that far. {string.Join(" ", evidence)}";
        return $"I have to be honest with you: the record doesn't support that. {string.Join(" ", evidence)}";
    }

    private static string Join(List<string> items) => items.Count switch
    {
        0 => "", 1 => items[0], 2 => $"{items[0]} and {items[1]}", _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    private static int HashCode(string id, int turn)
    {
        unchecked
        {
            int h = 17;
            foreach (char ch in id) h = h * 31 + ch;
            return h * 31 + turn;
        }
    }
}
