using Codenotch;
using System.Text.Json;

var passed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { Console.Error.WriteLine($"FAIL {name}: {e.Message}"); Environment.ExitCode = 1; }
}
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
JsonElement Parse(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
var now = DateTimeOffset.Parse("2026-09-06T12:00:00Z");

Test("Claude merges session after rollover without duplicating weekly limits", () =>
{
    var windows = Usage.Claude(Parse("""{"limits":[{"kind":"weekly_all","percent":44,"resets_at":"2026-09-10T12:00:00Z"}],"five_hour":{"utilization":0,"resets_at":"2026-09-06T17:00:00Z"},"seven_day":{"utilization":99}}"""));
    Check(windows.Count == 2 && windows[0].Id == "session" && windows[0].Percent == 0 && windows[1].Percent == 44);
});
Test("Unknown usage never becomes zero", () => Check(Usage.Claude(Parse("{}" )).Count == 0 && Usage.Cursor(Parse("{}" )).Count == 0));
Test("Cursor uses published percentage even with zero dollar limit", () =>
{
    var windows = Usage.Cursor(Parse("""{"individualUsage":{"plan":{"used":0,"limit":0,"totalPercentUsed":9.5,"apiPercentUsed":19},"onDemand":{"enabled":true,"used":5,"limit":20}}}"""));
    Check(windows.Count == 3 && windows[0].Percent == 9.5 && windows[2].Percent == 25);
});
Test("Cursor zero usage remains a reading", () => Check(Usage.Cursor(Parse("""{"individualUsage":{"plan":{"totalPercentUsed":0}}}"""))[0].Percent == 0));
Test("Codex rollout uses newest valid snapshot despite partial writes", () =>
{
    var text = """
    {"timestamp":"2026-09-06T11:58:00Z","rate_limits":{"primary":{"used_percent":10}}}
    {"timestamp":"2026-09-06T11:59:00Z","payload":{"rate_limits":{"primary":{"used_percent":28,"window_minutes":300,"resets_in_seconds":3600}}}}
    {"rate_limits":
    """;
    var reading = Usage.Rollout(text, now)!;
    Check(reading.Status == "OK" && reading.Windows[0].Percent == 28 && reading.Windows[0].ResetsAt == now.AddMinutes(59));
});
Test("Old or undated Codex logs are stale", () =>
{
    Check(Usage.Rollout("""{"timestamp":"2026-09-05T11:00:00Z","rate_limits":{"primary":{"used_percent":22}}}""", now)!.Status.StartsWith("Stale"));
    Check(Usage.Rollout("""{"rate_limits":{"primary":{"used_percent":0}}}""", now)!.Status.StartsWith("Stale"));
});
Test("Codex live camelCase response and absolute reset", () =>
{
    var windows = Usage.Codex(Parse("""{"primary":{"usedPercent":12,"windowDurationMins":300,"resetsAt":1788699600},"secondary":null}"""), true, now);
    Check(windows.Count == 1 && windows[0].Percent == 12 && windows[0].ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1788699600));
});
Test("GLM credit plans and millisecond reset", () =>
{
    var windows = Usage.Glm(Parse("""{"code":200,"success":true,"data":{"limits":[{"type":"TIME_LIMIT","percentage":5},{"type":"CREDIT_LIMIT","unit":3,"number":5,"percentage":12.5,"nextResetTime":1788699600000}]}}"""));
    Check(windows.Count == 2 && windows[0].Id == "session" && windows[0].ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1788699600) && windows[1].ResetsAt == null);
});
Test("GLM HTTP-200 error envelopes are rejected", () =>
{
    foreach (var body in new[] { "{\"code\":401,\"success\":false}", "{\"success\":false}", "{\"code\":429}" })
    {
        var threw = false;
        try { Usage.Glm(Parse(body)); } catch (ProviderFailure) { threw = true; }
        Check(threw);
    }
});
Test("GLM credentials cannot be sent to unrelated hosts", () =>
{
    Check(Providers.GlmHost("https://api.anthropic.com") == null);
    Check(Providers.GlmHost("https://api.z.ai.evil.example") == null);
    Check(Providers.GlmHost("https://open.bigmodel.cn/api/anthropic") == "https://open.bigmodel.cn");
});
Test("Invalid usage and timestamps do not produce false readings or crash", () =>
{
    Check(Usage.Codex(Parse("""{"primary":{"usedPercent":-10}}"""), true, now).Count == 0);
    Check(Json.Epoch(1e100) == null);
    Check(Usage.Rollout("garbage\n{\"rate_limits\":null}", now) == null);
});
Test("Settings and backoff survive an atomic save; corrupt files recover", () =>
{
    var directory = Path.Combine(Path.GetTempPath(), "codenotch-tests-" + Guid.NewGuid());
    State.DirectoryPath = directory;
    try
    {
        State.Save("settings.json", new Settings { Edge = "Bottom", Disabled = ["cursor"] });
        Check(State.Load<Settings>("settings.json").Disabled.Contains("cursor"));
        State.Save("usage.json", new Archive { RetryAfter = new() { ["claude"] = now } });
        Check(State.Load<Archive>("usage.json").RetryAfter["claude"] == now);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "broken");
        Check(State.Load<Settings>("settings.json").Edge == "Right");
    }
    finally { Directory.Delete(directory, true); }
});
Console.WriteLine($"{passed} tests passed.");
