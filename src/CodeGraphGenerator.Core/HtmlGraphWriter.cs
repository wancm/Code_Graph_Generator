using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CodeGraphGenerator.Core;

/// <summary>Writes a code graph as one self-contained HTML file: the viewer's code and the graph's data in a single page.</summary>
public static class HtmlGraphWriter
{
    private const string DataPlaceholder = "/*GRAPH_DATA*/null";

    private const string TitlePlaceholder = "{{TITLE}}";

    // The default encoder escapes <, > and & too, so the JSON can't close the <script> it sits in.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.Default };

    public static string Render(CodeGraph graph)
    {
        string template = LoadTemplate();
        string title = $"Code graph · {Path.GetFileName(graph.Source)}";

        return template
            .Replace(TitlePlaceholder, EscapeText(title), StringComparison.Ordinal)
            .Replace(DataPlaceholder, JsonSerializer.Serialize(ToData(graph), Json), StringComparison.Ordinal);
    }

    public static async Task WriteAsync(CodeGraph graph, string path, CancellationToken cancellationToken = default)
    {
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));

        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        await File.WriteAllTextAsync(path, Render(graph), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The data the page reads, with short property names to keep big graphs small.</summary>
    internal static object ToData(CodeGraph graph) => new
    {
        root = graph.Root,
        source = graph.Source,
        generatedAt = graph.GeneratedAt.ToString("o"),
        tool = graph.ToolVersion,
        warnings = graph.Warnings,
        projects = graph.Projects.Select(p => new { n = p.Name, f = p.Path, t = p.IsTest }),
        nodes = graph.Nodes.Select(n => new
        {
            n = n.Name,
            f = n.FullName,
            s = n.Namespace,
            p = n.Project,
            k = KindCode(n.Kind),
            r = ReferenceCode(n.Reference),
            l = n.Files.Select(f => new object[] { f.File, f.Line })
        }),
        edges = graph.Edges.Select(e => new object[]
        {
            e.From, e.To, EdgeCode(e.Kind), e.Where.Select(w => new object[] { w.File, w.Line })
        })
    };

    internal static string LoadTemplate()
    {
        using Stream stream = typeof(HtmlGraphWriter).Assembly.GetManifestResourceStream("CodeGraphGenerator.Core.GraphTemplate.html")
            ?? throw new InvalidOperationException("The HTML template is missing from the assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Only what HTML text needs; non-ASCII stays readable in the file.
    private static string EscapeText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EdgeCode(EdgeKind kind) => kind switch
    {
        EdgeKind.Implements => "i",
        EdgeKind.Calls => "c",
        _ => "u"
    };

    private static string KindCode(NodeKind kind) => kind switch
    {
        NodeKind.Interface => "interface",
        NodeKind.Record => "record",
        NodeKind.Struct => "struct",
        NodeKind.RecordStruct => "record struct",
        NodeKind.Enum => "enum",
        NodeKind.Delegate => "delegate",
        _ => "class"
    };

    private static string ReferenceCode(ReferenceState state) => state switch
    {
        ReferenceState.EntryPoint => "entry",
        ReferenceState.TestCode => "test",
        ReferenceState.TestsOnly => "testsOnly",
        ReferenceState.Unreferenced => "unreferenced",
        _ => "referenced"
    };
}
