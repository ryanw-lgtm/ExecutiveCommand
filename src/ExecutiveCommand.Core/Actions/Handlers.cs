using System.Text.Json.Nodes;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Actions;

internal static class TaskTypes
{
    public static readonly string[] Names =
        { "investigate", "dr_assessment", "backup_verification", "vendor_negotiation", "proof_of_concept", "cost_review", "clinical_assessment" };

    public static TaskType Parse(string? s) => (s ?? "").ToLowerInvariant() switch
    {
        "dr_assessment" => TaskType.DrAssessment,
        "backup_verification" => TaskType.BackupVerification,
        "vendor_negotiation" => TaskType.VendorNegotiation,
        "proof_of_concept" => TaskType.ProofOfConcept,
        "cost_review" => TaskType.CostReview,
        "clinical_assessment" => TaskType.ClinicalAssessment,
        _ => TaskType.Investigate,
    };

    public static string Label(TaskType t) => t switch
    {
        TaskType.DrAssessment => "disaster recovery assessment",
        TaskType.BackupVerification => "backup restore verification",
        TaskType.VendorNegotiation => "vendor negotiation",
        TaskType.ProofOfConcept => "proof of concept",
        TaskType.CostReview => "cost review",
        TaskType.ClinicalAssessment => "clinical impact assessment",
        _ => "investigation",
    };

    public static string DefaultTopic(TaskType t) => t switch
    {
        TaskType.DrAssessment => Topics.DisasterRecovery,
        TaskType.BackupVerification => Topics.Backup,
        TaskType.VendorNegotiation => Topics.EhrContract,
        TaskType.ProofOfConcept => Topics.DisasterRecovery,
        TaskType.CostReview => Topics.Budget,
        TaskType.ClinicalAssessment => Topics.Clinical,
        _ => Topics.General,
    };

    /// <summary>Baseline work-days for an average executive.</summary>
    public static double BaseEffort(TaskType t) => t switch
    {
        TaskType.DrAssessment => 14, TaskType.BackupVerification => 10, TaskType.VendorNegotiation => 18,
        TaskType.ProofOfConcept => 25, TaskType.CostReview => 10, TaskType.ClinicalAssessment => 7, _ => 8,
    };

    /// <summary>Which roles have the expertise to carry out a task type.</summary>
    public static bool Competent(ExecRole r, TaskType t) => t switch
    {
        TaskType.ClinicalAssessment => r is ExecRole.CMO or ExecRole.CNO,
        TaskType.BackupVerification => r is ExecRole.CIO or ExecRole.CISO,
        TaskType.DrAssessment => r is ExecRole.CIO or ExecRole.EADirector or ExecRole.CISO,
        TaskType.ProofOfConcept => r is ExecRole.CIO or ExecRole.EADirector,
        TaskType.VendorNegotiation => r is ExecRole.CIO or ExecRole.EADirector or ExecRole.CFO,
        TaskType.CostReview => r is ExecRole.CIO or ExecRole.EADirector or ExecRole.CISO or ExecRole.CFO,
        _ => r is not ExecRole.Vendor and not ExecRole.CEO,
    };
}

internal static class AssignmentFactory
{
    public static string ResolveTopic(TaskType type, string subject)
    {
        var fromSubject = Topics.Detect(subject, "");
        if (type == TaskType.ProofOfConcept || type == TaskType.DrAssessment) return Topics.DisasterRecovery;
        if (type == TaskType.BackupVerification) return Topics.Backup;
        if (type == TaskType.ClinicalAssessment && fromSubject is Topics.LabInterface or Topics.DisasterRecovery) return fromSubject;
        return string.IsNullOrEmpty(fromSubject) ? TaskTypes.DefaultTopic(type) : fromSubject;
    }

