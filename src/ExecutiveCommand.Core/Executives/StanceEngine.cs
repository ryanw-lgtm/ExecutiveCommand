using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Evaluation;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Executives;

public enum Position { Support, Conditional, Neutral, Concerned, Oppose }

/// <summary>
/// An executive's position on a matter, computed from game state and their own priorities, biases and
/// trust in the player. The language model is given this stance and may only voice it — it cannot
/// change it. This is the main defense against sycophancy: agreement must be earned in the simulation.
/// </summary>
public sealed class Stance
{
    public ExecRole Role { get; init; }
    public Position Position { get; set; }
    public double Score { get; set; }
    public List<string> Reasons { get; } = new();
    /// <summary>The subset of <see cref="Reasons"/> that argue in favor.</summary>
    public List<string> Pros { get; } = new();

    internal void Pro(string reason) { Reasons.Add(reason); Pros.Add(reason); }

    /// <summary>Reasons ordered so the ones consistent with the position come first.</summary>
    public IEnumerable<string> Ordered => Position == Position.Support
        ? Pros.Concat(Reasons.Except(Pros))
        : Reasons.Except(Pros).Concat(Pros);

    /// <summary>One sentence that explains the position — never contradicts it.</summary>
    public string Headline => Position switch
    {
        Position.Support => Pros.FirstOrDefault() ?? "On balance, it's worth doing.",
        Position.Oppose or Position.Concerned => Reasons.Except(Pros).FirstOrDefault() ?? "I'm not convinced it's worth it.",
        Position.Conditional => Conditions.Count > 0 ? $"Possibly — first I'd want {Conditions[0]}." : Reasons.Except(Pros).FirstOrDefault() ?? Pros.FirstOrDefault() ?? "It could go either way.",
        _ => Reasons.FirstOrDefault() ?? "No strong view either way.",
    };
    public List<string> Conditions { get; } = new();
    /// <summary>Facts (with numbers) the executive may cite. Dialogue citing other figures is rejected.</summary>
    public List<string> Facts { get; } = new();

    internal void Finish()
    {
        Score = Math.Clamp(Score, -1, 1);
        Position = Score > 0.25 ? Position.Support
                 : Score > -0.1 ? (Math.Abs(Score) < 0.06 && Conditions.Count == 0 ? Position.Neutral : Position.Conditional)
                 : Score > -0.35 ? Position.Concerned : Position.Oppose;
    }
}

public static class StanceEngine
{
    private static string M(decimal d) => SimulationEngine.Money(d);

