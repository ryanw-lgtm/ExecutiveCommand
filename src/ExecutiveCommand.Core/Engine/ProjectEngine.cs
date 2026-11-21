using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

public static class ProjectEngine
{
    public static bool IsWorkday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    public static Project StartProject(SimulationEngine e, Proposal p, string? decisionId)
    {
        var s = e.State;
        p.Status = ProposalStatus.Approved;
        if (p.EffectKey.StartsWith("dr_"))
            foreach (var alt in s.Proposals.Where(x => x != p && x.EffectKey.StartsWith("dr_") && x.Status is ProposalStatus.Pending or ProposalStatus.Deferred))
                alt.Status = ProposalStatus.Superseded;
        if (p.SupersedesId != null && s.Proposals.FirstOrDefault(x => x.Id == p.SupersedesId) is { } old && old.Status is ProposalStatus.Pending or ProposalStatus.Deferred)
            old.Status = ProposalStatus.Superseded;
        foreach (var alt in s.Proposals.Where(x => x.SupersedesId == p.Id && x.Status is ProposalStatus.Pending or ProposalStatus.Deferred))
            alt.Status = ProposalStatus.Superseded;

        s.Finance.TechCapitalBudgetRemaining -= p.Capex;
        var owner = s.TryExec(p.Sponsor)?.ReportsTo == s.Player.Role ? p.Sponsor : ExecRole.CIO;
        var proj = new Project
        {
            Id = s.NewId("PRJ"), ProposalId = p.Id, Name = p.Title, Owner = owner, BudgetCapex = p.Capex,
            AnnualOpex = p.AnnualOpex, StartDate = s.CurrentDate, PlannedEndDate = s.CurrentDate.AddDays(p.DurationDays),
            EffectKey = p.EffectKey, DecisionId = decisionId, ExpectedBenefits = p.Summary,
            Milestones =
            {
                new Milestone { Name = "Design approved", AtProgress = 0.25 },
                new Milestone { Name = "Build complete", AtProgress = 0.5 },
                new Milestone { Name = "Testing & cutover", AtProgress = 0.8 },
                new Milestone { Name = "Go-live", AtProgress = 1.0 },
            },
        };
        s.Projects.Add(proj);
        if (p.Capex > 0)
            s.Finance.Assets.Add(new CapitalAsset { Id = s.NewId("AST"), Name = p.Title, Cost = 0, InService = false, ProjectId = proj.Id, UsefulLifeMonths = 60 });
        var ownerExec = s.Exec(owner);
        ownerExec.Workload = SimulationEngine.Clamp(ownerExec.Workload + 12);
        e.Remember("project_start", $"Project '{p.Title}' approved ({SimulationEngine.Money(p.Capex)} capex, {SimulationEngine.Money(p.AnnualOpex)}/yr opex).",
            new[] { p.Topic, "project" }, new[] { owner, ExecRole.CFO });
        return proj;
    }

    /// <summary>Fraction of discretionary consulting budget still available (cuts slow delivery).</summary>
    public static double ConsultingCapacity(GameState s)
    {
        var line = DelegateAssignmentHandler.ConsultingLine(s);
        if (line == null) return 1;
        decimal pool = line.AnnualBudget - line.CommittedAnnual;
        if (pool <= 0) return 1;
        double cutRatio = (double)(line.ApprovedReductionsAnnual / pool);
        return Math.Max(0.6, 1 - 0.4 * cutRatio);
    }

