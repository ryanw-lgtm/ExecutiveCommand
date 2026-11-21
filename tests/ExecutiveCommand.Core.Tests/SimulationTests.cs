using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Core.Persistence;
using ExecutiveCommand.Core.Scenarios;
using Xunit;
using static ExecutiveCommand.Core.Tests.TestUtil;

namespace ExecutiveCommand.Core.Tests;

public class DeterminismTests
{
    private static string Play(ulong seed)
    {
        var e = NewGame(seed);
        e.Submit(A("delegate_assignment", new { assignee_role = "CIO", task_type = "dr_assessment", subject = "Measure real EHR recovery time", deadline_days = 30 }));
        e.Submit(A("reduce_budget", new { budget_line = "travel", annual_amount = 150_000, recurring = true }));
        e.AdvanceDays(40);
        e.Submit(A("approve_proposal", new { proposal_id = "PRP-BKP" }), confirmed: true);
        e.AdvanceDays(200);
        return StateSerializer.StateHash(e.State);
    }

    [Fact]
    public void SameSeedAndDecisionsProduceIdenticalState() => Assert.Equal(Play(7), Play(7));

    [Fact]
    public void DifferentSeedsProduceDifferentWorlds() => Assert.NotEqual(Play(7), Play(8));

    [Fact]
    public void SaveMidGameThenContinueMatchesUninterruptedRun()
    {
        var a = NewGame(99);
        a.AdvanceDays(50);
        var json = StateSerializer.Serialize(a.State);
        a.AdvanceDays(100);

        var b = new SimulationEngine(StateSerializer.Deserialize(json));
        b.AdvanceDays(100);
        Assert.Equal(StateSerializer.StateHash(a.State), StateSerializer.StateHash(b.State));
    }
}

public class FinanceTests
{
    [Fact]
    public void BalanceSheetReconcilesEveryMonthForTwoYears()
    {
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var e = NewGame(seed, Difficulty.Executive);
            e.Submit(A("approve_proposal", new { proposal_id = "PRP-BKP" }), confirmed: true);
            e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRH" }), confirmed: true); // routed to CFO/CEO
            e.Submit(A("reduce_budget", new { budget_line = "consulting", annual_amount = 200_000, recurring = false }), confirmed: true);
            for (int m = 0; m < 24 && e.State.Outcome == GameOutcome.InProgress; m++)
            {
                e.AdvanceDays(30);
                if (e.State.Incidents.FirstOrDefault(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0) is { } inc)
                    e.Submit(A("incident_response", new { incident_id = inc.Id, response = "rollback" }), confirmed: true);
                FinancialEngine.AssertReconciles(e.State); // throws on mismatch
            }
            Assert.True(e.State.Finance.Statements.Count >= 12);
        }
    }

    [Fact]
    public void BudgetCutCannotExceedDiscretionarySpend()
    {
        var e = NewGame();
        var line = e.State.Departments[0].BudgetLines.First(l => l.Id == "travel"); // 380k budget, 60k committed
        var r = e.Submit(A("reduce_budget", new { budget_line = "travel", annual_amount = 400_000 }), confirmed: true);
        Assert.False(r.Executed);
        Assert.Equal(0, line.ApprovedReductionsAnnual);
    }

    [Fact]
    public void SameDollarsCannotBeCutTwice()
    {
        var e = NewGame();
        Assert.True(e.Submit(A("reduce_budget", new { budget_line = "travel", annual_amount = 300_000 }), confirmed: true).Executed);
        var second = e.Submit(A("reduce_budget", new { budget_line = "travel", annual_amount = 50_000 }), confirmed: true);
        Assert.False(second.Executed);
        Assert.Contains("not achievable", second.Validation.Errors[0]);
    }

    [Fact]
    public void SavingsMoveFromApprovedToRealizedAndLowerActualSpend()
    {
        var e = NewGame();
        e.AdvanceDays(27); // close January without cuts
        var janTech = e.State.Finance.Statements[^1].TechOpex;
        e.Submit(A("reduce_budget", new { budget_line = "travel", annual_amount = 240_000, recurring = true }), confirmed: true);
        var sav = e.State.Savings.Single();
        Assert.Equal(SavingsStatus.Approved, sav.Status);
        Assert.Equal(0, sav.RealizedToDate);
        e.AdvanceDays(28); // February close: effective Feb 1
        Assert.Equal(SavingsStatus.Realized, sav.Status);
        Assert.Equal(20_000m, sav.RealizedToDate);
        Assert.True(e.State.Finance.Statements[^1].TechOpex < janTech);
    }

    [Fact]
    public void DelegationBudgetIsEarmarkedAndCannotAlsoBeCut()
    {
        var e = NewGame();
        var consulting = e.State.Departments[0].BudgetLines.First(l => l.Id == "consulting");
        decimal before = consulting.DiscretionaryRemaining;
        Assert.True(e.Submit(A("delegate_assignment", new { assignee_role = "CIO", task_type = "cost_review", subject = "Find savings", deadline_days = 30, authorized_budget = 100_000 })).Executed);
        Assert.Equal(before - 100_000, consulting.DiscretionaryRemaining);
        Assert.False(e.Submit(A("reduce_budget", new { budget_line = "consulting", annual_amount = before }), confirmed: true).Executed);
    }
}

