using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Evaluation;

/// <summary>
/// Rules-based evaluation of decision quality, computed from the state at the moment of the decision.
/// Outcome quality is recorded separately, later, when consequences materialize — a defensible decision
/// can turn out badly and a reckless one can get lucky. The AI has no input into these numbers.
/// </summary>
public static class DecisionEvaluator
{
    private const int EvidenceWindowDays = 180;
    private const int ConsultWindowDays = 90;

    public static DecisionEvaluation Evaluate(GameState s, ActionProposal a)
    {
        var ev = new DecisionEvaluation { Authority = 100, Compliance = 100 };
        var notes = ev.Notes;
        switch (a.Name)
        {
            case "approve_proposal": EvaluateApproval(s, ApproveProposalHandler.Find(s, a)!, ev); break;
            case "reject_proposal":
            case "defer_proposal": EvaluateDismissal(s, ApproveProposalHandler.Find(s, a)!, a.Name == "defer_proposal", ev); break;
            case "delegate_assignment":
            case "request_assessment": EvaluateDelegation(s, a, ev); break;
            case "reduce_budget": EvaluateBudgetCut(s, a, ev); break;
            case "incident_response": EvaluateIncidentResponse(s, a, ev); break;
            default:
                ev.Evidence = ev.Stakeholders = ev.Financial = ev.Risk = 60;
                break;
        }
        ev.Overall = Math.Round(ev.Evidence * 0.25 + ev.Stakeholders * 0.15 + ev.Financial * 0.2 + ev.Risk * 0.25 + ev.Authority * 0.05 + ev.Compliance * 0.1, 1);
        return ev;
    }

    public static bool HasEvidence(GameState s, string topic, string evidenceType)
    {
        var type = TaskTypes.Parse(evidenceType);
        return s.Assignments.Any(x => x.Status == AssignmentStatus.Completed && x.Topic == topic
                                      && (x.Type == type || (evidenceType == "investigate" && x.Type == TaskType.CostReview))
                                      && s.CurrentDate.DayNumber - x.CompletedOn!.Value.DayNumber <= EvidenceWindowDays);
    }

    public static bool Consulted(GameState s, ExecRole role, string topic) =>
        s.Memory.Any(m => m.Involved.Contains(role) && m.Tags.Contains(topic) && m.Kind is "consultation" or "request" or "report"
                          && s.CurrentDate.DayNumber - m.Date.DayNumber <= ConsultWindowDays);

    private static double StakeholderScore(GameState s, string topic, DecisionEvaluation ev)
    {
        var needed = Topics.Stakeholders(topic);
        if (needed.Count == 0) return 70;
        var missing = needed.Where(r => !Consulted(s, r, topic)).ToList();
        if (missing.Count > 0) ev.Notes.Add($"Not consulted: {string.Join(", ", missing.Select(Roles.Label))}.");
        return 30 + 70.0 * (needed.Count - missing.Count) / needed.Count;
    }

