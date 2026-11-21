using System.Text.Json;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Evaluation;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Core.Simulation;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// The single authority over game state. Every mutation — player decisions, AI-interpreted actions,
/// time progression — goes through this class. The AI layer never receives a reference to it with
/// write intent; it gets read-only snapshots and returns <see cref="ActionProposal"/>s.
/// </summary>
public sealed class SimulationEngine
{
    public GameState State { get; }
    public ActionRegistry Registry { get; }
    internal DeterministicRng Rng { get; }

    public SimulationEngine(GameState state, ActionRegistry? registry = null)
    {
        State = state;
        Registry = registry ?? ActionRegistry.CreateDefault();
        Rng = new DeterministicRng(state.RngState);
    }

    private void CommitRng() => State.RngState = Rng.State;

    // ------------------------------------------------------------------ Actions

    public ValidationResult Validate(ActionProposal proposal) => ActionValidator.Validate(State, Registry, proposal);

    /// <summary>
    /// Validates and, when valid (and confirmed if required), executes an action. Records a decision with
    /// an independent evaluation computed from the state *before* the action took effect.
    /// </summary>
    public ActionOutcome Submit(ActionProposal proposal, string playerText = "", bool confirmed = false)
    {
        if (State.Outcome != GameOutcome.InProgress)
            return new ActionOutcome { Validation = ValidationResult.Fail("Your tenure has ended; no further decisions can be made.") };

        var validation = Validate(proposal);
        if (validation.Status == ValidationStatus.NeedsConfirmation && confirmed)
            validation = ValidationResult.Ok();
        if (!validation.IsValid)
            return new ActionOutcome { Validation = validation, Summary = validation.Question ?? validation.ConfirmationPrompt ?? string.Join(" ", validation.Errors) };

        var handler = Registry.Get(proposal.Name)!;
        var evaluation = DecisionEvaluator.Evaluate(State, proposal);
        var decision = new DecisionRecord
        {
            Id = State.NewId("DEC"),
            Date = State.CurrentDate,
            Action = proposal.Name,
            ParametersJson = proposal.Params.ToJsonString(),
            PlayerText = Truncate(playerText, 500),
            Topic = handler.TopicOf(State, proposal),
            Evaluation = evaluation,
        };
        var result = handler.Execute(this, proposal, decision);
        decision.Summary = result.Summary;
        State.Decisions.Add(decision);
        DecisionEvaluator.ApplyInstitutionalEffects(this, decision);
        CommitRng();
        return new ActionOutcome { Executed = true, Validation = ValidationResult.Ok(), DecisionId = decision.Id, Summary = result.Summary, Effects = result.Effects };
    }

    // ------------------------------------------------------------------ Time

    /// <summary>Advance the simulation day by day. Returns the inbox messages generated.</summary>
    public List<InboxMessage> AdvanceDays(int days)
    {
        if (days < 1 || days > 400) throw new ArgumentOutOfRangeException(nameof(days));
        int before = State.Inbox.Count;
        for (int i = 0; i < days && State.Outcome == GameOutcome.InProgress; i++)
            AdvanceOneDay();
        CommitRng();
        return State.Inbox.Skip(before).ToList();
    }

    private void AdvanceOneDay()
    {
        State.CurrentDate = State.CurrentDate.AddDays(1);
        var d = State.CurrentDate;

        if (d.Day == 1 && d.Month == State.FiscalYearStartMonth)
            FinancialEngine.FiscalYearRollover(this);

        EventEngine.ProcessScheduled(this);
        AssignmentEngine.DailyTick(this);
        ProjectEngine.DailyTick(this);
        EventEngine.DailyHazards(this);
        EventEngine.ProcessActiveIncidents(this);
        ExecutiveDynamics.DailyTick(this);

        if (d.AddDays(1).Month != d.Month)
            FinancialEngine.MonthlyClose(this);

        if (d.AddDays(1).Month != d.Month && IsFiscalQuarterEnd(d))
            PerformanceEngine.QuarterlyReview(this);
    }

    public bool IsFiscalQuarterEnd(DateOnly d)
    {
        int monthsIntoFy = (d.Month - State.FiscalYearStartMonth + 12) % 12; // 0..11
        return monthsIntoFy % 3 == 2;
    }

    // ------------------------------------------------------------------ Helpers used by sub-engines

    public InboxMessage Notify(MessageKind kind, ExecRole from, string subject, string body, string? relatedId = null)
    {
        var m = new InboxMessage
        {
            Id = State.NewId("MSG"), Date = State.CurrentDate, Kind = kind, From = from,
            Subject = subject, Body = body, RelatedId = relatedId,
        };
        State.Inbox.Add(m);
        return m;
    }

    public MemoryRecord Remember(string kind, string text, IEnumerable<string> tags, IEnumerable<ExecRole> involved, double weight = 1)
    {
        var r = new MemoryRecord
        {
            Id = State.NewId("MEM"), Date = State.CurrentDate, Kind = kind, Text = Truncate(text, 400),
            Tags = tags.Distinct().ToList(), Involved = involved.Distinct().ToList(), Weight = weight,
        };
        State.Memory.Add(r);
        return r;
    }

    /// <summary>Records a line of meeting dialogue. Dialogue is data, not authority: it never changes finances.</summary>
    public void RecordMeetingTurn(string meetingId, ExecRole speaker, string text, string topic, bool aiGenerated)
    {
        State.MeetingLog.Add(new MeetingTurn
        {
            Date = State.CurrentDate, MeetingId = meetingId, Speaker = speaker,
            Text = Truncate(text, 2000), Topic = topic, AiGenerated = aiGenerated,
        });
        // Keep the log bounded; structured memory holds what matters long term.
        if (State.MeetingLog.Count > 400) State.MeetingLog.RemoveRange(0, State.MeetingLog.Count - 400);
    }

    /// <summary>Marks that the player discussed a topic with an executive (stakeholder consultation evidence).</summary>
    public void RecordConsultation(ExecRole role, string topic)
    {
        Remember("consultation", $"{role} consulted on {topic}", new[] { topic, "consultation" }, new[] { role }, 0.3);
    }

    public void AdjustTrust(ExecRole role, double delta, string? reason = null)
    {
        var e = State.TryExec(role);
        if (e == null) return;
        e.TrustInPlayer = Clamp(e.TrustInPlayer + delta);
    }

    public static double Clamp(double v, double lo = 0, double hi = 100) => Math.Max(lo, Math.Min(hi, v));

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public double DifficultyHazardMultiplier => State.Difficulty switch
    {
        Difficulty.Casual => 0.5, Difficulty.Executive => 1.25, _ => 1.0,
    };

    public static string Money(decimal amount)
    {
        decimal a = Math.Abs(amount);
        string s = a >= 1_000_000m ? $"${a / 1_000_000m:0.##}M" : a >= 1_000m ? $"${a / 1_000m:0.#}k" : $"${a:0}";
        return amount < 0 ? "-" + s : s;
    }

    internal static string ToJson(object o) => JsonSerializer.Serialize(o);
}
