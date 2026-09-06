using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Codenotch;

public enum ConnectionState { Checking, Connected, Stale, NeedsSignIn, RateLimited, Error, Disabled, Unsupported }
public record Connection(Provider Provider, ConnectionState State, Reading? Reading, string Message, DateTimeOffset? RetryAt = null)
{
    public bool HasReading => Reading?.Windows.Count > 0;
    public bool ShowInNotch => State != ConnectionState.Disabled && (HasReading || State == ConnectionState.RateLimited);
    public string StatusLabel => State switch
    {
        ConnectionState.Checking => "Checking…", ConnectionState.Connected => "Connected", ConnectionState.Stale => "Last reading",
        ConnectionState.NeedsSignIn => "Not connected", ConnectionState.RateLimited => "Rate limited", ConnectionState.Disabled => "Off",
        ConnectionState.Unsupported => "Unavailable", _ => "Connection issue"
    };
    public string PercentLabel => HasReading ? $"{Reading!.Windows[0].Percent:0}%" : "—";
}

// All state changes resume on the UI synchronization context. Only provider I/O runs on workers.
public sealed class UsageService : IDisposable
{
    private readonly Providers? adapters;
    private readonly Archive archive;
    private readonly Dictionary<string, CancellationTokenSource> pending = [];
    private readonly Dictionary<string, int> generations = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly bool persist;
    public Settings Settings { get; }
    public IReadOnlyList<Provider> Providers { get; }
    public Dictionary<string, Connection> Connections { get; } = [];
    public bool IsDemo { get; }
    public string? SaveError { get; private set; }
    public event Action? Changed;

    public UsageService(Settings settings, IReadOnlyList<Provider>? providers = null, Archive? archive = null, bool persist = true, bool demo = false)
    {
        Settings = settings;
        Settings.Disabled ??= [];
        this.persist = persist && !demo;
        IsDemo = demo;
        this.archive = archive ?? (this.persist ? State.Load<Archive>("usage.json") : new Archive());
        this.archive.Readings ??= []; this.archive.RetryAfter ??= []; this.archive.Failures ??= [];
        if (providers == null) { adapters = new Codenotch.Providers(); Providers = adapters.All; }
        else Providers = providers;
        foreach (var provider in Providers)
        {
            this.archive.Readings.TryGetValue(provider.Id, out var saved);
            // Old demo or malformed cache entries can never pass as account usage.
            if (saved?.Source == "Demo" || saved?.Windows == null) saved = null;
            var disabled = Settings.Disabled.Contains(provider.Id);
            Connections[provider.Id] = new(provider, disabled ? ConnectionState.Disabled : saved == null ? ConnectionState.Checking : ConnectionState.Stale,
                disabled ? null : saved, disabled ? "Disabled. Your tool remains signed in." : saved == null ? "Looking for an existing session on this PC…" : "Saved reading. Checking the connection…");
        }
    }

