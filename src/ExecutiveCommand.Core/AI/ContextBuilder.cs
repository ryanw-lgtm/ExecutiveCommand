using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

/// <summary>
/// Builds what an executive knows. Executives only know *reported* values (never hidden ground truth),
/// plus structured institutional memories retrieved by topic relevance, involvement, weight and recency.
/// </summary>
public static class ContextBuilder
{
    private static string M(decimal d) => SimulationEngine.Money(d);

    public static List<MemoryRecord> RetrieveMemories(GameState s, ExecRole role, string topic, int k = 6)
    {
        return s.Memory
            .Select(m => (m, score: (m.Tags.Contains(topic) ? 3.0 : 0) + (m.Involved.Contains(role) ? 1.5 : 0) + m.Weight
                                    - (s.CurrentDate.DayNumber - m.Date.DayNumber) / 120.0 - (m.Kind == "consultation" ? 2 : 0)))
            .Where(x => x.score > 0.5)
            .OrderByDescending(x => x.score).ThenByDescending(x => x.m.Date)
            .Take(k).Select(x => x.m).OrderBy(m => m.Date).ToList();
    }

    public static List<string> Facts(GameState s, Executive x, string topic)
    {
        var t = s.Technology;
        var f = new List<string>();
        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        switch (topic)
        {
            case Topics.DisasterRecovery:
                f.Add($"Documented EHR recovery time: {t.ReportedRtoHours:0} hours; target {t.TargetRtoHours:0} hours");
                f.Add($"Documented data-loss window (RPO): {t.ReportedRpoHours:0.#} hours; target {t.TargetRpoHours:0} hour");
                f.Add(t.HasSecondarySite ? "A secondary recovery site is in place" : "There is no secondary site that can run the EHR");
                foreach (var p in s.Proposals.Where(p => p.EffectKey.StartsWith("dr_") && p.Status is ProposalStatus.Pending or ProposalStatus.Deferred or ProposalStatus.AwaitingApproval))
                    f.Add($"{p.Title}: {M(p.Capex)} capital, {M(p.AnnualOpex)} per year, about {p.DurationDays / 30} months");
                if (t.CloudDrCertificationKnown is bool known) f.Add(known ? "Proof of concept: the vendor supports cloud-hosted EHR recovery" : "Proof of concept: the vendor will not support cloud-hosted EHR recovery");
                foreach (var pr in s.Projects.Where(p => p.EffectKey.StartsWith("dr_")))
                    f.Add($"Project {pr.Name}: {pr.Status}, {pr.Progress:P0} complete");
                break;
            case Topics.Backup:
                f.Add(t.BackupsVerified ? $"Last backup restore test: {t.MeasuredBackupRestoreSuccess:P0} of critical sets restored" : "Backups have not been restore-tested in over a year");
                f.Add(t.ImmutableBackups ? "Backups are immutable" : "Backups are not immutable and share the production network");
                break;
            case Topics.Identity:
                foreach (var p in s.Proposals.Where(p => p.Topic == Topics.Identity && p.Status is ProposalStatus.Pending or ProposalStatus.Deferred))
                    f.Add($"{p.Title}: {M(p.Capex)} capital, {M(p.AnnualOpex)} per year");
                break;
            case Topics.EhrContract:
                foreach (var c in s.Contracts)
                    f.Add(c.Renewed ? $"{c.Vendor} contract renewed; now {M(c.AnnualCost)} per year"
                        : $"{c.Vendor} contract: {M(c.AnnualCost)} per year, renews {c.RenewalDate:MMMM d}, vendor proposes {c.ProposedUpliftPct:+0%}{(c.Negotiated ? $", negotiated to {c.AgreedUpliftPct:+0.0%;-0.0%}" : ", not yet negotiated")}");
                break;
            case Topics.Budget:
                f.Add($"CFO target: {M(PerformanceEngine.SavingsTarget)} of recurring technology savings this fiscal year");
                decimal approved = s.Savings.Where(v => v.Source == "budget_cut" && v.Status != SavingsStatus.Reversed).Sum(v => v.AnnualAmount);
                decimal realized = s.Savings.Where(v => v.Source == "budget_cut").Sum(v => v.RealizedToDate);
                f.Add($"Approved budget reductions so far: {M(approved)} per year; realized to date: {M(realized)}");
                if (x.Role is ExecRole.CFO or ExecRole.CIO or ExecRole.EADirector)
                    foreach (var l in tech.BudgetLines.Where(l => l.DiscretionaryRemaining > 0))
                        f.Add($"{l.Name}: {M(l.DiscretionaryRemaining)} discretionary remaining of {M(l.AnnualBudget)}");
                f.Add($"Technology capital remaining this fiscal year: {M(s.Finance.TechCapitalBudgetRemaining)}");
                break;
            case Topics.Security:
                f.Add($"Security posture assessment: {(t.SecurityPosture >= 70 ? "good" : t.SecurityPosture >= 55 ? "fair" : "weak")}");
                break;
            case Topics.LabInterface:
                foreach (var i in s.Incidents.Where(i => i.Kind == IncidentEngine.CutoverKind && i.Status == IncidentStatus.Active))
                    f.Add($"{i.MessageDelayPct:0}% of lab result messages delayed; day {i.DaysActive + 1}; {i.SafetyEvents} safety events{(i.ClinicalHold ? "; CMO clinical safety hold in effect" : "")}");
                break;
        }
        foreach (var r in s.Risks.Where(r => r.Known && !r.Closed && (r.Topic == topic || r.Owner == x.Role)).OrderByDescending(r => r.Score).Take(2))
            f.Add($"Open risk: {r.Title} (score {r.Score} of 25)");
        if (s.Finance.Statements.Count > 0)
        {
            var last = s.Finance.Statements[^1];
            if (x.Role is ExecRole.CFO or ExecRole.CEO) f.Add($"Last month operating margin: {last.OperatingMargin:P1}; cash {M(last.ClosingCash)}");
        }
        return f;
    }

    public static string RoleBrief(GameState s, Executive x)
    {
        string authority = x.ReportsTo == s.Player.Role ? $"You report to the player ({s.Player.Title})."
            : x.Role == ExecRole.Vendor ? "You are an external vendor representative; you have no authority inside the organization."
            : x.Role == ExecRole.CEO ? "The player reports to you."
            : $"You are the player's peer; you report to the {Roles.Label(x.ReportsTo)}.";
        return $"You are {x.Name}, {x.Title} at {s.Company.Name}. {x.Background} " +
               $"Responsibilities: {string.Join(", ", x.Responsibilities)}. Priorities: {string.Join(", ", x.Priorities)}. " +
               (x.Biases.Count > 0 ? $"Tendencies: {string.Join(", ", x.Biases)}. " : "") + authority +
               (x.HasClinicalSafetyAuthority ? " You hold binding authority over clinical safety decisions." : "") +
               $" Your trust in the player is {(x.TrustInPlayer >= 65 ? "high" : x.TrustInPlayer >= 45 ? "moderate" : "low")}.";
    }
}