    public static void DailyTick(SimulationEngine e)
    {
        var s = e.State;
        if (!IsWorkday(s.CurrentDate)) return;
        foreach (var proj in s.Projects.Where(p => p.Status is ProjectStatus.Active or ProjectStatus.OnHold).ToList())
        {
            if (proj.HeldBy != null) { proj.DaysOnHold++; proj.Status = ProjectStatus.OnHold; continue; }
            proj.Status = ProjectStatus.Active;
            var owner = s.Exec(proj.Owner);
            double workdays = Math.Max(10, (proj.PlannedEndDate.DayNumber - proj.StartDate.DayNumber) * 5.0 / 7.0);
            double capability = (0.55 + owner.Competence / 100.0 * 0.6) * (1.2 - owner.Workload / 250.0) * ConsultingCapacity(s);
            double noise = 0.85 + e.Rng.NextDouble() * 0.3;
            double before = proj.Progress;
            proj.Progress = Math.Min(1.0, proj.Progress + capability * noise / workdays);

            // Capital spend follows progress; overruns arise from slow delivery (more labor) and rework.
            decimal target = proj.BudgetCapex * (decimal)proj.Progress * (decimal)(1 + Math.Max(0, proj.DaysOnHold) * 0.002);
            decimal delta = Math.Round(target - proj.SpentCapex, 2);
            if (delta > 0) BookCapex(e, proj, delta);

            foreach (var m in proj.Milestones.Where(m => m.ReachedOn == null && proj.Progress >= m.AtProgress && m.AtProgress < 1.0))
            {
                m.ReachedOn = s.CurrentDate;
                e.Notify(MessageKind.Report, proj.Owner, $"{proj.Name}: {m.Name}", $"Milestone '{m.Name}' reached. Progress {proj.Progress:P0}; capital spent {SimulationEngine.Money(proj.SpentCapex)} of {SimulationEngine.Money(proj.BudgetCapex)}.", proj.Id);
            }

            if (proj.EffectKey.StartsWith("dr_") && !proj.CutoverIncidentTriggered && before < 0.8 && proj.Progress >= 0.8)
            {
                proj.CutoverIncidentTriggered = true;
                if (IncidentEngine.MaybeStartCutoverIncident(e, proj)) continue;
            }
            if (proj.Progress >= 1.0) Complete(e, proj);
        }
    }

    internal static void BookCapex(SimulationEngine e, Project proj, decimal amount)
    {
        var s = e.State;
        proj.SpentCapex += amount; // posted to the asset subledger and general ledger at month-end close
        s.Finance.Ledger.Add(new LedgerEntry
        {
            Id = s.NewId("LED"), Date = s.CurrentDate, Category = LedgerCategory.Capex, Amount = amount,
            Account = "Capital projects", DepartmentId = s.Player.DepartmentId, SourceId = proj.Id, Memo = proj.Name,
        });
    }

    private static void Complete(SimulationEngine e, Project proj)
    {
        var s = e.State;
        proj.Status = ProjectStatus.Completed;
        proj.ActualEndDate = s.CurrentDate;
        var asset = s.Finance.Assets.FirstOrDefault(a => a.ProjectId == proj.Id);
        if (asset != null) { asset.InService = true; asset.InServiceDate = s.CurrentDate; }
        var owner = s.Exec(proj.Owner);
        owner.Workload = SimulationEngine.Clamp(owner.Workload - 12);

        string effect = ProjectEffects.Apply(e, proj);
        int lateDays = s.CurrentDate.DayNumber - proj.PlannedEndDate.DayNumber;
        decimal overrun = proj.SpentCapex - proj.BudgetCapex;
        proj.ActualOutcome = effect;

        double outcome = 70;
        if (lateDays > 14) outcome -= Math.Min(25, lateDays / 3.0);
        if (overrun > 0 && proj.BudgetCapex > 0) outcome -= Math.Min(20, (double)(overrun / proj.BudgetCapex) * 200);
        if (effect.Contains("FAILED", StringComparison.Ordinal)) outcome = 20;
        else outcome += 15;
        outcome = SimulationEngine.Clamp(outcome);
        if (proj.DecisionId != null && s.Decisions.FirstOrDefault(d => d.Id == proj.DecisionId) is { } dec)
        {
            dec.OutcomeScore = outcome;
            dec.OutcomeNotes.Add($"Project completed {(lateDays > 0 ? $"{lateDays} days late" : "on schedule")}, capex variance {SimulationEngine.Money(overrun)}. {effect}");
        }
        e.Notify(MessageKind.Report, proj.Owner, $"Post-implementation review: {proj.Name}",
            $"{proj.Name} went live {(lateDays > 0 ? $"{lateDays} days behind plan" : "on schedule")}. " +
            $"Capital spent {SimulationEngine.Money(proj.SpentCapex)} against a budget of {SimulationEngine.Money(proj.BudgetCapex)}. " +
            $"Recurring operating cost of {SimulationEngine.Money(proj.AnnualOpex)}/yr now begins. {effect}", proj.Id);
        e.Remember("project_complete", $"'{proj.Name}' completed: {effect}", new[] { s.Proposals.FirstOrDefault(p => p.Id == proj.ProposalId)?.Topic ?? Topics.General, "project" }, new[] { proj.Owner });
        s.Player.Experience += 10;
    }
}

