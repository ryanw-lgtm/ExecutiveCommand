using System.Text.Json.Serialization;

namespace ExecutiveCommand.Core.Model;

/// <summary>
/// The complete authoritative state of one game. Everything the simulation knows lives here so that
/// it can be serialized, checksummed and reloaded exactly. UI and AI layers only read from this; all
/// mutations go through <see cref="Engine.SimulationEngine"/>.
/// </summary>
public sealed class GameState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string ScenarioId { get; set; } = "";
    public ulong Seed { get; set; }
    public ulong RngState { get; set; }
    public Difficulty Difficulty { get; set; } = Difficulty.Professional;
    public DateOnly StartDate { get; set; }
    public DateOnly CurrentDate { get; set; }
    /// <summary>Calendar month in which the fiscal year starts (healthcare commonly uses July).</summary>
    public int FiscalYearStartMonth { get; set; } = 7;
    public int NextId { get; set; } = 1;
    public GameOutcome Outcome { get; set; } = GameOutcome.InProgress;

    public Company Company { get; set; } = new();
    public PlayerCharacter Player { get; set; } = new();
    public List<Executive> Executives { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public FinancialState Finance { get; set; } = new();
    public TechnologyState Technology { get; set; } = new();
    public OrgMetrics Metrics { get; set; } = new();

    public List<Proposal> Proposals { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public List<Assignment> Assignments { get; set; } = new();
    public List<SavingsInitiative> Savings { get; set; } = new();
    public List<RiskItem> Risks { get; set; } = new();
    public List<Incident> Incidents { get; set; } = new();
    public List<VendorContract> Contracts { get; set; } = new();

    public List<DecisionRecord> Decisions { get; set; } = new();
    public List<MemoryRecord> Memory { get; set; } = new();
    public List<InboxMessage> Inbox { get; set; } = new();
    public List<MeetingTurn> MeetingLog { get; set; } = new();
    public List<PerformanceReview> Reviews { get; set; } = new();
    public List<ScheduledEvent> Schedule { get; set; } = new();
    /// <summary>Month-end snapshots of headline metrics, for trends and sparklines. Older saves start empty.</summary>
    public List<MetricSnapshot> History { get; set; } = new();

    public string NewId(string prefix) => $"{prefix}-{NextId++:D4}";

    public Executive Exec(ExecRole role) =>
        Executives.FirstOrDefault(e => e.Role == role) ?? throw new InvalidOperationException($"No executive with role {role}");

    public Executive? TryExec(ExecRole role) => Executives.FirstOrDefault(e => e.Role == role);
}

public sealed class Company
{
    public string Name { get; set; } = "";
    public string Industry { get; set; } = "Healthcare";
    public string Description { get; set; } = "";
    public int Facilities { get; set; }
    public int Employees { get; set; }
    /// <summary>Disclaimer shown in UI: all entities are fictional.</summary>
    public string FictionNotice { get; set; } = "";
}

public sealed class PlayerCharacter
{
    public string Name { get; set; } = "";
    public ExecRole Role { get; set; } = ExecRole.CTO;
    public string Title { get; set; } = "";
    public decimal Salary { get; set; }
    /// <summary>Single capital purchase the player may approve without CFO/CEO sign-off.</summary>
    public decimal CapexApprovalLimit { get; set; }
    /// <summary>Annual recurring opex commitment the player may approve alone.</summary>
    public decimal OpexApprovalLimit { get; set; }
    public string DepartmentId { get; set; } = "";
    public int Experience { get; set; }
    public double Reputation { get; set; } = 50;
    public List<string> Achievements { get; set; } = new();
}

public sealed class Executive
{
    public string Id { get; set; } = "";
    public ExecRole Role { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public ExecRole ReportsTo { get; set; }
    public string Background { get; set; } = "";
    public List<string> Responsibilities { get; set; } = new();
    public List<string> Priorities { get; set; } = new();
    public List<string> Biases { get; set; } = new();

    // Traits, 0–100
    public double Competence { get; set; } = 60;
    public double RiskTolerance { get; set; } = 50;
    public double Assertiveness { get; set; } = 50;
    public double Ambition { get; set; } = 50;
    public double Candor { get; set; } = 60;

    // Dynamic, 0–100
    public double TrustInPlayer { get; set; } = 50;
    public double Workload { get; set; } = 50;
    public double Morale { get; set; } = 65;

    /// <summary>True when a clinical-safety hold by this executive is binding on technology changes.</summary>
    public bool HasClinicalSafetyAuthority { get; set; }
    public decimal CapexApprovalLimit { get; set; }
}

public sealed class Department
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ExecRole HeadRole { get; set; }
    public int Headcount { get; set; }
    public double Morale { get; set; } = 65;
    public List<BudgetLine> BudgetLines { get; set; } = new();
}

public sealed class BudgetLine
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public decimal AnnualBudget { get; set; }
    /// <summary>Portion of the annual budget already locked by contracts, salaries etc.</summary>
    public decimal CommittedAnnual { get; set; }
    /// <summary>Sum of approved reductions against this line (annualized). Prevents double-counting.</summary>
    public decimal ApprovedReductionsAnnual { get; set; }
    /// <summary>Discretionary dollars earmarked for delegated assignments (spent within the line, cannot be cut).</summary>
    public decimal EarmarkedAnnual { get; set; }
    public decimal ActualFiscalYtd { get; set; }

    [JsonIgnore] public decimal EffectiveAnnualBudget => AnnualBudget - ApprovedReductionsAnnual;
    /// <summary>Discretionary dollars that can still be cut.</summary>
    [JsonIgnore] public decimal DiscretionaryRemaining => Math.Max(0, AnnualBudget - CommittedAnnual - EarmarkedAnnual - ApprovedReductionsAnnual);
}

