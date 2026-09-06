using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Codenotch;

public sealed class Settings
{
    public string Edge { get; set; } = "Right";
    public string? Screen { get; set; }
    public bool AlwaysShow { get; set; }
    public HashSet<string> Disabled { get; set; } = [];
}

public sealed class Archive
{
    public Dictionary<string, Reading> Readings { get; set; } = [];
    public Dictionary<string, DateTimeOffset> RetryAfter { get; set; } = [];
    public Dictionary<string, int> Failures { get; set; } = [];
}

public static class State
{
    public static string DirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codenotch");
    public static T Load<T>(string name) where T : new()
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DirectoryPath, name))) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        var target = Path.Combine(DirectoryPath, name);
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, target, true);
    }
}
