# SignalLight 状态与完成判定策略

Date: 2026-06-05

本文是当前项目的状态判定权威口径。README、使用教程、架构文档和进度记录如果出现差异，以本文为准。

## 1. 状态来源

SignalLight 的显示状态来自本地事件文件：

```text
%LOCALAPPDATA%\SignalLight\events
%LOCALAPPDATA%\SignalLight\sessions
%LOCALAPPDATA%\SignalLight\snapshot.json
```

Codex lifecycle hooks 通过 PowerShell 脚本写入这些文件：

```text
hooks\codex-hook.ps1
```

当前不再让 Codex lifecycle hooks 直接加载 `SignalLight.Agent.dll`，因为本机 Windows Smart App Control / Code Integrity 可能拦截便携包中的未签名 DLL。

## 2. 灯色含义

| 灯色 | 聚合状态 | 含义 |
|---|---|---|
| 红灯 | `Thinking` | Codex / AI / Agent 正在执行任务 |
| 黄灯 | `Waiting` | Codex 正在等待用户授权或人工操作 |
| 绿灯 | `Completed` / `Idle` | 当前任务真正结束，或当前没有活跃任务 |
| 异常 | `Failed` / `Stale` / `Unknown` | 任务失败、状态过期或无法判断 |

聚合优先级：

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

因此，只要存在一个等待授权的任务，主灯优先显示黄灯；只要存在仍在执行的任务，主灯不能因为其他完成事件提前变绿。

## 3. Codex hook 映射

| Codex 事件 | SignalLight 事件 | 灯色 | 说明 |
|---|---|---|---|
| `UserPromptSubmit` | `TaskStarted` | 红灯 | 用户提交 prompt，任务开始 |
| `PermissionRequest` | `UserActionRequired` | 黄灯 | Codex 请求人工授权 |
| `PreToolUse` | `TaskStarted` | 红灯 | 工具即将执行，任务继续运行 |
| `PostToolUse` | `TaskStarted` | 红灯 | 工具执行后 Codex 仍在处理本轮任务 |
| `Stop` | `TaskCompleted` | 绿灯 | Codex 当前 turn 结束 |
| `SessionStart` | ignored | 不改变 | 只记录诊断，不写入任务状态 |

## 4. 完成判定

当前只有两类事件可以把任务判定为完成：

1. Codex `Stop` hook。
2. Codex 明确取消授权或中断当前 turn。

取消授权/中断只识别 Codex 明确文本，例如：

```text
you canceled the request
you cancelled the request
conversation interrupted
<turn_aborted>
turn_aborted
turn aborted
the user interrupted the previous turn on purpose
```

这些文本只从结构化的结果字段中读取，例如：

```text
tool_response
message
status
decision
permission_resolution
```

不会因为原始 prompt、命令文本或无关字段中出现类似词语就把任务标记完成。

## 5. 手动 completed 不生效

为了避免测试脚本或其他工具误把未完成任务变绿，当前 Agent 会忽略这些完成类手动状态：

```text
completed
done
idle
green
```

也就是说，下面命令不会把任务变成绿灯：

```powershell
dotnet SignalLight.Agent.dll emit --state completed
```

手动 emit 仍可用于写入红灯、黄灯、失败和心跳类状态，但不能替代 Codex `Stop` 完成判定。

## 6. 授权流程

正常期望：

```text
提交 prompt -> 红灯
Codex 请求授权 -> 黄灯
授权通过并继续执行 -> 红灯
Codex turn 结束 -> 绿灯
```

实际限制：

- SignalLight 不能猜测用户是否点击了 `Yes`。
- 如果 Codex 没有在授权通过后立刻写入可识别的 approval transcript 或后续 hook，SignalLight 会保持黄灯。
- 一旦收到真实 `PreToolUse`、`PostToolUse` 或明确 approval 记录，才会切回红灯。

曾经尝试过通过进程命令行检测工具是否开始执行，但该方案会误匹配 watcher 自身命令行，导致用户未授权时也提前变红。因此当前已移除这类兜底检测。

## 7. 黄灯无超时

黄灯没有固定时长限制。只要 Codex 仍处于等待用户授权状态，SignalLight 就应该保持黄灯。

如果用户选择 `No` 并导致 Codex 中断当前 turn，SignalLight 通过 Codex 取消/中断文本把任务转为完成/空闲。

## 8. 绿灯延迟确认

UI 收到 `Completed` 后不会立刻显示绿灯，而是等待 0.8 秒确认窗口。

如果这 0.8 秒内没有新的红灯或黄灯事件，才显示绿灯。

如果马上来了新的 `Thinking` 或 `Waiting` 事件，则取消绿灯显示，继续保持真实执行状态。

这个策略用于避免 Codex 中间阶段触发 `Stop` 时用户看到短暂绿灯闪烁。

## 9. 诊断文件

排查状态转移时优先查看：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-permission-watch.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\snapshot.json"
```

历史 hook 诊断：

```text
%LOCALAPPDATA%\SignalLight\diagnostics\hooks\
%LOCALAPPDATA%\SignalLight\diagnostics\permission-watch\
```

## 10. 当前验证状态

已验证：

- `dotnet test SignalLight.sln` 通过。
- `tools\validate-phase1.ps1` 通过。
- `tools\package-portable.ps1` 通过。
- 手动 `Agent emit --state completed/done/idle/green` 不会把快照切为完成。

仍需真实 Codex 环境继续观察：

- 不同 Codex 版本是否会在授权通过后立刻写入 approval transcript。
- 多终端并发任务在缺少稳定 session id 时的归属准确性。
