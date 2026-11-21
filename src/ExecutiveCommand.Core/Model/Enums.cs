namespace ExecutiveCommand.Core.Model;

public enum Difficulty { Casual, Professional, Executive }

public enum ExecRole
{
    None,
    CEO,
    CFO,
    CTO,
    CIO,
    CISO,
    CMO,
    CNO,
    EADirector,
    Vendor,
}

public enum ProposalStatus { Pending, UnderReview, AwaitingApproval, Approved, Rejected, Deferred, Superseded }

public enum ProjectStatus { Active, Completed, Cancelled, OnHold }

public enum AssignmentStatus { Active, Completed, Overdue, Declined, Failed }

public enum TaskType
{
    Investigate,
    DrAssessment,
    BackupVerification,
    VendorNegotiation,
    ProofOfConcept,
    CostReview,
    ClinicalAssessment,
}

public enum LedgerCategory { Revenue, Opex, Capex, Depreciation, Interest }

public enum SavingsStatus { Proposed, Approved, Realized, Reversed }

public enum IncidentStatus { Active, Resolved }

public enum ReviewRating { Unsatisfactory, BelowExpectations, MeetsExpectations, ExceedsExpectations }

public enum MessageKind { Briefing, Report, Escalation, Incident, Finance, Review, System }

public enum GameOutcome { InProgress, Terminated, Resigned }