    public static Assignment Create(SimulationEngine e, ExecRole assignee, TaskType type, string subject, int deadlineDays, decimal budget, bool isRequest, string decisionId)
    {
        var s = e.State;
        double effort = TaskTypes.BaseEffort(type) * (0.8 + e.Rng.NextDouble() * 0.5);
        return new Assignment
        {
            Id = s.NewId("ASG"), Assignee = assignee, Type = type, Subject = subject,
            Topic = ResolveTopic(type, subject), Created = s.CurrentDate, Due = s.CurrentDate.AddDays(deadlineDays),
            AuthorizedBudget = budget, EffortRequired = Math.Round(effort, 2), IsRequest = isRequest, DecisionId = decisionId,
        };
    }
}

// ============================================================================ delegate_assignment

public sealed class DelegateAssignmentHandler : ActionHandler
{
    public override ActionSpec Spec { get; } = new("delegate_assignment",
        "Assign work to one of your direct reports with a deadline and optional budget.",
        new[]
        {
            new ParamSpec("assignee_role", ParamType.Enum, true, "Role of the direct report", EnumValues: new[] { "CIO", "CISO", "EADirector", "CEO", "CFO", "CMO", "CNO", "Vendor" }),
            new ParamSpec("task_type", ParamType.Enum, true, "Kind of work", EnumValues: TaskTypes.Names),
            new ParamSpec("subject", ParamType.String, true, "What to work on", MaxLength: 200),
            new ParamSpec("deadline_days", ParamType.Integer, true, "Calendar days until due", 3, 180),
            new ParamSpec("authorized_budget", ParamType.Money, false, "Consulting money the assignee may spend", 0, 250_000),
        });

    public override string TopicOf(GameState s, ActionProposal a) =>
        AssignmentFactory.ResolveTopic(TaskTypes.Parse(a.Str("task_type")), a.Str("subject") ?? "");

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var role = Roles.Parse(a.Str("assignee_role"));
        var exec = s.TryExec(role);
        if (exec == null || role == s.Player.Role) return ValidationResult.Fail("There is no such executive to assign this to.");
        if (exec.ReportsTo != s.Player.Role)
            return ValidationResult.Fail(
                $"{exec.Name} ({Roles.Label(role)}) does not report to you. You can ask for their help, but you cannot assign them work.",
                suggestion: "request_assessment");

        var type = TaskTypes.Parse(a.Str("task_type"));
        if (!TaskTypes.Competent(role, type))
            return ValidationResult.Fail($"A {TaskTypes.Label(type)} is outside the {Roles.Label(role)}'s expertise.",
                suggestion: type == TaskType.ClinicalAssessment ? "request_assessment" : null);

        if (string.IsNullOrWhiteSpace(a.Str("subject"))) return ValidationResult.Clarify("What exactly should they work on?");

        string topic = TopicOf(s, a);
        if (s.Assignments.Any(x => x.Status is AssignmentStatus.Active or AssignmentStatus.Overdue && x.Type == type && x.Topic == topic))
            return ValidationResult.Fail($"There is already an open {TaskTypes.Label(type)} on this topic.");

        decimal budget = a.Money("authorized_budget") ?? 0;
        if (budget > 0)
        {
            var consulting = ConsultingLine(s);
            if (consulting == null || budget > consulting.DiscretionaryRemaining)
                return ValidationResult.Fail($"Only {SimulationEngine.Money(consulting?.DiscretionaryRemaining ?? 0)} of uncommitted consulting budget remains; you cannot authorize {SimulationEngine.Money(budget)}.");
            if (budget > s.Player.OpexApprovalLimit)
                return ValidationResult.Fail($"Authorizing {SimulationEngine.Money(budget)} exceeds your {SimulationEngine.Money(s.Player.OpexApprovalLimit)} spending authority.");
        }
        return ValidationResult.Ok();
    }

    internal static BudgetLine? ConsultingLine(GameState s) =>
        s.Departments.FirstOrDefault(d => d.Id == s.Player.DepartmentId)?.BudgetLines.FirstOrDefault(l => l.Id == "consulting");

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var s = e.State;
        var role = Roles.Parse(a.Str("assignee_role"));
        var type = TaskTypes.Parse(a.Str("task_type"));
        decimal budget = a.Money("authorized_budget") ?? 0;
        int days = (int)a.Int("deadline_days")!.Value;
        var asg = AssignmentFactory.Create(e, role, type, a.Str("subject")!, days, budget, false, decision.Id);
        s.Assignments.Add(asg);
        if (budget > 0) ConsultingLine(s)!.EarmarkedAnnual += budget; // earmarked: can no longer be cut (no double counting)

        var exec = s.Exec(role);
        exec.Workload = SimulationEngine.Clamp(exec.Workload + 8);
        e.Remember("delegation", $"Player delegated a {TaskTypes.Label(type)} to {role}: {asg.Subject} (due {asg.Due:yyyy-MM-dd}).",
            new[] { asg.Topic, "delegation" }, new[] { role });
        var effects = new List<string> { $"Assignment {asg.Id} created for {exec.Name}, due {asg.Due:MMM d}." };
        if (budget > 0) effects.Add($"{SimulationEngine.Money(budget)} of consulting budget earmarked.");
        return new HandlerResult($"Delegated {TaskTypes.Label(type)} to {Roles.Label(role)} ({asg.Subject}).", effects);
    }
}

