using System.Text.Json.Nodes;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.Engine;
using ExecutiveCommand.Core.Model;
using ExecutiveCommand.Game.UI;
using Godot;

namespace ExecutiveCommand.Game.Views;

/// <summary>Base for read-mostly views that rebuild their contents from state on refresh.</summary>
public abstract partial class StateView : VBoxContainer, IGameView
{
    protected readonly GameScreen Game = null!;
    protected VBoxContainer Body = null!;
    public abstract string Title { get; }

    protected StateView() { }

    protected StateView(GameScreen game)
    {
        Game = game;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Body = Ui.VBox(12);
        AddChild(Ui.Scroll(Body));
    }

    public void Refresh(GameSession session)
    {
        Ui.Clear(Body);
        Body.AddChild(Ui.Heading(Title));
        Build(session.State);
    }

    protected abstract void Build(GameState s);

    protected static string M(decimal d) => SimulationEngine.Money(d);

    /// <summary>UI buttons create the same ActionProposals the interpreter does and go through the same validator.</summary>
    protected void SubmitWithConfirmation(ActionProposal a, string playerText)
    {
        var v = Game.Session.Engine!.Validate(a);
        if (v.Status == ValidationStatus.Invalid) { Game.Toast(string.Join(" ", v.Errors), Palette.Bad); return; }
        if (v.Status == ValidationStatus.NeedsClarification) { Game.Toast(v.Question ?? "More detail needed.", Palette.Warn); return; }
        void Go(bool confirmed)
        {
            var r = Game.Session.Engine!.Submit(a, playerText, confirmed);
            Game.Toast(r.Executed ? r.Summary : string.Join(" ", r.Validation.Errors), r.Executed ? Palette.Good : Palette.Bad);
            Game.Session.NotifyChanged();
        }
        if (v.Status == ValidationStatus.NeedsConfirmation)
        {
            var dlg = new ConfirmationDialog { DialogText = v.ConfirmationPrompt ?? "Confirm?", Title = "Confirm decision", OkButtonText = "Confirm", CancelButtonText = "Cancel", DialogAutowrap = true, Size = new Vector2I(560, 200) };
            AddChild(dlg);
            dlg.Confirmed += () => { Go(true); dlg.QueueFree(); };
            dlg.Canceled += () => dlg.QueueFree();
            dlg.PopupCentered();
        }
        else Go(false);
    }
}

// =========================================================================== Dashboard

public partial class DashboardView : StateView
{
    public override string Title => "Executive Dashboard";
    public DashboardView() { }
    public DashboardView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        var f = s.Finance;
        var last = f.Statements.LastOrDefault();
        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        int risk = s.Risks.Where(r => r.Known && !r.Closed).Sum(r => r.Score);

        if (s.Outcome != GameOutcome.InProgress)
            Body.AddChild(Ui.Card(Ui.Label("Your tenure as CTO has ended. Review the record in Performance, then start a new career from the menu.", 16, Palette.Bad, wrap: true), Palette.SurfaceRaised, Palette.Bad));

        var row1 = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row1.AddChild(Ui.Tile("Role", s.Player.Title, $"{s.Company.Name}"));
        row1.AddChild(Ui.Tile("Date", s.CurrentDate.ToString("MMM d, yyyy"), $"Fiscal year ends {FinancialEngine.FiscalYearEnd(s, s.CurrentDate):MMM d, yyyy}"));
        row1.AddChild(Ui.Tile("Operating margin", last == null ? "—" : last.OperatingMargin.ToString("P1"),
            last == null ? "No month closed yet" : $"{new DateTime(last.Year, last.Month, 1):MMMM} close", last == null ? null : Palette.ForScore(last.OperatingMargin * 100, 2.5, 0)));
        row1.AddChild(Ui.Tile("Cash reserves", M(f.Cash), $"≈ {(double)(f.Cash / Math.Max(1, (last?.Opex ?? 93_500_000m) / 30m)):0} days cash on hand"));
        row1.AddChild(Ui.Tile("Available capital", M(f.TechCapitalBudgetRemaining), $"Technology capital remaining of {M(f.TechCapitalBudgetAnnual)} FY budget", f.TechCapitalBudgetRemaining < 0 ? Palette.Bad : null));
        row1.AddChild(Ui.Tile("Board confidence", $"{s.Metrics.BoardConfidence:0}", "Moves with reviews, incidents and escalations", Palette.ForScore(s.Metrics.BoardConfidence)));
        row1.AddChild(Ui.Tile("Employee morale", $"{s.Metrics.EmployeeMorale:0}", $"Technology team {tech.Morale:0}", Palette.ForScore(s.Metrics.EmployeeMorale)));
        row1.AddChild(Ui.Tile("Organizational risk", $"{risk}", $"Sum of known open risk scores ({s.Risks.Count(r => r.Known && !r.Closed)} risks)", risk > 60 ? Palette.Bad : risk > 40 ? Palette.Warn : Palette.Good));
        Body.AddChild(row1);