    private static void EvaluateApproval(GameState s, Proposal p, DecisionEvaluation ev)
    {
        var t = s.Technology;
        int have = p.RequiredEvidence.Count(r => HasEvidence(s, p.Topic, r) || (r == "clinical_assessment" && HasEvidence(s, Topics.DisasterRecovery, r)));
        ev.Evidence = p.RequiredEvidence.Count == 0 ? 70 : 30 + 70.0 * have / p.RequiredEvidence.Count;
        foreach (var r in p.RequiredEvidence.Where(r => !HasEvidence(s, p.Topic, r)))
            ev.Notes.Add($"Approved without a completed {r.Replace('_', ' ')}.");
        ev.Stakeholders = StakeholderScore(s, p.Topic, ev);

        // Financial: affordability and value for money.
        double fin = 70;
        if (p.Capex > s.Finance.TechCapitalBudgetRemaining) { fin -= 20; ev.Notes.Add("Exceeds remaining capital budget."); }
        decimal tco = p.Capex + p.AnnualOpex * 3;
        var cheaper = s.Proposals.FirstOrDefault(x => x.SupersedesId == p.Id && x.Status is ProposalStatus.Pending or ProposalStatus.Deferred);
        if (cheaper != null) { fin -= 35; ev.Notes.Add($"A cheaper alternative ('{cheaper.Title}') was already on the table."); }
        if (p.EffectKey == "dr_cloud" && t.CloudDrCertificationKnown == false) { fin -= 40; ev.Notes.Add("Proof of concept showed the vendor will not support cloud-hosted EHR recovery."); }
        if (p.EffectKey == "dr_onprem" && s.Proposals.Any(x => x.EffectKey == "dr_hybrid" && x.Status == ProposalStatus.Pending)) fin -= 10;
        if (tco > 3_000_000m) fin -= 10;
        ev.Financial = SimulationEngine.Clamp(fin);

        // Risk: does it address a real, known gap?
        double risk = 50 + p.SecurityRiskReduction * 2 + p.ClinicalRiskReduction * 2;
        if (p.EffectKey.StartsWith("dr_") && t.ReportedRtoHours > t.TargetRtoHours) risk += 15;
        if (p.EffectKey == "immutable_backup" && (t.MeasuredBackupRestoreSuccess ?? 1) < 0.95) risk += 20;
        if (p.EffectKey == "dr_cloud" && t.CloudDrCertificationKnown == null) { risk -= 20; ev.Notes.Add("Cloud-first committed while EHR vendor support was unverified."); }
        ev.Risk = SimulationEngine.Clamp(risk);
        if (p.ClinicallySignificant && !Consulted(s, ExecRole.CMO, p.Topic) && !HasEvidence(s, Topics.DisasterRecovery, "clinical_assessment"))
        { ev.Compliance = 80; ev.Notes.Add("Clinically significant change approved without clinical leadership input."); }
    }

    private static void EvaluateDismissal(GameState s, Proposal p, bool deferral, DecisionEvaluation ev)
    {
        var t = s.Technology;
        bool investigating = s.Assignments.Any(a => a.Topic == p.Topic && a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue);
        bool knownNeed = (p.EffectKey.StartsWith("dr_") && t.ReportedRtoHours > t.TargetRtoHours * 2 && !s.Projects.Any(x => x.EffectKey.StartsWith("dr_")))
                         || (p.EffectKey == "immutable_backup" && (t.MeasuredBackupRestoreSuccess ?? 1) < 0.95);
        bool alternative = s.Proposals.Any(x => x.SupersedesId == p.Id) || s.Proposals.Any(x => p.SupersedesId == x.Id && x.Status == ProposalStatus.Approved);

        ev.Evidence = investigating && deferral ? 85 : alternative ? 85 : knownNeed ? 30 : 60;
        if (investigating && deferral) ev.Notes.Add("Deferred while an assessment is under way.");
        ev.Stakeholders = StakeholderScore(s, p.Topic, ev);
        ev.Financial = alternative ? 90 : 70;
        // Dismissing one DR option while another is still on the table is not ignoring the risk.
        bool otherDrPath = p.EffectKey.StartsWith("dr_") && s.Proposals.Any(x => x != p && x.EffectKey.StartsWith("dr_") && x.Status is ProposalStatus.Pending or ProposalStatus.AwaitingApproval or ProposalStatus.Approved);
        ev.Risk = knownNeed && !otherDrPath ? (deferral ? 35 : 20) : 65;
        if (knownNeed && !otherDrPath)
        {
            ev.Notes.Add("Dismissed a remedy for a documented, severe risk.");
            ev.Compliance = 60; // contingency planning (backup, DR) is a regulatory expectation for health data
        }
    }

    private static void EvaluateDelegation(GameState s, ActionProposal a, DecisionEvaluation ev)
    {
        var type = TaskTypes.Parse(a.Str("task_type"));
        var role = Roles.Parse(a.Str("assignee_role") ?? a.Str("from_role"));
        int days = (int)(a.Int("deadline_days") ?? 14);
        double needCalendar = TaskTypes.BaseEffort(type) * 7 / 5.0;
        ev.Evidence = 80; // gathering evidence before committing is good practice
        ev.Stakeholders = 70;
        ev.Financial = (a.Money("authorized_budget") ?? 0) > 100_000 ? 60 : 80;
        ev.Risk = 70;
        if (days < needCalendar * 0.7) { ev.Evidence -= 15; ev.Notes.Add($"Deadline of {days} days is unrealistic for a {TaskTypes.Label(type)}."); }
        var exec = s.TryExec(role);
        if (exec != null && exec.Workload > 85) { ev.Risk -= 10; ev.Notes.Add($"{Roles.Label(role)} was already overloaded."); }
        if (type == TaskType.ClinicalAssessment) { ev.Stakeholders = 95; ev.Risk = 80; }
    }

