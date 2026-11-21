using ExecutiveCommand.Core.Model;

namespace ExecutiveCommand.Core.Engine;

/// <summary>
/// Monthly close. Every dollar is a ledger entry; statements are derived from the ledger and the
/// balance sheet identity (Cash + Net PP&amp;E = Debt + Equity) is enforced after every close.
/// </summary>
public static class FinancialEngine
{
    public sealed class ReconciliationException(string message) : Exception(message);

    public static DateOnly FiscalYearEnd(GameState s, DateOnly d)
    {
        int startYear = d.Month >= s.FiscalYearStartMonth ? d.Year : d.Year - 1;
        return new DateOnly(startYear, s.FiscalYearStartMonth, 1).AddYears(1).AddDays(-1);
    }

    public static DateOnly FiscalYearStart(GameState s, DateOnly d) => FiscalYearEnd(s, d).AddDays(1).AddYears(-1);

    /// <summary>Annualized reduction actually in effect for a line during the month starting at <paramref name="monthStart"/>.</summary>
    public static decimal ReductionsInEffect(GameState s, string lineId, DateOnly monthStart) =>
        s.Savings.Where(x => x.BudgetLineId == lineId && x.Source == "budget_cut" && x.Status is SavingsStatus.Approved or SavingsStatus.Realized
                             && x.EffectiveDate <= monthStart && (x.EndDate == null || x.EndDate >= monthStart))
                 .Sum(x => x.AnnualAmount);

    public static void MonthlyClose(SimulationEngine e)
    {
        var s = e.State;
        var f = s.Finance;
        var end = s.CurrentDate;
        var start = new DateOnly(end.Year, end.Month, 1);
        decimal openingCash = f.Cash;

        void Book(LedgerCategory cat, decimal amount, string account, string memo, string? dept = null, string? src = null)
        {
            if (amount == 0) return;
            f.Ledger.Add(new LedgerEntry { Id = s.NewId("LED"), Date = end, Category = cat, Amount = Math.Round(amount, 2), Account = account, Memo = memo, DepartmentId = dept, SourceId = src });
        }

        // Revenue (patient service revenue with mild variance) less revenue lost to incidents.
        decimal revenue = f.MonthlyRevenueBase * (decimal)(1 + e.Rng.NextGaussian(0, 0.012));
        Book(LedgerCategory.Revenue, revenue, "Net patient service revenue", "Monthly revenue");
        if (f.PendingRevenueLoss > 0) { Book(LedgerCategory.Revenue, -f.PendingRevenueLoss, "Net patient service revenue", "Revenue lost to downtime"); }
        decimal lost = f.PendingRevenueLoss;
        f.PendingRevenueLoss = 0;

        Book(LedgerCategory.Opex, f.MonthlyOtherOpex * (decimal)(1 + e.Rng.NextGaussian(0, 0.008)), "Clinical & administrative operations", "Non-technology operating costs");

        // Technology department, line by line.
        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        foreach (var line in tech.BudgetLines)
        {
            decimal annual = line.AnnualBudget - ReductionsInEffect(s, line.Id, start);
            decimal monthly = annual / 12m;
            if (line.Id != "personnel") monthly *= (decimal)(1 + e.Rng.NextGaussian(0, 0.02));
            monthly = Math.Round(monthly, 2);
            line.ActualFiscalYtd += monthly;
            Book(LedgerCategory.Opex, monthly, line.Name, "Technology spend", tech.Id, line.Id);
        }
        foreach (var p in s.Projects.Where(p => p.Status == ProjectStatus.Completed && p.AnnualOpex > 0))
            Book(LedgerCategory.Opex, p.AnnualOpex / 12m, "Project run costs", p.Name, tech.Id, p.Id);
        if (f.PendingIncidentCost > 0) Book(LedgerCategory.Opex, f.PendingIncidentCost, "Incident response & recovery", "Incident costs", tech.Id);
        f.PendingIncidentCost = 0;

        // Depreciation for in-service assets.
        foreach (var a in f.Assets.Where(a => a.InService && a.AccumulatedDepreciation < a.Cost))
        {
            decimal dep = Math.Min(a.Cost - a.AccumulatedDepreciation, Math.Round(a.Cost / a.UsefulLifeMonths, 2));
            if (a.InServiceDate > start && a.ProjectId != null) dep = Math.Round(dep / 2, 2); // half-month convention
            a.AccumulatedDepreciation += dep;
            Book(LedgerCategory.Depreciation, dep, "Depreciation", a.Name, src: a.Id);
        }
        Book(LedgerCategory.Interest, f.Debt * f.AnnualInterestRate / 12m, "Interest expense", "Long-term debt");

        // Savings realization: realized = reduction actually in effect this month.
        foreach (var sv in s.Savings.Where(x => x.Source == "budget_cut" && x.Status is SavingsStatus.Approved or SavingsStatus.Realized
                                                 && x.EffectiveDate <= start && (x.EndDate == null || x.EndDate >= start)))
        {
            sv.RealizedToDate += Math.Round(sv.AnnualAmount / 12m, 2);
            sv.Status = SavingsStatus.Realized;
        }

        // Derive the statement from this month's ledger only.
        var month = f.Ledger.Where(l => l.Date >= start && l.Date <= end).ToList();
        decimal rev = month.Where(l => l.Category == LedgerCategory.Revenue).Sum(l => l.Amount);
        decimal opex = month.Where(l => l.Category == LedgerCategory.Opex).Sum(l => l.Amount);
        decimal techOpex = month.Where(l => l.Category == LedgerCategory.Opex && l.DepartmentId == tech.Id).Sum(l => l.Amount);
        decimal dep2 = month.Where(l => l.Category == LedgerCategory.Depreciation).Sum(l => l.Amount);
        decimal interest = month.Where(l => l.Category == LedgerCategory.Interest).Sum(l => l.Amount);
        decimal capex = month.Where(l => l.Category == LedgerCategory.Capex).Sum(l => l.Amount);
        decimal netIncome = rev - opex - dep2 - interest;

        f.Cash += rev - opex - interest - capex;
        f.GrossPpe += capex;
        foreach (var c in month.Where(l => l.Category == LedgerCategory.Capex))
        {
            var asset = f.Assets.FirstOrDefault(a => a.ProjectId == c.SourceId);
            if (asset == null) { asset = new CapitalAsset { Id = s.NewId("AST"), Name = c.Memo, ProjectId = c.SourceId, UsefulLifeMonths = 60 }; f.Assets.Add(asset); }
            asset.Cost += c.Amount;
        }
        f.AccumulatedDepreciation += dep2;
        f.RetainedEarningsSinceStart += netIncome;

        var st = new MonthlyStatement
        {
            Year = end.Year, Month = end.Month, Revenue = rev, Opex = opex, TechOpex = techOpex, Depreciation = dep2, Interest = interest,
            Capex = capex, NetIncome = netIncome, OpeningCash = openingCash, ClosingCash = f.Cash, RevenueLostToIncidents = lost,
        };
        f.Statements.Add(st);
        AssertReconciles(s);
        // Keep the save compact: only the last 24 months of detailed entries are retained; statements keep history.
        var cutoff = start.AddMonths(-24);
        f.Ledger.RemoveAll(l => l.Date < cutoff);

        decimal ytdBudget = TechBudgetYtd(s);
        decimal ytdActual = tech.BudgetLines.Sum(l => l.ActualFiscalYtd);
        e.Notify(MessageKind.Finance, ExecRole.CFO, $"Monthly close — {end:MMMM yyyy}",
            $"Revenue {SimulationEngine.Money(rev)}, operating margin {st.OperatingMargin:P1}, net income {SimulationEngine.Money(netIncome)}. " +
            $"Cash {SimulationEngine.Money(f.Cash)}. Technology opex this month {SimulationEngine.Money(techOpex)}; fiscal YTD {SimulationEngine.Money(ytdActual)} vs plan {SimulationEngine.Money(ytdBudget)}." +
            (lost > 0 ? $" Downtime cost {SimulationEngine.Money(lost)} in lost revenue." : ""));
    }

