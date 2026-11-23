namespace ExecutiveCommand.Core.Model;

public enum ObjectiveStatus { Active, Completed, Failed }

/// <summary>A goal the player is playing for. Progress is computed from game state, never declared.</summary>
public sealed class Objective
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Stars { get; set; } = 1;
    public DateOnly Deadline { get; set; }
    public ObjectiveStatus Status { get; set; } = ObjectiveStatus.Active;
    public double Progress { get; set; }
    public DateOnly? ResolvedOn { get; set; }
    /// <summary>A short hint pointing at what would move it forward.</summary>
    public string Hint { get; set; } = "";
}

public sealed class Achievement
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly UnlockedOn { get; set; }
}

/// <summary>Letter grades and verdict for a quarter, derived from the performance review.</summary>
public sealed class QuarterReport
{
    public string Id { get; set; } = "";
    public string Period { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Overall { get; set; } = "";
    public string Decisions { get; set; } = "";
    public string Delivery { get; set; } = "";
    public string Finance { get; set; } = "";
    public string Risk { get; set; } = "";
    public int StarsEarned { get; set; }
    public int StarsPossible { get; set; }
    public int XpGained { get; set; }
    public string Verdict { get; set; } = "";
    public List<string> Highlights { get; set; } = new();
    public bool Seen { get; set; }
}

public sealed class CareerProgress
{
    public int Xp { get; set; }
    public int Stars { get; set; }
    public List<Achievement> Achievements { get; set; } = new();
    public List<QuarterReport> ReportCards { get; set; } = new();
    /// <summary>Events not yet shown to the player (achievement pop-ups, objective completions).</summary>
    public List<string> PendingCelebrations { get; set; } = new();
}