// ============================================================================ request_assessment

public sealed class RequestAssessmentHandler : ActionHandler
{
    public override ActionSpec Spec { get; } = new("request_assessment",
        "Ask a peer or superior executive (who does not report to you) for an assessment.",
        new[]
        {
            new ParamSpec("from_role", ParamType.Enum, true, "Executive to ask", EnumValues: new[] { "CEO", "CFO", "CMO", "CNO", "CIO", "CISO", "EADirector", "Vendor" }),
            new ParamSpec("task_type", ParamType.Enum, true, "Kind of work", EnumValues: TaskTypes.Names),
            new ParamSpec("subject", ParamType.String, true, "What to assess", MaxLength: 200),
            new ParamSpec("deadline_days", ParamType.Integer, false, "Requested turnaround in days", 3, 180),
        });

    public override string TopicOf(GameState s, ActionProposal a) =>
        AssignmentFactory.ResolveTopic(TaskTypes.Parse(a.Str("task_type")), a.Str("subject") ?? "");

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var role = Roles.Parse(a.Str("from_role"));
        var exec = s.TryExec(role);
        if (exec == null || role == s.Player.Role) return ValidationResult.Fail("There is no such executive.");
        if (exec.ReportsTo == s.Player.Role)
            return ValidationResult.Fail($"{exec.Name} reports to you — delegate it instead.", suggestion: "delegate_assignment");
        if (role == ExecRole.CEO) return ValidationResult.Fail("The CEO expects you to bring analysis to her, not the other way round.");
        var type = TaskTypes.Parse(a.Str("task_type"));
        if (!TaskTypes.Competent(role, type))
            return ValidationResult.Fail($"A {TaskTypes.Label(type)} is not something the {Roles.Label(role)} would perform.");
        string topic = TopicOf(s, a);
        if (s.Assignments.Any(x => x.Status is AssignmentStatus.Active or AssignmentStatus.Overdue && x.Assignee == role && x.Topic == topic))
            return ValidationResult.Fail($"You already have an open request with the {Roles.Label(role)} on this.");
        return ValidationResult.Ok();
    }

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var s = e.State;
        var role = Roles.Parse(a.Str("from_role"));
        var exec = s.Exec(role);
        var type = TaskTypes.Parse(a.Str("task_type"));
        int days = (int)(a.Int("deadline_days") ?? 14);
        var asg = AssignmentFactory.Create(e, role, type, a.Str("subject")!, days, 0, true, decision.Id);

        // Peers decide for themselves. Clinical leaders always engage on clinical safety; others weigh
        // workload and trust. This is deterministic given state and the seeded RNG.
        bool remit = type == TaskType.ClinicalAssessment && role is ExecRole.CMO or ExecRole.CNO;
        double acceptScore = exec.TrustInPlayer - exec.Workload * 0.4 + (remit ? 60 : 0) + e.Rng.NextDouble() * 20;
        if (acceptScore < 25)
        {
            asg.Status = AssignmentStatus.Declined;
            s.Assignments.Add(asg);
            string why = exec.Workload > 70 ? "their team is at capacity" : "they are not convinced it is a priority";
            e.Notify(MessageKind.Report, role, $"Re: your request — {asg.Subject}",
                $"{exec.Name} declined the request; {why}. You may need to make the case more directly or find another route.", asg.Id);
            return new HandlerResult($"{Roles.Label(role)} declined the request ({why}).", new() { "Request declined." });
        }
        if (days < 7 && !remit) asg.Due = s.CurrentDate.AddDays(7); // peers set their own timelines
        s.Assignments.Add(asg);
        exec.Workload = SimulationEngine.Clamp(exec.Workload + 5);
        e.Remember("request", $"Player asked {role} for a {TaskTypes.Label(type)}: {asg.Subject}.", new[] { asg.Topic, "consultation" }, new[] { role });
        return new HandlerResult($"{Roles.Label(role)} agreed to provide a {TaskTypes.Label(type)} by {asg.Due:MMM d}.",
            new() { $"Request {asg.Id} accepted by {exec.Name}." });
    }
}

