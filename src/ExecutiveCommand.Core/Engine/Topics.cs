using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// Topic tags connect conversations, assignments, proposals, decisions and memories so the evaluator
/// can check whether evidence existed and stakeholders were consulted before a decision.
/// </summary>
public static class Topics
{
    public const string DisasterRecovery = "dr";
    public const string Identity = "identity";
    public const string Backup = "backup";
    public const string EhrContract = "ehr_contract";
    public const string Budget = "budget";
    public const string LabInterface = "lab_interface";
    public const string Security = "security";
    public const string Clinical = "clinical";
    public const string Performance = "performance";
    public const string General = "general";

    private static readonly (string Topic, string[] Words)[] Map =
    {
        (LabInterface, new[] { "lab interface", "interface engine", "lab messages", "hl7", "laboratory", "lab results", "lab delay" }),
        (Backup, new[] { "backup", "back up", "restore", "immutable", "vault" }),
        (DisasterRecovery, new[] { "disaster recovery", " dr ", "dr ", " dr", "rto", "rpo", "failover", "secondary site", "data center", "datacenter", "colocation", "recovery", "cloud-first", "cloud first", "hybrid" }),
        (Identity, new[] { "identity", "active directory", "sso", "single sign", "authentication", "iam", "mfa" }),
        (EhrContract, new[] { "contract", "renewal", "vendor", "halcyon", "negotiat", "licens" }),
        (Budget, new[] { "budget", "savings", "cut", "reduce", "spending", "cost", "expense", "opex", "consulting", "travel", "training" }),
        (Security, new[] { "security", "ransomware", "cyber", "breach", "phishing", "vulnerab" }),
        (Clinical, new[] { "clinical", "patient", "nurs", "physician", "safety", "downtime" }),
        (Performance, new[] { "performance", "my record", "how am i doing", "greatest", "best cto", "promotion", "raise", "review" }),
    };

    public static string Detect(string? text, string fallback = General)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        string t = " " + text.ToLowerInvariant() + " ";
        foreach (var (topic, words) in Map)
            if (words.Any(w => t.Contains(w))) return topic;
        return fallback;
    }

    public static IReadOnlyList<string> DetectAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        string t = " " + text.ToLowerInvariant() + " ";
        return Map.Where(m => m.Words.Any(w => t.Contains(w))).Select(m => m.Topic).Distinct().ToList();
    }

    /// <summary>Who a well-run organization would expect to be consulted on a topic.</summary>
    public static IReadOnlyList<ExecRole> Stakeholders(string topic) => topic switch
    {
        DisasterRecovery => new[] { ExecRole.CFO, ExecRole.CIO, ExecRole.CMO, ExecRole.CISO },
        Backup => new[] { ExecRole.CISO, ExecRole.CIO },
        Identity => new[] { ExecRole.CISO, ExecRole.CIO },
        EhrContract => new[] { ExecRole.CFO, ExecRole.CIO },
        Budget => new[] { ExecRole.CFO },
        LabInterface => new[] { ExecRole.CMO, ExecRole.CNO, ExecRole.CIO },
        Security => new[] { ExecRole.CISO },
        Clinical => new[] { ExecRole.CMO, ExecRole.CNO },
        _ => Array.Empty<ExecRole>(),
    };
}
