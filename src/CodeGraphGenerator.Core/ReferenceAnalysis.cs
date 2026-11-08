namespace CodeGraphGenerator.Core;

/// <summary>Decides, for every type, whether other types refer to it.</summary>
public static class ReferenceAnalysis
{
    /// <summary>
    /// A type with no incoming edge is Unreferenced (possible dead code) unless it is an entry point or lives in a
    /// test project. One referred to only by test code is TestsOnly. Links from a type to itself don't count.
    /// </summary>
    public static IReadOnlyList<GraphNode> Classify(
        IReadOnlyList<GraphProject> projects, IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        var fromCode = new bool[nodes.Count];
        var fromTests = new bool[nodes.Count];

        foreach (GraphEdge edge in edges)
        {
            if (edge.From == edge.To)
            {
                continue;
            }

            if (projects[nodes[edge.From].Project].IsTest)
            {
                fromTests[edge.To] = true;
            }
            else
            {
                fromCode[edge.To] = true;
            }
        }

        return nodes.Select((node, i) => node with
        {
            Reference = projects[node.Project].IsTest ? ReferenceState.TestCode
                : node.IsEntryPoint || node.IsFrameworkEntry ? ReferenceState.EntryPoint
                : fromCode[i] ? ReferenceState.Referenced
                : fromTests[i] ? ReferenceState.TestsOnly
                : ReferenceState.Unreferenced
        }).ToArray();
    }
}