public sealed class FinancialState
{
    public decimal Cash { get; set; }
    public decimal Debt { get; set; }
    public decimal AnnualInterestRate { get; set; }
    public decimal GrossPpe { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    /// <summary>Equity at game start; current equity = this + retained earnings since start.</summary>
    public decimal OpeningEquity { get; set; }
    public decimal RetainedEarningsSinceStart { get; set; }

    public decimal MonthlyRevenueBase { get; set; }
    /// <summary>Non-technology operating costs of the whole organization per month (abstracted).</summary>
    public decimal MonthlyOtherOpex { get; set; }
    /// <summary>Remaining capital budget for the current fiscal year (organization-wide pool for technology).</summary>
    public decimal TechCapitalBudgetRemaining { get; set; }
    public decimal TechCapitalBudgetAnnual { get; set; }

    public List<CapitalAsset> Assets { get; set; } = new();
    public List<LedgerEntry> Ledger { get; set; } = new();
    public List<MonthlyStatement> Statements { get; set; } = new();
    /// <summary>Revenue lost to incidents, booked in the next monthly close.</summary>
    public decimal PendingRevenueLoss { get; set; }
    /// <summary>One-off incident costs (overtime, recovery) booked in the next monthly close.</summary>
    public decimal PendingIncidentCost { get; set; }

    [JsonIgnore] public decimal NetPpe => GrossPpe - AccumulatedDepreciation;
    [JsonIgnore] public decimal Equity => OpeningEquity + RetainedEarningsSinceStart;
}

public sealed class CapitalAsset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Cost { get; set; }
    public int UsefulLifeMonths { get; set; } = 60;
    public DateOnly InServiceDate { get; set; }
    public decimal AccumulatedDepreciation { get; set; }
    public string? ProjectId { get; set; }
    /// <summary>Asset still under construction (project not complete): not depreciated yet.</summary>
    public bool InService { get; set; }
}

public sealed class LedgerEntry
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public LedgerCategory Category { get; set; }
    /// <summary>Positive values: revenue for Revenue, cost for every other category.</summary>
    public decimal Amount { get; set; }
    public string Account { get; set; } = "";
    public string? DepartmentId { get; set; }
    public string? SourceId { get; set; }
    public string Memo { get; set; } = "";
}

public sealed class MonthlyStatement
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Revenue { get; set; }
    public decimal Opex { get; set; }
    public decimal TechOpex { get; set; }
    public decimal Depreciation { get; set; }
    public decimal Interest { get; set; }
    public decimal Capex { get; set; }
    public decimal NetIncome { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ClosingCash { get; set; }
    public decimal RevenueLostToIncidents { get; set; }
    [JsonIgnore] public decimal OperatingIncome => Revenue - Opex - Depreciation;
    [JsonIgnore] public double OperatingMargin => Revenue == 0 ? 0 : (double)(OperatingIncome / Revenue);
}

