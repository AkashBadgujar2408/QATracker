namespace QaTracker.Application.Abstractions;

/// <summary>Cleans rich text (issue steps, results, comments) to a safe HTML subset before it is stored.</summary>
public interface IRichTextSanitizer
{
    /// <summary>Safe HTML, or null when nothing but whitespace/markup is left.</summary>
    string? Sanitize(string? html);

    /// <summary>The visible text of the HTML (tags removed, whitespace collapsed).</summary>
    string ToPlainText(string? html);
}
