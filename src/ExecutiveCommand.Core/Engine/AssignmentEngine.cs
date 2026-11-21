using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// Executes delegated work. Executives act autonomously within their authority; their competence,
/// workload and resources determine speed and — crucially — how accurately their findings reflect the
/// hidden ground truth. A weak assessment can give the player false confidence.
/// </summary>
public static class AssignmentEngine
{
    public static void DailyTick(SimulationEngine e)
    {
        var s = e.State;
        bool workday = ProjectEngine.IsWorkday(s.CurrentDate);
        foreach (var a in s.Assignments.Where(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue).ToList())
        {
            var exec = s.Exec(a.Assignee);
            if (workday)
            {
                double rate = ExpectedDailyRate(s, exec, a.AuthorizedBudget, a.IsRequest) * (0.85 + e.Rng.NextDouble() * 0.3);
                a.EffortDone += rate;
                if (a.AuthorizedBudget > 0)
                    a.Spent = Math.Min(a.AuthorizedBudget, Math.Round(a.AuthorizedBudget * (decimal)Math.Min(1, a.EffortDone / a.EffortRequired), 2));
            }

            if (a.EffortDone >= a.EffortRequired) { Complete(e, a, exec); continue; }

            if (s.CurrentDate > a.Due && !a.OverdueNotified)
            {
                a.Status = AssignmentStatus.Overdue;
                a.OverdueNotified = true;
                double remainingDays = Math.Ceiling((a.EffortRequired - a.EffortDone) / 0.8 * 7 / 5);
                e.Notify(MessageKind.Escalation, a.Assignee, $"Deadline missed: {a.Subject}",
                    $"{exec.Name} has not finished the {TaskTypesLabel(a.Type)} due {a.Due:MMM d}. " +
                    $"Estimated {remainingDays:0} more days. Workload is {(exec.Workload > 75 ? "very high" : "manageable")}.", a.Id);
                e.Remember("missed_deadline", $"{a.Assignee} missed the deadline on '{a.Subject}'.", new[] { a.Topic, "delegation" }, new[] { a.Assignee });
            }
        }
    }

    private static string TaskTypesLabel(TaskType t) => TaskTypes.Label(t);

    /// <summary>Work-days of effort an executive completes per workday. Shared by the engine and by the
    /// executive's own estimate, so "that timeline is workable" means what the simulation will do.</summary>
    public static double ExpectedDailyRate(GameState s, Executive exec, decimal budget, bool isRequest, double extraWorkload = 0)
    {
        double rate = (0.5 + exec.Competence / 100.0) * (1.25 - Math.Min(100, exec.Workload + extraWorkload) / 100.0);
        rate = Math.Clamp(rate, 0.3, 1.6);
        rate *= 1 + Math.Min(0.4, (double)budget / 150_000.0);
        if (!isRequest) rate *= ProjectEngine.ConsultingCapacity(s);
        return rate;
    }

    /// <summary>Calendar days an executive expects a task to take (mean effort, no luck).</summary>
    public static int EstimatedCalendarDays(GameState s, Executive exec, TaskType type, decimal budget, bool isRequest) =>
        (int)Math.Ceiling(TaskTypes.BaseEffort(type) * 1.05 / ExpectedDailyRate(s, exec, budget, isRequest, extraWorkload: isRequest ? 5 : 8) * 7 / 5.0);