    public static Stance OnProposal(GameState s, Executive x, Proposal p)
    {
        var st = new Stance { Role = x.Role };
        var t = s.Technology;
        double trust = (x.TrustInPlayer - 50) / 50.0;
        int evidenceHave = p.RequiredEvidence.Count(r => DecisionEvaluator.HasEvidence(s, p.Topic, r));
        double evidence = p.RequiredEvidence.Count == 0 ? 0.7 : evidenceHave / (double)p.RequiredEvidence.Count;
        decimal tco = p.Capex + p.AnnualOpex * 3;
        double drGap = Math.Clamp((t.ReportedRtoHours - t.TargetRtoHours) / Math.Max(1, t.TargetRtoHours * 4), 0, 1);
        bool isDr = p.EffectKey.StartsWith("dr_");
        var missing = p.RequiredEvidence.Where(r => !DecisionEvaluator.HasEvidence(s, p.Topic, r)).ToList();
        st.Facts.Add($"{p.Title}: {M(p.Capex)} capital, {M(p.AnnualOpex)} per year to operate, about {p.DurationDays} days to deliver");
        var cheaper = s.Proposals.FirstOrDefault(a => a.SupersedesId == p.Id && a.Status is ProposalStatus.Pending or ProposalStatus.Deferred or ProposalStatus.AwaitingApproval);
        if (cheaper != null) st.Facts.Add($"Alternative on the table: {cheaper.Title}, {M(cheaper.Capex)} capital");
        st.Facts.Add($"Technology capital remaining this fiscal year: {M(s.Finance.TechCapitalBudgetRemaining)}");
        if (isDr) st.Facts.Add($"Current documented recovery time: {t.ReportedRtoHours:0} hours vs {t.TargetRtoHours:0}-hour target");

        switch (x.Role)
        {
            case ExecRole.CFO:
                st.Score = 0.35 * evidence - 0.45 * Math.Min(1, (double)tco / 2_500_000) + (p.Capex <= s.Finance.TechCapitalBudgetRemaining ? 0.2 : -0.25)
                           + 0.25 * trust + 0.25 * (isDr ? drGap : 0) + (p.Capex + p.AnnualOpex < 100_000 ? 0.2 : 0);
                if (p.Capex > s.Finance.TechCapitalBudgetRemaining)
                    st.Reasons.Add($"It needs {M(p.Capex)} of capital and only {M(s.Finance.TechCapitalBudgetRemaining)} remains in this year's technology capital budget.");
                if (p.AnnualOpex > 0) st.Reasons.Add($"It adds {M(p.AnnualOpex)} a year to operating costs while I'm being asked to find savings.");
                if (evidence < 1 && missing.Count > 0) { st.Reasons.Add("I haven't seen analysis that supports the numbers."); st.Conditions.AddRange(missing.Select(EvidenceLabel)); }
                if (isDr && drGap > 0.5) st.Pro($"I accept that a {t.ReportedRtoHours:0}-hour recovery time is an exposure we can't carry indefinitely.");
                if (x.TrustInPlayer < 40) st.Reasons.Add("Frankly, recent approvals haven't delivered what was promised.");
                if (cheaper != null) { st.Score -= 1.0; st.Reasons.Insert(0, $"Your own team found a {M(cheaper.Capex)} alternative that does the same job. I won't fund the {M(p.Capex)} version."); }
                break;
            case ExecRole.CEO:
                st.Score = 0.3 * drGap * (isDr ? 1 : 0) + 0.3 * evidence + 0.15 * (s.Metrics.BoardConfidence - 50) / 50 - 0.25 * Math.Min(1, (double)tco / 3_000_000)
                           + 0.03 * p.ClinicalRiskReduction + 0.02 * p.SecurityRiskReduction + 0.2 * trust
                           + (DecisionEvaluator.Consulted(s, ExecRole.CMO, p.Topic) || !p.ClinicallySignificant ? 0.05 : -0.15);
                if (isDr && drGap > 0.4) st.Pro("The board has asked me directly about our ability to recover the EHR.");
                if (p.ClinicallySignificant && !DecisionEvaluator.Consulted(s, ExecRole.CMO, p.Topic)) { st.Reasons.Add("I want our clinical leaders behind anything that touches patient care."); st.Conditions.Add("the CMO's view on clinical impact"); }
                if (tco > 2_000_000m) st.Reasons.Add($"At roughly {M(tco)} over three years this is a board-level commitment.");
                if (evidence < 0.5 && missing.Count > 0) st.Conditions.AddRange(missing.Select(EvidenceLabel));
                if (cheaper != null) { st.Score -= 0.6; st.Reasons.Insert(0, "Why would we spend more when a cheaper option meets the need?"); }
                break;
            case ExecRole.CIO:
                st.Score = (p.Sponsor == x.Role ? 0.45 : 0) + (p.EffectKey is "dr_onprem" or "identity_expansion" ? 0.2 : 0) - 0.4 * Math.Max(0, (x.Workload - 60) / 40.0)
                           + (p.EffectKey == "dr_cloud" ? -0.15 : 0) + 0.1 * trust;
                if (p.Sponsor == x.Role) st.Pro("My team put this together; it reflects what we've seen operationally.");
                if (p.EffectKey == "dr_cloud") st.Reasons.Add("My team doesn't yet have the cloud skills to run EHR recovery there.");
                if (x.Workload > 75) { st.Reasons.Add("My team is stretched; we'd need to stage this carefully."); st.Conditions.Add("relief on other commitments or contractor support"); }
                if (p.EffectKey == "identity_optimization") st.Reasons.Add("Optimization is cheaper, but it leans on my people to do the cleanup.");
                break;
            case ExecRole.CISO:
                st.Score = 0.06 * p.SecurityRiskReduction - 0.05 + (p.EffectKey == "immutable_backup" ? 0.5 : 0) + (isDr ? 0.15 : 0)
                           + (p.EffectKey == "dr_cloud" && t.CloudDrCertificationKnown == null ? -0.2 : 0);
                if (p.EffectKey == "immutable_backup") st.Pro("Immutable backups are the difference between a bad week and a catastrophe in a ransomware event.");
                if ((t.MeasuredBackupRestoreSuccess ?? 1) < 0.95) st.Facts.Add($"Measured backup restore success: {t.MeasuredBackupRestoreSuccess:P0}");
                if (p.EffectKey == "dr_cloud") st.Reasons.Add("A cloud DR tier is a new attack surface; I'd need to design the controls first.");
                if (p.SecurityRiskReduction < 3 && !isDr) st.Reasons.Add("From a security standpoint this is neutral.");
                break;
            case ExecRole.CMO:
            case ExecRole.CNO:
                bool clinicalInput = DecisionEvaluator.HasEvidence(s, Topics.DisasterRecovery, "clinical_assessment") || DecisionEvaluator.Consulted(s, x.Role, p.Topic);
                st.Score = (isDr ? 0.35 * drGap + 0.1 : 0) + 0.02 * p.ClinicalRiskReduction + (p.ClinicallySignificant && !clinicalInput ? -0.35 : 0.05);
                if (isDr) st.Pro($"{t.ReportedRtoHours:0} hours without the EHR is not a safe position for our patients.");
                if (p.ClinicallySignificant && !clinicalInput) { st.Reasons.Add("Any cutover affects every unit; clinical leadership has to shape the plan."); st.Conditions.Add("a clinical impact assessment and a rehearsed downtime drill"); }
                if (!isDr && p.ClinicalRiskReduction == 0) { st.Score = 0; st.Reasons.Add("This doesn't materially affect clinical operations."); }
                break;
            case ExecRole.EADirector:
                st.Score = p.EffectKey switch { "dr_cloud" => 0.5, "dr_hybrid" => 0.45, "dr_onprem" => -0.3, "identity_expansion" => -0.2, "identity_optimization" => 0.5, _ => 0.1 }
                           + 0.03 * p.TechDebtReduction;
                if (p.EffectKey == "dr_onprem") st.Reasons.Add("A second data center locks us into owning more hardware for another decade.");
                if (p.EffectKey is "dr_cloud" or "dr_hybrid") st.Pro("This moves us toward the target architecture rather than away from it.");
                if (p.EffectKey == "dr_cloud" && t.CloudDrCertificationKnown == null) st.Conditions.Add("a proof of concept to confirm vendor support");
                if (p.EffectKey == "identity_expansion") st.Reasons.Add("We'd be buying capacity to carry configuration debt.");
                if (cheaper != null) st.Score -= 0.3;
                break;
            case ExecRole.Vendor:
                st.Score = p.EffectKey == "dr_cloud" ? (t.CloudDrVendorCertified ? 0.2 : -0.1) : 0.3;
                if (p.EffectKey == "dr_cloud") st.Reasons.Add("We'd need our certification team to review any cloud-hosted configuration."); // the vendor never volunteers the answer
                else st.Pro("We can support that configuration and our professional services team is available.");
                break;
        }
        ApplyTraits(x, st);
        st.Finish();
        return st;
    }

