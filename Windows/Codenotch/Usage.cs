using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Codenotch;

public record LimitWindow(string Id, string Label, double Percent, DateTimeOffset? ResetsAt);
public record Reading(string Id, string Name, string Status, string Source, DateTimeOffset? RecordedAt, List<LimitWindow> Windows);
public sealed class ProviderFailure(string message, double? retrySeconds = null) : Exception(message)
{
    public double? RetrySeconds { get; } = retrySeconds;
}

public static class Json
{
    public static JsonElement At(this JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var child) ? child : default;
    public static string? Text(this JsonElement node) => node.ValueKind == JsonValueKind.String ? node.GetString() : null;
    public static double? Number(this JsonElement node) => node.ValueKind == JsonValueKind.Number && node.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    public static IEnumerable<JsonElement> Items(this JsonElement node) => node.ValueKind == JsonValueKind.Array ? node.EnumerateArray() : [];
    public static DateTimeOffset? Date(this JsonElement node) => DateTimeOffset.TryParse(node.Text(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    public static DateTimeOffset? Epoch(double? seconds)
    {
        if (seconds is not { } n || n < -62135596800 || n > 253402300799) return null;
        return DateTimeOffset.FromUnixTimeSeconds((long)n);
    }
    public static JsonElement Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}

public static class Usage
{
    private static void Add(List<LimitWindow> windows, string id, string label, double? percent, DateTimeOffset? reset)
    {
        if (percent is { } n && double.IsFinite(n) && n >= 0) windows.Add(new(id, label, n, reset));
    }

    public static List<LimitWindow> Claude(JsonElement root)
    {
        var windows = new List<LimitWindow>();
        foreach (var limit in root.At("limits").Items())
        {
            var id = limit.At("kind").Text();
            if (id != null) Add(windows, id, id.Replace('_', ' '), limit.At("percent").Number(), limit.At("resets_at").Date());
        }
        foreach (var (key, id, label) in new[] { ("five_hour", "session", "Current session"), ("seven_day", "weekly_all", "All models") })
            if (!windows.Any(w => w.Id == id)) Add(windows, id, label, root.At(key).At("utilization").Number(), root.At(key).At("resets_at").Date());
        return windows.OrderBy(w => w.Id == "session" ? 0 : w.Id == "weekly_all" ? 1 : 2).ToList();
    }

    public static List<LimitWindow> Codex(JsonElement limits, bool live, DateTimeOffset recordedAt)
    {
        var windows = new List<LimitWindow>();
        foreach (var id in new[] { "primary", "secondary" })
        {
            var bucket = limits.At(id);
            var minutes = bucket.At(live ? "windowDurationMins" : "window_minutes").Number();
            var label = minutes switch { null => id, < 60 => $"{minutes:0}m limit", < 1440 => $"{minutes / 60:0}h limit", _ => $"{minutes / 1440:0}d limit" };
            var reset = Json.Epoch(bucket.At(live ? "resetsAt" : "resets_at").Number());
            if (reset == null && bucket.At("resets_in_seconds").Number() is { } seconds && seconds >= 0 && seconds < 315360000)
                reset = recordedAt.AddSeconds(seconds);
            Add(windows, id, label, bucket.At(live ? "usedPercent" : "used_percent").Number(), reset);
        }
        return windows;
    }

    public static Reading? Rollout(string text, DateTimeOffset now)
    {
        foreach (var line in text.Split('\n').Reverse())
        {
            if (!line.Contains("rate_limits", StringComparison.Ordinal)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var limits = root.At("rate_limits");
                if (limits.ValueKind != JsonValueKind.Object) limits = root.At("payload").At("rate_limits");
                var stamp = root.At("timestamp").Date();
                var windows = Codex(limits, false, stamp ?? now);
                if (windows.Count == 0) continue;
                var fresh = stamp != null && now - stamp <= TimeSpan.FromMinutes(5) && stamp <= now.AddMinutes(1);
                return new("codex", "Codex", fresh ? "OK" : "Stale · use Codex to update", "Codex rollout · recorded usage", stamp, windows);
            }
            catch (JsonException) { /* An actively written final line can be incomplete. */ }
        }
        return null;
    }

    public static List<LimitWindow> Cursor(JsonElement root)
    {
        var windows = new List<LimitWindow>();
        var usage = root.At("individualUsage");
        var reset = root.At("billingCycleEnd").Date();
        Add(windows, "included", "Included usage", usage.At("plan").At("totalPercentUsed").Number(), reset);
        var api = usage.At("plan").At("apiPercentUsed").Number();
        if (api > 0) Add(windows, "api", "API usage", api, reset);
        var demand = usage.At("onDemand");
        if (demand.At("enabled").ValueKind == JsonValueKind.True && demand.At("limit").Number() is > 0 and var ceiling)
            Add(windows, "on_demand", "On demand", demand.At("used").Number() / ceiling * 100, reset);
        return windows;
    }

    public static List<LimitWindow> Glm(JsonElement root)
    {
        var code = root.At("code").Number();
        if (code is 401 or 403) throw new ProviderFailure("Sign in to your GLM coding tool");
        if (code == 429) throw new ProviderFailure("Rate limited", 60);
        if (root.At("success").ValueKind == JsonValueKind.False || (code != null && code != 200)) throw new ProviderFailure("GLM rejected the usage request");
        var windows = new List<LimitWindow>();
        foreach (var limit in root.At("data").At("limits").Items())
        {
            var id = limit.At("type").Text() == "TIME_LIMIT" ? "mcp" : (limit.At("unit").Number(), limit.At("number").Number()) switch
            { (3, 5) => "session", (6, 1) => "weekly", _ => "usage" };
            Add(windows, id, id switch { "session" => "Current session", "weekly" => "Weekly", "mcp" => "MCP (1 month)", _ => "Usage" }, limit.At("percentage").Number(), Json.Epoch(limit.At("nextResetTime").Number() / 1000));
        }
        return windows.OrderBy(w => w.Id == "session" ? 0 : w.Id == "weekly" ? 1 : 2).ToList();
    }
}
