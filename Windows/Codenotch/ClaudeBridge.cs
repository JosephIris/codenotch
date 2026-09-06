using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codenotch;

public static class ClaudeBridge
{
    public static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsage", "streamdeck.json");
    public static bool Installed(string directory) =>
        string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), StringComparison.OrdinalIgnoreCase)
        && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.anthropic.claude-usage.sdPlugin", "actions", "codenotch-bridge.json"));

    public static Reading Read(string id, string name, string token)
    {
        if (!File.Exists(CachePath)) throw new ProviderFailure("Waiting for Stream Deck's Claude collector. Open your Claude usage buttons in Stream Deck.");
        return Parse(Json.Read(CachePath), id, name, token, DateTimeOffset.UtcNow);
    }

    public static Reading Parse(JsonElement root, string id, string name, string token, DateTimeOffset now)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        if (root.At("version").Number() != 1) throw new ProviderFailure("Stream Deck usage cache has an unsupported format.");
        if (root.At("credentialHash").Text() != fingerprint || root.At("error").Text() == "auth")
            throw new ProviderFailure("Waiting for Stream Deck to verify your current Claude login.", kind: FailureKind.NeedsSignIn);
        var stamp = root.At("fetchedAt").Date();
        var windows = Usage.Claude(root.At("data"));
        if (stamp == null || stamp > now.AddMinutes(1) || windows.Count == 0)
            throw new ProviderFailure("Waiting for a valid usage reading from Stream Deck. Codenotch is not making extra Claude requests.");
        var stale = now - stamp > TimeSpan.FromMinutes(6) || root.At("error").Text() != null;
        return new(id, name, stale ? "Stale · Stream Deck's last reading; waiting for its collector to refresh." : "OK", "Stream Deck · shared Claude usage", stamp, windows);
    }
}
