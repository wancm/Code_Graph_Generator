using System.Diagnostics;
using System.IO;
using CodeGraphGenerator.Core;

namespace CodeGraphGenerator.App.Services;

/// <summary>One line of the Progress list.</summary>
public sealed record ProgressLine(DateTime At, string Text, bool IsError = false);

/// <summary>What the last generation produced.</summary>
public sealed record GenerationResult(CodeGraph Graph, string OutputPath, TimeSpan Duration);

/// <summary>
/// Runs one generation at a time, off the UI thread, and keeps its progress and result so they survive a visit to
/// another page.
/// </summary>
public sealed class GenerationService
{
    private const int MaxLines = 600;

    private readonly List<ProgressLine> _lines = [];

    private CancellationTokenSource? _cancel;

    public event Action? Changed;

    public bool IsRunning { get; private set; }

    public GenerationResult? Result { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<ProgressLine> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToArray();
            }
        }
    }

    public async Task RunAsync(CodeBaseCandidate codeBase, string folder, bool restoreFirst, string outputPath)
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Result = null;
        Error = null;
        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;

        lock (_lines)
        {
            _lines.Clear();
        }

        Changed?.Invoke();
        var progress = new Progress<string>(text => Add(text));
        var clock = Stopwatch.StartNew();

        try
        {
            // MSBuild and Roslyn work is slow and synchronous in places; keep it off the UI thread.
            Result = await Task.Run(async () =>
            {
                if (restoreFirst)
                {
                    await RestoreAsync(codeBase, progress, token).ConfigureAwait(false);
                }

                CodeGraph graph = await new CodeGraphBuilder().BuildAsync(codeBase, folder, progress, token).ConfigureAwait(false);
                Add($"Writing {outputPath}");
                await HtmlGraphWriter.WriteAsync(graph, outputPath, token).ConfigureAwait(false);
                return new GenerationResult(graph, outputPath, clock.Elapsed);
            }, token);

            Add($"Done in {clock.Elapsed.TotalSeconds:N1} s");
        }
        catch (OperationCanceledException)
        {
            Error = "Cancelled. Nothing was written.";
            Add(Error, isError: true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Add(ex.Message, isError: true);
        }
        finally
        {
            IsRunning = false;
            _cancel.Dispose();
            _cancel = null;
            Changed?.Invoke();
        }
    }

    public void Cancel() => _cancel?.Cancel();

    private async Task RestoreAsync(CodeBaseCandidate codeBase, IProgress<string> progress, CancellationToken token)
    {
        IEnumerable<string> targets = codeBase.Kind == CodeBaseKind.Solution ? [codeBase.Path] : codeBase.ProjectPaths;

        foreach (string target in targets)
        {
            Add($"dotnet restore {Path.GetFileName(target)}");
            int exitCode = await DotnetRestore.RunAsync(target, progress, token).ConfigureAwait(false);

            if (exitCode != 0)
            {
                // Carry on: the graph is still useful, and the warnings will name the projects that lack types.
                Add($"dotnet restore ended with exit code {exitCode}; reading the code anyway.", isError: true);
            }
        }
    }

    private void Add(string text, bool isError = false)
    {
        lock (_lines)
        {
            _lines.Add(new ProgressLine(DateTime.Now, text, isError));

            if (_lines.Count > MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            }
        }

        Changed?.Invoke();
    }
}