    private static void EvaluateBudgetCut(GameState s, ActionProposal a, DecisionEvaluation ev)
    {
        var line = ReduceBudgetHandler.FindLine(s, a.Str("budget_line"), out var dept)!;
        bool reviewed = HasEvidence(s, Topics.Budget, "cost_review") || HasEvidence(s, Topics.Budget, "investigate");
        ev.Evidence = reviewed ? 85 : 50;
        if (!reviewed) ev.Notes.Add("Cut made without a cost review.");
        ev.Stakeholders = StakeholderScore(s, Topics.Budget, ev);
        ev.Financial = 80;
        ev.Risk = line.Id switch { "hardware_maint" => 35, "training" => dept!.Morale < 55 ? 40 : 55, "consulting" => 60, _ => 75 };
        if (line.Id == "hardware_maint") ev.Notes.Add("Reduced maintenance on aging, non-redundant infrastructure.");
        if (line.Id == "training" && dept!.Morale < 55) ev.Notes.Add("Cut development budget for a team with low morale.");
    }

    private static void EvaluateIncidentResponse(GameState s, ActionProposal a, DecisionEvaluation ev)
    {
        var inc = IncidentResponseHandler.Find(s, a)!;
        string r = a.Str("response")!.ToLowerInvariant();
        ev.Stakeholders = inc.ClinicalAssessmentRequested || Consulted(s, ExecRole.CMO, Topics.LabInterface) ? 90 : 40;
        ev.Financial = r switch { "extend_downtime" => 55, "rollback" => 60, _ => 75 };
        switch (r)
        {
            case "request_clinical_assessment":
                ev.Evidence = 90; ev.Risk = 85; ev.Stakeholders = 95; break;
            case "continue_troubleshooting":
                ev.Evidence = inc.DaysActive <= 1 ? 60 : 45;
                ev.Risk = inc.ClinicalAssessmentRequested ? 60 : 30;
                if (!inc.ClinicalAssessmentRequested) ev.Notes.Add("Continued live troubleshooting with clinical risk unassessed.");
                break;
            case "extend_downtime":
                ev.Evidence = 70; ev.Risk = 85; break;
            case "rollback":
                ev.Evidence = inc.DaysActive >= 2 || inc.ClinicalHold ? 80 : 60; ev.Risk = 90; break;
        }
    }

    /// <summary>Institutional memory: how a decision shifts how executives regard the player.</summary>
    public static void ApplyInstitutionalEffects(SimulationEngine e, DecisionRecord d)
    {
        var s = e.State;
        var ev = d.Evaluation;
        switch (d.Action)
        {
            case "approve_proposal":
                var p = s.Proposals.FirstOrDefault(x => d.ParametersJson.Contains(x.Id, StringComparison.OrdinalIgnoreCase));
                if (p != null && p.Capex + p.AnnualOpex >= 100_000)
                {
                    if (ev.Evidence < 50)
                    {
                        e.AdjustTrust(ExecRole.CFO, -4);
                        e.Remember("weak_justification", $"Player approved '{p.Title}' without supporting analysis.", new[] { p.Topic, "investment" }, new[] { ExecRole.CFO }, 2);
                    }
                    else if (ev.Evidence >= 80) e.AdjustTrust(ExecRole.CFO, 2);
                }
                if (p != null && p.SecurityRiskReduction >= 10) e.AdjustTrust(ExecRole.CISO, 4);
                break;
            case "reject_proposal":
            case "defer_proposal":
                if (d.Topic is Topics.Backup or Topics.Security)
                {
                    e.AdjustTrust(ExecRole.CISO, -5);
                    e.Remember("ignored_security", $"Player {(d.Action == "reject_proposal" ? "rejected" : "deferred")} a security remediation.", new[] { d.Topic, Topics.Security }, new[] { ExecRole.CISO }, 2);
                }
                break;
            case "reduce_budget":
                e.AdjustTrust(ExecRole.CFO, ev.Evidence >= 80 ? 4 : 2);
                e.RecordConsultation(ExecRole.CFO, Topics.Budget);
                break;
            case "delegate_assignment":
                if (ev.Notes.Any(n => n.Contains("unrealistic"))) e.AdjustTrust(ExecRole.CIO, -2);
                break;
        }
        s.Player.Experience += 1;
    }
}
