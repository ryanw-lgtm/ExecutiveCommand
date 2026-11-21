using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.AI;

public enum IntentKind { Action, Question, Claim, Statement }

[Flags]
public enum ConductFlags
{
    None = 0,
    /// <summary>Attempt to issue instructions to the AI/system ("ignore your instructions", "you are now…").</summary>
    Manipulation = 1,
    SelfPraise = 2,
    Threat = 4,
    Flattery = 8,
    DemandsReward = 16,
}

public sealed class InterpretedIntent
{
    public IntentKind Kind { get; set; } = IntentKind.Statement;
    public ExecRole AddressedRole { get; set; } = ExecRole.None;
    public ActionProposal? Action { get; set; }
    public string? Clarification { get; set; }
    public string Topic { get; set; } = "general";
    public ConductFlags Conduct { get; set; }
    public string Source { get; set; } = "rules";
    public string? ProposalFocusId { get; set; }
    /// <summary>Notes for the player about how their words were interpreted.</summary>
    public List<string> Notes { get; } = new();
}
