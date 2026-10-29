using QaTracker.Application.Features.Reports;

namespace QaTracker.Infrastructure.Reports;

/// <summary>Sprint reports and the issue-list export (ClosedXML for Excel, SkiaSharp for PDF – ADR-035).</summary>
public sealed class ReportRenderer : IReportRenderer
{
    public byte[] SprintExcel(SprintReport report) => ExcelReportWriter.Sprint(report);

    public byte[] SprintPdf(SprintReport report) => PdfReportWriter.Sprint(report);

    public byte[] IssueListExcel(IssueExport export) => ExcelReportWriter.IssueList(export);
}