        var t = s.Technology;
        var row2 = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row2.AddChild(Ui.Tile("EHR recovery time", $"{t.ReportedRtoHours:0} h", $"Documented · target {t.TargetRtoHours:0} h{(t.HasSecondarySite ? " · secondary site live" : "")}", t.ReportedRtoHours <= t.TargetRtoHours ? Palette.Good : Palette.Bad));
        row2.AddChild(Ui.Tile("Active projects", $"{s.Projects.Count(p => p.Status is ProjectStatus.Active or ProjectStatus.OnHold)}", string.Join(", ", s.Projects.Where(p => p.Status != ProjectStatus.Completed).Select(p => $"{p.Name} {p.Progress:P0}"))));
        row2.AddChild(Ui.Tile("Pending decisions", $"{s.Proposals.Count(p => p.Status == ProposalStatus.Pending)}", $"{s.Proposals.Count(p => p.Status == ProposalStatus.AwaitingApproval)} awaiting CFO/CEO"));
        decimal approved = s.Savings.Where(v => v.Source == "budget_cut" && v.Recurring && v.Status != SavingsStatus.Reversed).Sum(v => v.AnnualAmount);
        row2.AddChild(Ui.Tile("Savings vs target", $"{M(approved)}", $"Recurring approved toward {M(PerformanceEngine.SavingsTarget)} · realized {M(s.Savings.Sum(v => v.RealizedToDate))}", approved >= PerformanceEngine.SavingsTarget ? Palette.Good : Palette.Warn));
        Body.AddChild(row2);

        // Upcoming: assignments due, approvals, scheduled items.
        var upcoming = new List<(DateOnly, string)>();
        upcoming.AddRange(s.Assignments.Where(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue).Select(a => (a.Due, $"{a.Assignee}: {a.Subject}{(a.Status == AssignmentStatus.Overdue ? " (OVERDUE)" : "")}")));
        upcoming.AddRange(s.Proposals.Where(p => p.Status == ProposalStatus.AwaitingApproval && p.ApprovalDueDate != null).Select(p => (p.ApprovalDueDate!.Value, $"CFO/CEO decision: {p.Title}")));
        upcoming.AddRange(s.Contracts.Where(c => !c.Renewed).Select(c => (c.RenewalDate, $"{c.Vendor} contract renewal{(c.Negotiated ? " (negotiated)" : " — not negotiated")}")));
        upcoming.AddRange(s.Projects.Where(p => p.Status != ProjectStatus.Completed).Select(p => (p.PlannedEndDate, $"Planned go-live: {p.Name}")));
        var q = Ui.VBox(4, Ui.Caption("Upcoming"));
        foreach (var (d, text) in upcoming.OrderBy(u => u.Item1).Take(8))
            q.AddChild(Ui.HBox(12, Ui.Label(d.ToString("MMM d"), 14, d < s.CurrentDate ? Palette.Bad : Palette.Accent), Ui.Label(text, 14, null, wrap: true)));
        if (upcoming.Count == 0) q.AddChild(Ui.Label("Nothing scheduled.", 14, Palette.Muted));

