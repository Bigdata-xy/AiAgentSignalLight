# 2026-06-05 文档与状态策略统一落盘记录

本次更新只做项目文档落盘和描述逻辑统一，不执行 Git 操作。

## 更新范围

新增：

- `docs/04-protocol/state-completion-policy.md`

恢复并重写为 UTF-8：

- `README.zh-CN.md`
- `docs/05-engineering/reproducible-project-state-guide.md`

同步更新：

- `README.md`
- `docs/03-architecture/architecture.md`
- `docs/04-protocol/event-protocol.md`
- `docs/07-user-guide/usage-tutorial.md`
- `docs/README.md`
- `docs/00-progress/current-completion-record.md`

## 统一状态口径

| 灯色 | 含义 | 来源 |
|---|---|---|
| 红灯 | 正在执行 | `UserPromptSubmit`、`PreToolUse`、`PostToolUse` 或 Agent running/thinking/red |
| 黄灯 | 等待人工授权 | `PermissionRequest` |
| 绿灯 | 任务完成或空闲 | Codex `Stop` 或 Codex 明确取消授权/中断 |

完成判定只接受：

- Codex `Stop` hook。
- Codex 明确取消授权或中断当前 turn。

不再接受这些手动完成状态作为真实完成依据：

```text
completed
done
idle
green
```

这样可以避免任务仍在执行过程中误变绿。

## 授权流程口径

当前真实状态转换：

```text
提交 prompt -> 红灯
Codex 请求授权 -> 黄灯
授权通过并收到继续执行信号 -> 红灯
Codex Stop 或明确取消/中断 -> 绿灯
```

说明：

- 黄灯没有固定超时。
- SignalLight 不通过进程命令行猜测用户是否点击 `Yes`。
- 授权通过后，只有收到 Codex 后续 `PreToolUse`、`PostToolUse` 或明确 approval 记录，才切回红灯。
- 如果用户选择 `No` 并导致 Codex 中断当前 turn，则通过 Codex 明确取消/中断文本转为完成/空闲。

## 绿灯确认窗口

UI 收到完成事件后不会立刻显示绿灯，而是等待 0.8 秒确认窗口。

如果窗口内出现新的红灯或黄灯事件，绿灯显示会被取消。

目的：

- 避免执行过程中出现短暂绿灯闪烁。
- 避免 `Stop` 后马上进入下一段执行时给用户错误完成感。

## 文档维护要求

- 当前事实以代码、脚本和 `docs/04-protocol/state-completion-policy.md` 为准。
- 用户教程负责说明“如何使用”。
- 工程现状文档负责说明“如何复现、如何排查、为什么这样做”。
- 历史进度文档可以保留过程，但新增里程碑必须明确当前实现。
- 中文文档必须保持 UTF-8。