    private static void Complete(SimulationEngine e, Assignment a, Executive exec)
    {
        var s = e.State;
        bool onTime = s.CurrentDate <= a.Due;
        a.Status = AssignmentStatus.Completed;
        a.CompletedOn = s.CurrentDate;
        exec.Workload = SimulationEngine.Clamp(exec.Workload - (a.IsRequest ? 5 : 8));

        double budgetBoost = Math.Min(0.1, (double)a.AuthorizedBudget / 500_000.0);
        double acc = 0.3 + exec.Competence / 100.0 * 0.55 + budgetBoost + e.Rng.NextDouble() * 0.15;
        if (s.Difficulty == Difficulty.Casual) acc += 0.1;
        if (s.Difficulty == Difficulty.Executive) acc -= 0.05;
        a.Accuracy = Math.Round(Math.Clamp(acc, 0.2, 0.98), 3);

        Findings.Generate(e, a, exec);
        string body = string.Join("\n• ", a.Findings.Prepend($"{exec.Name}, {exec.Title} — {TaskTypes.Label(a.Type)}: {a.Subject}"));
        if (a.AuthorizedBudget > 0) body += $"\n\nConsulting spend: {SimulationEngine.Money(a.Spent)} of {SimulationEngine.Money(a.AuthorizedBudget)} authorized.";
        body += onTime ? "" : $"\n\nDelivered {s.CurrentDate.DayNumber - a.Due.DayNumber} days after the agreed deadline.";
        var msg = e.Notify(MessageKind.Report, a.Assignee, $"Report: {a.Subject}", body, a.Id);
        a.ReportMessageId = msg.Id;
        // Unspent earmark returns to the discretionary pool.
        if (a.AuthorizedBudget > a.Spent && DelegateAssignmentHandler.ConsultingLine(s) is { } line)
            line.EarmarkedAnnual = Math.Max(0, line.EarmarkedAnnual - (a.AuthorizedBudget - a.Spent));

        if (a.DecisionId != null && s.Decisions.FirstOrDefault(d => d.Id == a.DecisionId) is { } dec)
        {
            dec.OutcomeScore = onTime ? 75 : 50;
            dec.OutcomeNotes.Add($"{TaskTypes.Label(a.Type)} delivered {(onTime ? "on time" : "late")}.");
        }
        e.Remember("report", $"{a.Assignee} reported on '{a.Subject}': {a.Findings.FirstOrDefault()}", new[] { a.Topic, "evidence", a.Type.ToString() }, new[] { a.Assignee }, 1.5);
        s.Player.Experience += 2;
    }
}

public static class Findings
{
    public static void Generate(SimulationEngine e, Assignment a, Executive exec)
    {
        var s = e.State;
        var t = s.Technology;
        double acc = a.Accuracy ?? 0.6;
        var f = a.Findings;
        switch (a.Type)
        {
            case TaskType.DrAssessment:
                DrAssessment(e, a, acc, f, full: true);
                break;
            case TaskType.BackupVerification:
                BackupVerification(e, acc, f);
                break;
            case TaskType.ProofOfConcept:
            {
                t.ProofOfConceptCompleted = true;
                bool correct = e.Rng.NextDouble() < 0.55 + acc * 0.45;
                bool reported = correct ? t.CloudDrVendorCertified : !t.CloudDrVendorCertified;
                t.CloudDrCertificationKnown = reported;
                f.Add(reported
                    ? "The test failover of a production-sized EHR copy into the cloud environment succeeded, and the vendor indicated it will support this configuration."
                    : "The vendor will not certify running the production EHR database in public-cloud infrastructure. Cloud-first DR would protect ancillary systems only.");
                f.Add("Cutover rehearsal identified interface-engine sequencing issues that have now been documented; this should lower migration risk.");
                break;
            }
            case TaskType.VendorNegotiation:
                VendorNegotiation(e, a, exec, acc, f);
                break;
            case TaskType.CostReview:
                CostReview(e, a, acc, f);
                break;
            case TaskType.ClinicalAssessment:
                if (a.Topic == Topics.LabInterface)
                    f.Add("Delayed lab results are a patient-safety concern for time-critical tests (troponin, potassium, blood gases). Units have been asked to phone critical results until the interface is stable.");
                else
                {
                    f.Add($"Clinical operations can sustain roughly {t.TargetRtoHours:0} hours of EHR downtime using paper downtime procedures. Beyond 8 hours, medication administration and lab result delays create material patient-safety risk.");
                    f.Add("Clinical leadership requests involvement in cutover planning and a rehearsed downtime drill before any migration go-live.");
                }
                break;
            default:
                Investigate(e, a, acc, f);
                break;
        }
        if (acc < 0.5) f.Add("(Analysis was limited in depth; confidence in these conclusions is moderate.)");
    }

    private static void DrAssessment(SimulationEngine e, Assignment a, double acc, List<string> f, bool full)
    {
        var t = e.State.Technology;
        // Low-accuracy assessments anchor on what the organization already believes (optimism bias).
        double measuredRto = Math.Round(t.ReportedRtoHours + (t.ActualRtoHours - t.ReportedRtoHours) * acc + e.Rng.NextGaussian(0, (1 - acc) * 6), 0);
        double measuredRpo = Math.Round(t.ReportedRpoHours + (t.ActualRpoHours - t.ReportedRpoHours) * acc, 1);
        measuredRto = Math.Max(2, measuredRto);
        t.ReportedRtoHours = measuredRto;
        t.ReportedRpoHours = Math.Max(0.5, measuredRpo);
        f.Add($"A partial failover test indicates full EHR recovery would take about {measuredRto:0} hours against a {t.TargetRtoHours:0}-hour target; potential data loss (RPO) is about {t.ReportedRpoHours:0.#} hours against a {t.TargetRpoHours:0}-hour target.");
        if (!t.HasSecondarySite) f.Add("There is no secondary site capable of running the EHR. Recovery depends on rebuilding infrastructure in the primary data center.");
        if (full)
        {
            f.Add("Options: a secondary on-premises data center (highest capital cost, longest timeline), cloud-first DR (lowest capital, highest recurring cost, depends on EHR vendor support), or hybrid colocation with a cloud backup tier (balanced).");
            if (t.CloudDrCertificationKnown == null) f.Add("Whether the EHR vendor supports cloud-hosted recovery is unconfirmed. A proof of concept is recommended before committing to cloud-first.");
        }
        var risk = e.State.Risks.FirstOrDefault(r => r.Topic == Topics.DisasterRecovery && !r.Closed);
        if (risk != null && measuredRto > 24) risk.Likelihood = Math.Max(risk.Likelihood, 4);
    }