    public static Stance OnBudgetCut(GameState s, Executive x, BudgetLine line, decimal amount)
    {
        var st = new Stance { Role = x.Role };
        decimal pool = line.AnnualBudget - line.CommittedAnnual;
        st.Facts.Add($"{line.Name}: {M(line.AnnualBudget)} annual budget, {M(line.DiscretionaryRemaining)} discretionary and uncommitted");
        st.Facts.Add($"Proposed reduction: {M(amount)} per year");
        bool feasible = amount <= line.DiscretionaryRemaining;
        switch (x.Role)
        {
            case ExecRole.CFO:
                st.Score = feasible ? 0.6 : -0.4;
                if (feasible) st.Pro("Savings that actually come out of the run rate are exactly what I asked for.");
                else st.Reasons.Add($"That's more than the {M(line.DiscretionaryRemaining)} that isn't already committed. I can't book savings that don't exist.");
                break;
            case ExecRole.CIO:
                st.Score = line.Id switch { "consulting" => -0.4, "hardware_maint" => -0.6, "training" => -0.3, _ => 0.1 };
                if (line.Id == "consulting") st.Reasons.Add("Contractors are carrying a third of our project work. Cut them and timelines slip.");
                if (line.Id == "hardware_maint") st.Reasons.Add("Our storage array is six years old and has no redundancy. Cutting maintenance there is a gamble.");
                if (line.Id == "training") st.Reasons.Add("We're already losing engineers to better-paying employers; this won't help.");
                break;
            case ExecRole.CISO:
                st.Score = line.Id is "hardware_maint" or "training" ? -0.3 : 0;
                if (line.Id == "training") st.Reasons.Add("Security awareness training comes out of that line.");
                break;
            default:
                st.Score = 0;
                st.Reasons.Add("That's a technology budget decision; I don't have a strong view.");
                break;
        }
        ApplyTraits(x, st);
        st.Finish();
        return st;
    }

