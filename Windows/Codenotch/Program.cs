using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Codenotch;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Preview is explicit, isolated from live state, and visibly labeled on every surface.
        var preview = args.Contains("--demo") || args.Contains("--smoke-test");
        using var mutex = new Mutex(true, preview ? "Local\\Codenotch.Windows.Preview" : "Local\\Codenotch.Windows", out var first);
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, preview ? "Local\\Codenotch.Windows.Preview.Show" : "Local\\Codenotch.Windows.Show");
        if (!first) { activation.Set(); return; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Only exception type/stack: provider payloads and secrets must never enter crash reports.
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codenotch");
            try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "crash.txt"), e.Exception.GetType().FullName + "\n" + e.Exception.StackTrace); } catch (IOException) { }
        };
        var service = preview ? UsageService.Preview() : new UsageService(State.Load<Settings>("settings.json"));
        var notch = new Notch(service, args.Contains("--smoke-test"), args.Contains("--settings"), args.Contains("--verify-live"));
        var registration = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => app.Dispatcher.BeginInvoke(notch.OpenSettings), null, -1, false);
        try { app.Run(notch); }
        finally { registration.Unregister(null); service.Dispose(); }
    }
}
