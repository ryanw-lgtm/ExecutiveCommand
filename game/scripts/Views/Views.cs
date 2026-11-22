using System.Text.Json.Nodes;
using ExecutiveCommand.Core.Actions;
using ExecutiveCommand.Core.AI;
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
        Body = Ui.VBox(16);
        AddChild(Ui.Scroll(Body));
    }

    public void Refresh(GameSession session)
    {
        Ui.Clear(Body);
        Body.AddChild(Ui.Heading(Title, 28));
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
        var hist = s.History;
        MetricSnapshot? prev = hist.Count >= 2 ? hist[^2] : null, now = hist.LastOrDefault();
        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        int risk = s.Risks.Where(r => r.Known && !r.Closed).Sum(r => r.Score);

        if (s.Outcome != GameOutcome.InProgress)
            Body.AddChild(Ui.AccentCard(Ui.Label("Your tenure as CTO has ended. Review the record in Performance, then start a new career from the menu.", Fonts.Base, Palette.TextPrimary, wrap: true), Palette.Danger));

        // ── Row 1: attention (wide) + financial pulse (narrow)
        var row1 = Ui.HBox(16);
        var attention = Guidance.Attention(s);
        var att = Ui.VBox(10, Ui.HBox(8, Ui.Caption("Needs your attention"), Ui.Spacer(), Ui.Label($"{attention.Count} item{(attention.Count == 1 ? "" : "s")}", Fonts.Xs, Palette.TextMuted)));
        if (attention.Count == 0) att.AddChild(Ui.Empty("Nothing needs you right now.", "A good moment to plan ahead in the Meeting Room — or advance time.", "check-circle"));
        foreach (var item in attention.Take(6))
        {
            var (icon, color) = item.Level switch
            {
                AttentionLevel.Urgent => ("warning-circle", Palette.Danger),
                AttentionLevel.Important => ("arrow-right", Palette.Warning),
                _ => ("info", Palette.Info),
            };
            var btn = Ui.Button(item.ButtonLabel, () =>
            {
                if (item.NavigateTo == "Meeting Room" || (item.MeetWith != null && item.NavigateTo != "Decisions")) Game.OpenMeeting(item.MeetWith ?? new[] { ExecRole.CIO }, item.Prefill);
                else if (item.NavigateTo == "Inbox") Game.OpenInbox(item.RelatedId);
                else Game.Navigate(item.NavigateTo);
            }, primary: item.Level == AttentionLevel.Urgent);
            btn.CustomMinimumSize = new Vector2(168, 34);
            btn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            var label = Ui.Label(item.Text, Fonts.Base, Palette.TextPrimary, wrap: true);
            att.AddChild(Ui.HBox(12, Ui.Icon(icon, 20, color), label, btn));
        }
        var attCard = Ui.Card(att, null, null, 20);
        attCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        attCard.SizeFlagsStretchRatio = 2.1f;
        row1.AddChild(attCard);

        var pulse = Ui.VBox(10, Ui.Caption("Financial pulse"));
        var margins = hist.Where(h => h.OperatingMargin != null).Select(h => h.OperatingMargin!.Value * 100).ToArray();
        var marginRow = Ui.HBox(12, Ui.Num(last == null ? "—" : $"{last.OperatingMargin * 100:0.0}%", 32, last == null ? Palette.TextPrimary : Palette.ForScore(last.OperatingMargin * 100, 2.5, 0)), Ui.Spacer());
        if (margins.Length >= 3) marginRow.AddChild(new Sparkline { Values = margins, LineColor = Palette.TextSecondary, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        pulse.AddChild(Ui.VBox(0, Ui.Label("Operating margin", Fonts.Sm, Palette.TextSecondary), marginRow,
            Ui.Label(last == null ? "Appears after the first month-end close." : $"{new DateTime(last.Year, last.Month, 1):MMMM} close · healthy is 2–4%", Fonts.Xs, Palette.TextMuted)));
        pulse.AddChild(new HSeparator());
        void Line(string name, string value, Control? trend = null, string? tip = null)
        {
            var r = Ui.HBox(8, Ui.Label(name, Fonts.Sm, Palette.TextSecondary), Ui.Spacer(), Ui.Num(value, Fonts.Base));
            if (trend != null) { trend.CustomMinimumSize = new Vector2(84, 0); r.AddChild(trend); }
            if (tip != null) r.TooltipText = tip;
            pulse.AddChild(r);
        }
        decimal dailyOpex = (last?.Opex ?? 93_500_000m) / 30m;
        Line("Cash", SimulationEngine.Money(f.Cash), prev != null && now != null ? Ui.Trend((double)((now.Cash - prev.Cash) / 1_000_000m), true, "+0.0;-0.0", "M") : null,
            $"About {(double)(f.Cash / Math.Max(1, dailyOpex)):0} days of operating costs on hand.");
        Line("Tech capital left", SimulationEngine.Money(f.TechCapitalBudgetRemaining), null, $"Remaining from the {SimulationEngine.Money(f.TechCapitalBudgetAnnual)} technology capital budget this fiscal year.");
        decimal approved = s.Savings.Where(v => v.Source == "budget_cut" && v.Recurring && v.Status != SavingsStatus.Reversed).Sum(v => v.AnnualAmount);
        Line("Savings vs target", $"{SimulationEngine.Money(approved)} / {SimulationEngine.Money(PerformanceEngine.SavingsTarget)}", null, "Recurring savings approved toward the CFO's target.");
        pulse.AddChild(Ui.Bar((double)approved, (double)PerformanceEngine.SavingsTarget, approved >= PerformanceEngine.SavingsTarget ? Palette.Success : Palette.Accent));
        var pulseCard = Ui.Card(pulse, null, null, 20);
        pulseCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pulseCard.CustomMinimumSize = new Vector2(340, 0);
        row1.AddChild(pulseCard);
        Body.AddChild(row1);

        // ── Row 2: KPI strip with trend vs last month and sparkline
        double[] Series(Func<MetricSnapshot, double> sel) => hist.Select(sel).ToArray();
        Control? D(Func<MetricSnapshot, double> sel, bool higherBetter) => prev != null && now != null ? Ui.Trend(sel(now) - sel(prev), higherBetter) : null;
        var t = s.Technology;
        var kpis = Ui.HBox(16);
        kpis.AddChild(Ui.Tile("Board confidence", $"{s.Metrics.BoardConfidence:0}", "Moves with reviews, incidents and escalations", Palette.ForScore(s.Metrics.BoardConfidence),
            D(h => h.BoardConfidence, true), Series(h => h.BoardConfidence), "How much the board trusts the leadership team. Very low confidence can end your tenure."));
        kpis.AddChild(Ui.Tile("Employee morale", $"{s.Metrics.EmployeeMorale:0}", $"Technology team {tech.Morale:0}", Palette.ForScore(s.Metrics.EmployeeMorale),
            D(h => h.Morale, true), Series(h => h.Morale), "Cuts, overload and incidents lower it."));
        kpis.AddChild(Ui.Tile("Known risk", $"{risk}", $"{s.Risks.Count(r => r.Known && !r.Closed)} open risks in the register", risk > 60 ? Palette.Danger : risk > 40 ? Palette.Warning : Palette.Success,
            D(h => h.AssessedRisk, false), Series(h => h.AssessedRisk), "Total score of known open risks. Some risks are only discovered by investigating."));
        kpis.AddChild(Ui.Tile("EHR recovery time", $"{t.ReportedRtoHours:0} h", $"Documented · target {t.TargetRtoHours:0} h{(t.HasSecondarySite ? " · secondary site live" : "")}",
            t.ReportedRtoHours <= t.TargetRtoHours ? Palette.Success : Palette.Danger, null, null,
            "How long recovering the EHR after a disaster would take, as currently documented. It may be wrong until someone tests it."));
        Body.AddChild(kpis);

        // ── Row 3: ideas (wide) + upcoming timeline (narrow)
        var row3 = Ui.HBox(16);
        var ideas = Guidance.Suggestions(s).Where(x => !x.Text.EndsWith('?')).Take(4).ToList();
        var box = Ui.VBox(8, Ui.Caption("Things you could say"));
        if (ideas.Count == 0) box.AddChild(Ui.Empty("No suggestions right now.", "You've covered the obvious moves. Ask your team what they're seeing in the Meeting Room.", "chats-circle"));
        foreach (var idea in ideas)
        {
            var b = Ui.Button("\u201c" + idea.Text + "\u201d", () => Game.OpenMeeting(idea.Attendees, idea.Text), icon: "chats-circle");
            b.Alignment = HorizontalAlignment.Left;
            b.TooltipText = idea.Why + " — opens the meeting room with this sentence ready to send.";
            b.AddThemeFontOverride("font", Fonts.Sans);
            box.AddChild(b);
        }
        var ideasCard = Ui.Card(box, null, null, 20);
        ideasCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ideasCard.SizeFlagsStretchRatio = 2.1f;
        row3.AddChild(ideasCard);

        var upcoming = new List<(DateOnly, string)>();
        upcoming.AddRange(s.Assignments.Where(a => a.Status is AssignmentStatus.Active or AssignmentStatus.Overdue).Select(a => (a.Due, $"{a.Assignee}: {a.Subject}{(a.Status == AssignmentStatus.Overdue ? " (late)" : "")}")));
        upcoming.AddRange(s.Proposals.Where(p => p.Status == ProposalStatus.AwaitingApproval && p.ApprovalDueDate != null).Select(p => (p.ApprovalDueDate!.Value, $"CFO/CEO decide: {p.Title}")));
        upcoming.AddRange(s.Contracts.Where(c => !c.Renewed).Select(c => (c.RenewalDate, $"{c.Vendor} renewal{(c.Negotiated ? " (negotiated)" : "")}")));
        upcoming.AddRange(s.Projects.Where(p => p.Status != ProjectStatus.Completed).Select(p => (p.PlannedEndDate, $"Go-live: {p.Name}")));
        var q = Ui.VBox(8, Ui.Caption("Coming up"));
        if (upcoming.Count == 0) q.AddChild(Ui.Empty("Nothing scheduled.", "Delegated work, approvals and go-lives will appear here."));
        foreach (var (d, text) in upcoming.OrderBy(u => u.Item1).Take(6))
        {
            var date = Ui.Num(d.ToString("MMM dd"), Fonts.Sm, d < s.CurrentDate ? Palette.Danger : Palette.Accent);
            date.CustomMinimumSize = new Vector2(60, 0);
            q.AddChild(Ui.HBox(12, date, Ui.Label(text, Fonts.Sm, Palette.TextPrimary, wrap: true)));
        }
        var upCard = Ui.Card(q, null, null, 20);
        upCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        upCard.CustomMinimumSize = new Vector2(340, 0);
        row3.AddChild(upCard);
        Body.AddChild(row3);
    }
}

// =========================================================================== Inbox

public partial class InboxView : StateView
{
    public override string Title => "Inbox";
    private string? _selected;
    public InboxView() { }
    public InboxView(GameScreen g) : base(g) { }

    public void Select(string? messageId) { if (messageId != null) _selected = messageId; }

    protected override void Build(GameState s)
    {
        var split = Ui.HBox(12);
        split.SizeFlagsVertical = SizeFlags.ExpandFill;
        // This view lives in a scroll area, so size it to the window explicitly.
        if (IsInsideTree()) split.CustomMinimumSize = new Vector2(0, Math.Max(420, GetViewportRect().Size.Y - 190));
        var list = new ItemList { CustomMinimumSize = new Vector2(460, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        var msgs = s.Inbox.AsEnumerable().Reverse().ToList();
        foreach (var m in msgs)
        {
            int i = list.AddItem($"{(m.Read ? "  " : "● ")}{m.Date:MMM d}  {m.Subject}");
            list.SetItemCustomFgColor(i, m.Kind switch { MessageKind.Incident => Palette.Bad, MessageKind.Escalation => Palette.Warn, _ => m.Read ? Palette.Muted : Palette.Text });
        }
        var reader = new RichTextLabel { BbcodeEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(400, 0), SelectionEnabled = true };
        reader.AddThemeFontSizeOverride("normal_font_size", 16);
        var actions = Ui.HBox(8);
        var right = Ui.VBox(8, reader, actions);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        void Show(int idx)
        {
            var m = msgs[idx];
            _selected = m.Id;
            m.Read = true;
            list.SetItemText(idx, $"  {m.Date:MMM d}  {m.Subject}");
            var from = m.From == ExecRole.None ? "System" : s.TryExec(m.From) is { } x ? $"{x.Name}, {x.Title}" : m.From.ToString();
            reader.Clear();
            reader.AppendText($"[font_size=20]{Ui.Esc(m.Subject)}[/font_size]\n[color={Palette.Hex(Palette.Muted)}]{m.Date:dddd, MMMM d, yyyy} · {Ui.Esc(from)} · {m.Kind}[/color]\n\n{Ui.Esc(m.Body)}");
            Ui.Clear(actions);
            if (s.Incidents.Any(i => i.Id == m.RelatedId && i.Status == IncidentStatus.Active && i.ResponseOptions.Count > 0))
                actions.AddChild(Ui.Button("Respond to incident", () => Game.Navigate("Decisions"), primary: true));
            if (m.From is not ExecRole.None && s.TryExec(m.From) is { } sender)
            {
                var first = sender.Name.Replace("Dr. ", "").Split(' ')[0];
                actions.AddChild(Ui.Button($"Discuss with {first}", () => Game.OpenMeeting(new[] { sender.Role }, $"{first}, about \"{m.Subject}\" — what do you recommend?")));
            }
            actions.AddChild(Ui.Spacer());
            actions.AddChild(Ui.Button("Mark all read", () => { foreach (var x in s.Inbox) x.Read = true; Game.RefreshAll(); }));
        }
        list.ItemSelected += i => Show((int)i);
        split.AddChild(list);
        split.AddChild(right);
        Body.AddChild(split);
        int sel = Math.Max(0, msgs.FindIndex(m => m.Id == _selected));
        if (msgs.Count > 0) { list.Select(sel); Show(sel); }
    }
}

// =========================================================================== Decisions (proposals + incidents)

public partial class ProposalsView : StateView
{
    private static IEnumerable<ExecRole> DiscussWith(Proposal p)
    {
        var roles = new List<ExecRole> { p.Sponsor, ExecRole.CFO };
        if (p.ClinicallySignificant) roles.Add(ExecRole.CMO);
        if (p.SecurityRiskReduction >= 4 || p.Topic is "backup" or "security") roles.Add(ExecRole.CISO);
        return roles.Distinct();
    }

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

        Body.AddChild(Ui.Label("Approve, defer or reject here — or press \"Discuss first\" to hear people out. Positions reflect the evidence available today: commissioning an assessment can change minds.", 14, Palette.Muted, wrap: true));
        foreach (var p in s.Proposals.Where(p => p.Status is ProposalStatus.Pending or ProposalStatus.Deferred or ProposalStatus.AwaitingApproval))
        {
            var v = Ui.VBox(6);
            v.AddChild(Ui.HBox(10, Ui.Label(p.Title, Fonts.Lg, font: Fonts.SansSemi), Ui.Spacer(), Ui.Label(p.Status == ProposalStatus.Deferred ? $"Deferred until {p.DeferredUntil:MMM d}" : p.Status == ProposalStatus.AwaitingApproval ? $"With CFO/CEO — decision {p.ApprovalDueDate:MMM d}" : "Pending", 13, Palette.Muted)));
            v.AddChild(Ui.Label(p.Summary, 14, Palette.Muted, wrap: true));
            v.AddChild(Ui.Label($"Sponsor: {Roles.Label(p.Sponsor)}   ·   Capital {M(p.Capex)}   ·   Operating {M(p.AnnualOpex)}/yr   ·   ~{p.DurationDays} days" +
                                (ApproveProposalHandler.RequiresExecutiveApproval(s, p) ? "   ·   too big for you alone: approving sends it to the CFO and CEO" : "   ·   you can approve this yourself"), 14, null, wrap: true));
            // Who supports it and why — shown in plain view.
            var stances = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            stances.AddThemeConstantOverride("v_separation", 2);
            foreach (var role in new[] { ExecRole.CFO, ExecRole.CEO, ExecRole.CIO, ExecRole.CISO, ExecRole.CMO, ExecRole.EADirector })
            {
                var st = Core.Executives.StanceEngine.OnProposal(s, s.Exec(role), p);
                var col = st.Position switch { Core.Executives.Position.Support => Palette.Good, Core.Executives.Position.Oppose => Palette.Bad, Core.Executives.Position.Concerned => Palette.Warn, _ => Palette.Muted };
                string word = st.Position switch { Core.Executives.Position.Support => "supports", Core.Executives.Position.Oppose => "opposes", Core.Executives.Position.Concerned => "concerned", Core.Executives.Position.Neutral => "neutral", _ => "it depends" };
                var who = Ui.Label($"{s.Exec(role).Name.Replace("Dr. ", "").Split(' ')[0]} ({role})", 13, Palette.Muted);
                who.CustomMinimumSize = new Vector2(150, 0);
                var pos = Ui.Label(word, 13, col);
                pos.CustomMinimumSize = new Vector2(90, 0);
                string why = st.Headline;
                if (st.Conditions.Count > 0 && st.Position != Core.Executives.Position.Conditional) why += " Wants: " + st.Conditions[0] + ".";
                stances.AddChild(who); stances.AddChild(pos); stances.AddChild(Ui.Label(why, 13, null, wrap: true));
            }
            v.AddChild(stances);
            if (p.Status != ProposalStatus.AwaitingApproval)
                v.AddChild(Ui.HBox(8,
                    Ui.Button("Approve", () => SubmitWithConfirmation(new ActionProposal("approve_proposal", new JsonObject { ["proposal_id"] = p.Id }), $"[Decisions panel] approve {p.Id}"), primary: true),
                    Ui.Button("Defer 30 days", () => SubmitWithConfirmation(new ActionProposal("defer_proposal", new JsonObject { ["proposal_id"] = p.Id, ["days"] = 30 }), $"[Decisions panel] defer {p.Id}")),
                    Ui.Button("Reject", () => SubmitWithConfirmation(new ActionProposal("reject_proposal", new JsonObject { ["proposal_id"] = p.Id }), $"[Decisions panel] reject {p.Id}")),
                    Ui.Spacer(),
                    Ui.Button("Discuss first", () => Game.OpenMeeting(DiscussWith(p), $"Where do you stand on {p.Title.ToLowerInvariant()}?"))));
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
        if (s.Projects.Count == 0) Body.AddChild(Ui.Card(Ui.Empty("No approved projects yet.", "Approve a proposal in Decisions and it becomes a project here, with progress, milestones and spend.", "kanban")));
        foreach (var p in s.Projects)
        {
            var v = Ui.VBox(6);
            var color = p.Status == ProjectStatus.OnHold ? Palette.Warn : p.Status == ProjectStatus.Completed ? Palette.Good : Palette.Accent;
            v.AddChild(Ui.HBox(10, Ui.Label(p.Name, Fonts.Lg, font: Fonts.SansSemi), Ui.Spacer(), Ui.Label($"{p.Status}  ·  owner {Roles.Label(p.Owner)}", 13, color)));
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
            st.Count == 0 ? Ui.Empty("No month has closed yet.", "Statements appear at each month-end close. Advance time to reach the end of the month.", "chart-line-up")
            : Ui.Table(new[] { "Month", "Revenue", "Operating exp.", "Technology", "Depreciation", "Op. margin", "Net income", "Capex", "Cash" },
                st.Select(m => new[] { new DateTime(m.Year, m.Month, 1).ToString("MMMM yyyy"), M(m.Revenue), M(m.Opex), M(m.TechOpex), M(m.Depreciation), m.OperatingMargin.ToString("P1"), M(m.NetIncome), M(m.Capex), M(m.ClosingCash) }),
                (r, c) => c == 6 && st[r].NetIncome < 0 ? Palette.Bad : null))));

        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Technology budget (annual)"),
            Ui.Table(new[] { "Line", "Budget", "Committed", "Earmarked", "Cut", "Discretionary left", "Fiscal YTD actual" },
                tech.BudgetLines.Select(l => new[] { l.Name, M(l.AnnualBudget), M(l.CommittedAnnual), M(l.EarmarkedAnnual), M(l.ApprovedReductionsAnnual), M(l.DiscretionaryRemaining), M(l.ActualFiscalYtd) })))));

        var sv = s.Savings.ToList();
        Body.AddChild(Ui.Card(Ui.VBox(6, Ui.Caption("Savings tracker — proposed ≠ approved ≠ realized"),
            sv.Count == 0 ? Ui.Empty("No savings yet.", "Cut a discretionary budget line in a meeting (e.g. \"Cut $100k from travel\"), or ask for a cost review first to find realistic savings.", "chart-line-up")
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
                Ui.HBox(10, Ui.Label(r.Title, Fonts.Base, font: Fonts.SansSemi), Ui.Spacer(), Ui.Label(r.Closed ? "CLOSED" : $"Score {r.Score}  (L{r.Likelihood} × I{r.Impact})", 14, color)),
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
            var portrait = Ui.Avatar(x.Name, 56);
            v.AddChild(Ui.HBox(16, portrait, Ui.VBox(2, Ui.Label(x.Name, Fonts.Lg, font: Fonts.SansSemi), Ui.Label(x.Title, Fonts.Sm, Palette.TextSecondary),
                Ui.Label(x.ReportsTo == s.Player.Role ? "Reports to you" : x.Role == ExecRole.Vendor ? "External" : x.ReportsTo == ExecRole.None ? "Reports to the board" : $"Reports to {Roles.Label(x.ReportsTo)}", Fonts.Xs, x.ReportsTo == s.Player.Role ? Palette.Accent : Palette.TextMuted))));
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
        Body.AddChild(Ui.Card(rows.Count == 0 ? Ui.Empty("No decisions recorded yet.", "Decisions you make in meetings or on the Decisions screen are scored here — first on the evidence you had, later on how they turned out.", "medal")
            : Ui.Table(new[] { "Decision", "Date", "Evidence", "Stakeholders", "Risk", "Quality", "Outcome" }, rows)));
    }
}
