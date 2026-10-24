using System.Globalization;
using WebAppForge.Core.Model;
using WebAppForge.Core.Reports;
using static WebAppForge.Core.Tests.ReportSupport;

namespace WebAppForge.Core.Tests;

public sealed class ReportQueryTests : IDisposable
{
    private readonly string _root = TempFolder();
    private readonly Project _project;
    private readonly ReportQueryBuilder _builder;
    private readonly ReportRunner _runner;
    private readonly List<Row> _rows = Rows();

    public ReportQueryTests()
    {
        _project = CreateProject(_root);
        _builder = new ReportQueryBuilder(_project);
        _runner = new ReportRunner(Conn(_project));
    }

    public void Dispose() => MdSupport.TryDelete(_root);

    private ReportDef Sum(Action<SummarySettings> edit, Action<ReportDef>? report = null)
    {
        var r = new ReportDef { Name = "s", Table = "orders", Type = ReportType.Summary };
        edit(r.Summary);
        report?.Invoke(r);
        return r;
    }

    private ReportResult Groups(ReportDef r, SpecRequest? req = null, int level = 1) =>
        _runner.Run(_builder.SummaryGroups(r, req ?? _builder.Request(), level));

    private static string Num(object? v) => ReportEngine.KeyOf(v);

    private static string D(double? v) => v is null ? "" : Math.Round(v.Value, 6).ToString("R", CultureInfo.InvariantCulture);

    [Fact]
    public void AggregatesPerGroupMatchLinq()
    {
        var r = Sum(s =>
        {
            s.Groups.Add(new ReportField { Field = "region" });
            foreach (var f in new[] { AggregateFunction.Count, AggregateFunction.CountDistinct, AggregateFunction.Sum, AggregateFunction.Average, AggregateFunction.Min, AggregateFunction.Max })
            {
                s.Aggregates.Add(new ReportAggregate { Field = f == AggregateFunction.Count ? "" : "amount", Function = f });
            }

            s.Aggregates[1].Field = "customer";
        });
        var result = Groups(r);
        var expected = _rows.GroupBy(x => x.Region).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Count, result.Rows.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var g = expected[i];
            var row = result.Rows[i];
            Assert.Equal(g.Key ?? "", Num(row[0]));
            Assert.Equal(g.Count(), Convert.ToInt32(row[1]));
            Assert.Equal(g.Where(x => x.Customer != null).Select(x => x.Customer).Distinct().Count(), Convert.ToInt32(row[2]));
            var amounts = g.Where(x => x.Amount != null).Select(x => x.Amount!.Value).ToList();
            Assert.Equal(D(amounts.Sum()), D(Convert.ToDouble(row[3])));
            Assert.Equal(D(amounts.Average()), D(Convert.ToDouble(row[4])));
            Assert.Equal(D(amounts.Min()), D(Convert.ToDouble(row[5])));
            Assert.Equal(D(amounts.Max()), D(Convert.ToDouble(row[6])));
        }