    private static void BackupVerification(SimulationEngine e, double acc, List<string> f)
    {
        var s = e.State;
        var t = s.Technology;
        const int sets = 9;
        int failing = (int)Math.Round(sets * (1 - t.BackupRestoreSuccess));
        int detected = 0;
        for (int i = 0; i < failing; i++) if (e.Rng.NextDouble() < 0.35 + acc * 0.65) detected++;
        t.BackupsVerified = true;
        t.MeasuredBackupRestoreSuccess = Math.Round((sets - detected) / (double)sets, 2);
        if (detected > 0)
        {
            f.Add($"Test restores of all {sets} critical backup sets: {detected} failed to restore cleanly (including EHR document imaging). Backups are not reliable enough to meet recovery objectives.");
            f.Add("Backups are stored on the same network as production and are not immutable; ransomware could encrypt them.");
            var hidden = s.Risks.FirstOrDefault(r => r.Topic == Topics.Backup && !r.Closed);
            if (hidden != null && !hidden.Known)
            {
                hidden.Known = true;
                hidden.Identified = s.CurrentDate;
                e.Notify(MessageKind.Escalation, ExecRole.CISO, "New risk: backup restore failures", hidden.Description, hidden.Id);
            }
            t.ActualRpoHours = Math.Max(t.ActualRpoHours, 24);
        }
        else
        {
            f.Add($"Test restores of all {sets} critical backup sets completed without errors.");
            if (failing > 0) e.Remember("false_assurance", "Backup verification missed failing backup sets.", new[] { Topics.Backup }, Array.Empty<ExecRole>(), 0.1);
        }
    }

    private static void VendorNegotiation(SimulationEngine e, Assignment a, Executive exec, double acc, List<string> f)
    {
        var s = e.State;
        var c = s.Contracts.FirstOrDefault(x => !x.Renewed);
        if (c == null) { f.Add("There is no open contract renewal to negotiate."); return; }
        double leverage = acc * 0.09 + (s.Technology.CloudDrCertificationKnown == true ? 0.015 : 0) + e.Rng.NextGaussian(0, 0.01);
        double agreed = Math.Round(Math.Clamp(c.ProposedUpliftPct - leverage, -0.04, c.ProposedUpliftPct), 3);
        c.AgreedUpliftPct = agreed;
        c.Negotiated = true;
        decimal delta = Math.Round(c.AnnualCost * (decimal)agreed, 0);
        decimal avoided = Math.Round(c.AnnualCost * (decimal)(c.ProposedUpliftPct - agreed), 0);
        f.Add($"{c.Vendor} has agreed to a renewal price change of {agreed:+0.0%;-0.0%} instead of the proposed {c.ProposedUpliftPct:+0.0%} ({SimulationEngine.Money(delta)}/yr vs {SimulationEngine.Money(c.AnnualCost * (decimal)c.ProposedUpliftPct)}/yr). Cost avoidance: {SimulationEngine.Money(avoided)}/yr, effective at renewal on {c.RenewalDate:MMM d, yyyy}.");
        string subj = a.Subject.ToLowerInvariant();
        if (subj.Contains("dr") || subj.Contains("disaster") || subj.Contains("sla") || subj.Contains("protect") || subj.Contains("recovery") || acc > 0.75)
        {
            c.HasDrSupportClause = true;
            f.Add("The renewal now includes a contractual DR support clause: vendor engineers on site within 4 hours of a declared disaster, with service credits if missed.");
        }
        if (avoided > 0)
            s.Savings.Add(new SavingsInitiative
            {
                Id = s.NewId("SAV"), DepartmentId = s.Player.DepartmentId, BudgetLineId = c.BudgetLineId, AnnualAmount = avoided,
                Recurring = true, Status = SavingsStatus.Proposed, EffectiveDate = c.RenewalDate, Reason = "Cost avoidance vs proposed renewal uplift",
                Source = "negotiation", DecisionId = a.DecisionId,
            });
    }