// ============================================================================ approve_proposal

public sealed class ApproveProposalHandler : ActionHandler
{
    public override ActionSpec Spec { get; } = new("approve_proposal",
        "Approve a pending proposal. Above your authority it is routed to the CFO and CEO.",
        new[] { new ParamSpec("proposal_id", ParamType.String, true, "Proposal identifier", MaxLength: 40) });

    public override string TopicOf(GameState s, ActionProposal a) => Find(s, a)?.Topic ?? Topics.General;

    internal static Proposal? Find(GameState s, ActionProposal a) =>
        s.Proposals.FirstOrDefault(p => string.Equals(p.Id, a.Str("proposal_id"), StringComparison.OrdinalIgnoreCase));

    public static bool RequiresExecutiveApproval(GameState s, Proposal p) =>
        p.Capex > s.Player.CapexApprovalLimit || p.AnnualOpex > s.Player.OpexApprovalLimit || p.Capex > s.Finance.TechCapitalBudgetRemaining;

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var p = Find(s, a);
        if (p == null) return ValidationResult.Clarify("I'm not sure which proposal you mean. Which one?");
        if (p.Status is not (ProposalStatus.Pending or ProposalStatus.Deferred))
            return ValidationResult.Fail($"'{p.Title}' is {p.Status.ToString().ToLowerInvariant()} and cannot be approved now.");
        if (p.Topic == Topics.DisasterRecovery && p.EffectKey.StartsWith("dr_") &&
            s.Projects.Any(x => x.EffectKey.StartsWith("dr_") && x.Status is ProjectStatus.Active or ProjectStatus.OnHold))
            return ValidationResult.Fail("A disaster recovery program is already under way.");
        if (p.Topic == Topics.DisasterRecovery && p.EffectKey.StartsWith("dr_") &&
            s.Proposals.Any(x => x != p && x.EffectKey.StartsWith("dr_") && x.Status == ProposalStatus.AwaitingApproval))
            return ValidationResult.Fail("Another disaster recovery option is already awaiting executive approval.");
        return ValidationResult.Ok();
    }

    public override string? ConfirmationPrompt(GameState s, ActionProposal a)
    {
        var p = Find(s, a)!;
        if (RequiresExecutiveApproval(s, p))
        {
            var why = p.Capex > s.Finance.TechCapitalBudgetRemaining
                ? $"it needs {SimulationEngine.Money(p.Capex)} of capital and only {SimulationEngine.Money(s.Finance.TechCapitalBudgetRemaining)} remains in this year's technology capital budget"
                : $"it exceeds your approval authority ({SimulationEngine.Money(s.Player.CapexApprovalLimit)} capex / {SimulationEngine.Money(s.Player.OpexApprovalLimit)} annual opex)";
            return $"'{p.Title}' will be submitted to the CFO and CEO because {why}. They will decide within a week. Submit?";
        }
        if (p.Capex >= 100_000 || p.AnnualOpex >= 100_000)
            return $"Approve '{p.Title}' — {SimulationEngine.Money(p.Capex)} capital and {SimulationEngine.Money(p.AnnualOpex)}/yr operating cost?";
        return null;
    }

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var s = e.State;
        var p = Find(s, a)!;
        if (RequiresExecutiveApproval(s, p))
        {
            p.Status = ProposalStatus.AwaitingApproval;
            p.ApprovalDueDate = s.CurrentDate.AddDays(7);
            p.ApprovalDecisionDue = decision.Id;
            e.Remember("submission", $"Player submitted '{p.Title}' for CFO/CEO approval.", new[] { p.Topic, "approval" }, new[] { ExecRole.CFO, ExecRole.CEO });
            return new HandlerResult($"Submitted '{p.Title}' to the CFO and CEO for approval.", new() { $"Decision expected by {p.ApprovalDueDate:MMM d}." });
        }
        var project = ProjectEngine.StartProject(e, p, decision.Id);
        return new HandlerResult($"Approved '{p.Title}'.", new()
        {
            $"Project {project.Id} started; planned completion {project.PlannedEndDate:MMM d, yyyy}.",
            $"{SimulationEngine.Money(p.Capex)} reserved from the technology capital budget.",
        });
    }
}

