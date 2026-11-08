namespace CodeGraphGenerator.Core;

public enum CodeBaseKind { Solution, Project, AllProjects }

/// <summary>Something the graph can be built from: a solution, one project, or every project in a folder.</summary>
public sealed record CodeBaseCandidate(CodeBaseKind Kind, string Path, string Display, IReadOnlyList<string> ProjectPaths);

/// <summary>Finds the solutions (.sln, .slnx) and C# projects in a folder.</summary>
public static class CodeBaseFinder
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules", "packages", "TestResults"
    };

    /// <summary>
    /// Solutions come first, shallowest first. Projects are offered only when the folder has no solution,
    /// with an "all projects" choice in front when there are several.
    /// </summary>
    public static IReadOnlyList<CodeBaseCandidate> Find(string folder, int maxDepth = 4)
    {
        var solutions = new List<string>();
        var projects = new List<string>();
        Walk(new DirectoryInfo(folder), 0, maxDepth, solutions, projects);

        var found = solutions
            .OrderBy(Depth).ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(s => new CodeBaseCandidate(CodeBaseKind.Solution, s, Relative(folder, s), []))
            .ToList();

        if (found.Count > 0 || projects.Count == 0)
        {
            return found;
        }

        var ordered = projects.OrderBy(Depth).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

        if (ordered.Length > 1)
        {
            found.Add(new CodeBaseCandidate(CodeBaseKind.AllProjects, folder, $"All {ordered.Length} projects in the folder", ordered));
        }

        found.AddRange(ordered.Select(p => new CodeBaseCandidate(CodeBaseKind.Project, p, Relative(folder, p), [p])));
        return found;
    }

    private static void Walk(DirectoryInfo dir, int depth, int maxDepth, List<string> solutions, List<string> projects)
    {
        IEnumerable<FileInfo> files;
        IEnumerable<DirectoryInfo> children;

        try
        {
            files = dir.EnumerateFiles().ToArray();
            children = dir.EnumerateDirectories().ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (FileInfo file in files)
        {
            string ext = file.Extension.ToLowerInvariant();

            if (ext is ".sln" or ".slnx")
            {
                solutions.Add(file.FullName);
            }
            else if (ext == ".csproj")
            {
                projects.Add(file.FullName);
            }
        }

        if (depth >= maxDepth)
        {
            return;
        }

        foreach (DirectoryInfo child in children)
        {
            if (!SkippedFolders.Contains(child.Name) && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Walk(child, depth + 1, maxDepth, solutions, projects);
            }
        }
    }

    private static int Depth(string path) => path.Count(c => c is '\\' or '/');

    internal static string Relative(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path.Replace('\\', '/')
            : relative.Replace('\\', '/');
    }
}
