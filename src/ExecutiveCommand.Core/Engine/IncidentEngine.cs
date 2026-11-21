using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// Decision-bearing incidents. The flagship case is the lab-interface degradation during DR/migration
/// cutover. The right answer is not scripted: it depends on a hidden root cause, executive capability,
/// clinical mitigations and elapsed time. The CMO holds binding clinical-safety authority.
/// </summary>
public static class IncidentEngine
{
    public const string CutoverKind = "cutover_lab_interface";

    public static bool MaybeStartCutoverIncident(SimulationEngine e, Project proj)
    {
        var s = e.State;
        double p = s.Technology.ProofOfConceptCompleted ? 0.3 : 0.65;
        if (s.Assignments.Any(a => a.Type == TaskType.ClinicalAssessment && a.Status == AssignmentStatus.Completed && a.Topic == Topics.DisasterRecovery)) p -= 0.1;
        if (!e.Rng.Chance(p)) return false;

        double r = e.Rng.NextDouble();
        string cause = r < 0.5 ? "queue_config" : r < 0.85 ? "mapping_defect" : "vendor_defect";
        var inc = new Incident
        {
            Id = s.NewId("INC"), Kind = CutoverKind, Title = "Lab interface delays during cutover",
            Description = "Since cutover to the new environment began, roughly 8% of laboratory result messages are arriving 20–90 minutes late. Root cause unknown.",
            Started = s.CurrentDate, ProjectId = proj.Id, HiddenRootCause = cause, MessageDelayPct = 8,
            ResponseOptions = IncidentResponseHandler.Responses.ToList(),
        };
        inc.Timeline.Add($"{s.CurrentDate:MMM d}: Delays detected in {inc.MessageDelayPct:0}% of lab result messages.");
        s.Incidents.Add(inc);
        proj.HeldBy = inc.Id;
        proj.Status = ProjectStatus.OnHold;
        e.Notify(MessageKind.Incident, ExecRole.CIO, "URGENT: Lab interface delays during cutover",
            $"{inc.Description}\n\nThe cutover is paused. I need your direction: continue troubleshooting live, extend controlled downtime, " +
            "ask the CMO for a clinical assessment, or roll back to the legacy environment.", inc.Id);
        e.Remember("incident", "Lab interface delays during cutover (8% of lab messages delayed).", new[] { Topics.LabInterface, Topics.Clinical, "incident" },
            new[] { ExecRole.CIO, ExecRole.CMO, ExecRole.CNO }, 2);
        return true;
    }

    public static HandlerResult ApplyResponse(SimulationEngine e, Incident inc, string response, DecisionRecord decision)
    {
        var s = e.State;
        var effects = new List<string>();
        inc.Timeline.Add($"{s.CurrentDate:MMM d}: Directed response — {response.Replace('_', ' ')}.");
        switch (response)
        {
            case "request_clinical_assessment":
                inc.ClinicalAssessmentRequested = true;
                // The CMO responds within the day and decides for themself whether a safety hold is required.
                bool hold = inc.MessageDelayPct >= 5 && !inc.Fixed;
                effects.Add("The CMO has instituted manual phoning of critical lab results, cutting the safety exposure substantially.");
                if (hold)
                {
                    inc.ClinicalHold = true;
                    inc.ResponseOptions.Remove("continue_troubleshooting");
                    if (inc.ChosenResponse == "continue_troubleshooting") inc.ChosenResponse = null;
                    effects.Add("The CMO has placed a clinical safety hold: live troubleshooting on production clinical traffic is no longer permitted. Choose controlled downtime or rollback.");
                    e.Notify(MessageKind.Escalation, ExecRole.CMO, "Clinical safety hold — lab interface",
                        "With 8% of lab results delayed and no confirmed root cause, I am exercising my clinical safety authority. " +
                        "We will not troubleshoot on live clinical traffic. Either take controlled downtime with full downtime procedures, or roll back.", inc.Id);
                }
                inc.ResponseOptions.Remove("request_clinical_assessment");
                e.RecordConsultation(ExecRole.CMO, Topics.LabInterface);
                break;
            case "continue_troubleshooting":
                inc.ChosenResponse = response;
                effects.Add("The CIO's team continues diagnosing in production. Lab delays continue until the cause is found.");
                break;
            case "extend_downtime":
                inc.ChosenResponse = response;
                effects.Add("Facilities move to paper downtime procedures; interface traffic is quiesced so engineers can test safely. Costs and nursing workload rise each day.");
                break;
            case "rollback":
                inc.ChosenResponse = response;
                Rollback(e, inc);
                effects.Add("All facilities are back on the legacy environment. The cutover will be re-planned; schedule and cost increase.");
                break;
        }
        return new HandlerResult($"Incident response: {response.Replace('_', ' ')}.", effects);
    }

