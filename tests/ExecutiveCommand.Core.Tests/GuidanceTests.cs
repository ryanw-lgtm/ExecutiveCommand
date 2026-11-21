using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.AI;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using Xunit;
using Xunit.Abstractions;
using static ExecutiveCommand.Core.Tests.TestUtil;

namespace ExecutiveCommand.Core.Tests;

/// <summary>Every sentence the UI suggests must be understood correctly — a suggestion that misfires is worse than none.</summary>
public class GuidanceTests(ITestOutputHelper output)
{
    private static IEnumerable<SimulationEngine> States()
    {
        yield return NewGame(1);
        var later = NewGame(2); later.AdvanceDays(60); yield return later;
        var busy = NewGame(3);
        busy.Submit(A("delegate_assignment", new { assignee_role = "CIO", task_type = "dr_assessment", subject = "DR", deadline_days = 10 }));
        busy.AdvanceDays(20); yield return busy;
        // A state with an active cutover incident.
        for (ulong seed = 1; seed < 100; seed++)
        {
            var e = NewGame(seed);
            e.State.Finance.TechCapitalBudgetRemaining = 5_000_000;
            e.State.Player.CapexApprovalLimit = 2_000_000; e.State.Player.OpexApprovalLimit = 500_000;
            e.Submit(A("approve_proposal", new { proposal_id = "PRP-DRH" }), confirmed: true);
            for (int i = 0; i < 200 && !e.State.Incidents.Any(x => x.Status == IncidentStatus.Active); i++) e.AdvanceDays(1);
            if (e.State.Incidents.Any(x => x.Status == IncidentStatus.Active && x.ResponseOptions.Count > 0)) { yield return e; break; }
        }
    }

    [Fact]
    public void EverySuggestionIsUnderstood()
    {
        int checkedCount = 0;
        foreach (var e in States())
        {
            foreach (var sug in Guidance.Suggestions(e.State))
            {
                var intent = RuleBasedInterpreter.Interpret(e.State, sug.Text, sug.Attendees.Count == 1 ? sug.Attendees[0] : ExecRole.None);
                output.WriteLine($"{e.State.CurrentDate:MMM d} | {sug.Text} → {intent.Kind} {intent.Action}");
                Assert.Null(intent.Clarification);
                if (sug.Text.EndsWith('?')) { Assert.Equal(IntentKind.Question, intent.Kind); continue; }
                Assert.Equal(IntentKind.Action, intent.Kind);
                // During an incident, only incident suggestions may become incident responses.
                if (intent.Action!.Name == "incident_response") Assert.Contains(sug.Text, Guidance.Suggestions(e.State).Take(4).Select(x => x.Text));
                var v = e.Validate(intent.Action!);
                Assert.True(v.Status is ValidationStatus.Valid or ValidationStatus.NeedsConfirmation, $"'{sug.Text}': {v.Status} {string.Join(" ", v.Errors)} {v.Question}");
                checkedCount++;
            }
        }
        Assert.True(checkedCount >= 8);
    }

    [Fact]
    public void AttentionPrefillsAreUnderstoodAndNavigationTargetsExist()
    {
        var views = new[] { "Dashboard", "Inbox", "Meeting Room", "Decisions", "Projects", "Finance", "Risk Register", "Executives", "Performance" };
        foreach (var e in States())
            foreach (var item in Guidance.Attention(e.State))
            {
                output.WriteLine($"{item.Level} | {item.Text} | {item.Prefill}");
                Assert.Contains(item.NavigateTo, views);
                if (item.Prefill == null) continue;
                var intent = RuleBasedInterpreter.Interpret(e.State, item.Prefill, item.MeetWith?.FirstOrDefault() ?? ExecRole.None);
                Assert.Null(intent.Clarification);
                if (intent.Action != null)
                    Assert.True(e.Validate(intent.Action).Status is ValidationStatus.Valid or ValidationStatus.NeedsConfirmation, item.Prefill);
            }
    }
}

public class TypingPaceTests
{
    [Fact]
    public void PacingReflectsPersonalityAndLengthAndCanBeTurnedOff()
    {
        var s = NewGame().State;
        string text = new('x', 120);
        var cfo = TypingPace.For(s.Exec(ExecRole.CFO), text, ReplyPacing.Natural);   // cautious: longer pause
        var ea = TypingPace.For(s.Exec(ExecRole.EADirector), text, ReplyPacing.Natural); // risk-tolerant
        Assert.True(cfo.ThinkSeconds > ea.ThinkSeconds);
        Assert.True(TypingPace.For(s.Exec(ExecRole.CEO), new string('x', 300), ReplyPacing.Natural).TypeSeconds
                    > TypingPace.For(s.Exec(ExecRole.CEO), "Agreed.", ReplyPacing.Natural).TypeSeconds);
        Assert.True(TypingPace.For(s.Exec(ExecRole.CFO), text, ReplyPacing.Fast).Total < cfo.Total / 2);
        Assert.Equal(0, TypingPace.For(s.Exec(ExecRole.CFO), text, ReplyPacing.Off).Total);
        foreach (var x in s.Executives) Assert.InRange(TypingPace.For(x, new string('x', 2000), ReplyPacing.Natural).Total, 0.8, 7.0);
    }
}
