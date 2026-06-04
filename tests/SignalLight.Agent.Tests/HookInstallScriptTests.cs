using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SignalLight.Agent.Tests;

public sealed class HookInstallScriptTests
{
    [Fact]
    public void Agent_hook_reads_utf8_payload_and_writes_snapshot()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var agentDll = Path.Combine(repoRoot, "src", "SignalLight.Agent", "bin", "Debug", "net8.0", "SignalLight.Agent.dll");
        var prompt = string.Concat('\u4fee', '\u590d', '\u4e2d', '\u6587', '\u4efb', '\u52a1', '\u6807', '\u9898');
        var payload = $$"""
        {
          "session_id": "codex-session-1",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "{{prompt}}"
        }
        """;

        RunAgentHook(agentDll, signalRoot, "UserPromptSubmit", payload);

        var snapshotPath = Path.Combine(signalRoot, "snapshot.json");
        Assert.True(File.Exists(snapshotPath), "Expected hook execution to write snapshot.json.");

        using var snapshot = JsonDocument.Parse(File.ReadAllText(snapshotPath));
        Assert.Equal("thinking", snapshot.RootElement.GetProperty("aggregateState").GetString());
        Assert.Equal(1, snapshot.RootElement.GetProperty("totalCount").GetInt32());

        var sessionPath = Path.Combine(signalRoot, "sessions", "codex-session-1.json");
        using var session = JsonDocument.Parse(File.ReadAllText(sessionPath));
        Assert.Equal(prompt, session.RootElement.GetProperty("displayName").GetString());

        var diagnosticsPath = Path.Combine(signalRoot, "diagnostics", "latest-hook-context.json");
        Assert.True(File.Exists(diagnosticsPath), "Expected hook execution to write latest-hook-context.json.");

        using var diagnostics = JsonDocument.Parse(File.ReadAllText(diagnosticsPath));
        Assert.Equal("UserPromptSubmit", diagnostics.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("running", diagnostics.RootElement.GetProperty("mappedState").GetString());
        Assert.True(diagnostics.RootElement.GetProperty("agentFound").GetBoolean());
        Assert.Equal("codex-session-1", diagnostics.RootElement.GetProperty("session").GetString());
        Assert.Contains(prompt, diagnostics.RootElement.GetProperty("rawPayload").GetString());
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
        var command = signalLightHooks[0].GetProperty("command").GetString();
        Assert.Contains("codex-hook.ps1", command);
        Assert.Contains("-EventName \"UserPromptSubmit\"", command);

        var preToolUseHooks = document.RootElement
            .GetProperty("hooks")
            .GetProperty("PreToolUse")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
            .Where(hook => hook.GetProperty("statusMessage").GetString() == "SignalLight: PreToolUse")
            .ToArray();

        Assert.Single(preToolUseHooks);
        Assert.Contains("-EventName \"PreToolUse\"", preToolUseHooks[0].GetProperty("command").GetString());

        var postToolUseHooks = document.RootElement
            .GetProperty("hooks")
            .GetProperty("PostToolUse")
            .EnumerateArray()
            .SelectMany(group => group.GetProperty("hooks").EnumerateArray())
            .Where(hook => hook.GetProperty("statusMessage").GetString() == "SignalLight: PostToolUse")
            .ToArray();

        Assert.Single(postToolUseHooks);
        Assert.Contains("-EventName \"PostToolUse\"", postToolUseHooks[0].GetProperty("command").GetString());
    }

    [Fact]
    public void PowerShell_hook_repairs_mojibake_title_and_stop_keeps_original_task_name()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var hookScript = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");

        RunPowerShellHook(hookScript, signalRoot, "UserPromptSubmit", """
        {
          "session_id": "original-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "鏆傛棤浠诲姟"
        }
        """);
        RunPowerShellHook(hookScript, signalRoot, "Stop", """
        {
          "session_id": "different-stop-session",
          "cwd": "B:\\AI Traffic Signal"
        }
        """);

        var sessions = Directory.EnumerateFiles(Path.Combine(signalRoot, "sessions"), "*.json")
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .ToArray();

