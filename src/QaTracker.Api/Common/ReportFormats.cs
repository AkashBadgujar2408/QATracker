using QaTracker.Application.Common;
using QaTracker.Application.Features.Reports;

namespace QaTracker.Api.Common;

/// <summary>The <c>format</c> query value of report downloads: xlsx (default) or pdf.</summary>
public static class ReportFormats
{
    public static bool TryParse(string? value, out ReportFormat format)
    {
        format = ReportFormat.Xlsx;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        format = ReportFormat.Pdf;
        return value.Equals("pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static IResult Invalid() => Error.Validation("format", "Format must be xlsx or pdf.").ToProblem();
}
