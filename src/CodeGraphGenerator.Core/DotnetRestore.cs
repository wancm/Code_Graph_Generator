using System.Diagnostics;

namespace CodeGraphGenerator.Core;

/// <summary>Runs <c>dotnet restore</c> so the code base's package types resolve when Roslyn reads it.</summary>
public static class DotnetRestore
{
    /// <summary>Returns dotnet's exit code; 0 means the restore worked. Every output line goes to <paramref name="progress"/>.</summary>
    public static async Task<int> RunAsync(string path, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
        };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(path);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Report(progress, e.Data);
        process.ErrorDataReceived += (_, e) => Report(progress, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return process.ExitCode;
    }

    private static void Report(IProgress<string>? progress, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            progress?.Report(line.Trim());
        }
    }
}
