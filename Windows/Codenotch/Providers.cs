using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Codenotch;

public record Provider(string Id, string Name, string Color, Func<CancellationToken, Task<Reading>> Fetch);

public sealed class Providers : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public List<Provider> All { get; } = [];

    public Providers()
    {
        var claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude");
        All.Add(new("claude", "Claude", "#D99B7C", ct => Claude("claude", "Claude", claudeHome, ct)));
        foreach (var path in Directory.EnumerateDirectories(home, ".claude-*").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(claudeHome), StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(Path.Combine(path, ".credentials.json"))) continue;
            var slug = Path.GetFileName(path)[8..];
            var id = "claude-" + slug;
            var name = $"Claude ({slug})";
            All.Add(new(id, name, "#D99B7C", ct => Claude(id, name, path, ct)));
        }
        All.Add(new("codex", "Codex", "#84DCC6", Codex));
        All.Add(new("cursor", "Cursor", "#C1BEF5", Cursor));
        All.Add(new("glm", "GLM", "#80B5FF", Glm));
    }

    private async Task<JsonElement> Get(string endpoint, string header, string value, CancellationToken ct, bool claude = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add(header, value);
        request.Headers.Add("Accept", "application/json");
        if (claude) request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderFailure("Sign in again in the owning tool");
        if ((int)response.StatusCode == 429)
        {
            var retry = response.Headers.RetryAfter;
            throw new ProviderFailure("Rate limited", Math.Max(60, retry?.Delta?.TotalSeconds ?? (retry?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 60));
        }
        if (!response.IsSuccessStatusCode) throw new ProviderFailure($"Usage service returned HTTP {(int)response.StatusCode}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private static Reading Result(string id, string name, string source, List<LimitWindow> windows) =>
        new(id, name, windows.Count == 0 ? "No usage windows reported" : "OK", source, DateTimeOffset.UtcNow, windows);

    private async Task<Reading> Claude(string id, string name, string directory, CancellationToken ct)
    {
        var path = Path.Combine(directory, ".credentials.json");
        if (!File.Exists(path)) throw new ProviderFailure("Run Claude Code and sign in first");
        var oauth = Json.Read(path).At("claudeAiOauth");
        var token = oauth.At("accessToken").Text();
        if (string.IsNullOrWhiteSpace(token)) throw new ProviderFailure("No Claude OAuth session found");
        if (Json.Epoch(oauth.At("expiresAt").Number() / 1000) is { } expiry && expiry <= DateTimeOffset.UtcNow)
            throw new ProviderFailure("Session expired · open Claude Code to refresh");
        var root = await Get("https://api.anthropic.com/api/oauth/usage", "Authorization", "Bearer " + token, ct, true);
        return Result(id, name, "Claude OAuth usage", Usage.Claude(root));
    }

    private async Task<Reading> Cursor(CancellationToken ct)
    {
        var db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb");
        if (!File.Exists(db)) throw new ProviderFailure("Open Cursor and sign in first");
        var token = Sqlite.Query(db, "SELECT value FROM ItemTable WHERE key = 'cursorAuth/accessToken'").FirstOrDefault();
        var account = Sqlite.Query(db, "SELECT value FROM ItemTable WHERE key = 'cursorAuth/stripeMembershipAuthId'").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(account)) throw new ProviderFailure("No readable Cursor session found");
        var root = await Get("https://cursor.com/api/usage-summary", "Cookie", $"WorkosCursorSessionToken={account}::{token}", ct);
        return Result("cursor", "Cursor", "Cursor usage summary", Usage.Cursor(root));
    }

    private async Task<Reading> Glm(CancellationToken ct)
    {
        var credential = GlmCredential();
        if (credential == null) throw new ProviderFailure("No GLM key found in Claude Code, ZCode, or OpenCode");
        var (token, host) = credential.Value;
        var root = await Get(host + "/api/monitor/usage/quota/limit", "Authorization", token, ct);
        return Result("glm", "GLM", "GLM Coding Plan monitor", Usage.Glm(root));
    }

    internal static string? GlmHost(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Host == "z.ai" || uri.Host.EndsWith(".z.ai", StringComparison.OrdinalIgnoreCase)) return "https://api.z.ai";
        if (uri.Host == "bigmodel.cn" || uri.Host.EndsWith(".bigmodel.cn", StringComparison.OrdinalIgnoreCase)) return "https://open.bigmodel.cn";
        return null;
    }

    private (string Token, string Host)? GlmCredential()
    {
        var settings = Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude"), "settings.json");
        if (File.Exists(settings))
        {
            var env = Json.Read(settings).At("env");
            var token = env.At("ANTHROPIC_AUTH_TOKEN").Text() ?? env.At("ANTHROPIC_API_KEY").Text();
            var host = GlmHost(env.At("ANTHROPIC_BASE_URL").Text());
            if (!string.IsNullOrWhiteSpace(token) && host != null) return (token, host);
        }
        var config = Path.Combine(home, ".zcode", "v2", "config.json");
        if (File.Exists(config))
        {
            var providers = Json.Read(config).At("provider");
            if (providers.ValueKind == JsonValueKind.Object)
                foreach (var entry in providers.EnumerateObject().OrderBy(p => p.Name))
                {
                    if (!entry.Name.Contains("coding-plan") || entry.Value.At("enabled").ValueKind == JsonValueKind.False) continue;
                    var options = entry.Value.At("options");
                    var key = options.At("apiKey").Text();
                    var host = GlmHost(options.At("baseURL").Text());
                    if (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("enc:") && host != null) return (key, host);
                }
        }
        var credentials = Path.Combine(home, ".zcode", "v2", "credentials.json");
        if (File.Exists(credentials) && Json.Read(credentials).At("oauth:zai:access_token").Text() is { Length: > 0 } zcode && !zcode.StartsWith("enc:"))
            return (zcode, "https://api.z.ai");
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(home, ".local", "share");
        var auth = Path.Combine(dataHome, "opencode", "auth.json");
        if (File.Exists(auth))
        {
            var root = Json.Read(auth);
            foreach (var id in new[] { "zai-coding-plan", "zai", "z-ai", "z.ai", "zhipu", "zhipuai" })
            {
                var entry = root.At(id);
                var token = entry.Text() ?? new[] { "apiKey", "api_key", "token", "key", "accessToken", "auth_token" }.Select(k => entry.At(k).Text()).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                if (!string.IsNullOrWhiteSpace(token) && !token.StartsWith("enc:")) return (token, id.StartsWith("zhipu") ? "https://open.bigmodel.cn" : "https://api.z.ai");
            }
        }
        return null;
    }

    private async Task<Reading> Codex(CancellationToken ct)
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(home, ".codex");
        var executable = FindCodex(codexHome);
        if (executable != null)
        {
            try
            {
                var live = await CodexLive(executable, ct);
                if (live != null) return live;
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or System.ComponentModel.Win32Exception or JsonException) { ct.ThrowIfCancellationRequested(); }
        }
        var paths = new List<string>();
        foreach (var db in Directory.Exists(codexHome) ? Directory.GetFiles(codexHome, "state_*.sqlite").OrderDescending() : Enumerable.Empty<string>())
        {
            try { paths.AddRange(Sqlite.Query(db, "SELECT rollout_path FROM threads WHERE archived = 0 ORDER BY updated_at_ms DESC LIMIT 12")); }
            catch (ProviderFailure) { }
        }
        var sessions = Path.Combine(codexHome, "sessions");
        if (paths.Count == 0 && Directory.Exists(sessions))
            paths.AddRange(new DirectoryInfo(sessions).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).Take(12).Select(f => f.FullName));
        Reading? newest = null;
        foreach (var path in paths.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(Math.Max(0, stream.Length - 256 * 1024), SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var reading = Usage.Rollout(await reader.ReadToEndAsync(ct), DateTimeOffset.UtcNow);
                if (reading != null && (newest == null || reading.RecordedAt > newest.RecordedAt)) newest = reading;
            }
            catch (IOException) { }
        }
        return newest ?? throw new ProviderFailure("Use Codex once to record usage on this PC");
    }

    private static string? FindCodex(string codexHome)
    {
        var candidates = new List<string>();
        var configured = Environment.GetEnvironmentVariable("CODENOTCH_CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.Add(Path.Combine(codexHome, "bin", "codex.exe"));
        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var dir = path.Trim('"');
            candidates.Add(Path.Combine(dir, "codex.exe"));
            var modules = Path.Combine(dir, "node_modules", "@openai");
            if (Directory.Exists(modules))
                candidates.AddRange(Directory.EnumerateFiles(modules, "codex.exe", SearchOption.AllDirectories));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<Reading?> CodexLive(string executable, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        using var process = new Process { StartInfo = new(executable, "app-server") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        process.Start();
        process.ErrorDataReceived += (_, _) => { }; // Drain stderr without recording credentials or session content.
        process.BeginErrorReadLine();
        try
        {
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codenotch_windows\",\"version\":\"0.1.0\"}}}".AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
            var initialized = false;
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.At("id").Number() == 1 && !initialized)
                {
                    if (root.At("error").ValueKind == JsonValueKind.Object) return null;
                    initialized = true;
                    await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), token);
                    await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":null}".AsMemory(), token);
                    await process.StandardInput.FlushAsync(token);
                }
                if (root.At("id").Number() != 2) continue;
                var limits = root.At("result").At("rateLimits");
                var windows = Usage.Codex(limits, true, DateTimeOffset.UtcNow);
                if (windows.Count == 0) return null;
                var result = Result("codex", "Codex", "Codex app server · live", windows);
                return limits.At("rateLimitReachedType").Text() is { } blocked ? result with { Status = "Paused · " + blocked.Replace('_', ' ') } : result;
            }
            return null;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    public void Dispose() => http.Dispose();
}

// Windows supplies SQLite. READONLY preserves the owning editor's WAL and never creates a database.
internal static class Sqlite
{
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);

    public static List<string> Query(string path, string sql)
    {
        var code = sqlite3_open_v2(path, out var db, 1, IntPtr.Zero);
        try
        {
            if (code != 0) throw new ProviderFailure("Local database is unavailable");
            sqlite3_busy_timeout(db, 1000);
            if (sqlite3_prepare_v2(db, sql, -1, out var statement, IntPtr.Zero) != 0) throw new ProviderFailure("Local database format is not supported");
            try
            {
                var rows = new List<string>();
                int step;
                while ((step = sqlite3_step(statement)) == 100) rows.Add(Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0)) ?? "");
                if (step != 101) throw new ProviderFailure("Local database is busy; retry later");
                return rows;
            }
            finally { sqlite3_finalize(statement); }
        }
        finally { if (db != IntPtr.Zero) sqlite3_close(db); }
    }
}