        var total = _runner.Run(_builder.SummaryTotal(r, _builder.Request())).Rows[0];
        Assert.Equal(_rows.Count, Convert.ToInt32(total[0]));
    }

    private static string Week(DateTime d) => d.Year.ToString("0000") + "-W" + ((d.DayOfYear - 1 + 7 - ((int)d.DayOfWeek + 6) % 7) / 7).ToString("00");

    [Theory]
    [InlineData(GroupInterval.Day)]
    [InlineData(GroupInterval.Week)]
    [InlineData(GroupInterval.Month)]
    [InlineData(GroupInterval.Quarter)]
    [InlineData(GroupInterval.Year)]
    public void DateIntervalsMatchLinq(GroupInterval interval)
    {
        var r = Sum(s =>
        {
            s.Groups.Add(new ReportField { Field = "odate", Interval = interval });
            s.Aggregates.Add(new ReportAggregate());
        });
        string? Key(Row x)
        {
            if (x.Date is null) return null;
            var d = DateTime.ParseExact(x.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return interval switch
            {
                GroupInterval.Day => x.Date,
                GroupInterval.Week => Week(d),
                GroupInterval.Month => d.ToString("yyyy-MM"),
                GroupInterval.Quarter => d.Year + "-Q" + ((d.Month + 2) / 3),
                _ => d.Year.ToString(),
            };
        }

        var expected = _rows.GroupBy(Key).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key ?? "", g.Count())).ToList();
        var actual = Groups(r).Rows.Select(row => (Num(row[0]), Convert.ToInt32(row[1]))).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RangeAndFirstLetterMatchLinq()
    {
        var range = Sum(s => { s.Groups.Add(new ReportField { Field = "qty", Interval = GroupInterval.Range, RangeSize = 5 }); s.Aggregates.Add(new ReportAggregate()); });
        var expected = _rows.GroupBy(x => x.Qty is null ? (long?)null : x.Qty / 5 * 5).OrderBy(g => g.Key).Select(g => (g.Key?.ToString() ?? "", g.Count())).ToList();
        Assert.Equal(expected, Groups(range).Rows.Select(row => (Num(row[0]), Convert.ToInt32(row[1]))).ToList());

        var letter = Sum(s => { s.Groups.Add(new ReportField { Field = "customer", Interval = GroupInterval.FirstLetter }); s.Aggregates.Add(new ReportAggregate()); });
        var expectedLetters = _rows.GroupBy(x => x.Customer?.Substring(0, 1).ToUpperInvariant()).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key ?? "", g.Count())).ToList();
        Assert.Equal(expectedLetters, Groups(letter).Rows.Select(row => (Num(row[0]), Convert.ToInt32(row[1]))).ToList());
    }

    [Fact]
    public void SortModes()
    {
        ReportDef R(SortDirection dir, GroupSortBy by) => Sum(s =>
        {
            s.Groups.Add(new ReportField { Field = "customer", Sort = dir, SortBy = by });
            s.Aggregates.Add(new ReportAggregate { Field = "qty", Function = AggregateFunction.Sum });
        });
        var byCustomer = _rows.GroupBy(x => x.Customer).ToList();
        Assert.Equal(byCustomer.OrderByDescending(g => g.Key, StringComparer.Ordinal).Select(g => g.Key ?? "").ToList(),
            Groups(R(SortDirection.Descending, GroupSortBy.Value)).Rows.Select(r => Num(r[0])).ToList());
        var byAgg = byCustomer.OrderByDescending(g => g.Where(x => x.Qty != null).Sum(x => x.Qty!.Value)).Select(g => g.Key ?? "").ToList();
        Assert.Equal(byAgg, Groups(R(SortDirection.Descending, GroupSortBy.Aggregate)).Rows.Select(r => Num(r[0])).ToList());
        var asc = byCustomer.OrderBy(g => g.Where(x => x.Qty != null).Sum(x => x.Qty!.Value)).Select(g => g.Key ?? "").ToList();
        Assert.Equal(asc, Groups(R(SortDirection.Ascending, GroupSortBy.Aggregate)).Rows.Select(r => Num(r[0])).ToList());
    }

    [Fact]
    public void FiltersOwnerAndTableFilter()
    {
        var r = Sum(s =>
        {
            s.Groups.Add(new ReportField { Field = "region" });
            s.Aggregates.Add(new ReportAggregate());
            s.Filters.Add(new ReportFilter { Field = "customer" });
            s.Filters.Add(new ReportFilter { Field = "odate" });
            s.Filters.Add(new ReportFilter { Field = "qty" });
        });
        long Count(SpecRequest req) => Convert.ToInt64(_runner.Run(_builder.SummaryTotal(r, req)).Rows[0][0]);

        var req = _builder.Request();
        req.Filters["customer"] = new SpecFilterValue("Ann");
        Assert.Equal(_rows.Count(x => x.Customer == "Ann"), Count(req));

        req = _builder.Request();
        req.Filters["odate"] = new SpecFilterValue(From: "2023-03-01", To: "2023-06-30");
        Assert.Equal(_rows.Count(x => x.Date != null && string.CompareOrdinal(x.Date, "2023-03-01") >= 0 && string.CompareOrdinal(x.Date, "2023-06-30") <= 0), Count(req));

        req = _builder.Request();
        req.Today = new DateTime(2024, 5, 20);
        req.Filters["odate"] = new SpecFilterValue(Relative: "thisYear");
        Assert.Equal(_rows.Count(x => x.Date != null && x.Date.StartsWith("2024")), Count(req));
        req.Filters["odate"] = new SpecFilterValue(Relative: "lastMonth");
        Assert.Equal(_rows.Count(x => x.Date != null && x.Date.StartsWith("2024-04")), Count(req));

        req = _builder.Request();
        req.Filters["qty"] = new SpecFilterValue("3");
        Assert.Equal(_rows.Count(x => x.Qty == 3), Count(req));
        req.Filters["qty"] = new SpecFilterValue("abc");
        Assert.Equal(_rows.Count, Count(req));

        // a filter field the report does not define is ignored
        req = _builder.Request();
        req.Filters["region"] = new SpecFilterValue("N");
        Assert.Equal(_rows.Count, Count(req));

        // report filter and table filter both apply
        var table = _project.Tables.Single();
        table.Filter = "\"qty\" > 5";
        r.Filter = "\"region\" = 'N'";
        Assert.Equal(_rows.Count(x => x.Qty > 5 && x.Region == "N"), Count(_builder.Request()));
        table.Filter = string.Empty;
    }

    [Fact]
    public void OwnerConditionAppliesOnlyWithSecurity()
    {
        var r = Sum(s => { s.Aggregates.Add(new ReportAggregate()); });
        var table = _project.Tables.Single();
        table.OwnerField = "owner";
        Assert.Equal(_rows.Count, Convert.ToInt32(_runner.Run(_builder.SummaryTotal(r, _builder.Request())).Rows[0][0]));
        _project.Security.Enabled = true;
        var plan = _builder.SummaryTotal(r, _builder.Request("alice"));
        Assert.Contains("\"owner\" = @owner", plan.Sql);
        Assert.Equal(_rows.Count(x => x.Owner == "alice"), Convert.ToInt32(_runner.Run(plan).Rows[0][0]));
        Assert.Equal(0, Convert.ToInt32(_runner.Run(_builder.SummaryTotal(r, _builder.Request("nobody"))).Rows[0][0]));
    }

    [Fact]
    public void InjectionInFilterValuesDoesNotChangeTheSql()
    {
        var r = Sum(s => { s.Aggregates.Add(new ReportAggregate()); s.Filters.Add(new ReportFilter { Field = "customer" }); });
        var good = _builder.Request();
        good.Filters["customer"] = new SpecFilterValue("Ann");
        var bad = _builder.Request();
        bad.Filters["customer"] = new SpecFilterValue("x' OR '1'='1'; DROP TABLE orders; --");
        var a = _builder.SummaryTotal(r, good);
        var b = _builder.SummaryTotal(r, bad);
        Assert.Equal(a.Sql, b.Sql);
        Assert.DoesNotContain("DROP", b.Sql);
        Assert.Equal(0, Convert.ToInt32(_runner.Run(b).Rows[0][0]));
        Assert.Equal(_rows.Count, Convert.ToInt32(_runner.Run(_builder.SummaryTotal(Sum(s => s.Aggregates.Add(new ReportAggregate())), _builder.Request())).Rows[0][0]));
    }

    [Fact]
    public void NestedLevelsDetailAndPaging()
    {
        var r = Sum(s =>
        {
            s.Groups.Add(new ReportField { Field = "region" });
            s.Groups.Add(new ReportField { Field = "customer" });
            s.Aggregates.Add(new ReportAggregate());
            s.DetailFields.AddRange(["id", "amount"]);
            s.GroupsPerPage = 2;
            s.DetailRowsPerGroup = 3;
        });
        var data = _runner.RunSummary(_project, r, _builder.Request(), 1);
        Assert.Equal(2, data.Groups.Count);
        Assert.Equal(_rows.Select(x => x.Region).Distinct().Count(), data.TotalGroups);
        Assert.Equal(2, data.PageCount);
        var first = data.Groups[0];
        var expectedChildren = _rows.Where(x => (x.Region ?? "") == first.Keys[0]).GroupBy(x => x.Customer).Count();
        Assert.Equal(expectedChildren, first.Children.Count);
        var leaf = first.Children[0];
        var leafRows = _rows.Where(x => (x.Region ?? "") == first.Keys[0] && (x.Customer ?? "") == leaf.Keys[1]).OrderBy(x => x.Id).ToList();
        Assert.Equal(leafRows.Count, leaf.DetailTotal);
        Assert.Equal(leafRows.Take(3).Select(x => x.Id), leaf.Detail!.Rows.Select(row => (long)row[0]!));
        Assert.Equal(_rows.Count, Convert.ToInt32(data.GrandTotal![0]));

        var all = _runner.RunSummary(_project, r, _builder.Request(), 1, [first.Keys[0], leaf.Keys[1]]);
        Assert.Equal(leafRows.Count, all.Groups[0].Children[0].Detail!.Rows.Count);
        var bogus = _runner.RunSummary(_project, r, _builder.Request(), 1, ["zzz", "zzz"]);
        Assert.All(bogus.Groups.SelectMany(g => g.Children), c => Assert.True(c.Detail!.Rows.Count <= 3));
    }

    [Fact]
    public void CrosstabCellsTotalsAndFolding()
    {
        var r = Crosstab();
        r.Crosstab.Value = new ReportAggregate { Field = "qty", Function = AggregateFunction.Sum };
        r.Crosstab.MaxColumns = 0;
        var data = _runner.RunCrosstab(_project, r, _builder.Request());
        long? Sum(IEnumerable<Row> rows) { var q = rows.Where(x => x.Qty != null).ToList(); return q.Count == 0 ? null : q.Sum(x => x.Qty!.Value); }
        foreach (var region in _rows.Select(x => x.Region ?? "").Distinct())
        {
            foreach (var customer in _rows.Select(x => x.Customer ?? "").Distinct())
            {
                var cell = _rows.Where(x => (x.Region ?? "") == region && (x.Customer ?? "") == customer).ToList();
                data.Cells.TryGetValue((region, customer), out var value);
                Assert.Equal(Sum(cell), value is null ? null : Convert.ToInt64(value));
            }
        }

        Assert.Equal(Sum(_rows), Convert.ToInt64(data.GrandTotal));
        Assert.Equal(Sum(_rows.Where(x => x.Region == "N")), Convert.ToInt64(data.Rows.Single(x => x.Key == "N").Total));

        r.Crosstab.MaxColumns = 2;
        r.Crosstab.Value = new ReportAggregate();
        var folded = _runner.RunCrosstab(_project, r, _builder.Request());
        Assert.Equal(3, folded.Columns.Count);
        Assert.True(folded.Columns[2].IsOther);
        var keep = folded.Columns.Take(2).Select(c => c.Key).ToList();
        var otherCount = _rows.Count(x => !keep.Contains(x.Customer ?? ""));
        Assert.Equal(otherCount, Convert.ToInt32(folded.Columns[2].Total));
        var northOther = _rows.Count(x => x.Region == "N" && !keep.Contains(x.Customer ?? ""));
        folded.Cells.TryGetValue(("N", ReportEngine.OtherKey), out var nOther);
        Assert.Equal(northOther, nOther is null ? 0 : Convert.ToInt32(nOther));
    }

    [Fact]
    public void SeriesKpiAndRowsPlans()
    {
        var chart = new ReportDef { Name = "c", Table = "orders", Type = ReportType.Chart };
        chart.Chart.Category = new ReportField { Field = "region" };
        chart.Chart.Series.Add(new ChartSeries { Value = new ReportAggregate { Field = "amount", Function = AggregateFunction.Sum } });
        chart.Chart.Series.Add(new ChartSeries());
        chart.Chart.TopN = 2;
        var result = _runner.Run(_builder.Series(chart, _builder.Request()));
        Assert.Equal(2, result.Rows.Count);

        var item = new DashboardItem { Kind = DashboardItemKind.Kpi, Table = "orders", Value = new ReportAggregate { Function = AggregateFunction.Count }, Filter = "\"region\" = 'N'" };
        Assert.Equal(_rows.Count(x => x.Region == "N"), Convert.ToInt32(_runner.Run(_builder.Kpi(item, _builder.Request())).Rows[0][0]));

        var cal = new ReportDef { Name = "k", Table = "orders", Type = ReportType.Calendar };
        var rows = _runner.Run(_builder.Rows(cal, _builder.Request(), ["id", "odate"], 5));
        Assert.Equal(5, rows.Rows.Count);
    }

    [Fact]
    public void ConstraintKeysAreParametersAndParsedSafely()
    {
        var source = _builder.Source(Sum(s => s.Aggregates.Add(new ReportAggregate())));
        Assert.False(ReportEngine.TryParseConstraint(source, "nope:None:10:x", out _));
        Assert.False(ReportEngine.TryParseConstraint(source, "region:Bogus:10:x", out _));
        Assert.True(ReportEngine.TryParseConstraint(source, "region:None:10:N", out var c));
        var r = Sum(s => { s.Groups.Add(new ReportField { Field = "region" }); s.Aggregates.Add(new ReportAggregate()); });
        var req = _builder.Request();
        req.Constraints.Add(new SpecConstraint(c.Group, "N' OR 1=1 --"));
        var plan = _builder.SummaryTotal(r, req);
        Assert.DoesNotContain("1=1", plan.Sql);
        Assert.Equal(0, Convert.ToInt32(_runner.Run(plan).Rows[0][0]));
    }
}