// ============================================================================ reject / defer

public sealed class RejectProposalHandler : ActionHandler
{
    public override ActionSpec Spec { get; } = new("reject_proposal", "Reject a pending proposal.",
        new[]
        {
            new ParamSpec("proposal_id", ParamType.String, true, "Proposal identifier", MaxLength: 40),
            new ParamSpec("reason", ParamType.String, false, "Rationale communicated to the sponsor", MaxLength: 300),
        });

    public override string TopicOf(GameState s, ActionProposal a) => ApproveProposalHandler.Find(s, a)?.Topic ?? Topics.General;

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var p = ApproveProposalHandler.Find(s, a);
        if (p == null) return ValidationResult.Clarify("Which proposal do you want to reject?");
        if (p.Status is not (ProposalStatus.Pending or ProposalStatus.Deferred))
            return ValidationResult.Fail($"'{p.Title}' is {p.Status.ToString().ToLowerInvariant()}; there is nothing to reject.");
        return ValidationResult.Ok();
    }

    public override string? ConfirmationPrompt(GameState s, ActionProposal a)
    {
        var p = ApproveProposalHandler.Find(s, a)!;
        return p.SecurityRiskReduction >= 10 || p.ClinicalRiskReduction >= 10
            ? $"'{p.Title}' addresses a significant {(p.SecurityRiskReduction >= 10 ? "security" : "clinical")} risk. Reject it anyway?"
            : null;
    }

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var p = ApproveProposalHandler.Find(e.State, a)!;
        p.Status = ProposalStatus.Rejected;
        e.AdjustTrust(p.Sponsor, -4);
        string reason = a.Str("reason") ?? "no reason given";
        e.Remember("rejection", $"Player rejected '{p.Title}' ({reason}).", new[] { p.Topic, "rejection" }, new[] { p.Sponsor });
        return new HandlerResult($"Rejected '{p.Title}'.", new() { $"{Roles.Label(p.Sponsor)} has been informed." });
    }
}

public sealed class DeferProposalHandler : ActionHandler
{
    public override ActionSpec Spec { get; } = new("defer_proposal", "Postpone a decision on a proposal.",
        new[]
        {
            new ParamSpec("proposal_id", ParamType.String, true, "Proposal identifier", MaxLength: 40),
            new ParamSpec("days", ParamType.Integer, false, "How long to defer", 7, 180),
            new ParamSpec("reason", ParamType.String, false, "Rationale", MaxLength: 300),
        });