    /// <summary>Prorated fiscal-year-to-date budget for the technology department lines (before reductions).</summary>
    public static decimal TechBudgetYtd(GameState s)
    {
        var tech = s.Departments.First(d => d.Id == s.Player.DepartmentId);
        var fyStart = FiscalYearStart(s, s.CurrentDate);
        int monthsClosed = s.Finance.Statements.Count(m => new DateOnly(m.Year, m.Month, 1) >= fyStart);
        return tech.BudgetLines.Sum(l => l.AnnualBudget) / 12m * monthsClosed;
    }

    public static void AssertReconciles(GameState s)
    {
        var f = s.Finance;
        decimal assets = f.Cash + f.NetPpe;
        decimal claims = f.Debt + f.Equity;
        if (Math.Abs(assets - claims) > 0.05m)
            throw new ReconciliationException($"Balance sheet does not reconcile: assets {assets} vs liabilities+equity {claims}");
        decimal assetCost = f.Assets.Sum(a => a.Cost);
        decimal assetDep = f.Assets.Sum(a => a.AccumulatedDepreciation);
        if (Math.Abs(assetCost - f.GrossPpe) > 0.05m || Math.Abs(assetDep - f.AccumulatedDepreciation) > 0.05m)
            throw new ReconciliationException($"Fixed asset subledger ({assetCost}/{assetDep}) does not match general ledger ({f.GrossPpe}/{f.AccumulatedDepreciation})");
    }

    public static void FiscalYearRollover(SimulationEngine e)
    {
        var s = e.State;
        s.Finance.TechCapitalBudgetRemaining = s.Finance.TechCapitalBudgetAnnual;
        foreach (var line in s.Departments.SelectMany(d => d.BudgetLines)) line.ActualFiscalYtd = 0;
        foreach (var sv in s.Savings.Where(x => !x.Recurring && x.EndDate < s.CurrentDate && x.Source == "budget_cut" && x.Status != SavingsStatus.Reversed))
        {
            var line = s.Departments.SelectMany(d => d.BudgetLines).FirstOrDefault(l => l.Id == sv.BudgetLineId);
            if (line != null) line.ApprovedReductionsAnnual = Math.Max(0, line.ApprovedReductionsAnnual - sv.AnnualAmount);
        }
        e.Notify(MessageKind.Finance, ExecRole.CFO, $"New fiscal year FY{s.CurrentDate.Year + 1}",
            $"Technology capital budget reset to {SimulationEngine.Money(s.Finance.TechCapitalBudgetAnnual)}. One-time savings from last year have expired.");
    }
}
