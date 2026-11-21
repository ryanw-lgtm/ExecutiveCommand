using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Evaluation;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

public enum AttentionLevel { Urgent, Important, Info }

/// <summary>Something the player should look at, with a way to act on it from the UI.</summary>
public sealed record AttentionItem(
    AttentionLevel Level,
    string Text,
    string ButtonLabel,
    string NavigateTo,
    IReadOnlyList<ExecRole>? MeetWith = null,
    string? Prefill = null,
    string? RelatedId = null);

/// <summary>A sentence the player could say. Guaranteed (by tests) to be understood by the interpreter.</summary>
public sealed record Suggestion(string Text, string Why, IReadOnlyList<ExecRole> Attendees);

/// <summary>
/// Player guidance derived purely from state. It never acts; it only points at things and offers
/// example sentences. Suggestions describe sensible options, not "correct answers".
/// </summary>
public static class Guidance
{
    private static string First(GameState s, ExecRole r) => s.TryExec(r)?.Name.Replace("Dr. ", "").Split(' ')[0] ?? r.ToString();

    public static List<AttentionItem> Attention(GameState s)
    {
        var items = new List<AttentionItem>();
        var t = s.Technology;

        foreach (var inc in s.Incidents.Where(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0))
            items.Add(new(AttentionLevel.Urgent, $"{inc.Title}: {inc.MessageDelayPct:0}% of lab results delayed. The cutover is paused until you decide how to respond.",
                "Respond", "Decisions", new[] { ExecRole.CMO, ExecRole.CIO, ExecRole.CNO }, "What's the clinical risk from the lab delays right now?", inc.Id));

        foreach (var a in s.Assignments.Where(a => a.Status == AssignmentStatus.Overdue))
            items.Add(new(AttentionLevel.Important, $"{s.Exec(a.Assignee).Name} is late on \"{a.Subject}\" (was due {a.Due:MMM d}).",
                "Ask for an update", "Meeting Room", new[] { a.Assignee }, $"{First(s, a.Assignee)}, where are we on \"{a.Subject}\"?", a.Id));

        // Findings, reviews and escalations matter; routine milestone notices do not need to interrupt.
        foreach (var m in s.Inbox.Where(m => !m.Read && (m.Kind is MessageKind.Escalation or MessageKind.Review
                     || (m.Kind == MessageKind.Report && (m.Subject.StartsWith("Report:") || m.Subject.StartsWith("Post-implementation") || m.Subject.StartsWith("Not approved") || m.Subject.StartsWith("Approved"))))).TakeLast(4))
            items.Add(new(m.Kind == MessageKind.Escalation ? AttentionLevel.Important : AttentionLevel.Info,
                $"New {m.Kind.ToString().ToLowerInvariant()}: {m.Subject}", "Read", "Inbox", RelatedId: m.Id));

        int pending = s.Proposals.Count(p => p.Status == ProposalStatus.Pending);
        if (pending > 0)
            items.Add(new(AttentionLevel.Important, $"{pending} proposal{(pending == 1 ? " is" : "s are")} waiting for your decision.", "Review", "Decisions"));

        foreach (var c in s.Contracts.Where(c => !c.Renewed && !c.Negotiated))
        {
            int days = c.RenewalDate.DayNumber - s.CurrentDate.DayNumber;
            if (days <= 75)
                items.Add(new(days <= 30 ? AttentionLevel.Urgent : AttentionLevel.Important,
                    $"The {c.Vendor} contract auto-renews in {days} days at {c.ProposedUpliftPct:+0%} unless someone negotiates.",
                    "Delegate negotiation", "Meeting Room", new[] { ExecRole.CIO }, $"{First(s, ExecRole.CIO)}, negotiate the Halcyon renewal and include DR support. Four weeks.", c.Id));
        }

        if (s.Inbox.Any(m => m.Subject.StartsWith("Technology operating savings")))
        {
            decimal approved = s.Savings.Where(v => v.Source == "budget_cut" && v.Recurring && v.Status != SavingsStatus.Reversed).Sum(v => v.AnnualAmount);
            if (approved < PerformanceEngine.SavingsTarget)
                items.Add(new(AttentionLevel.Info, $"CFO savings target: {SimulationEngine.Money(approved)} of {SimulationEngine.Money(PerformanceEngine.SavingsTarget)} recurring savings approved so far.",
                    "See budget", "Finance"));
        }

        bool drUnderway = s.Projects.Any(p => p.EffectKey.StartsWith("dr_")) || s.Proposals.Any(p => p.EffectKey.StartsWith("dr_") && p.Status == ProposalStatus.AwaitingApproval);
        if (!drUnderway && t.ReportedRtoHours > t.TargetRtoHours)
            items.Add(new(AttentionLevel.Important, $"Disaster recovery: documented EHR recovery time is {t.ReportedRtoHours:0} hours against a {t.TargetRtoHours:0}-hour target, with no plan approved.",
                "Discuss", "Meeting Room", new[] { ExecRole.CIO, ExecRole.EADirector, ExecRole.CISO }, "What's our disaster recovery position?"));

        return items.OrderBy(i => i.Level).ToList();
    }