    public override string TopicOf(GameState s, ActionProposal a) => ApproveProposalHandler.Find(s, a)?.Topic ?? Topics.General;

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var p = ApproveProposalHandler.Find(s, a);
        if (p == null) return ValidationResult.Clarify("Which proposal do you want to put on hold?");
        if (p.Status != ProposalStatus.Pending && p.Status != ProposalStatus.Deferred)
            return ValidationResult.Fail($"'{p.Title}' is {p.Status.ToString().ToLowerInvariant()}.");
        return ValidationResult.Ok();
    }

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var p = ApproveProposalHandler.Find(e.State, a)!;
        int days = (int)(a.Int("days") ?? 30);
        p.Status = ProposalStatus.Deferred;
        p.DeferredUntil = e.State.CurrentDate.AddDays(days);
        e.AdjustTrust(p.Sponsor, -1);
        e.Remember("deferral", $"Player deferred '{p.Title}' for {days} days.", new[] { p.Topic, "deferral" }, new[] { p.Sponsor });
        return new HandlerResult($"Deferred '{p.Title}' until {p.DeferredUntil:MMM d}.", new());
    }
}

// ============================================================================ reduce_budget

public sealed class ReduceBudgetHandler : ActionHandler
{
    public override ActionSpec Spec { get; } = new("reduce_budget",
        "Reduce a discretionary budget line in your own department. Savings are tracked as approved, then realized monthly.",
        new[]
        {
            new ParamSpec("budget_line", ParamType.String, true, "Budget line id or name", MaxLength: 60),
            new ParamSpec("annual_amount", ParamType.Money, true, "Annualized reduction", 1_000, 50_000_000),
            new ParamSpec("recurring", ParamType.Boolean, false, "True for permanent run-rate savings, false for this fiscal year only"),
            new ParamSpec("reason", ParamType.String, false, "Rationale", MaxLength: 300),
        });

    public override string TopicOf(GameState s, ActionProposal a) => Topics.Budget;

    internal static BudgetLine? FindLine(GameState s, string? key, out Department? dept)
    {
        dept = null;
        if (string.IsNullOrWhiteSpace(key)) return null;
        string k = key.Trim().ToLowerInvariant();
        foreach (var d in s.Departments)
            foreach (var l in d.BudgetLines)
                if (l.Id == k || l.Name.ToLowerInvariant() == k || l.Keywords.Any(w => k.Contains(w)))
                { dept = d; return l; }
        return null;
    }

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var line = FindLine(s, a.Str("budget_line"), out var dept);
        if (line == null) return ValidationResult.Clarify("Which budget line — consulting, training, travel, software, hardware maintenance or cloud?");
        if (dept!.Id != s.Player.DepartmentId)
            return ValidationResult.Fail($"The {dept.Name} budget is not yours to cut. Raise it with that department's executive.");
        decimal amount = a.Money("annual_amount")!.Value;
        if (amount > line.DiscretionaryRemaining)
            return ValidationResult.Fail(
                $"{line.Name} has {SimulationEngine.Money(line.DiscretionaryRemaining)} of uncommitted discretionary spend left " +
                $"({SimulationEngine.Money(line.CommittedAnnual)} is contractually committed, {SimulationEngine.Money(line.EarmarkedAnnual)} earmarked for assignments, {SimulationEngine.Money(line.ApprovedReductionsAnnual)} already cut). " +
                $"A {SimulationEngine.Money(amount)} reduction is not achievable.");
        return ValidationResult.Ok();
    }

    public override string? ConfirmationPrompt(GameState s, ActionProposal a)
    {
        var line = FindLine(s, a.Str("budget_line"), out _)!;
        decimal amount = a.Money("annual_amount")!.Value;
        decimal pool = line.AnnualBudget - line.CommittedAnnual;
        if (amount >= 250_000 || (pool > 0 && amount / pool >= 0.5m))
            return $"Cut {SimulationEngine.Money(amount)}/yr from {line.Name} ({(pool > 0 ? amount / pool : 1):P0} of its discretionary spend)? This will be visible to your teams.";
        return null;
    }

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var s = e.State;
        var line = FindLine(s, a.Str("budget_line"), out var dept)!;
        decimal amount = a.Money("annual_amount")!.Value;
        bool recurring = a.Bool("recurring") ?? true;
        line.ApprovedReductionsAnnual += amount;
        var first = new DateOnly(s.CurrentDate.Year, s.CurrentDate.Month, 1).AddMonths(1);
        var init = new SavingsInitiative
        {
            Id = s.NewId("SAV"), DepartmentId = dept!.Id, BudgetLineId = line.Id, AnnualAmount = amount, Recurring = recurring,
            Status = SavingsStatus.Approved, EffectiveDate = first, Reason = a.Str("reason") ?? "", DecisionId = decision.Id,
            EndDate = recurring ? null : FinancialEngine.FiscalYearEnd(s, s.CurrentDate),
        };
        s.Savings.Add(init);

        var effects = new List<string>
        {
            $"{SimulationEngine.Money(amount)}/yr reduction approved on {line.Name}; realization begins {first:MMM yyyy}.",
        };
        // Cuts are not free: consequences depend on what was cut.
        switch (line.Id)
        {
            case "training":
                dept.Morale = SimulationEngine.Clamp(dept.Morale - (double)(amount / 40_000m));
                effects.Add("Team morale is likely to suffer from reduced development opportunities.");
                break;
            case "hardware_maint":
                effects.Add("Reduced maintenance coverage will increase hardware failure exposure.");
                break;
            case "consulting":
                effects.Add("Less contractor capacity will slow assessments and projects.");
                break;
        }
        e.Remember("budget_cut", $"Player cut {SimulationEngine.Money(amount)}/yr from {line.Name} ({(recurring ? "recurring" : "one-time")}).",
            new[] { Topics.Budget, line.Id }, new[] { ExecRole.CFO, ExecRole.CIO });
        return new HandlerResult($"Reduced {line.Name} by {SimulationEngine.Money(amount)}/yr ({(recurring ? "recurring" : "one-time")}).", effects);
    }
}

