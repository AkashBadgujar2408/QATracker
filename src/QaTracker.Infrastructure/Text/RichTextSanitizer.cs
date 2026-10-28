using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using QaTracker.Application.Abstractions;

namespace QaTracker.Infrastructure.Text;

/// <summary>
/// HtmlSanitizer (Ganss.Xss) limited to what the rich-text editor produces: paragraphs, emphasis, lists, links, code,
/// quotes. No images (screenshots are attachments), styles, scripts or event handlers; links get rel="noopener".
/// </summary>
public sealed partial class RichTextSanitizer : IRichTextSanitizer
{
    private static readonly string[] AllowedTags =
        ["p", "br", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li", "a", "code", "pre", "blockquote", "h3", "h4"];

    private readonly HtmlSanitizer _sanitizer;

    public RichTextSanitizer()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(AllowedTags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(["http", "https", "mailto"], StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
        });
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement anchor)
            {
                anchor.SetAttribute("rel", "noopener noreferrer");
                anchor.SetAttribute("target", "_blank");
            }
        };
    }

    public string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var clean = _sanitizer.Sanitize(html).Trim();
        return ToPlainText(clean).Length == 0 ? null : clean;
    }

    public string ToPlainText(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
