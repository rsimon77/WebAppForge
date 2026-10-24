using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Model;
using WebAppForge.Core.Reports;
using CoreSeverity = WebAppForge.Core.Reports.ReportIssueSeverity;

namespace WebAppForge.Desktop.Services;

/// <summary>Validates reports with the same checks the generator applies.</summary>
public sealed class CoreReportValidationService : IReportValidationService
{
    public IReadOnlyList<ReportIssueView> Validate(Project project) =>
        ReportValidator.Validate(project)
            .Select(issue => new ReportIssueView(
                issue.Severity == CoreSeverity.Error ? ReportIssueSeverity.Error : ReportIssueSeverity.Warning,
                issue.ReportName,
                WithoutReportPrefix(issue)))
            .ToList();

    /// <summary>Core messages start with "Report 'Name': "; the editor's issue text adds the report name itself.</summary>
    private static string WithoutReportPrefix(ReportIssue issue)
    {
        var prefix = $"Report '{issue.ReportName}': ";
        return issue.Message.StartsWith(prefix, StringComparison.Ordinal) ? issue.Message[prefix.Length..] : issue.Message;
    }
}

/// <summary>
/// Runs a report's query against the project's SQLite database with the same query layer the generated site uses.
/// The preview shows the first rows of the data the report is built from (groups, cells, series points or events).
/// </summary>
public sealed class CoreReportPreviewService(Func<string?> projectFolder) : IReportPreviewService
{
    /// <summary>Rows kept in the preview grid.</summary>
    public const int MaxRows = 500;

    public async Task<ReportPreviewResult> RunAsync(Project project, ReportDef report, CancellationToken cancellationToken)
    {
        var error = ReportValidator.Errors(project).FirstOrDefault(i => string.Equals(i.ReportName, report.Name, StringComparison.OrdinalIgnoreCase));
        if (error is not null)
        {
            return ReportPreviewResult.Failed(error.Message);
        }

        if (report.Type == ReportType.Dashboard)
        {
            return ReportPreviewResult.Failed("A dashboard has no data of its own. Preview the reports it contains.");
        }

        var folder = projectFolder();
        if (string.IsNullOrEmpty(project.Database.Path))
        {
            return ReportPreviewResult.Failed("Set the SQLite database file on the Database tab first.");
        }

        if (!Path.IsPathRooted(project.Database.Path) && string.IsNullOrEmpty(folder))
        {
            return ReportPreviewResult.Failed("Save the project first so the relative database path can be resolved.");
        }

        var connectionString = project.Database.ResolveConnectionString(folder ?? string.Empty);
        var file = connectionString["Data Source=".Length..];
        if (!File.Exists(file))
        {
            return ReportPreviewResult.Failed($"The database file was not found: {file}");
        }

        var builder = new ReportQueryBuilder(project);
        if (builder.NeedsOwner(report))
        {
            return ReportPreviewResult.Failed("The table has an owner column, so the report's data depends on the signed-in user. Open the generated site to see it.");
        }

        try
        {
            var request = builder.Request();
            var plan = report.Type switch
            {
                ReportType.Summary => builder.SummaryGroups(report, request, report.Summary.Groups.Count, limit: MaxRows),
                ReportType.Crosstab => builder.Crosstab(report, request, CrosstabPart.Cells),
                ReportType.Chart => builder.Series(report, request),
                ReportType.Calendar => builder.Rows(report, request, CalendarFields(report), MaxRows),
                _ => throw new NotSupportedException(),
            };

            cancellationToken.ThrowIfCancellationRequested();
            var result = await new ReportRunner(connectionString).RunAsync(plan).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var rows = result.Rows.Take(MaxRows).Select(row => row).ToArray();
            return new ReportPreviewResult(result.Columns, rows, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ReportPreviewResult.Failed(ex.Message);
        }
    }

    private static IEnumerable<string> CalendarFields(ReportDef report)
    {
        var calendar = report.Calendar;
        return new[] { calendar.TitleField, calendar.StartField, calendar.EndField }
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