// ============================================================================ incident_response

public sealed class IncidentResponseHandler : ActionHandler
{
    public static readonly string[] Responses = { "continue_troubleshooting", "extend_downtime", "request_clinical_assessment", "rollback" };

    public override ActionSpec Spec { get; } = new("incident_response", "Direct the response to an active incident.",
        new[]
        {
            new ParamSpec("incident_id", ParamType.String, false, "Incident identifier (defaults to the active incident)", MaxLength: 40),
            new ParamSpec("response", ParamType.Enum, true, "Chosen response", EnumValues: Responses),
        });

    internal static Incident? Find(GameState s, ActionProposal a)
    {
        var id = a.Str("incident_id");
        return s.Incidents.FirstOrDefault(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0 &&
                                              (id == null || string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)));
    }

    public override string TopicOf(GameState s, ActionProposal a) => Topics.LabInterface;

    public override ValidationResult ValidateDomain(GameState s, ActionProposal a)
    {
        var inc = Find(s, a);
        if (inc == null) return ValidationResult.Fail("There is no active incident awaiting your direction.");
        var r = a.Str("response")!.ToLowerInvariant();
        if (!inc.ResponseOptions.Contains(r)) return ValidationResult.Fail($"'{r}' is not a viable response to this incident right now.");
        if (r == "request_clinical_assessment" && inc.ClinicalAssessmentRequested)
            return ValidationResult.Fail("The CMO is already assessing clinical impact.");
        return ValidationResult.Ok();
    }

    public override string? ConfirmationPrompt(GameState s, ActionProposal a) =>
        a.Str("response")!.ToLowerInvariant() == "rollback"
            ? "Rolling back returns all facilities to the legacy environment and adds downtime tonight. Proceed with rollback?"
            : null;

    public override HandlerResult Execute(SimulationEngine e, ActionProposal a, DecisionRecord decision)
    {
        var inc = Find(e.State, a)!;
        return IncidentEngine.ApplyResponse(e, inc, a.Str("response")!.ToLowerInvariant(), decision);
    }
}
