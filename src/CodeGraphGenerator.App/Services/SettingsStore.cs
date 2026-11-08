using System.IO;
using System.Text.Json;

namespace CodeGraphGenerator.App.Services;

/// <summary>What the window remembers between starts.</summary>
public sealed record AppSettings
{
    public string? LastFolder { get; init; }

    public string? OutputFolder { get; init; }

    public bool RestoreFirst { get; init; } = true;
}

/// <summary>Keeps <see cref="AppSettings"/> in %LOCALAPPDATA%\CodeGraphGenerator\settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeGraphGenerator", "settings.json");

    public AppSettings Current { get; private set; } = Load();

    public static string DefaultOutputFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Code graphs");

    public void Save(AppSettings settings)
    {
        Current = settings;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch (IOException)
        {
            // Not remembering the last folder is no reason to stop.
        }
    }

    private static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new AppSettings();
        }
    }
}
