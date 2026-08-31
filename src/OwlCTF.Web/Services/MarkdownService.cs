using Ganss.Xss;
using Markdig;

namespace OwlCTF.Services;

public sealed class MarkdownService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();
    private readonly HtmlSanitizer _sanitizer = new();

    public string Render(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        return _sanitizer.Sanitize(Markdown.ToHtml(markdown, _pipeline));
    }
}