    public static void DailyTick(SimulationEngine e)
    {
        var s = e.State;
        foreach (var inc in s.Incidents.Where(i => i.Status == IncidentStatus.Active && i.Kind == CutoverKind).ToList())
        {
            inc.DaysActive++;
            var cio = s.Exec(ExecRole.CIO);
            double skill = 0.6 + cio.Competence / 100.0 * 0.8;
            string mode = inc.ChosenResponse ?? "none";
            double fixP = inc.HiddenRootCause switch { "queue_config" => 0.4, "mapping_defect" => 0.12, _ => 0.03 } * skill;
            if (mode == "extend_downtime") fixP *= 1.7;
            if (mode == "none") fixP *= 0.6; // team works without clear direction

            if (mode is "continue_troubleshooting" or "none")
            {
                // Live clinical traffic: delayed results can harm patients. Manual callbacks mitigate.
                double pEvent = inc.MessageDelayPct / 100.0 * 1.4 * (inc.ClinicalAssessmentRequested ? 0.3 : 1);
                if (e.Rng.Chance(pEvent))
                {
                    s.Metrics.PatientSafetyEvents++;
                    s.Metrics.ClinicalSafetyIndex = SimulationEngine.Clamp(s.Metrics.ClinicalSafetyIndex - 4);
                    inc.SafetyEvents++;
                    inc.Timeline.Add($"{s.CurrentDate:MMM d}: Patient safety event — delayed critical lab result.");
                    e.Notify(MessageKind.Escalation, ExecRole.CNO, "Patient safety event: delayed lab result",
                        "A critical potassium result reached the care team 70 minutes late. The patient required intervention but is stable. Nursing staff are losing confidence in the system.", inc.Id);
                    e.AdjustTrust(ExecRole.CMO, -4);
                    e.AdjustTrust(ExecRole.CNO, -4);
                }
            }
            if (mode == "extend_downtime")
            {
                s.Finance.PendingRevenueLoss += 95_000m;
                s.Finance.PendingIncidentCost += 40_000m;
                inc.RevenueImpact += 95_000m;
                inc.RecoveryCost += 40_000m;
                s.Exec(ExecRole.CNO).Workload = SimulationEngine.Clamp(s.Exec(ExecRole.CNO).Workload + 3);
                if (inc.DaysActive % 3 == 0)
                    e.Notify(MessageKind.Report, ExecRole.CNO, "Downtime procedures — day " + inc.DaysActive,
                        "Paper downtime procedures are holding, but nursing overtime is climbing and staff are fatigued. We cannot sustain this indefinitely.", inc.Id);
            }

            if (e.Rng.Chance(fixP)) Resolve(e, inc);
            else if (inc.DaysActive == 5 && mode != "extend_downtime" && inc.HiddenRootCause != "queue_config")
                e.Notify(MessageKind.Report, ExecRole.CIO, "Lab interface: still no root cause",
                    inc.HiddenRootCause == "vendor_defect"
                        ? "We've ruled out our configuration. The interface vendor suspects a defect in their engine; they cannot commit to a fix date."
                        : "We've found mismatched result-code mappings in a subset of tests. Correcting them is slow and has to be validated test by test.", inc.Id);
        }
    }

    private static void Resolve(SimulationEngine e, Incident inc)
    {
        var s = e.State;
        inc.Fixed = true;
        inc.Status = IncidentStatus.Resolved;
        inc.Resolved = s.CurrentDate;
        inc.MessageDelayPct = 0;
        inc.ResponseOptions.Clear();
        inc.Timeline.Add($"{s.CurrentDate:MMM d}: Root cause ({Describe(inc.HiddenRootCause)}) fixed; lab messages flowing normally.");
        var proj = s.Projects.FirstOrDefault(p => p.Id == inc.ProjectId);
        if (proj != null) { proj.HeldBy = null; proj.Status = ProjectStatus.Active; }
        e.Notify(MessageKind.Report, ExecRole.CIO, "Resolved: lab interface delays",
            $"Root cause identified and fixed: {Describe(inc.HiddenRootCause)}. Duration {inc.DaysActive} days; patient safety events: {inc.SafetyEvents}. Cutover is resuming.", inc.Id);
        Score(e, inc);
    }

    private static void Rollback(SimulationEngine e, Incident inc)
    {
        var s = e.State;
        inc.Status = IncidentStatus.Resolved;
        inc.Resolved = s.CurrentDate;
        inc.MessageDelayPct = 0;
        inc.ResponseOptions.Clear();
        inc.DowntimeHours += 6;
        s.Finance.PendingRevenueLoss += 60_000m;
        inc.RevenueImpact += 60_000m;
        var proj = s.Projects.FirstOrDefault(p => p.Id == inc.ProjectId);
        if (proj != null)
        {
            proj.HeldBy = null;
            proj.Status = ProjectStatus.Active;
            proj.Progress = 0.62;
            proj.CutoverIncidentTriggered = true; // root cause is found offline during re-planning
            proj.PlannedEndDate = proj.PlannedEndDate.AddDays(21);
            proj.BudgetCapex = Math.Round(proj.BudgetCapex * 1.06m, 0);
            proj.Risks.Add("Cutover rolled back once; re-planned with interface validation.");
        }
        inc.Timeline.Add($"{s.CurrentDate:MMM d}: Rolled back to legacy environment. Root cause later identified as {Describe(inc.HiddenRootCause)}.");
        Score(e, inc);
    }

