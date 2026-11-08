using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace CodeGraphGenerator.Core;

/// <summary>Reads a C# code base with Roslyn and builds its code graph: every type, and every link between two types.</summary>
public sealed partial class CodeGraphBuilder
{
    private const int MaxLocationsPerEdge = 3;

    private const int MaxWarnings = 30;

    private static readonly string[] TestFrameworkAssemblies =
        ["xunit.core", "xunit.v3.core", "nunit.framework", "Microsoft.VisualStudio.TestPlatform.TestFramework", "MSTest.TestFramework", "TUnit.Core"];

    private static readonly string[] TestNameSuffixes = [".Tests", ".Test", ".UnitTests", ".IntegrationTests", ".Specs"];

    // A framework finds types carrying these attributes (name without "Attribute") by itself, so no code refers to them.
    private static readonly HashSet<string> DiscoveryAttributes = new(StringComparer.Ordinal)
    {
        "Route", "ApiController", "McpServerToolType", "McpServerPromptType", "McpServerResourceType",
        "Migration", "DbContext" // EF Core migrations and model snapshots
    };

    // ... and subclasses of these.
    private static readonly HashSet<string> DiscoveryBaseTypes = new(StringComparer.Ordinal) { "ControllerBase", "PageModel" };

    // ... and implementations of these (EF Core tooling, MediatR's assembly scan).
    private static readonly HashSet<string> DiscoveryInterfaces = new(StringComparer.Ordinal)
    {
        "IDesignTimeDbContextFactory", "IRequestHandler", "INotificationHandler"
    };

    private static readonly SymbolDisplayFormat NameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static readonly SymbolDisplayFormat FullNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    /// Opens the code base with MSBuildWorkspace (which runs MSBuild in its own build-host process), then reads every
    /// C# file twice: first to collect the types, then to find what each type calls, uses and implements.
    /// </summary>
    public async Task<CodeGraph> BuildAsync(
        CodeBaseCandidate codeBase, string root, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        using var workspace = MSBuildWorkspace.Create();
        using var failures = workspace.RegisterWorkspaceFailedHandler(e => AddWarning(warnings, e.Diagnostic.Message));

        progress?.Report($"Opening {codeBase.Display} (MSBuild reads the projects; this can take a while)");
        Solution solution = await OpenAsync(workspace, codeBase, progress, cancellationToken).ConfigureAwait(false);

        // A multi-targeted project loads once per target framework; the first one is enough.
        Project[] projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .GroupBy(p => p.FilePath ?? p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => ProjectName(p), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projects.Length == 0)
        {
            throw new InvalidOperationException($"{codeBase.Display} has no C# project that MSBuild could load.");
        }

        var graphProjects = new List<GraphProject>();
        var sources = new List<ProjectSource>();
        var types = new TypeTable();

        for (int i = 0; i < projects.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Project project = projects[i];
            progress?.Report($"Reading types: {ProjectName(project)} ({i + 1} of {projects.Length})");

            Compilation compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Roslyn could not compile {ProjectName(project)}.");

            bool isTest = IsTestProject(project, compilation);
            graphProjects.Add(new GraphProject(ProjectName(project), CodeBaseFinder.Relative(root, project.FilePath ?? project.Name), isTest));

            ReportCompileErrors(warnings, ProjectName(project), compilation, cancellationToken);

            var source = new ProjectSource(i, project, compilation, compilation.GetEntryPoint(cancellationToken)?.ContainingType);
            sources.Add(source);

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                string? origin = OriginOf(tree, project, cancellationToken);

                if (origin is null)
                {
                    continue;
                }

                source.Origins[tree] = origin;
                CollectTypes(types, source, tree, origin, root, cancellationToken);
            }
        }

        types.Freeze();
        progress?.Report($"Found {types.Count:N0} types in {projects.Length:N0} projects. Reading the links between them");

        var edges = new EdgeTable();