        var inc = s.Incidents.FirstOrDefault(i => i.Status == IncidentStatus.Active);
        var recent = Ui.VBox(4, Ui.Caption("Latest messages"));
        if (inc != null) recent.AddChild(Ui.Label($"ACTIVE INCIDENT: {inc.Title} — direct the response in the Meeting Room or Decisions.", 14, Palette.Bad, wrap: true));
        foreach (var m in s.Inbox.TakeLast(6).Reverse())
            recent.AddChild(Ui.Label($"{m.Date:MMM d} · {m.Subject}", 14, m.Read ? Palette.Muted : Palette.Text, wrap: true));
        var cols = Ui.HBox(12, Ui.Card(q), Ui.Card(recent));
        foreach (var c in cols.GetChildren().OfType<Control>()) c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Body.AddChild(cols);
    }
}

// =========================================================================== Inbox

public partial class InboxView : StateView
{
    public override string Title => "Inbox";
    private string? _selected;
    public InboxView() { }
    public InboxView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        var split = Ui.HBox(12);
        split.SizeFlagsVertical = SizeFlags.ExpandFill;
        var list = new ItemList { CustomMinimumSize = new Vector2(420, 560), SizeFlagsVertical = SizeFlags.ExpandFill };
        var msgs = s.Inbox.AsEnumerable().Reverse().ToList();
        foreach (var m in msgs)
        {
            int i = list.AddItem($"{(m.Read ? "  " : "● ")}{m.Date:MMM d}  {m.Subject}");
            list.SetItemCustomFgColor(i, m.Kind switch { MessageKind.Incident => Palette.Bad, MessageKind.Escalation => Palette.Warn, _ => m.Read ? Palette.Muted : Palette.Text });
        }
        var reader = new RichTextLabel { BbcodeEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400, 560), SelectionEnabled = true };
        void Show(int idx)
        {
            var m = msgs[idx];
            _selected = m.Id;
            m.Read = true;
            list.SetItemText(idx, $"  {m.Date:MMM d}  {m.Subject}");
            var from = m.From == ExecRole.None ? "System" : s.TryExec(m.From) is { } x ? $"{x.Name}, {x.Title}" : m.From.ToString();
            reader.Clear();
            reader.AppendText($"[font_size=20]{Ui.Esc(m.Subject)}[/font_size]\n[color={Palette.Hex(Palette.Muted)}]{m.Date:dddd, MMMM d, yyyy} · {Ui.Esc(from)} · {m.Kind}[/color]\n\n{Ui.Esc(m.Body)}");
        }
        list.ItemSelected += i => Show((int)i);
        split.AddChild(list);
        split.AddChild(reader);
        Body.AddChild(split);
        int sel = Math.Max(0, msgs.FindIndex(m => m.Id == _selected));
        if (msgs.Count > 0) { list.Select(sel); Show(sel); }
    }
}

// =========================================================================== Decisions (proposals + incidents)

public partial class ProposalsView : StateView
{
    public override string Title => "Decisions";
    public ProposalsView() { }
    public ProposalsView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        foreach (var inc in s.Incidents.Where(i => i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0))
        {
            var v = Ui.VBox(6, Ui.Label($"ACTIVE INCIDENT — {inc.Title}", 18, Palette.Bad), Ui.Label(inc.Description, 14, null, wrap: true));
            foreach (var line in inc.Timeline) v.AddChild(Ui.Label(line, 13, Palette.Muted, wrap: true));
            if (inc.ClinicalHold) v.AddChild(Ui.Label("The CMO has placed a clinical safety hold. Live troubleshooting is not permitted.", 14, Palette.Warn, wrap: true));
            var row = Ui.HBox(8);
            foreach (var opt in inc.ResponseOptions.ToList())
                row.AddChild(Ui.Button(opt.Replace('_', ' '), () => SubmitWithConfirmation(new ActionProposal("incident_response", new JsonObject { ["incident_id"] = inc.Id, ["response"] = opt }), $"[Decisions panel] {opt}")));
            v.AddChild(row);
            Body.AddChild(Ui.Card(v, Palette.SurfaceRaised, Palette.Bad));
        }

