using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Core.Simulation;

namespace ExecutiveCommand.Core.Scenarios;

/// <summary>
/// Vertical slice: the player is CTO of a fictional regional health system with an aging EHR platform
/// and inadequate disaster recovery. Some facts are hidden (true recovery time, backup integrity,
/// whether the EHR vendor supports cloud DR) and vary with the seed, so there is no single scripted answer.
/// All organizations, people and figures are invented.
/// </summary>
public static class HealthcareCtoScenario
{
    public const string Id = "healthcare_cto_dr";
    public const string Title = "Meridian Crest Health Partners — CTO";

    public static GameState Create(ulong seed, Difficulty difficulty, string playerName = "Alex Morgan")
    {
        var rng = new DeterministicRng(seed);
        var start = new DateOnly(2027, 1, 4);
        var s = new GameState
        {
            ScenarioId = Id, Seed = seed, Difficulty = difficulty, StartDate = start, CurrentDate = start, FiscalYearStartMonth = 7,
            Company = new Company
            {
                Name = "Meridian Crest Health Partners", Industry = "Healthcare", Facilities = 6, Employees = 7400,
                Description = "A fictional nonprofit regional health system: four acute-care hospitals, a rehabilitation center and a network of outpatient clinics.",
                FictionNotice = "Meridian Crest Health Partners, its people, vendors and all figures are fictional. Any resemblance to real organizations is coincidental.",
            },
            Player = new PlayerCharacter
            {
                Name = playerName, Role = ExecRole.CTO, Title = "Chief Technology Officer", Salary = 385_000,
                CapexApprovalLimit = 250_000, OpexApprovalLimit = 150_000, DepartmentId = "tech",
            },
        };

        s.Executives.AddRange(new[]
        {
            new Executive { Id = "x-ceo", Role = ExecRole.CEO, Name = "Evelyn Hart", Title = "President & CEO", ReportsTo = ExecRole.None,
                Background = "Former hospital COO; eight years as CEO. Measured, board-focused, intolerant of surprises.",
                Responsibilities = { "Overall performance", "Strategy", "Board relationship" }, Priorities = { "patient safety", "operating margin", "board confidence" },
                Competence = 80, RiskTolerance = 40, Assertiveness = 65, Candor = 70, TrustInPlayer = 55 },
            new Executive { Id = "x-cfo", Role = ExecRole.CFO, Name = "Raymond Castellano", Title = "Chief Financial Officer", ReportsTo = ExecRole.CEO,
                Background = "Twenty years in healthcare finance. Has watched two failed IT programs blow through budgets.",
                Responsibilities = { "Budgets", "Cash flow", "Capital allocation", "Investment analysis" }, Priorities = { "margin", "evidence-based investment", "realized savings" },
                Biases = { "skeptical of IT estimates" }, Competence = 78, RiskTolerance = 30, Assertiveness = 70, Candor = 80, TrustInPlayer = 45, CapexApprovalLimit = 2_000_000 },
            new Executive { Id = "x-cio", Role = ExecRole.CIO, Name = "Daniel Whitaker", Title = "Chief Information Officer", ReportsTo = ExecRole.CTO,
                Background = "Long-tenured operations leader who came up through the data center. Loyal to the incumbent vendors.",
                Responsibilities = { "IT operations", "Service delivery", "Infrastructure", "Implementation" }, Priorities = { "stability", "team workload", "vendor relationships" },
                Biases = { "vendor-led", "prefers on-premises", "optimistic about recovery capability" },
                Competence = 58 + rng.NextInt(-6, 7), RiskTolerance = 55, Assertiveness = 55, Candor = 50, TrustInPlayer = 55, Workload = 72 },
            new Executive { Id = "x-ciso", Role = ExecRole.CISO, Name = "Priya Raman", Title = "Chief Information Security Officer", ReportsTo = ExecRole.CTO,
                Background = "Former incident responder. Direct, evidence-driven, has a dotted line to the audit committee.",
                Responsibilities = { "Information security", "Cyber risk", "Incident preparedness" }, Priorities = { "ransomware resilience", "verified backups", "governance" },
                Competence = 78 + rng.NextInt(-4, 5), RiskTolerance = 22, Assertiveness = 70, Candor = 90, TrustInPlayer = 50, Workload = 60 },
            new Executive { Id = "x-cmo", Role = ExecRole.CMO, Name = "Dr. Samuel Adeyemi", Title = "Chief Medical Officer", ReportsTo = ExecRole.CEO,
                Background = "Emergency physician; chairs the patient safety committee.",
                Responsibilities = { "Clinical leadership", "Medical quality", "Patient safety" }, Priorities = { "patient safety", "clinician workload" },
                Competence = 82, RiskTolerance = 20, Assertiveness = 60, Candor = 85, TrustInPlayer = 50, HasClinicalSafetyAuthority = true },
            new Executive { Id = "x-cno", Role = ExecRole.CNO, Name = "Lauren Mitchell", Title = "Chief Nursing Officer", ReportsTo = ExecRole.CEO,
                Background = "Former ICU nurse manager; fiercely protective of nursing staff in a tight labor market.",
                Responsibilities = { "Nursing operations", "Staffing", "Nursing-related patient safety" }, Priorities = { "nurse staffing", "workflow stability" },
                Competence = 76, RiskTolerance = 30, Assertiveness = 65, Candor = 80, TrustInPlayer = 50, Workload = 70 },
            new Executive { Id = "x-ea", Role = ExecRole.EADirector, Name = "Marcus Lindqvist", Title = "Director of Enterprise Architecture", ReportsTo = ExecRole.CTO,
                Background = "Joined two years ago from a cloud consultancy. Sharp, occasionally underestimates operational complexity.",
                Responsibilities = { "Technology strategy", "Architecture standards", "Modernization roadmap" }, Priorities = { "modernization", "reducing technical debt" },
                Biases = { "cloud-first" }, Competence = 72 + rng.NextInt(-5, 6), RiskTolerance = 65, Assertiveness = 55, Candor = 70, TrustInPlayer = 60, Workload = 50, Ambition = 75 },
            new Executive { Id = "x-vendor", Role = ExecRole.Vendor, Name = "Grant Feld", Title = "Account Executive, Halcyon Clinical Systems", ReportsTo = ExecRole.None,
                Background = "Fictional EHR vendor's account lead. Friendly, commercially driven, careful not to commit in writing.",
                Responsibilities = { "Account growth", "Renewals" }, Priorities = { "renewal value", "upsell" },
                Competence = 70, RiskTolerance = 50, Assertiveness = 60, Candor = 40, TrustInPlayer = 60 },
        });

        s.Departments.Add(new Department
        {
            Id = "tech", Name = "Technology", HeadRole = ExecRole.CTO, Headcount = 212, Morale = 58,
            BudgetLines =
            {
                Line("personnel", "Personnel & Benefits", 16_800_000, 16_800_000, "personnel", "salar", "staff", "headcount"),
                Line("software", "Software Licenses & Maintenance", 11_200_000, 10_650_000, "software", "licens"),
                Line("hardware_maint", "Hardware Maintenance", 2_600_000, 2_100_000, "hardware", "maintenance"),
                Line("cloud", "Cloud & Hosting", 1_900_000, 1_520_000, "cloud", "hosting"),
                Line("consulting", "Consulting & Contractors", 3_400_000, 1_650_000, "consult", "contractor"),
                Line("training", "Training & Development", 620_000, 110_000, "training", "development", "education"),
                Line("travel", "Travel & Discretionary", 380_000, 60_000, "travel", "discretionary", "conference"),
            },
        });

        // July–December of the current fiscal year already happened under the previous CTO: on plan, slightly over.
        foreach (var l in s.Departments[0].BudgetLines) l.ActualFiscalYtd = Math.Round(l.AnnualBudget / 2m * (l.Id == "personnel" ? 1m : 1.01m), 0);

        decimal existingAssets = 620_000_000m, existingDep = 310_000_000m;
        s.Finance = new FinancialState
        {
            Cash = 212_000_000m, Debt = 280_000_000m, AnnualInterestRate = 0.045m,
            GrossPpe = existingAssets, AccumulatedDepreciation = existingDep,
            MonthlyRevenueBase = 98_300_000m, MonthlyOtherOpex = 90_420_000m,
            TechCapitalBudgetAnnual = 6_000_000m, TechCapitalBudgetRemaining = 1_900_000m,
            Assets = { new CapitalAsset { Id = "AST-BASE", Name = "Facilities, clinical equipment & existing IT", Cost = existingAssets, AccumulatedDepreciation = existingDep, UsefulLifeMonths = 240, InService = true, InServiceDate = new DateOnly(2012, 1, 1) } },
        };
        s.Finance.OpeningEquity = s.Finance.Cash + s.Finance.NetPpe - s.Finance.Debt;

        // Ground truth varies by seed. The organization believes the 2022 test result (24 h) still holds.
        double actualRto = 60 + rng.NextInt(0, 25);
        s.Technology = new TechnologyState
        {
            SecurityPosture = 52 + rng.NextInt(-4, 5), TechnicalDebt = 64,
            ActualRtoHours = actualRto, ReportedRtoHours = 24, TargetRtoHours = 4,
            ActualRpoHours = 24, ReportedRpoHours = 4, TargetRpoHours = 1,
            BackupRestoreSuccess = Math.Round(0.7 + rng.NextDouble() * 0.15, 2),
            CloudDrVendorCertified = rng.NextDouble() < 0.5,
            Systems =
            {
                new TechSystem { Id = "sys-ehr", Name = "Halcyon Ascend EHR (release 9)", Category = "ehr", AgeYears = 8, VendorSupportEnds = new DateOnly(2028, 6, 30), ClinicallyCritical = true, AvailabilityPct90d = 99.82 },
                new TechSystem { Id = "sys-lab", Name = "Laboratory interface engine", Category = "interface", AgeYears = 7, ClinicallyCritical = true, AvailabilityPct90d = 99.9 },
                new TechSystem { Id = "sys-san", Name = "Primary storage array", Category = "storage", AgeYears = 6, Redundant = false, AvailabilityPct90d = 99.95 },
                new TechSystem { Id = "sys-bkp", Name = "Backup platform", Category = "backup", AgeYears = 5 },
                new TechSystem { Id = "sys-idm", Name = "Identity & directory services", Category = "identity", AgeYears = 9, AvailabilityPct90d = 99.7 },
            },
        };

        s.Metrics = new OrgMetrics { BoardConfidence = 58, EmployeeMorale = 60, ClinicalSafetyIndex = 82 };

        s.Proposals.AddRange(new[]
        {
            new Proposal { Id = "PRP-ID", Title = "Identity infrastructure expansion", Sponsor = ExecRole.CIO, Topic = Topics.Identity,
                Summary = "Add directory servers and authentication capacity to address login slowness at peak shift change.",
                Keywords = { "identity", "active directory", "authentication", "login", "expansion", "180" },
                Capex = 180_000, AnnualOpex = 24_000, DurationDays = 45, EffectKey = "identity_expansion", SecurityRiskReduction = 4, TechDebtReduction = 0,
                RequiredEvidence = { "investigate" } },
            new Proposal { Id = "PRP-DRO", Title = "DR option A: secondary on-premises data center", Sponsor = ExecRole.CIO, Topic = Topics.DisasterRecovery,
                Summary = "Build out a secondary data center at the North campus with replicated storage and a warm EHR environment.",
                Keywords = { "on-prem", "on prem", "premises", "secondary data center", "option a", "data center", "north campus" },
                Capex = 2_600_000, AnnualOpex = 420_000, DurationDays = 240, EffectKey = "dr_onprem", ClinicalRiskReduction = 12, SecurityRiskReduction = 2, ClinicallySignificant = true,
                RequiredEvidence = { "dr_assessment", "clinical_assessment" } },
            new Proposal { Id = "PRP-DRC", Title = "DR option B: cloud-first disaster recovery", Sponsor = ExecRole.EADirector, Topic = Topics.DisasterRecovery,
                Summary = "Replicate the EHR and supporting systems to a public cloud region with automated failover runbooks.",
                Keywords = { "cloud", "cloud-first", "cloud first", "option b" },
                Capex = 200_000, AnnualOpex = 650_000, DurationDays = 120, EffectKey = "dr_cloud", ClinicalRiskReduction = 12, SecurityRiskReduction = 0, TechDebtReduction = 8, ClinicallySignificant = true,
                RequiredEvidence = { "dr_assessment", "proof_of_concept", "clinical_assessment" } },
            new Proposal { Id = "PRP-DRH", Title = "DR option C: hybrid colocation with cloud backup tier", Sponsor = ExecRole.EADirector, Topic = Topics.DisasterRecovery,
                Summary = "Lease colocation space for a warm EHR environment; tier backups and non-critical systems to the cloud.",
                Keywords = { "hybrid", "colocation", "colo", "option c" },
                Capex = 1_100_000, AnnualOpex = 260_000, DurationDays = 150, EffectKey = "dr_hybrid", ClinicalRiskReduction = 12, SecurityRiskReduction = 3, TechDebtReduction = 4, ClinicallySignificant = true,
                RequiredEvidence = { "dr_assessment", "clinical_assessment" } },
            new Proposal { Id = "PRP-BKP", Title = "Immutable backup vault", Sponsor = ExecRole.CISO, Topic = Topics.Backup,
                Summary = "Air-gapped, immutable backup storage with automated restore testing for critical systems.",
                Keywords = { "immutable", "backup", "vault", "air-gap", "air gap" },
                Capex = 240_000, AnnualOpex = 60_000, DurationDays = 60, EffectKey = "immutable_backup", SecurityRiskReduction = 12, ClinicalRiskReduction = 4,
                RequiredEvidence = { "backup_verification" } },
        });

        s.Risks.AddRange(new[]
        {
            new RiskItem { Id = "RSK-DR", Title = "Disaster recovery capability below target", Topic = Topics.DisasterRecovery, Owner = ExecRole.CIO, Likelihood = 3, Impact = 5, Identified = start.AddDays(-200),
                Description = "Last full DR test (2022) recovered the EHR in 24 hours against a 4-hour objective. No secondary site exists." },
            new RiskItem { Id = "RSK-EOS", Title = "EHR release approaching end of vendor support", Topic = Topics.EhrContract, Owner = ExecRole.CIO, Likelihood = 3, Impact = 4, Identified = start.AddDays(-90),
                Description = "Standard support for the current EHR release ends June 2028." },
            new RiskItem { Id = "RSK-RAN", Title = "Ransomware exposure", Topic = Topics.Security, Owner = ExecRole.CISO, Likelihood = 3, Impact = 5, Identified = start.AddDays(-300),
                Description = "Healthcare is heavily targeted; backups share the production network." },
            new RiskItem { Id = "RSK-BKP", Title = "Backup sets failing restore", Topic = Topics.Backup, Owner = ExecRole.CISO, Likelihood = 4, Impact = 5, Known = false, Identified = start,
                Description = "Restore testing shows several critical backup sets cannot be restored reliably. Recovery objectives cannot be met and ransomware recovery is at risk." },
            new RiskItem { Id = "RSK-SAN", Title = "Single non-redundant storage array", Topic = Topics.DisasterRecovery, Owner = ExecRole.CIO, Likelihood = 2, Impact = 5, Identified = start.AddDays(-400),
                Description = "The primary storage array serving the EHR is six years old with no redundant controller pair." },
        });

        s.Contracts.Add(new VendorContract
        {
            Id = "CON-EHR", Vendor = "Halcyon Clinical Systems", Service = "EHR licensing & support", AnnualCost = 6_400_000m, BudgetLineId = "software",
            RenewalDate = new DateOnly(2027, 4, 30), SlaAvailabilityPct = 99.5, ProposedUpliftPct = 0.07,
        });

        s.Schedule.AddRange(new[]
        {
            new ScheduledEvent { Id = "EVT-1", Date = start.AddDays(2), Kind = "cfo_savings_memo" },
            new ScheduledEvent { Id = "EVT-2", Date = start.AddDays(16), Kind = "vendor_eos_notice" },
            new ScheduledEvent { Id = "EVT-3", Date = new DateOnly(2027, 3, 31), Kind = "contract_reminder", RelatedId = "CON-EHR" },
            new ScheduledEvent { Id = "EVT-4", Date = new DateOnly(2027, 4, 30), Kind = "contract_renewal", RelatedId = "CON-EHR" },
        });

        s.Inbox.Add(new InboxMessage
        {
            Id = s.NewId("MSG"), Date = start, Kind = MessageKind.Briefing, From = ExecRole.CEO, Subject = "Welcome — and what I need from you",
            Body = "Welcome to Meridian Crest. You inherit a technology organization that keeps the lights on but hasn't modernized. " +
                   "The board's audit committee has asked twice about our ability to recover the EHR after a disaster; our last test took 24 hours against a 4-hour target. " +
                   "Daniel (CIO), Priya (CISO) and Marcus (Enterprise Architecture) report to you. Three DR options and an identity expansion request are waiting for a decision, " +
                   "and the Halcyon EHR contract renews April 30. I expect recommendations grounded in evidence, not enthusiasm. — Evelyn",
        });
        s.History.Add(FinancialEngine.Snapshot(s, null)); // baseline for trends
        ProgressionEngine.InitializeCampaign(s);
        s.RngState = rng.State;
        return s;
    }

    private static BudgetLine Line(string id, string name, decimal annual, decimal committed, params string[] keywords) =>
        new() { Id = id, Name = name, AnnualBudget = annual, CommittedAnnual = committed, Keywords = keywords.ToList() };
}