public sealed class TechnologyState
{
    public List<TechSystem> Systems { get; set; } = new();
    public double SecurityPosture { get; set; } = 55;
    public double TechnicalDebt { get; set; } = 60;
    /// <summary>Ground-truth recovery time objective actually achievable today (hours). Hidden from the player.</summary>
    public double ActualRtoHours { get; set; }
    /// <summary>Recovery time the organization believes it has. Revealed/corrected through assessments.</summary>
    public double ReportedRtoHours { get; set; }
    public double TargetRtoHours { get; set; } = 4;
    public double ActualRpoHours { get; set; }
    public double ReportedRpoHours { get; set; }
    public double TargetRpoHours { get; set; } = 1;
    /// <summary>Ground-truth fraction of backup sets that actually restore. Hidden.</summary>
    public double BackupRestoreSuccess { get; set; }
    public bool BackupsVerified { get; set; }
    public double? MeasuredBackupRestoreSuccess { get; set; }
    public bool ImmutableBackups { get; set; }
    public bool HasSecondarySite { get; set; }
    /// <summary>Hidden: whether the EHR vendor will support running the EHR in public cloud DR.</summary>
    public bool CloudDrVendorCertified { get; set; }
    public bool? CloudDrCertificationKnown { get; set; }
    public bool ProofOfConceptCompleted { get; set; }
    /// <summary>Used to space out major incidents so the game keeps a believable operational rhythm.</summary>
    public DateOnly? LastMajorIncident { get; set; }
}

public sealed class TechSystem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double AgeYears { get; set; }
    public DateOnly? VendorSupportEnds { get; set; }
    public bool Redundant { get; set; }
    public double AvailabilityPct90d { get; set; } = 99.9;
    public bool ClinicallyCritical { get; set; }
}

public sealed class OrgMetrics
{
    public double BoardConfidence { get; set; } = 60;
    public double EmployeeMorale { get; set; } = 62;
    public double ClinicalSafetyIndex { get; set; } = 80;
    public int PatientSafetyEvents { get; set; }
}

public sealed class Proposal
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Keywords { get; set; } = new();
    public ExecRole Sponsor { get; set; }
    public decimal Capex { get; set; }
    public decimal AnnualOpex { get; set; }
    public int DurationDays { get; set; }
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
    public string Topic { get; set; } = "";
    /// <summary>Key into <see cref="Engine.ProjectEffects"/> applied when the resulting project completes.</summary>
    public string EffectKey { get; set; } = "";
    public double SecurityRiskReduction { get; set; }
    public double ClinicalRiskReduction { get; set; }
    public double TechDebtReduction { get; set; }
    public DateOnly? DeferredUntil { get; set; }
    public string? ApprovalDecisionDue { get; set; }
    public DateOnly? ApprovalDueDate { get; set; }
    public bool ClinicallySignificant { get; set; }
    public string? SupersedesId { get; set; }
    public List<string> RequiredEvidence { get; set; } = new();
}