        Body.AddChild(Ui.Label("Approve, defer or reject here, or discuss in the Meeting Room first. Executive positions shown are their current, evidence-based views.", 13, Palette.Muted, wrap: true));
        foreach (var p in s.Proposals.Where(p => p.Status is ProposalStatus.Pending or ProposalStatus.Deferred or ProposalStatus.AwaitingApproval))
        {
            var v = Ui.VBox(6);
            v.AddChild(Ui.HBox(10, Ui.Label(p.Title, 18), Ui.Spacer(), Ui.Label(p.Status == ProposalStatus.Deferred ? $"Deferred until {p.DeferredUntil:MMM d}" : p.Status == ProposalStatus.AwaitingApproval ? $"With CFO/CEO — decision {p.ApprovalDueDate:MMM d}" : "Pending", 13, Palette.Muted)));
            v.AddChild(Ui.Label(p.Summary, 14, Palette.Muted, wrap: true));
            v.AddChild(Ui.Label($"Sponsor: {Roles.Label(p.Sponsor)}   ·   Capital {M(p.Capex)}   ·   Operating {M(p.AnnualOpex)}/yr   ·   ~{p.DurationDays} days" +
                                (ApproveProposalHandler.RequiresExecutiveApproval(s, p) ? "   ·   requires CFO/CEO approval" : "   ·   within your authority"), 14));
            var stances = Ui.HBox(14);
            foreach (var role in new[] { ExecRole.CFO, ExecRole.CEO, ExecRole.CIO, ExecRole.CISO, ExecRole.CMO, ExecRole.EADirector })
            {
                var st = Core.Executives.StanceEngine.OnProposal(s, s.Exec(role), p);
                var col = st.Position switch { Core.Executives.Position.Support => Palette.Good, Core.Executives.Position.Oppose => Palette.Bad, Core.Executives.Position.Concerned => Palette.Warn, _ => Palette.Muted };
                var lbl = Ui.Label($"{role}: {st.Position}", 13, col);
                lbl.TooltipText = string.Join("\n", st.Reasons.Concat(st.Conditions.Select(c => "Needs: " + c)));
                lbl.MouseFilter = MouseFilterEnum.Stop;
                stances.AddChild(lbl);
            }
            v.AddChild(stances);
            if (p.Status != ProposalStatus.AwaitingApproval)
                v.AddChild(Ui.HBox(8,
                    Ui.Button("Approve", () => SubmitWithConfirmation(new ActionProposal("approve_proposal", new JsonObject { ["proposal_id"] = p.Id }), $"[Decisions panel] approve {p.Id}"), primary: true),
                    Ui.Button("Defer 30 days", () => SubmitWithConfirmation(new ActionProposal("defer_proposal", new JsonObject { ["proposal_id"] = p.Id, ["days"] = 30 }), $"[Decisions panel] defer {p.Id}")),
                    Ui.Button("Reject", () => SubmitWithConfirmation(new ActionProposal("reject_proposal", new JsonObject { ["proposal_id"] = p.Id }), $"[Decisions panel] reject {p.Id}"))));
            Body.AddChild(Ui.Card(v));
        }
        var closed = s.Proposals.Where(p => p.Status is ProposalStatus.Approved or ProposalStatus.Rejected or ProposalStatus.Superseded).ToList();
        if (closed.Count > 0)
            Body.AddChild(Ui.Card(Ui.VBox(4, new Control[] { Ui.Caption("Closed") }.Concat(closed.Select(p => (Control)Ui.Label($"{p.Title} — {p.Status}", 14, Palette.Muted))).ToArray())));
    }
}

// =========================================================================== Projects & assignments

