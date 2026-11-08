using CodeGraphGenerator.Core;

namespace CodeGraphGenerator.Tests;

/// <summary>Restores tests/Fixtures/SampleShop and builds its graph once for every test that needs it.</summary>
public sealed class SampleShopGraph : IAsyncLifetime
{
    public string Root { get; } = Path.Combine(RepoRoot(), "tests", "Fixtures", "SampleShop");

    public CodeGraph Graph { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        CodeBaseCandidate solution = CodeBaseFinder.Find(Root).Single(c => c.Kind == CodeBaseKind.Solution);
        int exitCode = await DotnetRestore.RunAsync(solution.Path, progress: null, CancellationToken.None);
        Assert.Equal(0, exitCode);

        Graph = await new CodeGraphBuilder().BuildAsync(solution, Root);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public GraphNode Node(string name) => Graph.Nodes.Single(n => n.Name == name);

    public GraphEdge? Edge(string from, string to)
    {
        int a = Index(from), b = Index(to);
        return Graph.Edges.SingleOrDefault(e => e.From == a && e.To == b);
    }

    private int Index(string name) => Graph.Nodes.ToList().FindIndex(n => n.Name == name);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeGraphGenerator.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repository root (CodeGraphGenerator.slnx).");
    }
}
