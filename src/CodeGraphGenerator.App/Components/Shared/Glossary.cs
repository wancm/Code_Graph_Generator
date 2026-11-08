namespace CodeGraphGenerator.App.Components.Shared;

/// <summary>
/// One glossary entry, copied word for word from the global glossary (C:\Users\wanch\.claude\GLOSSARY.md).
/// The tooltip is always "{Standard term}: {Explanation}".
/// </summary>
public sealed record GlossaryTerm(string Display, string Standard, string Explanation)
{
    public string Tooltip => $"{Standard}: {Explanation}";
}

/// <summary>The glossary terms this app shows, verbatim from GLOSSARY.md ("Code Graph Generator" and shared terms).</summary>
public static class Glossary
{
    public static readonly GlossaryTerm CodeGraph = new("Code graph", "Code graph (dependency graph)", "A map of a code base: each type is a box, and each line is one type using another.");
    public static readonly GlossaryTerm CodeBase = new("Code base", "Solution or project file", "The .sln, .slnx or .csproj the code graph is built from.");
    public static readonly GlossaryTerm SelectFolder = new("Select folder", "Folder picker", "Opens Windows' folder dialog to choose the folder that holds the code base.");
    public static readonly GlossaryTerm RestoreFirst = new("Run dotnet restore first", "Package restore", "Runs dotnet restore on the code base before reading it, so the types of .NET and of NuGet packages resolve.");
    public static readonly GlossaryTerm OutputFile = new("Output file", "Output path", "Where the code graph's HTML file is written.");
    public static readonly GlossaryTerm Generate = new("Generate", "Generate code graph", "Reads the code base with Roslyn and writes the code graph as one HTML file.");
    public static readonly GlossaryTerm Link = new("Link", "Dependency (edge)", "One line in the code graph: a type calling, using or implementing another.");
    public static readonly GlossaryTerm NoIncomingReferences = new("No incoming references", "Unreferenced type (possible dead code)", "No other type in the code base refers to it; it may still be used through reflection, attributes or a framework.");
    public static readonly GlossaryTerm OnlyTestsUseIt = new("Only tests use it", "Test-only reference", "Only test projects refer to this type.");
    public static readonly GlossaryTerm Readme = new("Readme", "README file", "The app's own guide (README.md): what it does, how to start it, and every endpoint and MCP tool.");
    public static readonly GlossaryTerm Version = new("Version", "Application version", "The build of this app that is running.");
}
