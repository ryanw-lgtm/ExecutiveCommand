using ExecutiveCommand.Core.Evaluation;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// Objectives, XP, rank, achievements and quarterly report cards. Everything here is computed from
/// simulation state — measurable results only — so it can't be claimed, flattered or talked into.
/// </summary>
public static class ProgressionEngine
{
    public static readonly (int Xp, string Title)[] Ranks =
    {
        (0, "Newcomer"), (200, "Settling In"), (500, "Credible"), (900, "Trusted"), (1400, "Influential"),
        (2000, "Indispensable"), (2800, "Board Favorite"), (3800, "Legend"),
    };

    public static int Level(int xp) => Ranks.Count(r => xp >= r.Xp);
    public static string RankTitle(int xp) => Ranks[Level(xp) - 1].Title;
    public static (int Into, int Needed) LevelProgress(int xp)
    {
        int l = Level(xp);
        if (l >= Ranks.Length) return (1, 1);
        return (xp - Ranks[l - 1].Xp, Ranks[l].Xp - Ranks[l - 1].Xp);
    }

    // ------------------------------------------------------------------ objectives

    private sealed record Def(string Id, string Title, string Description, int Stars, DateOnly Deadline, string Hint, Func<GameState, double> Progress);

    private static decimal RecurringSavings(GameState s) =>
        s.Savings.Where(v => v.Source == "budget_cut" && v.Recurring && v.Status != SavingsStatus.Reversed).Sum(v => v.AnnualAmount);

    private static double AssignmentProgress(GameState s, TaskType type, string topic) =>
        DecisionEvaluator.HasEvidence(s, topic, type switch { TaskType.DrAssessment => "dr_assessment", TaskType.BackupVerification => "backup_verification", _ => "investigate" }) ? 1
        : s.Assignments.Where(a => a.Type == type && a.Topic == topic && a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue)
                       .Select(a => Math.Min(0.95, a.EffortDone / a.EffortRequired)).DefaultIfEmpty(0).Max();

    /// <summary>The healthcare CTO campaign. Deadlines line up with fiscal quarters and the contract renewal.</summary>
    private static IEnumerable<Def> Campaign() => new[]
    {
        new Def("obj-dr-truth", "Find out the truth about recovery", "Get a disaster recovery assessment completed, so decisions rest on tested numbers rather than a 2022 figure.",
            2, new DateOnly(2027, 3, 31), "Ask the CIO or EA Director to assess real recovery time.", s => AssignmentProgress(s, TaskType.DrAssessment, Topics.DisasterRecovery)),
        new Def("obj-backups", "Test the backups", "Have someone actually restore the critical backup sets.",
            1, new DateOnly(2027, 3, 31), "Ask the CISO to verify backups restore.", s => AssignmentProgress(s, TaskType.BackupVerification, Topics.Backup)),
        new Def("obj-savings-start", "Start on the savings", "Approve at least $200k of recurring technology savings.",
            1, new DateOnly(2027, 3, 31), "Cut discretionary spend — ideally after a cost review.", s => Math.Min(1, (double)(RecurringSavings(s) / 200_000m))),
        new Def("obj-contract", "Don't sign blind", "Negotiate the Halcyon EHR renewal before it auto-renews on April 30.",
            2, new DateOnly(2027, 4, 30), "Delegate a vendor negotiation to the CIO.", s => s.Contracts.Any(c => c.Negotiated) ? 1
                : s.Assignments.Where(a => a.Type == TaskType.VendorNegotiation && a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue).Select(a => Math.Min(0.95, a.EffortDone / a.EffortRequired)).DefaultIfEmpty(0).Max()),
        new Def("obj-savings", "Hit the savings target", "Secure the CFO's $400k of recurring savings.",
            2, new DateOnly(2027, 6, 30), "More cuts, or a negotiated contract price.", s => Math.Min(1, (double)(RecurringSavings(s) / PerformanceEngine.SavingsTarget))),
        new Def("obj-dr-funded", "Fund a disaster recovery plan", "Get a DR option approved and under way.",
            2, new DateOnly(2027, 6, 30), "Pick an option with evidence behind it — big ones need CFO and CEO approval.",
            s => s.Projects.Any(p => p.EffectKey.StartsWith("dr_")) ? 1 : s.Proposals.Any(p => p.EffectKey.StartsWith("dr_") && p.Status == ProposalStatus.AwaitingApproval) ? 0.5 : 0),
        new Def("obj-board", "Keep the board on side", "Have board confidence at 60 or above on June 30.",
            1, new DateOnly(2027, 6, 30), "Good reviews and no avoidable incidents.", s => Math.Min(1, s.Metrics.BoardConfidence / 60.0)),
        new Def("obj-immutable", "Ransomware-proof the backups", "Get immutable backups live.",
            2, new DateOnly(2027, 9, 30), "Approve the immutable backup vault.", s => s.Technology.ImmutableBackups ? 1 : s.Projects.Where(p => p.EffectKey == "immutable_backup").Select(p => p.Progress * 0.95).DefaultIfEmpty(0).Max()),
        new Def("obj-rto", "Recover the EHR in under 8 hours", "Prove — with a failover test — that the EHR can be recovered within 8 hours.",
            3, new DateOnly(2027, 12, 31), "Deliver a DR project and pass its go-live failover test.",
            s => s.Technology.HasSecondarySite && s.Technology.ReportedRtoHours <= 8 ? 1 : s.Projects.Where(p => p.EffectKey.StartsWith("dr_")).Select(p => p.Progress * 0.95).DefaultIfEmpty(0).Max()),
    };

