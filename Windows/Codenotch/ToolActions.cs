using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Codenotch;

internal static class ToolActions
{
    public static string Description(Provider p) => p.Kind switch
    {
        "claude" => "Claude Code subscription · local sign-in",
        "codex" => "ChatGPT account · Codex app or CLI",
        "cursor" => "Cursor editor · included plan usage",
        "glm" => "GLM Coding Plan · ZCode or OpenCode",
        _ => "Local tool connection"
    };
    public static string UsageUrl(Provider p) => p.Kind switch
    {
        "claude" => "https://claude.ai/settings/usage", "codex" => "https://chatgpt.com/codex/settings/usage",
        "cursor" => "https://cursor.com/dashboard", _ => "https://z.ai/manage-apikey/apikey-list"
    };
    public static void OpenUsage(Provider p) => OpenUrl(UsageUrl(p));
    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public static string Connect(Provider p)
    {
        if (p.Kind is "claude" or "codex")
        {
            var path = Find(p.Kind);
            if (path != null)
            {
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
                start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NoExit"); start.ArgumentList.Add("-Command");
                // Single-quoted PowerShell literals escape apostrophes, never interpolate paths as code.
                start.ArgumentList.Add("& '" + path.Replace("'", "''") + "' " + (p.Kind == "claude" ? "auth login" : "login"));
                if (p.ConfigDirectory != null) start.Environment["CLAUDE_CONFIG_DIR"] = p.ConfigDirectory;
                Process.Start(start);
                return $"Finish signing in with {p.Name} in the opened window, then click Check connection. Codenotch will also check when you return here.";
            }
            OpenUrl(p.Kind == "claude" ? "https://code.claude.com/docs/en/setup" : "https://developers.openai.com/codex/cli/");
            return $"Install {p.Name} using the page we opened, sign in there, then click Check connection.";
        }
        if (p.Kind == "cursor")
        {
            var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "cursor", "Cursor.exe");
            if (File.Exists(candidate)) Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
            else if (Find("cursor") is { } cursor) Process.Start(new ProcessStartInfo(cursor) { UseShellExecute = true });
            else OpenUrl("https://cursor.com/downloads");
            return "Sign in inside Cursor's editor, then return here and click Check connection.";
        }
        OpenUrl("https://docs.z.ai/devpack/overview");
        return "Connect your GLM Coding Plan in Claude Code, ZCode, or OpenCode using the setup guide, then click Check connection.";
    }
    private static string? Find(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p)))
            foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            {
                var path = Path.Combine(directory.Trim('"'), name + extension);
                if (File.Exists(path)) return path;
            }
        return null;
    }
}
