using System.Globalization;
using System.Text.RegularExpressions;

namespace ExecutiveCommand.Core.AI;

/// <summary>Extraction of money amounts and durations from free text. Shared by the rule-based
/// interpreter and the grounding check applied to LLM output.</summary>
public static partial class TextParsing
{
    private static readonly Dictionary<string, int> Words = new()
    {
        ["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["fourteen"] = 14, ["thirty"] = 30, ["sixty"] = 60, ["ninety"] = 90,
        ["a couple of"] = 2, ["a couple"] = 2, ["couple of"] = 2, ["a few"] = 3, ["few"] = 3,
    };

    [GeneratedRegex(@"(?<![\w.])\$?\s?(\d{1,3}(?:,\d{3})+|\d+(?:\.\d+)?)\s*(k|thousand|m|mm|million|mil)?\b(?!\s*(?:day|week|month|year|hour|%))", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyRx();

    [GeneratedRegex(@"\b(\d+|a couple of|a couple|couple of|a few|few|an|a|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fourteen|thirty|sixty|ninety)\s*(business\s+)?(day|week|month)s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRx();

    public sealed record MoneyMention(decimal Amount, int Index, int Length, bool HasDollarSign);

    public static List<MoneyMention> Money(string text)
    {
        var list = new List<MoneyMention>();
        foreach (Match m in MoneyRx().Matches(text))
        {
            if (!decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) continue;
            string unit = m.Groups[2].Value.ToLowerInvariant();
            bool dollar = m.Value.TrimStart().StartsWith('$');
            v *= unit switch { "k" or "thousand" => 1_000m, "m" or "mm" or "million" or "mil" => 1_000_000m, _ => 1m };
            // Bare small numbers ("3 options") are not money unless marked with $ or a unit.
            if (!dollar && unit.Length == 0 && v < 1_000) continue;
            if (!dollar && unit.Length == 0 && v is >= 1900 and <= 2100 && !m.Groups[1].Value.Contains(',')) continue; // a year, not money
            list.Add(new MoneyMention(v, m.Index, m.Length, dollar));
        }
        return list;
    }

    /// <summary>All durations mentioned, converted to calendar days.</summary>
    public static List<int> DurationsDays(string text)
    {
        var list = new List<int>();
        foreach (Match m in DurationRx().Matches(text))
        {
            string n = m.Groups[1].Value.ToLowerInvariant();
            int count = int.TryParse(n, out var i) ? i : Words.GetValueOrDefault(n, 1);
            bool business = m.Groups[2].Success;
            int days = m.Groups[3].Value.ToLowerInvariant() switch
            {
                "week" => count * 7,
                "month" => count * 30,
                _ => business ? (int)Math.Ceiling(count * 7 / 5.0) : count,
            };
            list.Add(days);
        }
        return list;
    }

    public static bool ContainsAny(string lower, params string[] needles) => needles.Any(lower.Contains);
}