public class AuthorityTests
{
    [Fact]
    public void UnknownActionsAreRejected()
    {
        var e = NewGame();
        var before = MaterialHash(e.State);
        var r = e.Submit(A("grant_money", new { amount = 100_000_000 }), confirmed: true);
        Assert.False(r.Executed);
        Assert.Equal(before, MaterialHash(e.State));
    }

    [Fact]
    public void UnexpectedParametersAreRejected()
    {
        var e = NewGame();
        var r = e.Submit(A("approve_proposal", new { proposal_id = "PRP-ID", capex_override = 0 }), confirmed: true);
        Assert.False(r.Executed);
    }

    [Fact]
    public void CannotAssignWorkToPeers()
    {
        var e = NewGame();
        var r = e.Validate(A("delegate_assignment", new { assignee_role = "CMO", task_type = "clinical_assessment", subject = "DR impact", deadline_days = 14 }));
        Assert.Equal(ValidationStatus.Invalid, r.Status);
        Assert.Equal("request_assessment", r.Suggestion);
    }

    [Fact]
    public void LargeCapitalRequestsAreRoutedNotApproved()
    {
        var e = NewGame();
        var v = e.Validate(A("approve_proposal", new { proposal_id = "PRP-DRO" }));
        Assert.Equal(ValidationStatus.NeedsConfirmation, v.Status);
        var r = e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRO" }), confirmed: true);
        Assert.True(r.Executed);
        Assert.Equal(ProposalStatus.AwaitingApproval, e.State.Proposals.First(p => p.Id == "PRP-DRO").Status);
        Assert.Empty(e.State.Projects);
    }

    [Fact]
    public void WithoutEvidenceTheCfoAndCeoRejectTheExpensiveOption()
    {
        int rejected = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var e = NewGame(seed);
            e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRO" }), confirmed: true);
            e.AdvanceDays(8);
            if (e.State.Proposals.First(p => p.Id == "PRP-DRO").Status == ProposalStatus.Rejected) rejected++;
        }
        Assert.True(rejected >= 8, $"Only {rejected}/10 unsupported $2.6M requests were rejected");
    }

    [Fact]
    public void ApprovingOneDrOptionSupersedesTheOthers()
    {
        var e = NewGame();
        e.State.Finance.TechCapitalBudgetRemaining = 5_000_000; // within capital; still above player's $250k limit
        e.State.Player.CapexApprovalLimit = 2_000_000; e.State.Player.OpexApprovalLimit = 500_000;
        Assert.True(e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRH" }), confirmed: true).Executed);
        Assert.Equal(ProposalStatus.Superseded, e.State.Proposals.First(p => p.Id == "PRP-DRO").Status);
        Assert.Equal(ProposalStatus.Superseded, e.State.Proposals.First(p => p.Id == "PRP-DRC").Status);
        Assert.False(e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRC" }), confirmed: true).Executed);
    }
}

public class EvaluationTests
{
    [Fact]
    public void EvidenceAndConsultationRaiseDecisionQuality()
    {
        var hasty = NewGame(5);
        hasty.Submit(A("approve_proposal", new { proposal_id = "PRP-ID" }), confirmed: true);
        double hastyScore = hasty.State.Decisions.Last().Evaluation.Overall;

        var careful = NewGame(5);
        careful.Submit(A("delegate_assignment", new { assignee_role = "EADirector", task_type = "investigate", subject = "Identity capacity and alternatives", deadline_days = 30 }));
        careful.RecordConsultation(ExecRole.CISO, Topics.Identity);
        careful.AdvanceDays(35);
        var id = careful.State.Proposals.FirstOrDefault(p => p.EffectKey == "identity_optimization")?.Id ?? "PRP-ID";
        careful.Submit(A("approve_proposal", new { proposal_id = id }), confirmed: true);
        Assert.True(careful.State.Decisions.Last().Evaluation.Overall > hastyScore + 10,
            $"careful {careful.State.Decisions.Last().Evaluation.Overall} vs hasty {hastyScore}");
    }

    [Fact]
    public void ReviewsListTheirEvidence()
    {
        var e = NewGame();
        e.AdvanceDays(90); // through fiscal Q3 end (Mar 31)
        var r = Assert.Single(e.State.Reviews);
        Assert.NotEmpty(r.Evidence);
        Assert.Contains(r.Evidence, x => x.Contains("No material decisions"));
        Assert.True(r.Rating <= ReviewRating.MeetsExpectations); // doing nothing is not rewarded
    }
}

public class IncidentTests
{
    private static (SimulationEngine e, Incident inc) WithCutoverIncident()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var e = NewGame(seed);
            e.State.Finance.TechCapitalBudgetRemaining = 5_000_000;
            e.State.Player.CapexApprovalLimit = 2_000_000; e.State.Player.OpexApprovalLimit = 500_000;
            e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRH" }), confirmed: true);
            for (int i = 0; i < 200; i++)
            {
                e.AdvanceDays(1);
                var inc = e.State.Incidents.FirstOrDefault(x => x.Kind == IncidentEngine.CutoverKind && x.Status == IncidentStatus.Active);
                if (inc != null) return (e, inc);
                if (e.State.Projects[0].Status == ProjectStatus.Completed) break;
            }
        }
        throw new InvalidOperationException("No cutover incident in 200 seeds");
    }

    [Fact]
    public void CutoverIncidentPausesTheProject()
    {
        var (e, inc) = WithCutoverIncident();
        Assert.Equal(ProjectStatus.OnHold, e.State.Projects.First(p => p.Id == inc.ProjectId).Status);
        Assert.Equal(8, inc.MessageDelayPct);
    }

    [Fact]
    public void CmoClinicalHoldIsBinding()
    {
        var (e, inc) = WithCutoverIncident();
        Assert.True(e.Submit(A("incident_response", new { incident_id = inc.Id, response = "request_clinical_assessment" })).Executed);
        Assert.True(inc.ClinicalHold);
        var r = e.Submit(A("incident_response", new { incident_id = inc.Id, response = "continue_troubleshooting" }), confirmed: true);
        Assert.False(r.Executed);
        Assert.True(e.Submit(A("incident_response", new { incident_id = inc.Id, response = "extend_downtime" })).Executed);
    }

    [Fact]
    public void RollbackResolvesAndReplansTheProject()
    {
        var (e, inc) = WithCutoverIncident();
        var proj = e.State.Projects.First(p => p.Id == inc.ProjectId);
        var end = proj.PlannedEndDate;
        Assert.True(e.Submit(A("incident_response", new { incident_id = inc.Id, response = "rollback" }), confirmed: true).Executed);
        Assert.Equal(IncidentStatus.Resolved, inc.Status);
        Assert.True(proj.PlannedEndDate > end);
        Assert.Equal(ProjectStatus.Active, proj.Status);
    }
}

