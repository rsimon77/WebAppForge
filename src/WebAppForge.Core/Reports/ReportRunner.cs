using Microsoft.Data.Sqlite;
using WebAppForge.Core.Model;

namespace WebAppForge.Core.Reports;

/// <summary>Runs report plans against a SQLite database (tests and the editor preview).</summary>
public sealed class ReportRunner(string connectionString)
{
    public string ConnectionString { get; } = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Runs one plan and returns its columns and rows (NULL is null, numbers are long or double, text is string).</summary>
    public ReportResult Run(ReportPlan plan)
    {
        using var connection = Open();
        return ReportEngine.Run(connection, plan);
    }

    public async Task<ReportResult> RunAsync(ReportPlan plan)
    {
        await using var connection = Open();
        return await ReportEngine.RunAsync(connection, plan);
    }

    /// <summary>Loads one page of a summary report, with its groups, totals and detail rows.</summary>
    public SummaryData RunSummary(Project project, ReportDef report, SpecRequest request, int page = 1, string[]? showAll = null)
    {
        using var connection = Open();
        return ReportEngine.LoadSummary(connection, new ReportQueryBuilder(project).Summary(report), request, page, showAll);
    }

    /// <summary>Loads a crosstab report with its headings, cells and totals.</summary>
    public CrosstabData RunCrosstab(Project project, ReportDef report, SpecRequest request)
    {
        using var connection = Open();
        return ReportEngine.LoadCrosstab(connection, new ReportQueryBuilder(project).Crosstab(report), request);
    }
}
