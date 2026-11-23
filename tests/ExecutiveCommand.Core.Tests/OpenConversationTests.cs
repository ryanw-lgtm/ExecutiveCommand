using ExecutiveCommand.Core.AI;
using ExecutiveCommand.Core.Model;
using Xunit;
using static ExecutiveCommand.Core.Tests.TestUtil;

namespace ExecutiveCommand.Core.Tests;

/// <summary>A meeting is an open conversation: nobody has to be named for things to work.</summary>
public class OpenConversationTests
{
    private static readonly ExecRole[] Tech = { ExecRole.CIO, ExecRole.CISO, ExecRole.EADirector };
    private static readonly ExecRole[] Committee = { ExecRole.CEO, ExecRole.CFO, ExecRole.CMO, ExecRole.CNO };

    [Theory]
    [InlineData("Verify the backups actually restore. Three weeks.", ExecRole.CISO)]
    [InlineData("Assess our real disaster recovery time, four weeks.", ExecRole.CIO)]
    [InlineData("Can someone negotiate the Halcyon renewal? We have a month.", ExecRole.CIO)]
    [InlineData("I need someone to run a proof of concept of cloud recovery in six weeks.", ExecRole.EADirector)]
    [InlineData("Please look into alternatives to the identity expansion, three weeks.", ExecRole.EADirector)]
    public async Task UnnamedInstructionsGoToTheNaturalOwnerInTheRoom(string text, ExecRole expected)
    {
        var e = NewGame();
        var m = new MeetingService(e, Tech);
        var r = await m.SayAsync(text);
        Assert.True(r.Outcome?.Executed, $"not executed: {string.Join(" ", r.SystemNotes)} {r.Replies.FirstOrDefault()?.Text}");
        Assert.Equal(expected, Assert.Single(e.State.Assignments).Assignee);
        Assert.Equal(expected, r.Replies[0].Speaker); // the owner answers
    }

    [Fact]
    public async Task PeersInTheRoomAreAskedRatherThanAssigned()
    {
        var e = NewGame();
        var m = new MeetingService(e, Committee);
        var r = await m.SayAsync("Give me a clinical assessment of the disaster recovery options within two weeks.");
        Assert.Equal("request_assessment", r.Intent!.Action!.Name);
        Assert.Equal(ExecRole.CMO, e.State.Assignments.Single().Assignee);
    }

    [Fact]
    public async Task WhenNobodyInTheRoomFitsTheGameAsks()
    {
        var e = NewGame();
        var m = new MeetingService(e, Committee);
        var r = await m.SayAsync("Verify the backups actually restore. Three weeks.");
        Assert.Null(r.Outcome);
        Assert.Empty(e.State.Assignments);
        Assert.Contains("Who should take it on", r.Replies[0].Text);
    }

    [Theory]
    [InlineData("We need to review our strategy at some point.")]
    [InlineData("I was reading about cloud recovery last night, interesting stuff.")]
    public async Task OrdinaryRemarksDoNotBecomeAssignments(string text)
    {
        var e = NewGame();
        var m = new MeetingService(e, Tech);
        await m.SayAsync(text);
        Assert.Empty(e.State.Assignments);
    }

    [Fact]
    public async Task OpenQuestionsGetTheExpertAndASecondView()
    {
        var e = NewGame();
        var m = new MeetingService(e, Tech);
        var r = await m.SayAsync("What's our disaster recovery position?");
        Assert.Equal(ExecRole.CIO, r.Replies[0].Speaker);
        Assert.True(r.Replies.Count >= 2);
        Assert.NotEqual(r.Replies[0].Speaker, r.Replies[1].Speaker);
    }

    [Fact]
    public async Task FollowUpsGoBackToWhoeverJustSpoke()
    {
        var e = NewGame();
        var m = new MeetingService(e, Tech);
        var first = await m.SayAsync("Who handles backups here?");
        var last = first.Replies[^1].Speaker;
        var r = await m.SayAsync("Why?");
        Assert.Equal(last, r.Replies[0].Speaker);
    }

    [Fact]
    public async Task AskingEveryoneGetsEveryonesView()
    {
        var e = NewGame();
        var m = new MeetingService(e, Tech);
        var r = await m.SayAsync("What does everyone think about the hybrid colocation option?");
        Assert.Equal(3, r.Replies.Select(x => x.Speaker).Distinct().Count());
    }

    [Fact]
    public async Task NamingSomeoneStillDirectsIt()
    {
        var e = NewGame();
        var m = new MeetingService(e, Tech);
        var r = await m.SayAsync("Marcus, verify the backups actually restore. Three weeks.");
        // Marcus can't do backup verification: naming him is respected, so the engine explains why not.
        Assert.Empty(e.State.Assignments);
        Assert.Equal(ExecRole.EADirector, r.Replies[0].Speaker);
    }
}

public class PositionConsistencyTests
{
    [Fact]
    public async Task SupportingExecutiveMayNotArgueForAnotherOption()
    {
        var e = NewGame();
        int calls = 0;
        var fake = new FakeModel { Text = _ => ++calls == 1
            ? "Hybrid colocation doesn't meet the target. Cloud-first is the only path to compliance."
            : "I'm for it. It's designed to bring EHR recovery to about 6 hours and moves us toward the target architecture." };
        var m = new MeetingService(e, new[] { ExecRole.EADirector }, fake);
        var r = await m.SayAsync("Marcus, where do you stand on the hybrid colocation option?");
        Assert.Equal(2, calls);
        Assert.StartsWith("I'm for it", r.Replies[0].Text);
    }
}
