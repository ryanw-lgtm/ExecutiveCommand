using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Executives;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// Scheduled routine business plus risk-driven incidents. Incident probability comes from actual
/// exposure (hardware age, redundancy, security posture, maintenance cuts) — not a fixed drama quota —
/// and major incidents are spaced out so the game keeps a believable operating rhythm.
/// </summary>
public static class EventEngine
{
    public static void ProcessScheduled(SimulationEngine e)
    {
        var s = e.State;
        foreach (var ev in s.Schedule.Where(x => !x.Fired && x.Date <= s.CurrentDate).ToList())
        {
            ev.Fired = true;
            ScheduledContent.Fire(e, ev);
        }

        foreach (var p in s.Proposals.Where(p => p.Status == ProposalStatus.Deferred && p.DeferredUntil <= s.CurrentDate))
        {
            p.Status = ProposalStatus.Pending;
            p.DeferredUntil = null;
            e.Notify(MessageKind.Briefing, p.Sponsor, $"Back on the agenda: {p.Title}", $"The deferral period has ended. '{p.Title}' needs a decision.", p.Id);
        }

        foreach (var p in s.Proposals.Where(p => p.Status == ProposalStatus.AwaitingApproval && p.ApprovalDueDate <= s.CurrentDate).ToList())
            ExecutiveApproval.Decide(e, p);
    }

    public static void DailyHazards(SimulationEngine e)
    {
        var s = e.State;
        var t = s.Technology;
        if (s.Incidents.Any(i => i.Status == IncidentStatus.Active)) return;
        if (t.LastMajorIncident is { } last && s.CurrentDate.DayNumber - last.DayNumber < 45) return;

        var storage = t.Systems.FirstOrDefault(x => x.Category == "storage");
        double age = storage?.AgeYears ?? 5;
        double maintCut = MaintenanceCutRatio(s);
        double pOutage = 0.0011 * Math.Pow(age / 6.0, 1.5) * (1 + maintCut * 1.5) * (storage?.Redundant == true ? 0.4 : 1) * e.DifficultyHazardMultiplier;
        double pRansom = 0.00022 * Math.Pow((100 - t.SecurityPosture) / 45.0, 2) * e.DifficultyHazardMultiplier;

        double roll = e.Rng.NextDouble();
        if (roll < pOutage) InfrastructureOutage(e);
        else if (roll < pOutage + pRansom) Ransomware(e);
    }

    internal static double MaintenanceCutRatio(GameState s)
    {
        var line = s.Departments.SelectMany(d => d.BudgetLines).FirstOrDefault(l => l.Id == "hardware_maint");
        if (line == null) return 0;
        decimal pool = line.AnnualBudget - line.CommittedAnnual;
        return pool <= 0 ? 0 : (double)(line.ApprovedReductionsAnnual / pool);
    }

    private static decimal RevenuePerDowntimeHour(GameState s) => Math.Round(s.Finance.MonthlyRevenueBase / 720m * 0.35m, 0);

    private static void InfrastructureOutage(SimulationEngine e)
    {
        var s = e.State;
        var t = s.Technology;
        double downtime = t.HasSecondarySite
            ? t.ActualRtoHours * (0.5 + e.Rng.NextDouble() * 0.5)
            : 6 + e.Rng.NextDouble() * Math.Max(1, t.ActualRtoHours - 6);
        double dataLoss = t.ActualRpoHours * e.Rng.NextDouble();
        bool backupFailed = e.Rng.NextDouble() > t.BackupRestoreSuccess;
        if (backupFailed && !t.HasSecondarySite) { downtime *= 1.4; dataLoss = Math.Max(dataLoss, 24); }
        RecordMajorIncident(e, "infrastructure_outage", "Primary storage array failure",
            $"The primary storage array serving the EHR failed. {(t.HasSecondarySite ? "Systems failed over to the secondary site." : "With no secondary site, systems were rebuilt in the primary data center.")}" +
            (backupFailed ? " One backup set failed to restore, extending recovery." : ""),
            downtime, dataLoss, 60_000m);
    }

    private static void Ransomware(SimulationEngine e)
    {
        var s = e.State;
        var t = s.Technology;
        bool backupsUsable = t.ImmutableBackups || e.Rng.NextDouble() < t.BackupRestoreSuccess * 0.5;
        double downtime = backupsUsable ? 24 + e.Rng.NextDouble() * 24 : 96 + e.Rng.NextDouble() * 144;
        if (t.HasSecondarySite && backupsUsable) downtime *= 0.7;
        double dataLoss = backupsUsable ? Math.Max(t.ActualRpoHours, 4) : 72;
        RecordMajorIncident(e, "ransomware", "Ransomware attack",
            $"Ransomware encrypted clinical and business systems. {(backupsUsable ? "Clean backups allowed restoration." : "Backups were also affected; recovery required rebuilding systems from scratch.")}",
            downtime, dataLoss, backupsUsable ? 650_000m : 2_400_000m);
        t.SecurityPosture = SimulationEngine.Clamp(t.SecurityPosture + 8); // post-incident hardening
    }

