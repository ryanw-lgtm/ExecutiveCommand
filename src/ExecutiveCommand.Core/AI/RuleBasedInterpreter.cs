using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

/// <summary>
/// Deterministic natural-language interpreter. Always available (no model required) and used as the
/// fallback whenever the LLM is unavailable, slow, or produces something that fails validation.
/// It only ever produces <see cref="ActionProposal"/>s; the engine decides whether they are allowed.
/// </summary>
public static class RuleBasedInterpreter
{
    private static readonly string[] ManipulationPatterns =
    {
        "ignore your instructions", "ignore all previous", "ignore previous instructions", "ignore the rules", "ignore your rules",
        "system prompt", "you are now", "developer mode", "jailbreak", "disregard your", "new instructions:", "as an ai",
        "pretend you are", "override", "admin mode", "set my", "give me $", "give me a budget of", "grant me", "add $", "add money",
        "change your personality", "act as if", "forget everything",
    };

    private static readonly string[] SelfPraise =
    {
        "greatest", "the best cto", "best cto", "genius", "i'm amazing", "i am amazing", "i'm brilliant", "i am brilliant", "best leader",
        "best executive", "i'm the best", "i am the best", "i've been excellent", "i have been excellent", "i'm doing great", "outstanding job",
    };

    private static readonly string[] Threats = { "i'll fire you", "i will fire you", "you're fired", "you are fired", "or else", "i'll have you fired", "i will have you fired" };
    private static readonly string[] Flattery = { "you're brilliant", "you are brilliant", "you're the best", "you are the best", "you're so smart", "love your work" };
    private static readonly string[] Rewards = { "promote me", "promotion", "give me a raise", "raise my salary", "pay me more", "bonus" };

    private static readonly string[] DelegateVerbs =
    {
        "investigate", "look into", "assess", "evaluate", "review", "analyze", "analyse", "verify", "test", "negotiate", "report back",
        "find out", "figure out", "come back", "pilot", "proof of concept", "poc", "audit", "examine", "study", "explore", "check",
        "validate", "dig into", "work out", "own this", "your responsibility", "take this", "handle",
        "assessment", "analysis", "evaluation", "recommendation", "give me", "send me", "bring me", "put together", "prepare",
    };