    public static void InitializeCampaign(GameState s)
    {
        s.Objectives = Campaign().Select(d => new Objective { Id = d.Id, Title = d.Title, Description = d.Description, Stars = d.Stars, Deadline = d.Deadline, Hint = d.Hint }).ToList();
    }

    // ------------------------------------------------------------------ achievements

    private sealed record Ach(string Id, string Title, string Description, Func<GameState, bool> Earned);

    private static readonly Ach[] Achievements =
    {
        new("ach-delegate", "First Orders", "Delegated work to one of your direct reports.", s => s.Assignments.Any(a => !a.IsRequest)),
        new("ach-evidence", "Evidence First", "Made a decision scored 80+ for evidence and quality.", s => s.Decisions.Any(d => d.Evaluation.Evidence >= 80 && d.Evaluation.Overall >= 75 && d.Action == "approve_proposal")),
        new("ach-listener", "Good Listener", "Consulted five different executives.", s => s.Memory.Where(m => m.Kind is "consultation" or "request").SelectMany(m => m.Involved).Distinct().Count() >= 5),
        new("ach-truth", "Inconvenient Truth", "Uncovered a risk nobody knew about.", s => s.Risks.Any(r => r.Known && r.Identified > s.StartDate && r.Id == "RSK-BKP")),
        new("ach-bargain", "Hard Bargain", "Got the EHR vendor below its proposed renewal price.", s => s.Contracts.Any(c => c.Negotiated && c.AgreedUpliftPct < c.ProposedUpliftPct - 0.02)),
        new("ach-savings", "Penny Wise", "Secured the CFO's full $400k of recurring savings.", s => RecurringSavings(s) >= PerformanceEngine.SavingsTarget),
        new("ach-alternative", "Second Opinion", "Found a cheaper alternative to a proposal on the table.", s => s.Proposals.Any(p => p.SupersedesId != null)),
        new("ach-noharm", "No Harm Done", "Resolved the cutover incident without a patient safety event.", s => s.Incidents.Any(i => i.Kind == IncidentEngine.CutoverKind && i.Status == IncidentStatus.Resolved && i.SafetyEvents == 0)),
        new("ach-ready", "Disaster Ready", "Passed a failover test with the EHR back inside 8 hours.", s => s.Technology.HasSecondarySite && s.Technology.ReportedRtoHours <= 8),
        new("ach-astudent", "Straight A", "Earned an A on a quarterly report card.", s => s.Career.ReportCards.Any(r => r.Overall == "A")),
    };