    private static void RecordMajorIncident(SimulationEngine e, string kind, string title, string description, double downtime, double dataLoss, decimal baseCost)
    {
        var s = e.State;
        downtime = Math.Round(downtime, 1);
        decimal revenue = RevenuePerDowntimeHour(s) * (decimal)downtime;
        decimal recovery = baseCost + 8_000m * (decimal)downtime;
        int safety = (int)Math.Floor(downtime / 12.0 * (0.6 + e.Rng.NextDouble() * 0.8));
        var inc = new Incident
        {
            Id = s.NewId("INC"), Kind = kind, Title = title, Description = description, Started = s.CurrentDate, Resolved = s.CurrentDate,
            Status = IncidentStatus.Resolved, DowntimeHours = downtime, DataLossHours = Math.Round(dataLoss, 1),
            RevenueImpact = revenue, RecoveryCost = recovery, SafetyEvents = safety,
        };
        s.Incidents.Add(inc);
        s.Technology.LastMajorIncident = s.CurrentDate;
        s.Finance.PendingRevenueLoss += revenue;
        s.Finance.PendingIncidentCost += recovery;
        s.Metrics.PatientSafetyEvents += safety;
        s.Metrics.ClinicalSafetyIndex = SimulationEngine.Clamp(s.Metrics.ClinicalSafetyIndex - downtime / 3.0 - safety * 2);
        double boardHit = Math.Min(18, downtime / 4.0 + safety * 1.5);
        bool warned = s.Technology.ReportedRtoHours > s.Technology.TargetRtoHours * 2 && s.Decisions.Any(d => d.Topic == Topics.DisasterRecovery && d.Action is "reject_proposal" or "defer_proposal");
        if (warned) boardHit *= 1.4; // known risk the technology leader chose not to address
        s.Metrics.BoardConfidence = SimulationEngine.Clamp(s.Metrics.BoardConfidence - boardHit);
        s.Metrics.EmployeeMorale = SimulationEngine.Clamp(s.Metrics.EmployeeMorale - downtime / 10.0);
        e.Notify(MessageKind.Incident, ExecRole.CIO, $"MAJOR INCIDENT: {title}",
            $"{description}\n\nEHR downtime: {downtime:0.#} hours. Data loss window: {inc.DataLossHours:0.#} hours. " +
            $"Estimated lost revenue {SimulationEngine.Money(revenue)}, recovery cost {SimulationEngine.Money(recovery)}. " +
            $"Patient safety events reported: {safety}.", inc.Id);
        e.Remember("incident", $"{title}: {downtime:0} h EHR downtime, {safety} safety events.", new[] { Topics.DisasterRecovery, Topics.Security, "incident" },
            new[] { ExecRole.CEO, ExecRole.CIO, ExecRole.CMO, ExecRole.CISO }, 3);
        foreach (var d in s.Decisions.Where(d => d.Topic is Topics.DisasterRecovery or Topics.Backup && d.Action is "reject_proposal" or "defer_proposal"))
            d.OutcomeNotes.Add($"{s.CurrentDate:yyyy-MM-dd}: {title} occurred while this risk remained unaddressed.");
    }

    public static void ProcessActiveIncidents(SimulationEngine e) => IncidentEngine.DailyTick(e);
}

/// <summary>Executive approval of proposals above the player's authority, decided on the merits.</summary>
public static class ExecutiveApproval
{
    public static void Decide(SimulationEngine e, Proposal p)
    {
        var s = e.State;
        var cfo = StanceEngine.OnProposal(s, s.Exec(ExecRole.CFO), p);
        var ceo = StanceEngine.OnProposal(s, s.Exec(ExecRole.CEO), p);
        double combined = (cfo.Score + ceo.Score) / 2 + e.Rng.NextGaussian(0, 0.05);
        bool approved = combined > 0.05 && cfo.Score > -0.5 && ceo.Score > -0.5;
        var decision = s.Decisions.FirstOrDefault(d => d.Id == p.ApprovalDecisionDue);
        if (approved)
        {
            bool exception = p.Capex > s.Finance.TechCapitalBudgetRemaining;
            var proj = ProjectEngine.StartProject(e, p, p.ApprovalDecisionDue);
            e.Notify(MessageKind.Report, ExecRole.CFO, $"Approved: {p.Title}",
                $"The CFO and CEO approved '{p.Title}'. {string.Join(" ", cfo.Reasons.Take(1))} " +
                (exception ? "This required a capital budget exception funded from organizational reserves. " : "") +
                $"Project {proj.Id} is under way.", p.Id);
            decision?.OutcomeNotes.Add("Approved by CFO and CEO.");
            e.AdjustTrust(ExecRole.CFO, 1);
        }
        else
        {
            p.Status = ProposalStatus.Rejected;
            var lead = cfo.Score < ceo.Score ? cfo : ceo;
            e.Notify(MessageKind.Report, lead.Role, $"Not approved: {p.Title}",
                $"'{p.Title}' was not approved. {string.Join(" ", lead.Reasons.Take(2))}" +
                (lead.Conditions.Count > 0 ? $" To reconsider: {string.Join("; ", lead.Conditions)}." : ""), p.Id);
            if (decision != null) { decision.OutcomeScore = 30; decision.OutcomeNotes.Add("Rejected by executive approvers."); }
            e.Remember("approval_denied", $"CFO/CEO declined '{p.Title}'.", new[] { p.Topic, "approval" }, new[] { ExecRole.CFO, ExecRole.CEO });
            // A rejected option can be reworked: return a revised copy later rather than losing the path.
            s.Proposals.Add(new Proposal
            {
                Id = s.NewId("PRP"), Title = p.Title + " (revised)", Summary = p.Summary, Keywords = p.Keywords.ToList(), Sponsor = p.Sponsor,
                Capex = p.Capex, AnnualOpex = p.AnnualOpex, DurationDays = p.DurationDays, Status = ProposalStatus.Deferred,
                DeferredUntil = s.CurrentDate.AddDays(30), Topic = p.Topic, EffectKey = p.EffectKey,
                SecurityRiskReduction = p.SecurityRiskReduction, ClinicalRiskReduction = p.ClinicalRiskReduction,
                TechDebtReduction = p.TechDebtReduction, ClinicallySignificant = p.ClinicallySignificant,
                RequiredEvidence = p.RequiredEvidence.ToList(),
            });
        }
    }
}
