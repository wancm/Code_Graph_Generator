using System.IO;
using Markdig;

namespace CodeGraphGenerator.App.Services;

/// <summary>README.md as HTML, and when the file last changed.</summary>
public sealed record ReadmePage(string Html, DateTime? ChangedAt);

public static class ReadmeRender
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static ReadmePage Read()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "README.md");

        if (!File.Exists(path))
        {
            return new ReadmePage("<p class=\"text-muted\">README.md is missing next to the app.</p>", null);
        }

        return new ReadmePage(Markdown.ToHtml(File.ReadAllText(path), Pipeline), File.GetLastWriteTime(path));
    }
}
