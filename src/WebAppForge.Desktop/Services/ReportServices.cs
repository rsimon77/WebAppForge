using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.Services;

public enum ReportIssueSeverity
{
    /// <summary>The generator would fail or produce a broken report. Generate is disabled.</summary>
    Error,

    /// <summary>Allowed, but probably not what was meant. Never blocks Generate.</summary>
    Warning,
}

/// <summary>One finding about a report, as the editor lists it.</summary>
public sealed record ReportIssueView(ReportIssueSeverity Severity, string ReportName, string Message)
{
    public bool IsError => Severity == ReportIssueSeverity.Error;

    /// <summary>"Error: Sales: message", as the validation list shows it.</summary>
    public string Text => (IsError ? "Error: " : "Warning: ")
        + (string.IsNullOrEmpty(ReportName) ? string.Empty : ReportName + ": ")
        + Message;
}

/// <summary>Checks the reports of a project. The editor calls it after every change; errors disable Generate.</summary>
public interface IReportValidationService
{
    IReadOnlyList<ReportIssueView> Validate(Project project);
}

/// <summary>The default validation: finds nothing. Replace it (MainViewModel.ReportValidation) with the Core validator.</summary>
public sealed class NoReportValidation : IReportValidationService
{
    public IReadOnlyList<ReportIssueView> Validate(Project project) => [];
}

/// <summary>The rows a preview run returned, or the reason it failed (<see cref="Error"/> is then set).</summary>
public sealed record ReportPreviewResult(string[] Columns, object?[][] Rows, string? Error)
{
    public bool Succeeded => string.IsNullOrEmpty(Error);

    public static ReportPreviewResult Failed(string error) => new([], [], error);
}

/// <summary>Runs a report against the project's database for the "Run preview" button.</summary>
public interface IReportPreviewService
{
    Task<ReportPreviewResult> RunAsync(Project project, ReportDef report, CancellationToken cancellationToken);
}

/// <summary>The default preview: reports that it is not available. Replace it (MainViewModel.ReportPreview) with the Core runner.</summary>
public sealed class NoReportPreview : IReportPreviewService
{
    public const string UnavailableText = "Preview is not available yet";

    public Task<ReportPreviewResult> RunAsync(Project project, ReportDef report, CancellationToken cancellationToken)
        => Task.FromResult(ReportPreviewResult.Failed(UnavailableText));
}
