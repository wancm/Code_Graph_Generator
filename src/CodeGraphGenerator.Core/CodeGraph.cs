namespace CodeGraphGenerator.Core;

/// <summary>Everything one generation found: the projects, their types and the links between the types.</summary>
public sealed record CodeGraph(
    string Root,
    string Source,
    DateTimeOffset GeneratedAt,
    string ToolVersion,
    IReadOnlyList<GraphProject> Projects,
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<string> Warnings);

/// <summary>One C# project of the code base.</summary>
public sealed record GraphProject(string Name, string Path, bool IsTest);

/// <summary>One type (class, interface, record, struct, enum or delegate) declared in the code base.</summary>
public sealed record GraphNode(
    string Id,
    string Name,
    string FullName,
    string Namespace,
    int Project,
    NodeKind Kind,
    IReadOnlyList<SourceLocation> Files,
    bool IsEntryPoint,
    bool IsFrameworkEntry,
    ReferenceState Reference = ReferenceState.Referenced);

/// <summary>One link from a type to a type it depends on. <see cref="Where"/> holds the first few places it happens.</summary>
public sealed record GraphEdge(int From, int To, EdgeKind Kind, IReadOnlyList<SourceLocation> Where);

/// <summary>A file, relative to the chosen folder when it lies inside it, and a 1-based line (0 when unknown).</summary>
public sealed record SourceLocation(string File, int Line);

public enum NodeKind { Class, Interface, Record, Struct, RecordStruct, Enum, Delegate }

/// <summary>Lower value = stronger link: when two types are linked in several ways, the edge keeps the strongest.</summary>
public enum EdgeKind { Implements = 0, Calls = 1, Uses = 2 }

/// <summary>Whether anything refers to a type, which is how possible dead code is found.</summary>
public enum ReferenceState { Referenced, EntryPoint, TestCode, TestsOnly, Unreferenced }