public partial class ProjectsView : StateView
{
    public override string Title => "Projects & Assignments";
    public ProjectsView() { }
    public ProjectsView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        Body.AddChild(Ui.Caption("Project portfolio"));
        if (s.Projects.Count == 0) Body.AddChild(Ui.Label("No approved projects yet.", 14, Palette.Muted));
        foreach (var p in s.Projects)
        {
            var v = Ui.VBox(6);
            var color = p.Status == ProjectStatus.OnHold ? Palette.Warn : p.Status == ProjectStatus.Completed ? Palette.Good : Palette.Accent;
            v.AddChild(Ui.HBox(10, Ui.Label(p.Name, 17), Ui.Spacer(), Ui.Label($"{p.Status}  ·  owner {Roles.Label(p.Owner)}", 13, color)));
            v.AddChild(Ui.HBox(10, Ui.Bar(p.Progress * 100, 100, color), Ui.Label($"{p.Progress:P0}", 14)));
            v.AddChild(Ui.Label($"Capital {M(p.SpentCapex)} of {M(p.BudgetCapex)}  ·  started {p.StartDate:MMM d}  ·  planned go-live {p.PlannedEndDate:MMM d, yyyy}" +
                                (p.ActualEndDate is { } e ? $"  ·  went live {e:MMM d}" : "") + $"  ·  run cost {M(p.AnnualOpex)}/yr", 13, Palette.Muted, wrap: true));
            v.AddChild(Ui.Label(string.Join("   ", p.Milestones.Select(m => $"{(m.ReachedOn != null ? "✓" : "○")} {m.Name}")), 13, Palette.Muted));
            if (p.ActualOutcome.Length > 0) v.AddChild(Ui.Label("Outcome: " + p.ActualOutcome, 14, null, wrap: true));
            foreach (var r in p.Risks) v.AddChild(Ui.Label("Risk: " + r, 13, Palette.Warn, wrap: true));
            Body.AddChild(Ui.Card(v));
        }
        Body.AddChild(Ui.Caption("Delegated assignments & requests"));
        var rows = s.Assignments.AsEnumerable().Reverse().Select(a => new[]
        {
            a.Subject, Roles.Label(a.Assignee), a.IsRequest ? "request" : "delegated", a.Status.ToString(), a.Due.ToString("MMM d"),
            a.EffortRequired <= 0 ? "" : $"{Math.Min(1, a.EffortDone / a.EffortRequired):P0}", a.AuthorizedBudget > 0 ? M(a.AuthorizedBudget) : "—",
        });
        Body.AddChild(Ui.Card(Ui.Table(new[] { "Subject", "Owner", "Type", "Status", "Due", "Progress", "Budget" }, rows,
            (r, c) => c == 3 ? (s.Assignments[s.Assignments.Count - 1 - r].Status switch { AssignmentStatus.Overdue => Palette.Bad, AssignmentStatus.Completed => Palette.Good, AssignmentStatus.Declined => Palette.Warn, _ => null }) : null)));
    }
}

// =========================================================================== Finance

public partial class FinanceView : StateView
{
    public override string Title => "Financial Statements";
    public FinanceView() { }
    public FinanceView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        var f = s.Finance;
        Body.AddChild(Ui.Label($"Balance sheet: cash {M(f.Cash)} + net PP&E {M(f.NetPpe)} = debt {M(f.Debt)} + equity {M(f.Equity)}. Every figure is derived from the ledger.", 14, Palette.Muted, wrap: true));
        var st = f.Statements.TakeLast(12).Reverse().ToList();
        Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Monthly income statements"),
            st.Count == 0 ? Ui.Label("No month has closed yet. Statements are produced at each month-end close.", 14, Palette.Muted)
            : Ui.Table(new[] { "Month", "Revenue", "Operating exp.", "Technology", "Depreciation", "Op. margin", "Net income", "Capex", "Cash" },
                st.Select(m => new[] { new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy"), M(m.Revenue), M(m.Opex), M(m.TechOpex), M(m.Depreciation), m.OperatingMargin.ToString("P1"), M(m.NetIncome), M(m.Capex), M(m.ClosingCash) }),
                (r, c) => c == 6 && st[r].NetIncome < 0 ? Palette.Bad : null))));

        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Technology budget (annual)"),
            Ui.Table(new[] { "Line", "Budget", "Committed", "Earmarked", "Cut", "Discretionary left", "Fiscal YTD actual" },
                tech.BudgetLines.Select(l => new[] { l.Name, M(l.AnnualBudget), M(l.CommittedAnnual), M(l.EarmarkedAnnual), M(l.ApprovedReductionsAnnual), M(l.DiscretionaryRemaining), M(l.ActualFiscalYtd) })))));

        var sv = s.Savings.ToList();
        Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Savings tracker — proposed ≠ approved ≠ realized"),
            sv.Count == 0 ? Ui.Label("No savings initiatives yet.", 14, Palette.Muted)
            : Ui.Table(new[] { "Initiative", "Annual", "Type", "Status", "Effective", "Realized to date" },
                sv.Select(x => new[] { $"{tech.BudgetLines.FirstOrDefault(l => l.Id == x.BudgetLineId)?.Name ?? x.BudgetLineId} ({x.Source.Replace('_', ' ')})", M(x.AnnualAmount), x.Recurring ? "recurring" : "one-time", x.Status.ToString(), x.EffectiveDate.ToString("MMM yyyy"), M(x.RealizedToDate) })))));

        Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Vendor contracts"),
            Ui.Table(new[] { "Contract", "Annual cost", "Renewal", "Proposed change", "Agreed", "DR clause" },
                s.Contracts.Select(c => new[] { $"{c.Vendor} — {c.Service}", M(c.AnnualCost), c.RenewalDate.ToString("MMM d, yyyy") + (c.Renewed ? " (renewed)" : ""), c.ProposedUpliftPct.ToString("+0.0%"), c.AgreedUpliftPct?.ToString("+0.0%;-0.0%") ?? "—", c.HasDrSupportClause ? "yes" : "no" })))));
    }
}