    public static IReadOnlyList<(string Id, string Title, string Description)> AllAchievements => Achievements.Select(a => (a.Id, a.Title, a.Description)).ToList();

    // ------------------------------------------------------------------ evaluation

    /// <summary>Re-evaluates objectives and achievements. Called daily and after every decision.</summary>
    public static void Update(SimulationEngine e)
    {
        var s = e.State;
        if (s.Objectives.Count == 0) InitializeCampaign(s);
        var defs = Campaign().ToDictionary(d => d.Id);
        foreach (var o in s.Objectives.Where(o => o.Status == ObjectiveStatus.Active))
        {
            if (!defs.TryGetValue(o.Id, out var d)) continue;
            o.Progress = Math.Round(Math.Clamp(d.Progress(s), 0, 1), 3);
            if (o.Progress >= 1)
            {
                o.Status = ObjectiveStatus.Completed;
                o.ResolvedOn = s.CurrentDate;
                s.Career.Stars += o.Stars;
                AddXp(e, o.Stars * 100);
                s.Career.PendingCelebrations.Add($"objective:{o.Id}");
                e.Remember("objective", $"Objective completed: {o.Title}.", new[] { "performance" }, new[] { ExecRole.CEO }, 1);
            }
            else if (s.CurrentDate > o.Deadline)
            {
                o.Status = ObjectiveStatus.Failed;
                o.ResolvedOn = s.CurrentDate;
                s.Career.PendingCelebrations.Add($"failed:{o.Id}");
            }
        }
        foreach (var a in Achievements)
        {
            if (s.Career.Achievements.Any(x => x.Id == a.Id) || !a.Earned(s)) continue;
            s.Career.Achievements.Add(new Achievement { Id = a.Id, Title = a.Title, Description = a.Description, UnlockedOn = s.CurrentDate });
            AddXp(e, 50);
            s.Career.PendingCelebrations.Add($"achievement:{a.Id}");
        }
    }

    public static void AddXp(SimulationEngine e, int amount)
    {
        var s = e.State;
        int before = Level(s.Career.Xp);
        s.Career.Xp += amount;
        int after = Level(s.Career.Xp);
        if (after > before) s.Career.PendingCelebrations.Add($"rank:{after}");
    }

    public static string Grade(double score) => score >= 80 ? "A" : score >= 68 ? "B" : score >= 55 ? "C" : score >= 45 ? "D" : "F";

    /// <summary>Turns a performance review into a report card with letter grades, stars and XP.</summary>
    public static QuarterReport ReportCard(SimulationEngine e, PerformanceReview review, DateOnly periodStart)
    {
        var s = e.State;
        Update(e);
        var inPeriod = s.Objectives.Where(o => o.Deadline >= periodStart && o.Deadline <= review.Date).ToList();
        int earned = inPeriod.Where(o => o.Status == ObjectiveStatus.Completed).Sum(o => o.Stars);
        int possible = inPeriod.Sum(o => o.Stars);
        string overall = Grade(review.Overall);
        int xp = overall switch { "A" => 250, "B" => 150, "C" => 80, "D" => 30, _ => 0 };
        var card = new QuarterReport
        {
            Id = s.NewId("RPT"), Period = review.Period, Date = review.Date, Overall = overall,
            Decisions = Grade(review.DecisionQuality), Delivery = Grade(review.Delivery), Finance = Grade(review.FinancialStewardship), Risk = Grade(review.RiskManagement),
            StarsEarned = earned, StarsPossible = possible, XpGained = xp,
            Verdict = review.ReviewerComment, Highlights = review.Evidence.Take(4).ToList(),
        };
        s.Career.ReportCards.Add(card);
        AddXp(e, xp);
        s.Career.PendingCelebrations.Add($"report:{card.Id}");
        return card;
    }
}