    public static Stance OnDelegation(GameState s, Executive x, TaskType type, int deadlineDays, decimal budget)
    {
        var st = new Stance { Role = x.Role };
        double needDays = AssignmentEngine.EstimatedCalendarDays(s, x, type, budget, isRequest: x.ReportsTo != s.Player.Role);
        st.Facts.Add($"Deadline: {deadlineDays} days; current workload {(x.Workload > 75 ? "very high" : x.Workload > 55 ? "high" : "manageable")}");
        if (budget > 0) st.Facts.Add($"Authorized consulting budget: {M(budget)}");
        st.Score = 0.4 + (deadlineDays >= needDays ? 0.2 : -0.5) - Math.Max(0, (x.Workload - 70) / 60.0);
        if (deadlineDays < needDays * 0.85) { st.Reasons.Add($"Realistically a proper {TaskTypes.Label(type)} takes me about {needDays:0} days with everything else on my plate; {deadlineDays} won't be enough."); st.Conditions.Add("more time, contractor support, or something taken off my plate"); }
        else if (deadlineDays < needDays * 1.1) st.Reasons.Add($"That's tight — I'd estimate about {needDays:0} days. I'll push, but I can't promise it.");
        else st.Pro("That timeline is workable.");
        if (x.Workload > 75) st.Reasons.Add("I'll have to move other work to fit this in.");
        if (x.Biases.Contains("vendor-led") && type == TaskType.CostReview) st.Reasons.Add("I'll bring in the vendor's specialists to help.");
        ApplyTraits(x, st);
        st.Finish();
        return st;
    }

    public static Stance OnIncident(GameState s, Executive x, Incident inc, string? response)
    {
        var st = new Stance { Role = x.Role };
        st.Facts.Add($"{inc.MessageDelayPct:0}% of lab result messages delayed; incident day {inc.DaysActive + 1}; safety events so far: {inc.SafetyEvents}");
        switch (x.Role)
        {
            case ExecRole.CMO:
            case ExecRole.CNO:
                st.Score = response switch
                {
                    "continue_troubleshooting" => inc.ClinicalAssessmentRequested ? -0.2 : -0.7,
                    "extend_downtime" => x.Role == ExecRole.CNO ? 0.1 : 0.4,
                    "rollback" => 0.5,
                    "request_clinical_assessment" => 0.8,
                    _ => -0.2,
                };
                if (response == "continue_troubleshooting") st.Reasons.Add("Delayed critical results — potassium, troponin — can hurt someone before anyone notices.");
                else if (x.Role == ExecRole.CNO && response == "extend_downtime") st.Reasons.Add("Paper downtime is safe for a day or two, but my nurses can't sustain it for a week.");
                else st.Pro("Patient safety has to set the pace here, not the project schedule.");
                break;
            case ExecRole.CIO:
                st.Score = response switch { "continue_troubleshooting" => 0.5, "rollback" => -0.3, "extend_downtime" => 0.2, _ => 0.1 };
                if (response == "rollback") st.Reasons.Add("Rolling back costs us weeks and we'd have to do this cutover again.");
                else st.Pro("Give my team room and we'll find it.");
                if (inc.DaysActive >= 4) st.Reasons.Add("I'll be honest: we're not as close as I'd hoped.");
                break;
            default:
                st.Score = 0;
                st.Reasons.Add("I'll defer to clinical leadership on the safety question.");
                break;
        }
        st.Finish();
        return st;
    }

    private static void ApplyTraits(Executive x, Stance st)
    {
        // Low-trust executives give the player less benefit of the doubt.
        st.Score += (x.TrustInPlayer - 50) / 500.0;
    }

    public static string EvidenceLabel(string evidenceType) => evidenceType switch
    {
        "dr_assessment" => "a disaster recovery assessment with tested recovery times",
        "proof_of_concept" => "a proof of concept",
        "backup_verification" => "verified backup restore results",
        "cost_review" => "a cost review",
        "clinical_assessment" => "a clinical impact assessment",
        _ => "an independent review of alternatives",
    };
}