// =========================================================================== Risk register

public partial class RiskView : StateView
{
    public override string Title => "Risk Register";
    public RiskView() { }
    public RiskView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        Body.AddChild(Ui.Label("Only risks the organization knows about appear here. Some exposures are discovered only through assessment — or through incidents.", 13, Palette.Muted, wrap: true));
        var risks = s.Risks.Where(r => r.Known).OrderBy(r => r.Closed).ThenByDescending(r => r.Score).ToList();
        foreach (var r in risks)
        {
            var color = r.Closed ? Palette.Good : r.Score >= 15 ? Palette.Bad : r.Score >= 9 ? Palette.Warn : Palette.Muted;
            Body.AddChild(Ui.Card(Ui.VBox(4,
                Ui.HBox(10, Ui.Label(r.Title, 16), Ui.Spacer(), Ui.Label(r.Closed ? "CLOSED" : $"Score {r.Score}  (L{r.Likelihood} × I{r.Impact})", 14, color)),
                Ui.Label(r.Description, 14, Palette.Muted, wrap: true),
                Ui.Label($"Owner: {Roles.Label(r.Owner)}  ·  identified {r.Identified:MMM d, yyyy}", 12, Palette.Muted))));
        }
        var incidents = s.Incidents.ToList();
        if (incidents.Count > 0)
            Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Incident history"),
                Ui.Table(new[] { "Incident", "Date", "Downtime", "Safety events", "Revenue impact", "Status" },
                    incidents.Select(i => new[] { i.Title, i.Started.ToString("MMM d"), $"{i.DowntimeHours:0.#} h", i.SafetyEvents.ToString(), M(i.RevenueImpact), i.Status.ToString() })))));
    }
}

// =========================================================================== People

public partial class PeopleView : StateView
{
    public override string Title => "Executives";
    public PeopleView() { }
    public PeopleView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var x in s.Executives)
        {
            var v = Ui.VBox(4);
            // Placeholder portrait: initials disc. (Final art is a Phase 4 deliverable.)
            var initials = string.Concat(x.Name.Replace("Dr. ", "").Split(' ').Where(w => w.Length > 0).Take(2).Select(w => w[0]));
            var portrait = Ui.Card(Ui.Label(initials, 22, Palette.Text), Palette.AccentDim, Palette.Accent);
            portrait.CustomMinimumSize = new Vector2(56, 56);
            v.AddChild(Ui.HBox(12, portrait, Ui.VBox(2, Ui.Label(x.Name, 17), Ui.Label(x.Title, 13, Palette.Muted),
                Ui.Label(x.ReportsTo == s.Player.Role ? "Reports to you" : x.Role == ExecRole.Vendor ? "External" : $"Reports to {Roles.Label(x.ReportsTo)}", 12, x.ReportsTo == s.Player.Role ? Palette.Accent : Palette.Muted))));
            v.AddChild(Ui.Label(x.Background, 13, Palette.Muted, wrap: true));
            v.AddChild(Ui.Label("Priorities: " + string.Join(", ", x.Priorities), 13, null, wrap: true));
            var g = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            g.AddChild(Ui.Label("Trust in you", 12, Palette.Muted)); g.AddChild(Ui.Bar(x.TrustInPlayer, 100, Palette.ForScore(x.TrustInPlayer, 60, 40)));
            g.AddChild(Ui.Label("Workload", 12, Palette.Muted)); g.AddChild(Ui.Bar(x.Workload, 100, x.Workload > 80 ? Palette.Bad : x.Workload > 65 ? Palette.Warn : Palette.Good));
            v.AddChild(g);
            var card = Ui.Card(v);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.AddChild(card);
        }
        Body.AddChild(grid);
    }
}