        Assert.Single(sessions);
        using var session = sessions[0];
        Assert.Equal("completed", session.RootElement.GetProperty("state").GetString());
        Assert.Equal("暂无任务", session.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public void Multiple_tasks_without_session_id_do_not_overwrite_each_other()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var agentDll = Path.Combine(repoRoot, "src", "SignalLight.Agent", "bin", "Debug", "net8.0", "SignalLight.Agent.dll");

        RunAgentEmit(agentDll, signalRoot, "running", "B:\\same-workspace", "Terminal task one");
        RunAgentEmit(agentDll, signalRoot, "running", "B:\\same-workspace", "Terminal task two");
        RunAgentEmit(agentDll, signalRoot, "completed", "B:\\same-workspace", "");

        var sessionsDirectory = Path.Combine(signalRoot, "sessions");
        var sessions = Directory.EnumerateFiles(sessionsDirectory, "*.json")
            .Select(path => JsonDocument.Parse(File.ReadAllText(path)))
            .ToArray();

        Assert.Equal(2, sessions.Length);
        Assert.Contains(sessions, session => session.RootElement.GetProperty("state").GetString() == "thinking");
        Assert.DoesNotContain(sessions, session => session.RootElement.GetProperty("displayName").GetString() == "AI Task");

        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(signalRoot, "snapshot.json")));
        Assert.Equal("thinking", snapshot.RootElement.GetProperty("aggregateState").GetString());

        foreach (var session in sessions)
        {
            session.Dispose();
        }
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("done")]
    [InlineData("idle")]
    [InlineData("green")]
    public void Agent_emit_completion_states_are_ignored(string state)
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var agentDll = Path.Combine(repoRoot, "src", "SignalLight.Agent", "bin", "Debug", "net8.0", "SignalLight.Agent.dll");

        RunAgentEmit(agentDll, signalRoot, "running", "B:\\same-workspace", "Manual task");
        RunAgentEmit(agentDll, signalRoot, state, "B:\\same-workspace", "Manual task");

        AssertSnapshotState(signalRoot, "thinking");
        var sessionPath = Directory.EnumerateFiles(Path.Combine(signalRoot, "sessions"), "*.json").Single();
        using var session = JsonDocument.Parse(File.ReadAllText(sessionPath));
        Assert.Equal("thinking", session.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void PreToolUse_resumes_single_waiting_task_without_session_id()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var agentDll = Path.Combine(repoRoot, "src", "SignalLight.Agent", "bin", "Debug", "net8.0", "SignalLight.Agent.dll");
        const string workspace = "B:\\AI Traffic Signal";

        RunAgentHook(agentDll, signalRoot, "UserPromptSubmit", $$"""
        {
          "cwd": "{{workspace}}",
          "prompt": "Needs permission"
        }
        """);
        RunAgentHook(agentDll, signalRoot, "PermissionRequest", $$"""
        {
          "cwd": "{{workspace}}"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunAgentHook(agentDll, signalRoot, "PreToolUse", $$"""
        {
          "session_id": "tool-call-session",
          "cwd": "C:\\Users\\gmlyx\\AppData\\Local",
          "tool": "shell_command"
        }
        """);

        AssertSnapshotState(signalRoot, "thinking");
        var sessions = Directory.EnumerateFiles(Path.Combine(signalRoot, "sessions"), "*.json").ToArray();
        Assert.Single(sessions);
    }

    [Fact]
    public void PostToolUse_resumes_single_waiting_task_when_pre_tool_use_is_missing()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var agentDll = Path.Combine(repoRoot, "src", "SignalLight.Agent", "bin", "Debug", "net8.0", "SignalLight.Agent.dll");
        const string workspace = "B:\\AI Traffic Signal";

        RunAgentHook(agentDll, signalRoot, "UserPromptSubmit", $$"""
        {
          "cwd": "{{workspace}}",
          "prompt": "Needs permission"
        }
        """);
        RunAgentHook(agentDll, signalRoot, "PermissionRequest", $$"""
        {
          "cwd": "{{workspace}}"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunAgentHook(agentDll, signalRoot, "PostToolUse", $$"""
        {
          "session_id": "tool-call-session",
          "cwd": "C:\\Users\\gmlyx\\AppData\\Local",
          "tool": "shell_command"
        }
        """);

        AssertSnapshotState(signalRoot, "thinking");
        var sessions = Directory.EnumerateFiles(Path.Combine(signalRoot, "sessions"), "*.json").ToArray();
        Assert.Single(sessions);
    }

    [Fact]
    public void Agent_hook_ignores_unrelated_cancelled_text_in_post_tool_payload()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var agentDll = Path.Combine(repoRoot, "src", "SignalLight.Agent", "bin", "Debug", "net8.0", "SignalLight.Agent.dll");

        RunAgentHook(agentDll, signalRoot, "UserPromptSubmit", """
        {
          "session_id": "agent-false-positive-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "Normal command after permission"
        }
        """);
        RunAgentHook(agentDll, signalRoot, "PermissionRequest", """
        {
          "session_id": "agent-false-positive-session",
          "cwd": "B:\\AI Traffic Signal"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunAgentHook(agentDll, signalRoot, "PostToolUse", """
        {
          "session_id": "agent-false-positive-session",
          "cwd": "B:\\AI Traffic Signal",
          "tool_response": "Command completed normally.",
          "raw_log": "Old transcript text: You canceled the request. Conversation interrupted."
        }
        """);

        AssertSnapshotState(signalRoot, "thinking");
    }

    [Fact]
    public void PowerShell_hook_cancelled_post_tool_use_completes_waiting_task()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var hookScript = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");
        const string prompt = "取消授权后应结束当前任务";

        RunPowerShellHook(hookScript, signalRoot, "UserPromptSubmit", $$"""
        {
          "session_id": "cancel-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "{{prompt}}"
        }
        """);
        RunPowerShellHook(hookScript, signalRoot, "PermissionRequest", """
        {
          "session_id": "cancel-session",
          "cwd": "B:\\AI Traffic Signal",
          "tool_name": "Bash"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunPowerShellHook(hookScript, signalRoot, "PostToolUse", """
        {
          "session_id": "cancel-session",
          "cwd": "B:\\AI Traffic Signal",
          "tool_name": "Bash",
          "tool_response": "You canceled the request to run Get-Item -LiteralPath 'C:\\Windows\\System32\\notepad.exe'. Conversation interrupted."
        }
        """);

        AssertSnapshotState(signalRoot, "completed");
        var sessionPath = Directory.EnumerateFiles(Path.Combine(signalRoot, "sessions"), "*.json").Single();
        using var session = JsonDocument.Parse(File.ReadAllText(sessionPath));
        Assert.Equal(prompt, session.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public void PowerShell_hook_turn_aborted_post_tool_use_completes_waiting_task()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var hookScript = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");

        RunPowerShellHook(hookScript, signalRoot, "UserPromptSubmit", """
        {
          "session_id": "turn-aborted-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "Cancel permission from approval dialog"
        }
        """);
        RunPowerShellHook(hookScript, signalRoot, "PermissionRequest", """
        {
          "session_id": "turn-aborted-session",
          "cwd": "B:\\AI Traffic Signal"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunPowerShellHook(hookScript, signalRoot, "PostToolUse", """
        {
          "session_id": "turn-aborted-session",
          "cwd": "B:\\AI Traffic Signal",
          "tool_response": "<turn_aborted> The user interrupted the previous turn on purpose."
        }
        """);

        AssertSnapshotState(signalRoot, "completed");
    }

    [Fact]
    public void Permission_watcher_turn_aborted_transcript_completes_waiting_task()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var hookScript = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");
        var watcherScript = Path.Combine(repoRoot, "hooks", "codex-permission-watch.ps1");
        var transcriptPath = Path.Combine(signalRoot, "transcript.jsonl");
        Directory.CreateDirectory(signalRoot);
        File.WriteAllText(transcriptPath, "{\"type\":\"start\"}\n", Encoding.UTF8);

        RunPowerShellHook(hookScript, signalRoot, "UserPromptSubmit", """
        {
          "session_id": "watcher-cancel-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "Cancel permission from approval dialog"
        }
        """);
        RunPowerShellHook(hookScript, signalRoot, "PermissionRequest", """
        {
          "session_id": "watcher-cancel-session",
          "cwd": "B:\\AI Traffic Signal"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunPermissionWatcher(
            watcherScript,
            hookScript,
            signalRoot,
            transcriptPath,
            "watcher-cancel-session",
            "B:\\AI Traffic Signal",
            "Cancel permission from approval dialog",
            "<turn_aborted> The user interrupted the previous turn on purpose.");

        AssertSnapshotState(signalRoot, "completed");
    }

    [Fact]
    public void PowerShell_hook_ignores_unrelated_cancelled_text_in_post_tool_payload()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var hookScript = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");

        RunPowerShellHook(hookScript, signalRoot, "UserPromptSubmit", """
        {
          "session_id": "ps-false-positive-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "Normal command after permission"
        }
        """);
        RunPowerShellHook(hookScript, signalRoot, "PermissionRequest", """
        {
          "session_id": "ps-false-positive-session",
          "cwd": "B:\\AI Traffic Signal"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunPowerShellHook(hookScript, signalRoot, "PostToolUse", """
        {
          "session_id": "ps-false-positive-session",
          "cwd": "B:\\AI Traffic Signal",
          "tool_response": "Command completed normally.",
          "raw_log": "Old transcript text: You canceled the request. Conversation interrupted."
        }
        """);

        AssertSnapshotState(signalRoot, "thinking");
    }

    [Fact]
    public void PowerShell_hook_does_not_complete_on_generic_denied_words()
    {
        var repoRoot = FindRepoRoot();
        var signalRoot = Path.Combine(Path.GetTempPath(), "SignalLightHookTests", Guid.NewGuid().ToString("N"));
        var hookScript = Path.Combine(repoRoot, "hooks", "codex-hook.ps1");

        RunPowerShellHook(hookScript, signalRoot, "UserPromptSubmit", """
        {
          "session_id": "generic-denied-session",
          "cwd": "B:\\AI Traffic Signal",
          "prompt": "Generic denied words should not finish"
        }
        """);
        RunPowerShellHook(hookScript, signalRoot, "PermissionRequest", """
        {
          "session_id": "generic-denied-session",
          "cwd": "B:\\AI Traffic Signal"
        }
        """);
        AssertSnapshotState(signalRoot, "waiting");

        RunPowerShellHook(hookScript, signalRoot, "PostToolUse", """
        {
          "session_id": "generic-denied-session",
          "cwd": "B:\\AI Traffic Signal",
          "tool_response": "permission denied",
          "decision": "deny",
          "status": "rejected"
        }
        """);

        AssertSnapshotState(signalRoot, "thinking");
    }

    private static void RunAgentHook(string agentDll, string signalRoot, string eventName, string payload)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(agentDll);
        startInfo.ArgumentList.Add("hook");
        startInfo.ArgumentList.Add("--event");
        startInfo.ArgumentList.Add(eventName);
        startInfo.ArgumentList.Add("--root");
        startInfo.ArgumentList.Add(signalRoot);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Agent hook.");
        using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
        {
            writer.Write(payload);
        }

        Assert.True(process.WaitForExit(15000), "SignalLight.Agent hook timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
    }

    private static void AssertSnapshotState(string signalRoot, string expectedState)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(signalRoot, "snapshot.json")));
        Assert.Equal(expectedState, snapshot.RootElement.GetProperty("aggregateState").GetString());
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

    private static void RunPowerShellHook(string hookScript, string signalRoot, string eventName, string payload)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{hookScript}\" -EventName \"{eventName}\"",
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.Environment["SIGNAL_LIGHT_ROOT"] = signalRoot;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
        {
            writer.Write(payload);
        }

        Assert.True(process.WaitForExit(15000), "codex-hook.ps1 timed out.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
    }

    private static void RunPermissionWatcher(
        string watcherScript,
        string hookScript,
        string signalRoot,
        string transcriptPath,
        string sessionId,
        string workspace,
        string title,
        string appendedText)
    {
        var initialLength = new FileInfo(transcriptPath).Length;
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{watcherScript}\" -HookScript \"{hookScript}\" -TranscriptPath \"{transcriptPath}\" -SessionId \"{sessionId}\" -Workspace \"{workspace}\" -Title \"{title}\" -InitialLength {initialLength} -TimeoutSeconds 10",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.Environment["SIGNAL_LIGHT_ROOT"] = signalRoot;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start permission watcher.");
        Thread.Sleep(500);
        File.AppendAllText(transcriptPath, appendedText + Environment.NewLine, Encoding.UTF8);

        Assert.True(process.WaitForExit(15000), "codex-permission-watch.ps1 timed out.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
    }

    private static void RunAgentEmit(string agentDll, string signalRoot, string state, string workspace, string title)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(agentDll);
        startInfo.ArgumentList.Add("emit");
        startInfo.ArgumentList.Add("--state");
        startInfo.ArgumentList.Add(state);
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add("codex-cli");
        startInfo.ArgumentList.Add("--adapter");
        startInfo.ArgumentList.Add("codex-hooks");
        startInfo.ArgumentList.Add("--workspace");
        startInfo.ArgumentList.Add(workspace);
        startInfo.ArgumentList.Add("--root");
        startInfo.ArgumentList.Add(signalRoot);

        if (!string.IsNullOrWhiteSpace(title))
        {
            startInfo.ArgumentList.Add("--title");
            startInfo.ArgumentList.Add(title);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start Agent.");
        Assert.True(process.WaitForExit(15000), "SignalLight.Agent timed out.");

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
