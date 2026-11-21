using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

public enum ReplyPacing { Off = 0, Fast = 1, Natural = 2 }

/// <summary>
/// How long an executive "takes to reply" in the meeting room. Purely presentational: it never touches
/// game state. Personality shapes it: decisive people answer quickly, cautious and deliberate people
/// pause first, overloaded people are slower, and longer answers take longer to type.
/// </summary>
public static class TypingPace
{
    public readonly record struct Timing(double ThinkSeconds, double TypeSeconds)
    {
        public double Total => ThinkSeconds + TypeSeconds;
    }

    /// <summary>Characters per second at Natural pacing (a readable chat rhythm, not literal typing speed).</summary>
    public static double CharsPerSecond(Executive x) => 30 + x.Assertiveness * 0.4;

    public static Timing For(Executive x, string text, ReplyPacing pacing)
    {
        if (pacing == ReplyPacing.Off) return new(0, 0);
        double think = 0.4 + (100 - x.Assertiveness) * 0.01; // decisive people answer sooner
        if (x.RiskTolerance < 35) think += 0.3;                 // cautious people weigh their words
        if (x.Workload > 80) think += 0.4;                      // busy people are slower to respond
        double type = Math.Clamp(text.Length / CharsPerSecond(x), 0.8, 4.5);
        double scale = pacing == ReplyPacing.Fast ? 0.35 : 1.0;
        return new(Math.Round(Math.Min(think, 2.0) * scale, 2), Math.Round(type * scale, 2));
    }
}