// =========================================================================== Performance

public partial class PerformanceView : StateView
{
    public override string Title => "Performance & Decision Record";
    public PerformanceView() { }
    public PerformanceView(GameScreen g) : base(g) { }

    protected override void Build(GameState s)
    {
        var live = PerformanceEngine.Compute(s, s.Reviews.Count > 0 ? s.Reviews[^1].Date.AddDays(1) : s.StartDate, s.CurrentDate);
        var tiles = new GridContainer { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        tiles.AddChild(Ui.Tile("Current period (projected)", $"{live.Overall:0}", PerformanceEngine.Label(live.Rating), Palette.ForScore(live.Overall, 70, 50)));
        tiles.AddChild(Ui.Tile("Decision quality", $"{live.DecisionQuality:0}", "Evidence, consultation, risk, finance"));
        tiles.AddChild(Ui.Tile("Delivery", $"{live.Delivery:0}", "Assignments & project schedule"));
        tiles.AddChild(Ui.Tile("Financial stewardship", $"{live.FinancialStewardship:0}", "Budget adherence & savings"));
        tiles.AddChild(Ui.Tile("Risk management", $"{live.RiskManagement:0}", "Incidents & remediation"));
        Body.AddChild(tiles);
        Body.AddChild(Ui.Card(Ui.VBox(4, new Control[] { Ui.Caption("Evidence the next review will use") }.Concat(live.Evidence.Select(e => (Control)Ui.Label("• " + e, 14, null, wrap: true))).ToArray())));

        foreach (var r in s.Reviews.AsEnumerable().Reverse())
            Body.AddChild(Ui.Card(Ui.VBox(4,
                Ui.HBox(10, Ui.Label($"Review {r.Period}", 17), Ui.Spacer(), Ui.Label($"{PerformanceEngine.Label(r.Rating)} · {r.Overall:0}", 15, Palette.ForScore(r.Overall, 70, 50))),
                Ui.Label(r.ReviewerComment, 14, Palette.Muted, wrap: true))));

        Body.AddChild(Ui.Caption("Decision record — decision quality is judged at the time; outcomes are recorded when they happen"));
        var rows = s.Decisions.AsEnumerable().Reverse().Select(d => new[]
        {
            $"{d.Summary}{(d.Evaluation.Notes.Count > 0 ? "\n   " + string.Join(" ", d.Evaluation.Notes) : "")}{(d.OutcomeNotes.Count > 0 ? "\n   Outcome: " + string.Join(" ", d.OutcomeNotes) : "")}",
            d.Date.ToString("MMM d"), $"{d.Evaluation.Evidence:0}", $"{d.Evaluation.Stakeholders:0}", $"{d.Evaluation.Risk:0}", $"{d.Evaluation.Overall:0}", d.OutcomeScore is double o ? $"{o:0}" : "pending",
        }).ToList();
        Body.AddChild(Ui.Card(rows.Count == 0 ? Ui.Label("No decisions recorded yet.", 14, Palette.Muted)
            : Ui.Table(new[] { "Decision", "Date", "Evidence", "Stakeholders", "Risk", "Quality", "Outcome" }, rows)));
    }
}