public sealed class Project
{
    public string Id { get; set; } = "";
    public string ProposalId { get; set; } = "";
    public string Name { get; set; } = "";
    public ExecRole Owner { get; set; }
    public decimal BudgetCapex { get; set; }
    public decimal SpentCapex { get; set; }
    public decimal AnnualOpex { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly PlannedEndDate { get; set; }
    public DateOnly? ActualEndDate { get; set; }
    public double Progress { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Active;
    public string EffectKey { get; set; } = "";
    public List<Milestone> Milestones { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public string? DecisionId { get; set; }
    public string? HeldBy { get; set; }
    public int DaysOnHold { get; set; }
    public bool CutoverIncidentTriggered { get; set; }
    public string ExpectedBenefits { get; set; } = "";
    public string ActualOutcome { get; set; } = "";
}

public sealed class Milestone
{
    public string Name { get; set; } = "";
    public double AtProgress { get; set; }
    public DateOnly? ReachedOn { get; set; }
}

public sealed class Assignment
{
    public string Id { get; set; } = "";
    public ExecRole Assignee { get; set; }
    public TaskType Type { get; set; }
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public DateOnly Created { get; set; }
    public DateOnly Due { get; set; }
    public decimal AuthorizedBudget { get; set; }
    public decimal Spent { get; set; }
    /// <summary>Total effort in work-days needed (hidden difficulty).</summary>
    public double EffortRequired { get; set; }
    public double EffortDone { get; set; }
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Active;
    /// <summary>True when the task was a request to a peer/superior rather than a delegation to a report.</summary>
    public bool IsRequest { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public string? ReportMessageId { get; set; }
    public string? DecisionId { get; set; }
    public bool OverdueNotified { get; set; }
    public List<string> Findings { get; set; } = new();
    /// <summary>Accuracy 0–1 the executive achieved; drives how close findings are to the truth.</summary>
    public double? Accuracy { get; set; }
}

public sealed class SavingsInitiative
{
    public string Id { get; set; } = "";
    public string DepartmentId { get; set; } = "";
    public string BudgetLineId { get; set; } = "";
    public decimal AnnualAmount { get; set; }
    public bool Recurring { get; set; }
    public SavingsStatus Status { get; set; } = SavingsStatus.Approved;
    public DateOnly EffectiveDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal RealizedToDate { get; set; }
    public string Reason { get; set; } = "";
    public string? DecisionId { get; set; }
    public string Source { get; set; } = "budget_cut";
}

public sealed class RiskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ExecRole Owner { get; set; }
    /// <summary>1–5 scales.</summary>
    public int Likelihood { get; set; }
    public int Impact { get; set; }
    public bool Known { get; set; } = true;
    public bool Closed { get; set; }
    public DateOnly Identified { get; set; }
    public string Topic { get; set; } = "";
    public int Score => Likelihood * Impact;
}

public sealed class Incident
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly Started { get; set; }
    public DateOnly? Resolved { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Active;
    public double DowntimeHours { get; set; }
    public double DataLossHours { get; set; }
    public decimal RevenueImpact { get; set; }
    public decimal RecoveryCost { get; set; }
    public int SafetyEvents { get; set; }
    public string? ProjectId { get; set; }
    /// <summary>Allowed responses for incidents that require a player decision (e.g. cutover issues).</summary>
    public List<string> ResponseOptions { get; set; } = new();
    public string? ChosenResponse { get; set; }
    public string? HiddenRootCause { get; set; }
    public double MessageDelayPct { get; set; }
    public int DaysActive { get; set; }
    public bool ClinicalAssessmentRequested { get; set; }
    public bool ClinicalHold { get; set; }
    public bool Fixed { get; set; }
    public List<string> Timeline { get; set; } = new();
}

public sealed class VendorContract
{
    public string Id { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Service { get; set; } = "";
    public decimal AnnualCost { get; set; }
    public string BudgetLineId { get; set; } = "";
    public DateOnly RenewalDate { get; set; }
    public double SlaAvailabilityPct { get; set; }
    public bool HasDrSupportClause { get; set; }
    public bool Negotiated { get; set; }
    /// <summary>Price change the vendor proposes at renewal (e.g. 0.07 = +7%).</summary>
    public double ProposedUpliftPct { get; set; }
    /// <summary>Price change agreed through negotiation; null until negotiated.</summary>
    public double? AgreedUpliftPct { get; set; }
    public bool Renewed { get; set; }
}

public sealed class DecisionRecord
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Action { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
    public string Summary { get; set; } = "";
    public string PlayerText { get; set; } = "";
    public string Topic { get; set; } = "";
    public DecisionEvaluation Evaluation { get; set; } = new();
    /// <summary>Filled in when consequences materialize. Separate from decision quality by design.</summary>
    public double? OutcomeScore { get; set; }
    public List<string> OutcomeNotes { get; set; } = new();
}

public sealed class DecisionEvaluation
{
    public double Evidence { get; set; }
    public double Stakeholders { get; set; }
    public double Financial { get; set; }
    public double Risk { get; set; }
    public double Authority { get; set; }
    public double Compliance { get; set; }
    public double Overall { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class MemoryRecord
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<ExecRole> Involved { get; set; } = new();
    public string Text { get; set; } = "";
    public double Weight { get; set; } = 1;
}

public sealed class InboxMessage
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public MessageKind Kind { get; set; }
    public ExecRole From { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Read { get; set; }
    public string? RelatedId { get; set; }
}

public sealed class MeetingTurn
{
    public DateOnly Date { get; set; }
    public string MeetingId { get; set; } = "";
    /// <summary>ExecRole.None means the player.</summary>
    public ExecRole Speaker { get; set; }
    public string Text { get; set; } = "";
    public string Topic { get; set; } = "";
    public bool AiGenerated { get; set; }
}

public sealed class PerformanceReview
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Period { get; set; } = "";
    public double DecisionQuality { get; set; }
    public double Delivery { get; set; }
    public double FinancialStewardship { get; set; }
    public double RiskManagement { get; set; }
    public double Overall { get; set; }
    public ReviewRating Rating { get; set; }
    public List<string> Evidence { get; set; } = new();
    public string ReviewerComment { get; set; } = "";
}

public sealed class MetricSnapshot
{
    public DateOnly Date { get; set; }
    public decimal Cash { get; set; }
    public double? OperatingMargin { get; set; }
    public decimal TechOpex { get; set; }
    public double BoardConfidence { get; set; }
    public double Morale { get; set; }
    public int AssessedRisk { get; set; }
    public decimal TechCapitalRemaining { get; set; }
}

public sealed class ScheduledEvent
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Kind { get; set; } = "";
    public string? RelatedId { get; set; }
    public bool Fired { get; set; }
}