    public Task RefreshAll() => Task.WhenAll(Providers.Select(p => Refresh(p.Id)));
    public async Task Refresh(string id)
    {
        if (lifetime.IsCancellationRequested || Settings.Disabled.Contains(id) || pending.ContainsKey(id)) return;
        var provider = Providers.First(p => p.Id == id);
        if (archive.RetryAfter.TryGetValue(id, out var deadline) && deadline > DateTimeOffset.UtcNow)
        {
            Set(id, ConnectionState.RateLimited, $"The service asked us to wait. Retry available at {deadline.ToLocalTime():HH:mm:ss}. Your login does not need changing.", deadline);
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        pending[id] = cancellation;
        var generation = generations.GetValueOrDefault(id);
        Set(id, ConnectionState.Checking, "Checking your saved session…");
        try
        {
            var result = await Task.Run(() => provider.Fetch(cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || generations.GetValueOrDefault(id) != generation) return;
            var stale = result.Status.StartsWith("Stale", StringComparison.OrdinalIgnoreCase);
            Connections[id] = new(provider, stale ? ConnectionState.Stale : ConnectionState.Connected, result,
                IsDemo ? "Preview only · sample data" : stale ? "Codex's last recorded usage. A live connection is not available." : result.Status == "OK" ? "Usage verified with your signed-in tool." : result.Status);
            if (!IsDemo) archive.Readings[id] = result;
            archive.RetryAfter.Remove(id); archive.Failures.Remove(id);
        }
        catch (Exception error)
        {
            if (cancellation.IsCancellationRequested || generations.GetValueOrDefault(id) != generation) return;
            var failure = error as ProviderFailure;
            var state = failure?.Kind switch
            {
                FailureKind.NeedsSignIn or FailureKind.NotInstalled => ConnectionState.NeedsSignIn,
                FailureKind.RateLimited => ConnectionState.RateLimited,
                FailureKind.Unsupported => ConnectionState.Unsupported,
                _ => ConnectionState.Error
            };
            var message = error switch
            {
                ProviderFailure => error.Message,
                UnauthorizedAccessException => "Windows denied access to the tool's saved session.",
                JsonException => "The saved session or usage response has a format this version does not recognize.",
                OperationCanceledException => "The usage request timed out. Check your connection and try again.",
                HttpRequestException => "Could not reach the usage service. Check your internet connection and retry.",
                _ => "Could not read usage from the tool. Check the connection and retry."
            };
            DateTimeOffset? retryAt = null;
            if (failure?.RetrySeconds is { } seconds)
            {
                var attempts = archive.Failures.GetValueOrDefault(id);
                archive.Failures[id] = Math.Min(attempts + 1, 5);
                retryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(Math.Min(900, 60 * Math.Pow(2, attempts)), Math.Clamp(seconds, 60, 86400)));
                archive.RetryAfter[id] = retryAt.Value;
                message = $"The service asked us to wait. Retry available at {retryAt.Value.ToLocalTime():HH:mm:ss}. Your login does not need changing.";
            }
            // Authentication changes must not show the previous account's numbers.
            if (state == ConnectionState.NeedsSignIn) { archive.Readings.Remove(id); Connections[id] = Connections[id] with { Reading = null }; }
            Set(id, state, message, retryAt);
        }
        finally
        {
            if (pending.TryGetValue(id, out var active) && ReferenceEquals(active, cancellation)) pending.Remove(id);
            if (!lifetime.IsCancellationRequested) { Save(); Changed?.Invoke(); }
        }
    }

    private void Set(string id, ConnectionState state, string message, DateTimeOffset? retryAt = null)
    {
        Connections[id] = Connections[id] with { State = state, Message = message, RetryAt = retryAt };
        Changed?.Invoke();
    }

    public void Enable(string id, bool enabled)
    {
        generations[id] = generations.GetValueOrDefault(id) + 1;
        if (pending.Remove(id, out var request)) request.Cancel();
        if (enabled) Settings.Disabled.Remove(id);
        else Settings.Disabled.Add(id);
        archive.Readings.Remove(id);
        Connections[id] = Connections[id] with { Reading = null, State = enabled ? ConnectionState.NeedsSignIn : ConnectionState.Disabled, Message = enabled ? "Ready to check your existing session." : "Disabled. Your tool remains signed in." };
        Save(); Changed?.Invoke();
    }

    public void Save()
    {
        if (!persist) return;
        try { State.Save("settings.json", Settings); State.Save("usage.json", archive); SaveError = null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SaveError = "Could not save settings in LocalAppData. Changes will last for this session only."; }
    }

    public static UsageService Preview()
    {
        var providers = new[] { ("claude", "Claude Code", 73d), ("codex", "Codex", 21d), ("cursor", "Cursor", 52d) }.Select(p =>
            new Provider(p.Item1, p.Item2, "", _ => Task.FromResult(new Reading(p.Item1, p.Item2, "Demo", "Demo", DateTimeOffset.UtcNow,
                [new("session", "Current session", p.Item3, DateTimeOffset.UtcNow.AddMinutes(51)), new("weekly", "All models", 7, DateTimeOffset.UtcNow.AddDays(3))])))).ToList();
        return new(new Settings { AlwaysShow = true }, providers, persist: false, demo: true);
    }

    public void Dispose() { lifetime.Cancel(); adapters?.Dispose(); lifetime.Dispose(); }
}
