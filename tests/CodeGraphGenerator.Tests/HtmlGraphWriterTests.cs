using CodeGraphGenerator.Core;

namespace CodeGraphGenerator.Tests;

public sealed class HtmlGraphWriterTests
{
    private static CodeGraph Graph(string typeName) => new(
        Root: @"C:\code\shop",
        Source: "Shop.sln",
        GeneratedAt: new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.FromHours(8)),
        ToolVersion: "1.0.0",
        Projects: [new GraphProject("Shop", "Shop/Shop.csproj", false)],
        Nodes:
        [
            new GraphNode("a", typeName, "Shop." + typeName, "Shop", 0, NodeKind.Class, [new SourceLocation("Shop/A.cs", 3)], false, false),
            new GraphNode("b", "B", "Shop.B", "Shop", 0, NodeKind.Interface, [new SourceLocation("Shop/B.cs", 1)], false, false, ReferenceState.Unreferenced)
        ],
        Edges: [new GraphEdge(0, 1, EdgeKind.Implements, [new SourceLocation("Shop/A.cs", 3)])],
        Warnings: []);

    [Fact]
    public void The_page_carries_the_graph_and_its_title()
    {
        string html = HtmlGraphWriter.Render(Graph("A"));

        Assert.DoesNotContain("/*GRAPH_DATA*/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{TITLE}}", html, StringComparison.Ordinal);
        Assert.Contains("<title>Code graph · Shop.sln</title>", html, StringComparison.Ordinal);
        Assert.Contains("\"r\":\"unreferenced\"", html, StringComparison.Ordinal);
        Assert.Contains("[0,1,\"i\",[[\"Shop/A.cs\",3]]]", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_looks_like_markup_cannot_break_out_of_the_script()
    {
        string html = HtmlGraphWriter.Render(Graph("X</script><script>alert(1)</script>"));

        Assert.Equal(2, html.Split("</script>").Length - 1);
    }

    [Fact]
    public void The_page_needs_nothing_from_the_network()
    {
        string html = HtmlGraphWriter.LoadTemplate();

        Assert.DoesNotContain("<script src", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html, StringComparison.OrdinalIgnoreCase);
    }
}
