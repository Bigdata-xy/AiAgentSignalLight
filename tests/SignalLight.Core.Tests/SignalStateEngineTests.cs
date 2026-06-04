using SignalLight.Core.Events;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;
using Xunit;

namespace SignalLight.Core.Tests;

public sealed class SignalStateEngineTests
{
    [Fact]
    public void Waiting_has_highest_user_attention_priority()
    {
        var sessions = new[]
        {
            new SignalSession { SessionId = "running", State = SignalSessionState.Thinking },
            new SignalSession { SessionId = "waiting", State = SignalSessionState.Waiting }
        };

        Assert.Equal(SignalSessionState.Waiting, SignalStateEngine.Aggregate(sessions));
    }

    [Fact]
    public void Task_started_maps_to_thinking()
    {
        var engine = new SignalStateEngine();
        var session = engine.Apply(new SignalEvent { EventType = SignalEventType.TaskStarted, SessionId = "demo" });

        Assert.Equal(SignalSessionState.Thinking, session.State);
    }

    [Fact]
    public void Completion_without_title_keeps_previous_task_excerpt()
    {
        var engine = new SignalStateEngine();
        var previous = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            SessionId = "demo",
            Source = "codex-cli",
            Title = "继续完成开发，到下一个里程碑阶段进行完成落盘。"
        });

        var completed = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskCompleted,
            SessionId = "demo",
            Source = "codex-cli"
        }, previous);

        Assert.Equal("继续完成开发，到下一个里程碑阶段进行完成落盘。", completed.DisplayName);
    }

    [Fact]
    public void Placeholder_codex_title_does_not_replace_previous_task_excerpt()
    {
        var engine = new SignalStateEngine();
        var previous = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            SessionId = "demo",
            Title = "添加功能，可以直接在抽屉的任务显示中，删除某个当前任务行。"
        });

        var completed = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskCompleted,
            SessionId = "demo",
            Title = "Codex"
        }, previous);

        Assert.Equal("添加功能，可以直接在抽屉的任务显示中，删除某个当前任务行。", completed.DisplayName);
    }

    [Fact]
    public void Placeholder_true_title_does_not_replace_previous_task_excerpt()
    {
        var engine = new SignalStateEngine();
        var previous = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            SessionId = "demo",
            Title = "用 cmd 打开 codex 后完成任务。"
        });

        var completed = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskCompleted,
            SessionId = "demo",
            Title = "true"
        }, previous);

        Assert.Equal("用 cmd 打开 codex 后完成任务。", completed.DisplayName);
    }

    [Fact]
    public void Placeholder_ai_task_title_does_not_replace_previous_task_excerpt()
    {
        var engine = new SignalStateEngine();
        var previous = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            SessionId = "demo",
            Title = "抽屉任务描述保持原始任务名称。"
        });

        var completed = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskCompleted,
            SessionId = "demo",
            Title = "AI Task"
        }, previous);

        Assert.Equal("抽屉任务描述保持原始任务名称。", completed.DisplayName);
    }

    [Fact]
    public void Completion_without_session_id_keeps_previous_session_id()
    {
        var engine = new SignalStateEngine();
        var previous = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            Source = "codex-cli",
            Adapter = "codex-hooks",
            Workspace = @"B:\AI Traffic Signal",
            Title = "修复 cmd 启动后红灯不结束。"
        });

        var completed = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskCompleted,
            Source = "codex-cli",
            Adapter = "codex-hooks",
            Workspace = @"B:\AI Traffic Signal"
        }, previous);

        Assert.Equal(previous.SessionId, completed.SessionId);
        Assert.Equal(SignalSessionState.Completed, completed.State);
        Assert.Equal("修复 cmd 启动后红灯不结束。", completed.DisplayName);
    }

    [Fact]
    public void Completion_preserves_started_at_for_elapsed_time()
    {
        var engine = new SignalStateEngine();
        var startedAt = DateTimeOffset.Parse("2026-06-04T17:00:00+08:00");
        var completedAt = startedAt.AddMinutes(3);
        var previous = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            SessionId = "demo",
            Title = "保持任务耗时",
            CreatedAt = startedAt
        });

        var completed = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskCompleted,
            SessionId = "demo",
            CreatedAt = completedAt
        }, previous);

        Assert.Equal(startedAt, completed.StartedAt);
        Assert.Equal(completedAt, completed.UpdatedAt);
    }

    [Fact]
    public void Task_started_without_session_id_uses_title_in_fallback_session_id()
    {
        var engine = new SignalStateEngine();

        var first = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            Source = "codex-cli",
            Adapter = "codex-hooks",
            Workspace = @"B:\same-workspace",
            Title = "终端一任务"
        });
        var second = engine.Apply(new SignalEvent
        {
            EventType = SignalEventType.TaskStarted,
            Source = "codex-cli",
            Adapter = "codex-hooks",
            Workspace = @"B:\same-workspace",
            Title = "终端二任务"
        });

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(SignalSessionState.Thinking, SignalStateEngine.Aggregate(new[] { first, second }));
    }

    [Fact]
    public void Active_session_becomes_stale_after_threshold()
    {
        var engine = new SignalStateEngine();
        var now = DateTimeOffset.Parse("2026-06-04T12:00:00+08:00");
        var sessions = new[]
        {
            new SignalSession
            {
                SessionId = "old-running",
                State = SignalSessionState.Thinking,
                UpdatedAt = now.AddMinutes(-31)
            }
        };

        var snapshot = engine.BuildSnapshot(sessions, now, staleAfter: TimeSpan.FromMinutes(30));

        Assert.Equal(SignalSessionState.Stale, snapshot.Sessions.Single().State);
    }

    [Fact]
    public void Old_completed_session_is_removed_from_snapshot()
    {
        var engine = new SignalStateEngine();
        var now = DateTimeOffset.Parse("2026-06-04T12:00:00+08:00");
        var sessions = new[]
        {
            new SignalSession
            {
                SessionId = "old-completed",
                State = SignalSessionState.Completed,
                UpdatedAt = now.AddHours(-9)
            },
            new SignalSession
            {
                SessionId = "recent-completed",
                State = SignalSessionState.Completed,
                UpdatedAt = now.AddMinutes(-10)
            }
        };

        var snapshot = engine.BuildSnapshot(sessions, now, completedRetention: TimeSpan.FromHours(8));

        Assert.Single(snapshot.Sessions);
        Assert.Equal("recent-completed", snapshot.Sessions.Single().SessionId);
    }
}