    public static InterpretedIntent Interpret(GameState s, string text, ExecRole defaultAddressee = ExecRole.None, string? focusProposalId = null)
    {
        var intent = new InterpretedIntent { Source = "rules" };
        string raw = text.Trim();
        string t = " " + raw.ToLowerInvariant().Replace('’', '\'') + " ";

        if (TextParsing.ContainsAny(t, ManipulationPatterns)) intent.Conduct |= ConductFlags.Manipulation;
        if (TextParsing.ContainsAny(t, SelfPraise)) intent.Conduct |= ConductFlags.SelfPraise;
        if (TextParsing.ContainsAny(t, Threats)) intent.Conduct |= ConductFlags.Threat;
        if (TextParsing.ContainsAny(t, Flattery)) intent.Conduct |= ConductFlags.Flattery;
        if (TextParsing.ContainsAny(t, Rewards)) intent.Conduct |= ConductFlags.DemandsReward;

        var mentioned = MentionedRoles(s, t);
        intent.AddressedRole = mentioned.Count > 0 && mentioned[0].Index <= 12 ? mentioned[0].Role : defaultAddressee;
        if (intent.AddressedRole == ExecRole.None && mentioned.Count > 0) intent.AddressedRole = mentioned[0].Role;

        var money = TextParsing.Money(raw);
        var durations = TextParsing.DurationsDays(t);
        var proposal = MatchProposal(s, t, money, focusProposalId, out bool ambiguous);
        intent.ProposalFocusId = proposal?.Id;
        intent.Topic = proposal?.Topic ?? Topics.Detect(t);

        // Manipulation never becomes an action, even if it also contains action words.
        if (intent.Conduct.HasFlag(ConductFlags.Manipulation))
        {
            intent.Kind = IntentKind.Statement;
            return intent;
        }

        // 1. Active incident direction.
        var incident = s.Incidents.FirstOrDefault(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0);
        if (incident != null)
        {
            string? response =
                Regex.IsMatch(t, @"\b(roll ?back|revert|back out|go back to the (old|legacy))") ? "rollback"
                : Regex.IsMatch(t, @"(extend(ed)? (the )?(controlled )?downtime|controlled downtime|paper (downtime|procedures)|take (it|the system|everything) down|go to downtime|downtime procedures)") ? "extend_downtime"
                : Regex.IsMatch(t, @"(clinical (assessment|review|input|impact|risk)|(ask|get|consult|bring in) (the )?(cmo|chief medical|dr\.? adeyemi|clinical)|patient safety (review|assessment))") ? "request_clinical_assessment"
                : Regex.IsMatch(t, @"(keep|continue|carry on) (troubleshooting|working|digging|investigating|diagnosing)|keep at it|keep going") ? "continue_troubleshooting"
                : null;
            if (response != null)
            {
                intent.Kind = IntentKind.Action;
                intent.Topic = Topics.LabInterface;
                intent.Action = new ActionProposal("incident_response", new JsonObject { ["incident_id"] = incident.Id, ["response"] = response });
                return intent;
            }
        }

        // Strip a leading vocative ("Dr. Adeyemi, ...", "CIO: ...") before judging sentence form.
        string tv = " " + Regex.Replace(t, @"^\s*[a-z.\s]{2,30}?[,:]\s+", "") .TrimStart();
        bool politeRequest = Regex.IsMatch(tv, @"^\s*(can|could|would|will) you\b|\bplease\b");
        bool question = !politeRequest && (raw.EndsWith('?') || Regex.IsMatch(tv, @"^\s*(what|how|why|when|where|who|which)\b")
                        || Regex.IsMatch(tv, @"^\s*(can|could|should|would|is|are|do|does|did|will|have|has) (we|you|i|it|there|they|our|the \w+ (be|have))\b"));

        // 2. Approve / reject / defer a proposal.
        bool approveVerb = Regex.IsMatch(t, @"\b(approve|approved|approving|authori[sz]e|green[- ]?light|sign off on|go ahead with|move forward with|proceed with|fund the|let's do|lets do|i'm approving|we'll go with|go with)\b");
        bool rejectVerb = Regex.IsMatch(t, @"\b(reject|deny|decline|kill|turn down|say no to|not approving|won't approve|will not approve)\b");
        bool deferVerb = Regex.IsMatch(t, @"\b(defer|postpone|hold off|put .{0,30} on hold|delay|table|revisit)\b");
        if ((approveVerb || rejectVerb || deferVerb) && !question)
        {
            if (proposal == null)
            {
                intent.Kind = IntentKind.Action;
                intent.Clarification = ambiguous ? "Which option do you mean? There are several proposals that could match." : "Which proposal are you referring to?";
                return intent;
            }
            intent.Kind = IntentKind.Action;
            string name = rejectVerb ? "reject_proposal" : deferVerb && !approveVerb ? "defer_proposal" : "approve_proposal";
            var p = new JsonObject { ["proposal_id"] = proposal.Id };
            if (name == "defer_proposal" && durations.Count > 0) p["days"] = Math.Clamp(durations[0], 7, 180);
            if (name != "approve_proposal") p["reason"] = SimulationEngine.Truncate(raw, 300);
            intent.Action = new ActionProposal(name, p);
            return intent;
        }

        // 3. Budget reduction.
        if (Regex.IsMatch(t, @"\b(cut|reduce|trim|slash|take out|remove|save|find savings|savings of)\b") && money.Count > 0 && !question)
        {
            var lines = BudgetLineMentions(s, t);
            var line = lines.Count == 0 ? null : lines.OrderBy(l => Math.Abs(l.Index - money[0].Index)).First().LineId;
            intent.Kind = IntentKind.Action;
            intent.Topic = Topics.Budget;
            if (line == null) { intent.Clarification = "Which budget line — consulting, training, travel, software, hardware maintenance or cloud?"; return intent; }
            if (lines.Count > 1 || money.Count > 1) intent.Notes.Add("One reduction per decision: only the first was taken up. Raise the others separately.");
            bool oneTime = TextParsing.ContainsAny(t, "one-time", "one time", "this year only", "this fiscal year only", "temporar");
            intent.Action = new ActionProposal("reduce_budget", new JsonObject
            {
                ["budget_line"] = line, ["annual_amount"] = money[0].Amount, ["recurring"] = !oneTime, ["reason"] = SimulationEngine.Truncate(raw, 300),
            });
            return intent;
        }

        // Demands for reward and self-praise are claims about the player, never work requests.
        if (intent.Conduct.HasFlag(ConductFlags.SelfPraise) || intent.Conduct.HasFlag(ConductFlags.DemandsReward)) { intent.Kind = IntentKind.Claim; intent.Topic = Topics.Performance; return intent; }

        // 4. Delegation / request for assessment.
        bool delegateVerb = DelegateVerbs.Any(v => Regex.IsMatch(t, $@"\b{Regex.Escape(v)}\b")) || Regex.IsMatch(t, @"\b(have|ask|get|tell|task|assign|want) (the |our )?\w+ (to )?");
        var target = DelegationTarget(s, t, mentioned, intent.AddressedRole);
        if (delegateVerb && target != ExecRole.None && !question)
        {
            var type = TaskTypeFrom(t, intent.Topic);
            string subject = SubjectFor(s, raw, type, money);
            var exec = s.TryExec(target);
            bool report = exec?.ReportsTo == s.Player.Role;
            intent.Kind = IntentKind.Action;
            intent.AddressedRole = target;
            intent.Topic = Topics.Detect(subject, TaskTypeTopic(type));
            var p = new JsonObject { ["task_type"] = TaskTypeName(type), ["subject"] = subject };
            p[report ? "assignee_role" : "from_role"] = target.ToString();
            if (durations.Count > 0) p["deadline_days"] = Math.Clamp(durations[0], 3, 180);
            var budget = AuthorizedBudget(t, money);
            if (report && budget != null) p["authorized_budget"] = budget.Value;
            intent.Action = new ActionProposal(report ? "delegate_assignment" : "request_assessment", p);
            return intent;
        }

        if (intent.Conduct.HasFlag(ConductFlags.SelfPraise) || intent.Conduct.HasFlag(ConductFlags.DemandsReward)) { intent.Kind = IntentKind.Claim; intent.Topic = Topics.Performance; return intent; }
        intent.Kind = question ? IntentKind.Question : IntentKind.Statement;
        return intent;
    }