    /// <summary>Context-aware example sentences for the meeting room.</summary>
    public static List<Suggestion> Suggestions(GameState s)
    {
        var list = new List<Suggestion>();
        var t = s.Technology;
        bool Open(TaskType type, string topic) => s.Assignments.Any(a => a.Type == type && a.Topic == topic && a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue);
        bool Done(string topic, string evidence) => DecisionEvaluator.HasEvidence(s, topic, evidence);
        var tech = new[] { ExecRole.CIO, ExecRole.CISO, ExecRole.EADirector };

        var inc = s.Incidents.FirstOrDefault(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0);
        if (inc != null)
        {
            var clinical = new[] { ExecRole.CMO, ExecRole.CIO, ExecRole.CNO };
            if (inc.ResponseOptions.Contains("request_clinical_assessment")) list.Add(new("Get the CMO's clinical assessment of the lab delays.", "Have clinical leadership judge patient risk", clinical));
            if (inc.ResponseOptions.Contains("extend_downtime")) list.Add(new("Extend controlled downtime while the team diagnoses it.", "Safer, but costly and tiring for nurses", clinical));
            if (inc.ResponseOptions.Contains("continue_troubleshooting")) list.Add(new("Keep troubleshooting live, but watch it closely.", "Fastest if it's simple, risky if not", clinical));
            if (inc.ResponseOptions.Contains("rollback")) list.Add(new("Roll back to the legacy environment.", "Safe but delays the project", clinical));
        }

        if (!Done(Topics.DisasterRecovery, "dr_assessment") && !Open(TaskType.DrAssessment, Topics.DisasterRecovery))
            list.Add(new($"{First(s, ExecRole.CIO)}, assess our real disaster recovery time. Four weeks.", "Find out whether the 24-hour figure is still true", tech));
        if (!t.BackupsVerified && !Open(TaskType.BackupVerification, Topics.Backup))
            list.Add(new($"{First(s, ExecRole.CISO)}, verify our backups actually restore. Four weeks.", "Untested backups are a hidden risk", tech));
        var identity = s.Proposals.FirstOrDefault(p => p.EffectKey == "identity_expansion" && p.Status is ProposalStatus.Pending or ProposalStatus.Deferred);
        if (identity != null && !Done(Topics.Identity, "investigate") && !s.Assignments.Any(a => a.Topic == Topics.Identity))
            list.Add(new($"{First(s, ExecRole.EADirector)}, investigate alternatives before we spend another $180,000. Give him four weeks.", "Check whether the expansion is really needed", tech));
        if (s.Proposals.Any(p => p.EffectKey == "dr_cloud" && p.Status is ProposalStatus.Pending or ProposalStatus.Deferred) && t.CloudDrCertificationKnown == null && !Open(TaskType.ProofOfConcept, Topics.DisasterRecovery))
            list.Add(new($"{First(s, ExecRole.EADirector)}, run a proof of concept of cloud recovery. Six weeks.", "Confirms whether the vendor supports cloud DR", tech));
        if (s.Contracts.Any(c => !c.Renewed && !c.Negotiated) && !s.Assignments.Any(a => a.Type == TaskType.VendorNegotiation && a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue))
            list.Add(new($"{First(s, ExecRole.CIO)}, negotiate the Halcyon renewal and include DR support. Four weeks.", "Avoid an automatic 7% price rise", tech));
        if (inc == null && !Done(Topics.DisasterRecovery, "clinical_assessment") && !s.Assignments.Any(a => a.Assignee == ExecRole.CMO && a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue))
            list.Add(new("Ask the CMO for a clinical assessment of the disaster recovery options within three weeks.", "Clinical leaders must back any EHR change", new[] { ExecRole.CMO, ExecRole.CNO }));
        if (!Done(Topics.Budget, "cost_review") && !Open(TaskType.CostReview, Topics.Budget))
            list.Add(new($"{First(s, ExecRole.CIO)}, do a cost review of our discretionary spending. Three weeks.", "Find realistic savings before cutting", tech));

        list.Add(new("What's our disaster recovery position?", "Ask what they know", tech));
        list.Add(new("How are we tracking against the savings target?", "Ask the CFO for a status check", new[] { ExecRole.CFO }));
        return list;
    }
}