    private static void CostReview(SimulationEngine e, Assignment a, double acc, List<string> f)
    {
        var s = e.State;
        if (a.Topic == Topics.Identity) { IdentityAlternative(e, acc, f); return; }
        var dept = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        var lines = dept.BudgetLines.Where(l => l.DiscretionaryRemaining > 20_000).OrderByDescending(l => l.DiscretionaryRemaining).Take(3).ToList();
        if (lines.Count == 0) { f.Add("There is no meaningful discretionary spend left to reduce without cutting committed contracts or staff."); return; }
        foreach (var l in lines)
        {
            decimal safe = Math.Round(l.DiscretionaryRemaining * (decimal)(0.15 + acc * 0.25) / 1000m, 0) * 1000m;
            string note = l.Id switch
            {
                "consulting" => "reducing contractor use will slow assessment and project delivery",
                "training" => "cuts here will hurt morale and retention in a tight labor market",
                "hardware_maint" => "lower maintenance coverage increases hardware failure exposure on aging equipment",
                "travel" => "minimal operational impact",
                "cloud" => "requires rightsizing idle workloads; low risk",
                _ => "moderate operational impact",
            };
            f.Add($"{l.Name}: about {SimulationEngine.Money(safe)}/yr could be saved ({note}).");
        }
        f.Add("These are proposed savings only. Nothing changes until you approve a specific reduction.");
    }

    private static void IdentityAlternative(SimulationEngine e, double acc, List<string> f)
    {
        var s = e.State;
        var original = s.Proposals.FirstOrDefault(p => p.EffectKey == "identity_expansion");
        bool found = e.Rng.NextDouble() < 0.4 + acc * 0.6;
        if (found && original != null && s.Proposals.All(p => p.EffectKey != "identity_optimization"))
        {
            s.Proposals.Add(new Proposal
            {
                Id = s.NewId("PRP"), Title = "Identity platform optimization", Topic = Topics.Identity, Sponsor = ExecRole.EADirector,
                Summary = "Decommission stale accounts, consolidate domain controllers and tune authentication policies instead of buying new capacity.",
                Keywords = { "identity", "optimization", "optimize", "active directory", "alternative" },
                Capex = 40_000, AnnualOpex = 6_000, DurationDays = 30, EffectKey = "identity_optimization",
                SecurityRiskReduction = 4, TechDebtReduction = 2, SupersedesId = original.Id,
                RequiredEvidence = { "investigate" },
            });
            f.Add($"Authentication load is inflated by roughly 30% from stale service accounts and misconfigured replication. Cleanup and consolidation (about {SimulationEngine.Money(40_000)}) would deliver the same capacity as the {SimulationEngine.Money(original.Capex)} expansion.");
            f.Add("A new proposal, 'Identity platform optimization', has been added for your decision.");
        }
        else
            f.Add("Current identity infrastructure is near capacity; the proposed expansion appears justified, although a vendor-neutral sizing review could refine the cost.");
    }

    private static void Investigate(SimulationEngine e, Assignment a, double acc, List<string> f)
    {
        switch (a.Topic)
        {
            case Topics.Identity: IdentityAlternative(e, acc, f); break;
            case Topics.DisasterRecovery: DrAssessment(e, a, acc * 0.85, f, full: false); break;
            case Topics.Backup: BackupVerification(e, acc * 0.8, f); break;
            case Topics.Budget: CostReview(e, a, acc * 0.9, f); break;
            case Topics.EhrContract:
                var c = e.State.Contracts.FirstOrDefault(x => !x.Renewed);
                f.Add(c == null ? "No open contract issues found."
                    : $"{c.Vendor} renewal is due {c.RenewalDate:MMM d, yyyy} at {SimulationEngine.Money(c.AnnualCost)}/yr with a proposed {c.ProposedUpliftPct:+0%} uplift. The contract {(c.HasDrSupportClause ? "includes" : "does not include")} DR support obligations. Negotiation is advisable.");
                break;
            default:
                var known = e.State.Risks.Where(r => r.Known && !r.Closed).OrderByDescending(r => r.Score).Take(2).Select(r => r.Title);
                f.Add($"No specific issue identified. The most significant open risks remain: {string.Join("; ", known)}.");
                break;
        }
    }
}
