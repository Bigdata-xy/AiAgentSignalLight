using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SignalLight.Agent.Tests;

public sealed class HookInstallScriptTests
{
    [Fact]
    public void Codex_hook_invokes_agent_and_writes_snapshot()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var script = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");
        var payload = """
        {
          "session_id": "codex-session-1",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "Continue Phase 1"
        }
        """;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -EventName UserPromptSubmit",
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.Environment["SIGNAL_LIGHT_ROOT"] = signalRoot;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        Assert.True(process.WaitForExit(15000), "codex-hook.ps1 timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);

        var snapshotPath = Path.Combine(signalRoot, "snapshot.json");
        Assert.True(File.Exists(snapshotPath), "Expected hook execution to write snapshot.json.");

        using var snapshot = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        Assert.Equal("thinking", snapshot.RootElement.GetProperty("aggregateState").GetString());
        Assert.Equal(1, snapshot.RootElement.GetProperty("totalCount").GetInt32());

        var diagnosticsPath = Path.Combine(signalRoot, "diagnostics", "latest-hook-context.json");
        Assert.True(File.Exists(diagnosticsPath), "Expected hook execution to write latest-hook-context.json.");

        using var diagnostics = JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
        Assert.Equal("UserPromptSubmit", diagnostics.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("running", diagnostics.RootElement.GetProperty("mappedState").GetString());
        Assert.True(diagnostics.RootElement.GetProperty("agentFound").GetBoolean());
        Assert.Equal("codex-session-1", diagnostics.RootElement.GetProperty("session").GetString());
        Assert.Contains("Continue Phase 1", diagnostics.RootElement.GetProperty("rawPayload").GetString());
    }

    [Fact]
    public void Install_hooks_is_idempotent_and_preserves_existing_hooks()
    {
        var repoRoot = FindRepoRoot();
        var codexHome = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(codexHome);

        var hooksJson = Path.Combine(codexHome, "hooks.json");
        File.WriteAllText(hooksJson, """
        {
          "hooks": {
            "UserPromptSubmit": [
              {
                "hooks": [
                  {
                    "type": "command",
                    "command": "echo user-owned",
                    "statusMessage": "User hook"
                  }
                ]
              }
            ]
          }
        }
        """);

        RunInstallHooks(repoRoot, codexHome);
        RunInstallHooks(repoRoot, codexHome);

        using var document = JsonDocument.Parse(File.ReadAllText(hooksJson));
        var groups = document.RootElement
            .GetProperty("hooks")
            .GetProperty("UserPromptSubmit")
            .EnumerateArray()
            .ToArray();

        Assert.Contains(groups, group => group.GetProperty("hooks")[0].GetProperty("command").GetString() == "echo user-owned");

        var signalLightHooks = groups
            .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
            .Where(hook => hook.GetProperty("statusMessage").GetString()?.StartsWith("SignalLight:", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Single(signalLightHooks);
        Assert.Contains("codex-hook.ps1", signalLightHooks[0].GetProperty("command").GetString());
        Assert.Contains("-EventName \"UserPromptSubmit\"", signalLightHooks[0].GetProperty("command").GetString());
    }

    private static void RunInstallHooks(string repoRoot, string codexHome)
    {
        var script = Path.Combine(repoRoot, "tools", "install-hooks.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.Environment["CODEX_HOME"] = codexHome;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        Assert.True(process.WaitForExit(15000), "install-hooks.ps1 timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SignalLight.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