/// <summary>What a completed project actually changes in the simulated organization.</summary>
public static class ProjectEffects
{
    public static string Apply(SimulationEngine e, Project proj)
    {
        var s = e.State;
        var t = s.Technology;
        var proposal = s.Proposals.FirstOrDefault(p => p.Id == proj.ProposalId);
        double quality = s.Exec(proj.Owner).Competence / 100.0;
        if (proposal != null)
        {
            t.SecurityPosture = SimulationEngine.Clamp(t.SecurityPosture + proposal.SecurityRiskReduction * (0.6 + 0.4 * quality));
            t.TechnicalDebt = SimulationEngine.Clamp(t.TechnicalDebt - proposal.TechDebtReduction);
        }
        switch (proj.EffectKey)
        {
            case "dr_hybrid":
                return SetDr(e, 6 + (1 - quality) * 4, 1, "Hybrid recovery");
            case "dr_onprem":
                return SetDr(e, 8 + (1 - quality) * 4, 2, "Secondary data center");
            case "dr_cloud":
                if (!t.CloudDrVendorCertified)
                {
                    t.ActualRtoHours = Math.Min(t.ActualRtoHours, 30);
                    t.ReportedRtoHours = t.ActualRtoHours;
                    t.CloudDrCertificationKnown = false;
                    return $"FAILED to meet objective: the EHR vendor will not support production EHR recovery in the public cloud. Only ancillary systems are protected; EHR recovery time remains ~{t.ActualRtoHours:0} hours.";
                }
                return SetDr(e, 4 + (1 - quality) * 3, 0.5, "Cloud disaster recovery");
            case "immutable_backup":
                t.BackupRestoreSuccess = 0.99;
                t.ImmutableBackups = true;
                t.BackupsVerified = true;
                t.MeasuredBackupRestoreSuccess = 0.99;
                t.ActualRpoHours = Math.Min(t.ActualRpoHours, 4);
                t.ReportedRpoHours = t.ActualRpoHours;
                CloseRisk(s, "backup");
                ReduceRisk(s, Topics.Security, 1);
                return "Restore tests now succeed for 99% of backup sets, and backups are immutable against ransomware encryption.";
            case "identity_expansion":
            case "identity_optimization":
                ReduceRisk(s, Topics.Security, 1);
                return "Identity infrastructure capacity and resilience improved.";
            default:
                return "Project objectives delivered.";
        }
    }

    private static string SetDr(SimulationEngine e, double rto, double rpo, string label)
    {
        var t = e.State.Technology;
        t.ActualRtoHours = Math.Round(rto, 1);
        t.ActualRpoHours = Math.Min(t.ActualRpoHours, rpo);
        t.HasSecondarySite = true;
        t.ReportedRtoHours = t.ActualRtoHours; // validated by the go-live failover test
        t.ReportedRpoHours = t.ActualRpoHours;
        bool met = t.ActualRtoHours <= t.TargetRtoHours + 0.01;
        if (met) CloseRisk(e.State, Topics.DisasterRecovery); else ReduceRisk(e.State, Topics.DisasterRecovery, 2);
        return $"{label} failover test: EHR recovered in {t.ActualRtoHours:0.#} hours (target {t.TargetRtoHours:0} h){(met ? "" : " — improved, but still above target")}.";
    }

    internal static void CloseRisk(GameState s, string topic)
    {
        foreach (var r in s.Risks.Where(r => r.Topic == topic && !r.Closed)) { r.Closed = true; r.Known = true; }
    }

    internal static void ReduceRisk(GameState s, string topic, int by)
    {
        foreach (var r in s.Risks.Where(r => r.Topic == topic && !r.Closed)) r.Likelihood = Math.Max(1, r.Likelihood - by);
    }
}
