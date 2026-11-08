using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CodeGraphGenerator.App.Services;

/// <summary>Windows' own dialogs and shell actions, which a browser page can't do: they give real file paths.</summary>
public sealed class DesktopDialogs(Window owner)
{
    public Task<string?> PickFolderAsync(string? startFolder) => owner.Dispatcher.InvokeAsync(() =>
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder that holds the code base" };

        if (startFolder is not null && Directory.Exists(startFolder))
        {
            dialog.InitialDirectory = startFolder;
        }

        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }).Task;

    public Task<string?> PickOutputFileAsync(string current) => owner.Dispatcher.InvokeAsync(() =>
    {
        var dialog = new SaveFileDialog
        {
            Title = "Where to write the code graph",
            Filter = "HTML file (*.html)|*.html",
            DefaultExt = ".html",
            FileName = Path.GetFileName(current),
            OverwritePrompt = false
        };

        if (Path.GetDirectoryName(current) is { } folder && Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }).Task;

    /// <summary>Opens the file in its default app: the browser, for the code graph.</summary>
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void ShowInFolder(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");
}
