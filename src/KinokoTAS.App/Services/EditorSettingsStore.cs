using System;
using System.IO;
using System.Text.Json;

namespace KinokoTAS.App.Services;

internal sealed class EditorSettingsStore
{
    public string Path { get; } = ResolvePath();

    public EditorSettings? Read()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize(File.ReadAllText(Path), SettingsJsonContext.Default.EditorSettings)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public void Write(EditorSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.EditorSettings));
    }

    private static string ResolvePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = System.IO.Path.Combine(root, "6kinokoTAS");
        var legacy = System.IO.Path.Combine(root, "KinokoTAS");
        if (!Directory.Exists(current) && Directory.Exists(legacy))
        {
            Directory.CreateDirectory(current);
            foreach (var file in Directory.GetFiles(legacy, "*", SearchOption.AllDirectories))
            {
                var destination = System.IO.Path.Combine(current, System.IO.Path.GetRelativePath(legacy, file));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, false);
            }
        }

        return System.IO.Path.Combine(current, "settings.json");
    }
}