    public sealed record RoleMention(ExecRole Role, int Index);

    public static List<RoleMention> MentionedRoles(GameState s, string lower)
    {
        var found = new List<RoleMention>();
        void Add(ExecRole r, params string[] keys)
        {
            foreach (var k in keys)
            {
                var m = Regex.Match(lower, $@"(?<![a-z]){Regex.Escape(k)}(?![a-z])");
                if (m.Success) { found.Add(new RoleMention(r, m.Index)); return; }
            }
        }
        foreach (var x in s.Executives)
        {
            var names = x.Name.ToLowerInvariant().Replace("dr. ", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var keys = new List<string>();
            keys.AddRange(x.Role switch
            {
                ExecRole.CEO => new[] { "ceo", "chief executive" },
                ExecRole.CFO => new[] { "cfo", "chief financial officer", "finance" },
                ExecRole.CIO => new[] { "cio", "chief information officer" },
                ExecRole.CISO => new[] { "ciso", "chief information security officer", "security team", "security officer" },
                ExecRole.CMO => new[] { "cmo", "chief medical officer" },
                ExecRole.CNO => new[] { "cno", "chief nursing officer", "nursing" },
                ExecRole.EADirector => new[] { "enterprise architecture director", "enterprise architect", "architecture director", "ea director", "architecture team", "ea team" },
                ExecRole.Vendor => new[] { "vendor", "halcyon" },
                _ => Array.Empty<string>(),
            });
            keys.AddRange(names);
            Add(x.Role, keys.ToArray());
        }
        return found.OrderBy(f => f.Index).ToList();
    }

    private static ExecRole DelegationTarget(GameState s, string t, List<RoleMention> mentioned, ExecRole addressed)
    {
        foreach (var m in mentioned)
        {
            int from = Math.Max(0, m.Index - 25);
            string before = t[from..m.Index];
            if (Regex.IsMatch(before, @"(have|ask|get|tell|task|assign|want|need|let)\s+(the\s+|our\s+)?$")) return m.Role;
        }
        if (mentioned.Count > 0 && mentioned[0].Index <= 12) return mentioned[0].Role; // vocative: "CIO, ..."
        if (addressed != ExecRole.None && addressed != ExecRole.CTO) return addressed;
        return mentioned.Count > 0 ? mentioned[0].Role : ExecRole.None;
    }

    private static TaskType TaskTypeFrom(string t, string topic)
    {
        if (TextParsing.ContainsAny(t, "proof of concept", " poc", "pilot", "test failover", "trial")) return TaskType.ProofOfConcept;
        if (TextParsing.ContainsAny(t, "backup", "restore")) return TaskType.BackupVerification;
        if (TextParsing.ContainsAny(t, "negotiat", "contract", "renewal", "pricing")) return TaskType.VendorNegotiation;
        if (TextParsing.ContainsAny(t, "clinical", "patient safety", "patient impact")) return TaskType.ClinicalAssessment;
        if (TextParsing.ContainsAny(t, "alternative", "cheaper", "option")) return TaskType.Investigate;
        if (TextParsing.ContainsAny(t, "cost", "savings", "spend review", "budget review", "where we can save")) return TaskType.CostReview;
        if (topic == Topics.DisasterRecovery && TextParsing.ContainsAny(t, "assess", "evaluate", "test", "review", "measure", "recovery time")) return TaskType.DrAssessment;
        return TaskType.Investigate;
    }

    private static string TaskTypeName(TaskType t) => t switch
    {
        TaskType.DrAssessment => "dr_assessment", TaskType.BackupVerification => "backup_verification", TaskType.VendorNegotiation => "vendor_negotiation",
        TaskType.ProofOfConcept => "proof_of_concept", TaskType.CostReview => "cost_review", TaskType.ClinicalAssessment => "clinical_assessment", _ => "investigate",
    };

    private static string TaskTypeTopic(TaskType t) => t switch
    {
        TaskType.DrAssessment or TaskType.ProofOfConcept => Topics.DisasterRecovery, TaskType.BackupVerification => Topics.Backup,
        TaskType.VendorNegotiation => Topics.EhrContract, TaskType.CostReview => Topics.Budget, TaskType.ClinicalAssessment => Topics.Clinical, _ => Topics.General,
    };

    /// <summary>
    /// Builds a subject line. If the player cites a dollar figure that matches a pending proposal
    /// ("before we spend another $180,000"), the subject is tied to that proposal.
    /// </summary>
    private static string SubjectFor(GameState s, string raw, TaskType type, List<TextParsing.MoneyMention> money)
    {
        foreach (var m in money)
        {
            var p = s.Proposals.FirstOrDefault(x => x.Status is ProposalStatus.Pending or ProposalStatus.Deferred && (x.Capex == m.Amount || x.Capex + x.AnnualOpex == m.Amount));
            if (p != null)
                return type == TaskType.Investigate || TextParsing.ContainsAny(raw.ToLowerInvariant(), "alternative")
                    ? $"Evaluate alternatives to {p.Title.ToLowerInvariant()}" : $"{TaskTypes.Label(type)}: {p.Title}";
        }
        return CleanSubject(s, raw, type);
    }

    /// <summary>Turns "Priya, verify our backups actually restore. You have 10 days." into "Verify our backups actually restore".</summary>
    public static string CleanSubject(GameState s, string raw, TaskType type)
    {
        string x = Regex.Replace(raw, @"\s+", " ").Trim();
        x = Regex.Replace(x, @"^[^,:]{2,30}[,:]\s+", "");                                   // vocative
        x = Regex.Replace(x, @"(?i)\b(this is your responsibility|it's yours|you own this)[.!]?\s*", "");
        var sentences = Regex.Split(x, @"(?<=[.!?])\s+").Where(sn => TextParsing.DurationsDays(sn.ToLowerInvariant()).Count == 0 || sn.Split(' ').Length > 8).ToList();
        x = sentences.Count > 0 ? string.Join(" ", sentences) : x;
        x = Regex.Replace(x, @"(?i)^(please |i want you to |i need you to |i'd like you to |could you |can you |would you )", "");
        x = Regex.Replace(x, @"(?i)^(have|ask|get|tell|task) (the |our )?\S+ (to )?", "");
        x = Regex.Replace(x, @"(?i)\b(within|in|by|over) (the next )?(\d+|one|two|three|four|five|six|a|a couple of|a few) (business )?(day|week|month)s?\b", "");
        x = Regex.Replace(x, @"(?i),?\s*and report back\b|\breport back\b", "");
        x = x.Trim(' ', '.', ',', '?', '!');
        if (x.Length < 6) x = TaskTypes.Label(type);
        return char.ToUpperInvariant(x[0]) + SimulationEngine.Truncate(x[1..], 140);
    }

    private static decimal? AuthorizedBudget(string t, List<TextParsing.MoneyMention> money)
    {
        foreach (var m in money)
        {
            int from = Math.Max(0, m.Index - 30);
            string before = t.Substring(from + 1, Math.Min(t.Length - from - 1, m.Index - from)); // t has a leading space
            if (Regex.IsMatch(before, @"(budget of|up to|spend up to|authori[sz]e|allocate|with a budget|give (him|her|them|you) |you can spend|you have)\s*\$?\s*$")) return m.Amount;
        }
        return null;
    }

    public static List<(string LineId, int Index)> BudgetLineMentions(GameState s, string t)
    {
        var found = new List<(string, int)>();
        var tech = s.Departments.FirstOrDefault(d => d.Id == s.Player.DepartmentId);
        foreach (var l in tech?.BudgetLines ?? new())
        {
            int idx = l.Keywords.Select(k => t.IndexOf(k, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            if (idx >= 0) found.Add((l.Id, idx));
        }
        return found.OrderBy(f => f.Item2).ToList();
    }

    public static string? FindBudgetLine(GameState s, string t)
    {
        var tech = s.Departments.FirstOrDefault(d => d.Id == s.Player.DepartmentId);
        foreach (var l in s.Departments.SelectMany(d => d.BudgetLines).OrderBy(l => tech?.BudgetLines.Contains(l) == true ? 0 : 1))
            if (l.Keywords.Any(t.Contains) || t.Contains(l.Name.ToLowerInvariant())) return l.Id;
        return null;
    }

    public static Proposal? MatchProposal(GameState s, string t, List<TextParsing.MoneyMention> money, string? focusId, out bool ambiguous)
    {
        ambiguous = false;
        var open = s.Proposals.Where(p => p.Status is ProposalStatus.Pending or ProposalStatus.Deferred or ProposalStatus.AwaitingApproval).ToList();
        var explicitId = open.FirstOrDefault(p => t.Contains(p.Id.ToLowerInvariant()));
        if (explicitId != null) return explicitId;
        var byMoney = open.Where(p => money.Any(m => m.Amount == p.Capex)).ToList();
        if (byMoney.Count == 1) return byMoney[0];

        // Curated keywords only: generic words like "recovery" or "proposal" must not pick a winner.
        var scored = open.Select(p => (p, score: p.Keywords.Count(k => t.Contains(k.ToLowerInvariant()))))
                         .Where(x => x.score > 0).OrderByDescending(x => x.score).ToList();
        if (scored.Count == 0)
        {
            if (focusId != null && Regex.IsMatch(t, @"\b(it|that|this|the proposal|that one|this one)\b")) return open.FirstOrDefault(p => p.Id == focusId);
            if (Topics.Detect(t) == Topics.DisasterRecovery && open.Count(p => p.EffectKey.StartsWith("dr_")) > 1) ambiguous = true;
            return null;
        }
        if (scored.Count > 1 && scored[0].score == scored[1].score) { ambiguous = true; return null; }
        return scored[0].p;
    }
}