public class PersistenceTests
{
    [Fact]
    public void SaveLoadRoundTripIsExact()
    {
        var repo = new SaveRepository(TempDb());
        var e = NewGame();
        e.Submit(A("delegate_assignment", new { assignee_role = "CISO", task_type = "backup_verification", subject = "Restore-test critical backups", deadline_days = 21 }));
        e.AdvanceDays(45);
        repo.Save("slot1", e.State);
        var loaded = repo.Load("slot1");
        Assert.False(loaded.RecoveredFromBackup);
        Assert.Equal(StateSerializer.StateHash(e.State), StateSerializer.StateHash(loaded.State));
    }

    [Fact]
    public void CorruptedSaveFallsBackToPreviousGeneration()
    {
        var repo = new SaveRepository(TempDb());
        var e = NewGame();
        repo.Save("s", e.State);
        string first = StateSerializer.StateHash(e.State);
        e.AdvanceDays(10);
        repo.Save("s", e.State);
        repo.CorruptForTesting("s", "{\"tampered\":true}");
        var r = repo.Load("s");
        Assert.True(r.RecoveredFromBackup);
        Assert.Equal(first, StateSerializer.StateHash(r.State));
        Assert.NotNull(r.Warning);
    }

    [Fact]
    public void EditedSaveIsDetected()
    {
        var repo = new SaveRepository(TempDb());
        var e = NewGame();
        repo.Save("s", e.State);
        var json = StateSerializer.Serialize(e.State).Replace("\"Cash\":212000000", "\"Cash\":999000000");
        repo.CorruptForTesting("s", json);
        Assert.Throws<SaveException>(() => repo.Load("s")); // no backup generation yet
    }

    [Fact]
    public void NewerSchemaIsRefusedAndOlderSchemaIsMigrated()
    {
        var json = StateSerializer.Serialize(NewGame().State);
        Assert.Throws<SaveException>(() => StateSerializer.Deserialize(json.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":99")));
        var v0 = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        v0.Remove("SchemaVersion"); v0.Remove("Difficulty");
        var migrated = StateSerializer.Deserialize(v0.ToJsonString());
        Assert.Equal(GameState.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(Difficulty.Professional, migrated.Difficulty);
    }

    [Fact]
    public void SlotNamesAreSanitized()
    {
        var repo = new SaveRepository(TempDb());
        Assert.Throws<ArgumentException>(() => repo.Save("../../evil", NewGame().State));
    }
}

public class DifficultyTests
{
    [Fact]
    public void ScenarioIsFictionalAndDifficultyIsRecorded()
    {
        var s = HealthcareCtoScenario.Create(1, Difficulty.Executive);
        Assert.Contains("fictional", s.Company.FictionNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Difficulty.Executive, s.Difficulty);
    }

    [Fact]
    public void QuietYearsAreNotConstantEmergencies()
    {
        int majors = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var e = NewGame(seed);
            e.AdvanceDays(365);
            majors += e.State.Incidents.Count(i => i.Kind is "infrastructure_outage" or "ransomware");
        }
        Assert.InRange(majors / 20.0, 0.0, 2.0); // average major incidents per year with no action taken
    }
}
