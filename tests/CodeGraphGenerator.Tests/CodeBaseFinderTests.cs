using CodeGraphGenerator.Core;

namespace CodeGraphGenerator.Tests;

public sealed class CodeBaseFinderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("code-graph-finder-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relative)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void Solutions_come_first_shallowest_first_and_hide_the_projects()
    {
        Touch("deep/Other.slnx");
        Touch("Main.sln");
        Touch("src/App/App.csproj");

        IReadOnlyList<CodeBaseCandidate> found = CodeBaseFinder.Find(_root);

        Assert.Equal(["Main.sln", "deep/Other.slnx"], found.Select(c => c.Display));
        Assert.All(found, c => Assert.Equal(CodeBaseKind.Solution, c.Kind));
    }

    [Fact]
    public void Without_a_solution_every_project_is_offered_with_an_all_projects_choice_first()
    {
        Touch("src/App/App.csproj");
        Touch("src/Lib/Lib.csproj");

        IReadOnlyList<CodeBaseCandidate> found = CodeBaseFinder.Find(_root);

        Assert.Equal(CodeBaseKind.AllProjects, found[0].Kind);
        Assert.Equal(2, found[0].ProjectPaths.Count);
        Assert.Equal(["src/App/App.csproj", "src/Lib/Lib.csproj"], found.Skip(1).Select(c => c.Display));
    }

    [Fact]
    public void Build_output_and_tool_folders_are_skipped()
    {
        Touch("bin/Debug/Copy.csproj");
        Touch("obj/Generated.csproj");
        Touch("node_modules/pkg/Thing.csproj");
        Touch(".git/x.sln");

        Assert.Empty(CodeBaseFinder.Find(_root));
    }

    [Fact]
    public void Folders_deeper_than_the_limit_are_not_searched()
    {
        Touch("a/b/c/d/e/Deep.csproj");

        Assert.Empty(CodeBaseFinder.Find(_root, maxDepth: 4));
        Assert.Single(CodeBaseFinder.Find(_root, maxDepth: 5));
    }
}
