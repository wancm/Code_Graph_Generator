using CodeGraphGenerator.Core;

namespace CodeGraphGenerator.Tests;

public sealed class ReferenceAnalysisTests
{
    private static readonly GraphProject[] Projects = [new("App", "App/App.csproj", false), new("App.Tests", "App.Tests/App.Tests.csproj", true)];

    private static GraphNode Node(string name, int project = 0, bool entry = false, bool framework = false) =>
        new(name, name, name, "App", project, NodeKind.Class, [], entry, framework);

    private static GraphEdge Edge(int from, int to) => new(from, to, EdgeKind.Calls, []);

    [Fact]
    public void A_type_nothing_refers_to_is_unreferenced()
    {
        IReadOnlyList<GraphNode> result = ReferenceAnalysis.Classify(Projects, [Node("Lonely")], []);

        Assert.Equal(ReferenceState.Unreferenced, result[0].Reference);
    }

    [Fact]
    public void A_link_from_a_type_to_itself_does_not_count()
    {
        IReadOnlyList<GraphNode> result = ReferenceAnalysis.Classify(Projects, [Node("Recursive")], [Edge(0, 0)]);

        Assert.Equal(ReferenceState.Unreferenced, result[0].Reference);
    }

    [Fact]
    public void Entry_points_and_framework_types_are_never_unreferenced()
    {
        IReadOnlyList<GraphNode> result = ReferenceAnalysis.Classify(
            Projects, [Node("Program", entry: true), Node("OrdersController", framework: true)], []);

        Assert.All(result, n => Assert.Equal(ReferenceState.EntryPoint, n.Reference));
    }

    [Fact]
    public void A_type_only_test_code_refers_to_is_tests_only()
    {
        IReadOnlyList<GraphNode> result = ReferenceAnalysis.Classify(
            Projects, [Node("Helper"), Node("HelperTests", project: 1)], [Edge(1, 0)]);

        Assert.Equal(ReferenceState.TestsOnly, result[0].Reference);
        Assert.Equal(ReferenceState.TestCode, result[1].Reference);
    }

    [Fact]
    public void One_reference_from_real_code_makes_a_type_referenced()
    {
        IReadOnlyList<GraphNode> result = ReferenceAnalysis.Classify(
            Projects, [Node("Helper"), Node("Service"), Node("HelperTests", project: 1)], [Edge(1, 0), Edge(2, 0)]);

        Assert.Equal(ReferenceState.Referenced, result[0].Reference);
    }
}