        foreach (ProjectSource source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Reading links: {ProjectName(source.Project)}");

            Parallel.ForEach(
                source.Origins,
                new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
                pair =>
                {
                    SemanticModel model = source.Compilation.GetSemanticModel(pair.Key);
                    var walker = new LinkWalker(model, types, edges, pair.Value, root, source.EntryType, cancellationToken);
                    walker.Visit(pair.Key.GetRoot(cancellationToken));
                });
        }

        GraphNode[] nodes = types.ToNodes();
        GraphEdge[] graphEdges = edges.ToEdges();
        progress?.Report($"Found {graphEdges.Length:N0} links");

        return new CodeGraph(
            Path.GetFullPath(root),
            codeBase.Display,
            DateTimeOffset.Now,
            ToolVersion(),
            graphProjects,
            ReferenceAnalysis.Classify(graphProjects, nodes, graphEdges),
            graphEdges,
            warnings);
    }

    private static async Task<Solution> OpenAsync(
        MSBuildWorkspace workspace, CodeBaseCandidate codeBase, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var loaded = new Progress<ProjectLoadProgress>(p =>
        {
            if (p.Operation == ProjectLoadOperation.Resolve)
            {
                progress?.Report($"Loaded {Path.GetFileName(p.FilePath)}{(p.TargetFramework is null ? "" : $" ({p.TargetFramework})")}");
            }
        });

        if (codeBase.Kind == CodeBaseKind.Solution)
        {
            return await workspace.OpenSolutionAsync(codeBase.Path, loaded, cancellationToken).ConfigureAwait(false);
        }

        foreach (string projectPath in codeBase.ProjectPaths)
        {
            // Opening a project also opens the projects it references, so some may already be in.
            bool alreadyOpen = workspace.CurrentSolution.Projects.Any(p =>
                string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase));

            if (!alreadyOpen)
            {
                await workspace.OpenProjectAsync(projectPath, loaded, cancellationToken).ConfigureAwait(false);
            }
        }

        return workspace.CurrentSolution;
    }

    private static void CollectTypes(TypeTable types, ProjectSource source, SyntaxTree tree, string origin, string root, CancellationToken cancellationToken)
    {
        SemanticModel model = source.Compilation.GetSemanticModel(tree);
        SyntaxNode treeRoot = tree.GetRoot(cancellationToken);

        IEnumerable<SyntaxNode> declarations = treeRoot
            .DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax or TypeDeclarationSyntax)
            .Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

        foreach (SyntaxNode declaration in declarations)
        {
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol symbol)
            {
                types.Add(symbol, source, LocationOf(declaration, origin, root), IsFrameworkEntry(symbol));
            }
        }

        // Top-level statements belong to a type the compiler makes up (Program); it is the entry point.
        if (treeRoot is CompilationUnitSyntax unit && source.EntryType is not null
            && unit.Members.OfType<GlobalStatementSyntax>().FirstOrDefault() is { } first)
        {
            types.Add(source.EntryType, source, LocationOf(first, origin, root), isFrameworkEntry: false);
        }
    }

    /// <summary>
    /// The file a tree's code was written in, or null for generated code that nobody writes by hand. Razor
    /// components are the exception: their generated C# names the .razor file it came from, so they count as that file.
    /// </summary>
    private static string? OriginOf(SyntaxTree tree, Project project, CancellationToken cancellationToken)
    {
        string path = tree.FilePath;
        bool generatedPath = IsGeneratedPath(path);
        SourceText text = tree.GetText(cancellationToken);

        if (generatedPath || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) is false)
        {
            Match razor = RazorSource().Match(text.ToString());

            if (razor.Success)
            {
                string projectDir = Path.GetDirectoryName(project.FilePath) ?? Environment.CurrentDirectory;
                string razorFile = Path.GetFullPath(razor.Groups["p"].Value, projectDir);

                // _Imports.razor only holds @using lines; Razor still makes a class for it that nothing refers to.
                return Path.GetFileName(razorFile).Equals("_Imports.razor", StringComparison.OrdinalIgnoreCase) ? null : razorFile;
            }

            return generatedPath ? null : path;
        }

        string head = text.ToString(new TextSpan(0, Math.Min(text.Length, 1500)));
        return head.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase) ? null : path;
    }

    private static bool IsGeneratedPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
        {
            return true; // source generators name their trees with a relative hint path
        }

        string normalized = path.Replace('/', '\\');
        string file = Path.GetFileName(normalized);

        return normalized.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    internal static SourceLocation LocationOf(SyntaxNode node, string origin, string root)
    {
        FileLinePositionSpan mapped = node.GetLocation().GetMappedLineSpan();

        if (mapped.HasMappedPath)
        {
            return new SourceLocation(CodeBaseFinder.Relative(root, mapped.Path), mapped.StartLinePosition.Line + 1);
        }

        // Inside a generated Razor tree without a #line mapping: the .razor file is known, the line is not.
        bool sameFile = string.Equals(mapped.Path, origin, StringComparison.OrdinalIgnoreCase);
        return new SourceLocation(CodeBaseFinder.Relative(root, origin), sameFile ? mapped.StartLinePosition.Line + 1 : 0);
    }

    private static bool IsFrameworkEntry(INamedTypeSymbol symbol)
    {
        if (symbol.GetAttributes().Any(a => a.AttributeClass is { } c && DiscoveryAttributes.Contains(TrimAttribute(c.Name))))
        {
            return true;
        }

        for (INamedTypeSymbol? b = symbol.BaseType; b is not null; b = b.BaseType)
        {
            if (DiscoveryBaseTypes.Contains(b.Name))
            {
                return true;
            }
        }

        return symbol.AllInterfaces.Any(i => DiscoveryInterfaces.Contains(i.Name));
    }

    private static string TrimAttribute(string name) =>
        name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;

    private static bool IsTestProject(Project project, Compilation compilation)
    {
        string name = ProjectName(project);

        if (TestNameSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return compilation.ReferencedAssemblyNames.Any(a =>
            TestFrameworkAssemblies.Contains(a.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static void ReportCompileErrors(List<string> warnings, string projectName, Compilation compilation, CancellationToken cancellationToken)
    {
        // Seen when the code base was restored by another .NET SDK than the one MSBuild picks now: the design-time
        // build finds no reference assemblies, and fails without saying so.
        if (compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error)
        {
            AddWarning(warnings,
                $"{projectName}: Roslyn found none of .NET's own types, so the project isn't restored for the .NET SDK in use " +
                "and most links are missing. Generate again with Run dotnet restore first ticked.");
            return;
        }

        Diagnostic[] errors = compilation.GetDeclarationDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length > 0)
        {
            AddWarning(warnings,
                $"{projectName}: {errors.Length:N0} compile errors, e.g. {errors[0].GetMessage()} Links to types Roslyn can't " +
                "resolve are missing; restoring or building the code base first usually fixes this.");
        }
    }

    private static void AddWarning(List<string> warnings, string message)
    {
        lock (warnings)
        {
            if (warnings.Count < MaxWarnings && !warnings.Contains(message))
            {
                warnings.Add(message);
            }
        }
    }

    internal static string ProjectName(Project project) =>
        project.FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : project.Name;

    private static string ToolVersion() =>
        typeof(CodeGraphBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "1.0.0";

    // A Razor component's generated C# starts with "#pragma checksum" naming the .razor file, and its #line
    // directives name it again; either is enough.
    [GeneratedRegex("""(?:#pragma\s+checksum|#line[^"\r\n]*)\s*"(?<p>[^"\r\n]+\.(?:razor|cshtml))"\s""", RegexOptions.IgnoreCase)]
    private static partial Regex RazorSource();

    private sealed class ProjectSource(int index, Project project, Compilation compilation, INamedTypeSymbol? entryType)
    {
        public int Index { get; } = index;

        public Project Project { get; } = project;

        public Compilation Compilation { get; } = compilation;

        public INamedTypeSymbol? EntryType { get; } = entryType;

        public Dictionary<SyntaxTree, string> Origins { get; } = [];
    }

    /// <summary>The types found so far, keyed by assembly and documentation id so the same type matches across projects.</summary>
    private sealed class TypeTable
    {
        private readonly Dictionary<string, TypeEntry> _byKey = new(StringComparer.Ordinal);

        private Dictionary<string, int> _index = [];

        public int Count => _byKey.Count;

        public void Add(INamedTypeSymbol symbol, ProjectSource source, SourceLocation location, bool isFrameworkEntry)
        {
            string? key = Key(symbol);

            if (key is null)
            {
                return;
            }

            if (!_byKey.TryGetValue(key, out TypeEntry? entry))
            {
                entry = new TypeEntry(
                    key,
                    symbol.ToDisplayString(NameFormat),
                    symbol.ToDisplayString(FullNameFormat),
                    symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "(global namespace)",
                    source.Index,
                    KindOf(symbol),
                    SymbolEqualityComparer.Default.Equals(symbol, source.EntryType),
                    isFrameworkEntry);
                _byKey[key] = entry;
            }

            // A partial type has one declaration per file.
            if (!entry.Files.Contains(location))
            {
                entry.Files.Add(location);
            }
        }

        public void Freeze()
        {
            _index = _byKey.Values
                .OrderBy(e => e.Project).ThenBy(e => e.FullName, StringComparer.Ordinal)
                .Select((e, i) => (e.Key, i))
                .ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);
        }

        public int IndexOf(ITypeSymbol? type)
        {
            while (type is IArrayTypeSymbol array)
            {
                type = array.ElementType;
            }

            return type is INamedTypeSymbol named && Key(named) is { } key && _index.TryGetValue(key, out int i) ? i : -1;
        }

        public GraphNode[] ToNodes() => _byKey.Values
            .OrderBy(e => _index[e.Key])
            .Select(e => new GraphNode(
                e.Key, e.Name, e.FullName, e.Namespace, e.Project, e.Kind,
                e.Files.OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Line).ToArray(),
                e.IsEntryPoint, e.IsFrameworkEntry))
            .ToArray();

        private static string? Key(INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string? id = definition.GetDocumentationCommentId();
            return id is null ? null : $"{definition.ContainingAssembly?.Name}|{id}";
        }

        private static NodeKind KindOf(INamedTypeSymbol symbol) => symbol.TypeKind switch
        {
            TypeKind.Interface => NodeKind.Interface,
            TypeKind.Enum => NodeKind.Enum,
            TypeKind.Delegate => NodeKind.Delegate,
            TypeKind.Struct => symbol.IsRecord ? NodeKind.RecordStruct : NodeKind.Struct,
            _ => symbol.IsRecord ? NodeKind.Record : NodeKind.Class
        };
    }

    private sealed record TypeEntry(
        string Key, string Name, string FullName, string Namespace, int Project, NodeKind Kind, bool IsEntryPoint, bool IsFrameworkEntry)
    {
        public List<SourceLocation> Files { get; } = [];
    }

    /// <summary>Links found by every walker at once; for each pair of types it keeps the strongest kind and a few places.</summary>
    private sealed class EdgeTable
    {
        private readonly ConcurrentDictionary<(int From, int To), EdgeEntry> _edges = new();

        public void Add(int from, int to, EdgeKind kind, SourceLocation where)
        {
            EdgeEntry entry = _edges.GetOrAdd((from, to), _ => new EdgeEntry(kind));

            lock (entry)
            {
                if (kind < entry.Kind)
                {
                    entry.Kind = kind;
                }

                if (entry.Where.Count < MaxLocationsPerEdge && !entry.Where.Contains(where))
                {
                    entry.Where.Add(where);
                }
            }
        }

        public GraphEdge[] ToEdges() => _edges
            .OrderBy(p => p.Key.From).ThenBy(p => p.Key.To)
            .Select(p => new GraphEdge(
                p.Key.From, p.Key.To, p.Value.Kind,
                p.Value.Where.OrderBy(w => w.File, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Line).ToArray()))
            .ToArray();
    }

    private sealed class EdgeEntry(EdgeKind kind)
    {
        public EdgeKind Kind { get; set; } = kind;

        public List<SourceLocation> Where { get; } = [];
    }

    /// <summary>
    /// Walks one file. Inside each type it records: implements (base list), calls (methods, properties, events,
    /// constructors) and uses (any other mention of a type, and fields). Using directives sit outside any type and
    /// don't count.
    /// </summary>
    private sealed class LinkWalker(
        SemanticModel model, TypeTable types, EdgeTable edges, string origin, string root, INamedTypeSymbol? entryType,
        CancellationToken cancellationToken) : CSharpSyntaxWalker
    {
        private readonly Stack<int> _owners = new();

        private int Owner => _owners.Count > 0 ? _owners.Peek() : -1;

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) => InType(node, () => base.VisitClassDeclaration(node));

        public override void VisitStructDeclaration(StructDeclarationSyntax node) => InType(node, () => base.VisitStructDeclaration(node));

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => InType(node, () => base.VisitInterfaceDeclaration(node));

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => InType(node, () => base.VisitRecordDeclaration(node));

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node) => InType(node, () => base.VisitEnumDeclaration(node));

        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node) => InType(node, () => base.VisitDelegateDeclaration(node));

        public override void VisitGlobalStatement(GlobalStatementSyntax node)
        {
            _owners.Push(types.IndexOf(entryType));
            base.VisitGlobalStatement(node);
            _owners.Pop();
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            LinkName(node);
            base.VisitIdentifierName(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            LinkName(node);
            base.VisitGenericName(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            LinkCreation(node);
            base.VisitObjectCreationExpression(node);
        }

        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            LinkCreation(node);
            base.VisitImplicitObjectCreationExpression(node);
        }

        private void InType(MemberDeclarationSyntax declaration, Action visitChildren)
        {
            int owner = model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol symbol ? types.IndexOf(symbol) : -1;
            _owners.Push(owner);

            if (owner >= 0 && declaration is BaseTypeDeclarationSyntax { BaseList: { } baseList })
            {
                foreach (BaseTypeSyntax baseType in baseList.Types)
                {
                    Link(model.GetTypeInfo(baseType.Type, cancellationToken).Type, EdgeKind.Implements, baseType);
                }
            }

            visitChildren();
            _owners.Pop();
        }

        private void LinkName(SimpleNameSyntax node)
        {
            if (Owner < 0)
            {
                return;
            }

            SymbolInfo info = model.GetSymbolInfo(node, cancellationToken);
            ISymbol? symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);

            if (symbol is IAliasSymbol alias)
            {
                symbol = alias.Target;
            }

            switch (symbol)
            {
                case ITypeSymbol type:
                    Link(type, EdgeKind.Uses, node);
                    break;

                case IMethodSymbol method:
                    Link((method.ReducedFrom ?? method).ContainingType, EdgeKind.Calls, node);

                    // Razor markup <MyComponent /> compiles to OpenComponent<MyComponent>(): the page creates it.
                    if (method.Name == "OpenComponent")
                    {
                        foreach (ITypeSymbol argument in method.TypeArguments)
                        {
                            Link(argument, EdgeKind.Calls, node);
                        }
                    }

                    break;

                case IPropertySymbol or IEventSymbol:
                    Link(symbol.ContainingType, EdgeKind.Calls, node);
                    break;

                case IFieldSymbol field:
                    Link(field.ContainingType, EdgeKind.Uses, node);
                    break;
            }
        }

        private void LinkCreation(BaseObjectCreationExpressionSyntax node)
        {
            if (Owner >= 0 && model.GetSymbolInfo(node, cancellationToken).Symbol is IMethodSymbol constructor)
            {
                Link(constructor.ContainingType, EdgeKind.Calls, node);
            }
        }

        private void Link(ITypeSymbol? type, EdgeKind kind, SyntaxNode at)
        {
            int target = types.IndexOf(type);

            if (Owner >= 0 && target >= 0 && target != Owner)
            {
                edges.Add(Owner, target, kind, LocationOf(at, origin, root));
            }
        }
    }
}