    private static string Describe(string? cause) => cause switch
    {
        "queue_config" => "a misconfigured message queue throttle in the interface engine",
        "mapping_defect" => "incorrect result-code mappings for a subset of lab tests",
        _ => "a defect in the interface vendor's message routing",
    };

    /// <summary>Outcome quality of the incident handling, recorded against every response decision.</summary>
    private static void Score(SimulationEngine e, Incident inc)
    {
        var s = e.State;
        double outcome = 85 - inc.SafetyEvents * 20 - Math.Max(0, inc.DaysActive - 2) * 3 - (double)(inc.RevenueImpact / 100_000m) * 2;
        outcome = SimulationEngine.Clamp(outcome);
        foreach (var d in s.Decisions.Where(d => d.Action == "incident_response" && d.Date >= inc.Started))
        {
            d.OutcomeScore = outcome;
            d.OutcomeNotes.Add($"Incident closed after {inc.DaysActive} days with {inc.SafetyEvents} safety events; root cause: {Describe(inc.HiddenRootCause)}.");
        }
        e.Remember("incident_closed", $"Lab interface incident closed: {inc.DaysActive} days, {inc.SafetyEvents} safety events.", new[] { Topics.LabInterface, "incident" },
            new[] { ExecRole.CMO, ExecRole.CIO }, 2);
    }
}

/// <summary>Routine calendar content: memos, notices, renewals.</summary>
public static class ScheduledContent
{
    public static void Fire(SimulationEngine e, ScheduledEvent ev)
    {
        var s = e.State;
        switch (ev.Kind)
        {
            case "cfo_savings_memo":
                e.Notify(MessageKind.Finance, ExecRole.CFO, "Technology operating savings target",
                    "Operating margin is running below plan. I need $400k of recurring run-rate savings from technology opex this fiscal year. " +
                    "I'll be looking for realized savings, not projections. Committed contracts and salaries are not discretionary — show me where the real money is.");
                break;
            case "vendor_eos_notice":
                e.Notify(MessageKind.Briefing, ExecRole.Vendor, "Notice: end of support for current EHR release",
                    "Halcyon Clinical Systems confirms that the EHR release you run reaches end of standard support on June 30, 2028. " +
                    "Extended support is available at additional cost. We would welcome a conversation about your platform roadmap.");
                break;
            case "contract_renewal":
                var c = s.Contracts.FirstOrDefault(x => x.Id == ev.RelatedId);
                if (c == null || c.Renewed) break;
                double uplift = c.AgreedUpliftPct ?? c.ProposedUpliftPct;
                decimal delta = Math.Round(c.AnnualCost * (decimal)uplift, 0);
                c.AnnualCost += delta;
                c.Renewed = true;
                var line = s.Departments.SelectMany(d => d.BudgetLines).First(l => l.Id == c.BudgetLineId);
                line.AnnualBudget += delta;
                line.CommittedAnnual += delta;
                foreach (var sv in s.Savings.Where(x => x.Source == "negotiation" && x.Status == SavingsStatus.Proposed))
                    sv.Status = SavingsStatus.Realized;
                e.Notify(MessageKind.Finance, ExecRole.CFO, $"Contract renewed: {c.Vendor}",
                    c.Negotiated
                        ? $"The {c.Service} contract renewed at {uplift:+0.0%;-0.0%} ({SimulationEngine.Money(delta)}/yr). Good work getting below the vendor's opening position."
                        : $"The {c.Service} contract auto-renewed at the vendor's proposed {uplift:+0.0%} ({SimulationEngine.Money(delta)}/yr). Nobody negotiated it. That increase lands on your budget.", c.Id);
                e.Remember("contract", $"{c.Vendor} contract renewed at {uplift:+0.0%;-0.0%}{(c.Negotiated ? " after negotiation" : " without negotiation")}.",
                    new[] { Topics.EhrContract, Topics.Budget }, new[] { ExecRole.CFO }, c.Negotiated ? 1 : 2);
                if (!c.Negotiated) e.AdjustTrust(ExecRole.CFO, -5);
                break;
            case "contract_reminder":
                var cr = s.Contracts.FirstOrDefault(x => x.Id == ev.RelatedId);
                if (cr != null && !cr.Negotiated)
                    e.Notify(MessageKind.Briefing, ExecRole.CIO, $"Reminder: {cr.Vendor} renewal in 30 days",
                        $"The {cr.Service} contract auto-renews on {cr.RenewalDate:MMM d} with a {cr.ProposedUpliftPct:+0%} uplift unless we negotiate.", cr.Id);
                break;
        }
    }
}
