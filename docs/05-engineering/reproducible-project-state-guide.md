# SignalLight 项目现状与可复现说明

Date: 2026-06-05

本文档记录当前项目的真实实现、启动方式、状态策略、已解决问题和验证方式。不包含 Git 推送操作。

## 1. 项目定位

SignalLight 是一个本地 AI / Agent 状态信号灯。当前 MVP 优先接入 Codex CLI hooks，把 Codex 的任务生命周期转换为本地 JSON 状态文件，再由 Windows WPF 小窗口显示红、黄、绿三种主要状态。

当前可用边界：

- Windows WPF 桌面 UI。
- Codex CLI 本地 hooks。
- 本地 JSON 存储。
- 便携 zip 包。
- 一键启动脚本。
- 任务抽屉、任务删除、托盘操作和诊断导出。

当前不是：

- Codex 替代客户端。
- 云服务。
- 跨平台桌面发布版。
- 已签名的正式 Windows 安装器。

## 2. 当前架构

```text
Codex lifecycle hook
-> powershell hooks\codex-hook.ps1 -EventName <event>
-> local JSON events / sessions / snapshot
-> SignalLight.App WPF FileSystemWatcher refresh
-> traffic-light UI / task drawer / tray diagnostics
```

主要项目：

- `src/SignalLight.Core`: 事件、会话、状态机和聚合规则。
- `src/SignalLight.Storage`: 本地 JSON 读写和路径管理。
- `src/SignalLight.Agent`: CLI 事件写入口。
- `src/SignalLight.Adapters`: Codex、浏览器等适配器契约。
- `src/SignalLight.App`: WPF 交通灯 UI。
- `tools`: 安装 hooks、打包和验证脚本。
- `hooks`: PowerShell hook 模板和兼容脚本。
- `docs`: 规划、架构、协议、工程、教程和进度文档。

## 3. 状态策略

权威状态策略见：

```text
docs/04-protocol/state-completion-policy.md
```

简要规则：

| Codex 事件 | SignalLight 状态 | 灯色 |
|---|---|---|
| `UserPromptSubmit` | `Thinking` | 红灯 |
| `PermissionRequest` | `Waiting` | 黄灯 |
| `PreToolUse` | `Thinking` | 红灯 |
| `PostToolUse` | `Thinking` | 红灯 |
| `Stop` | `Completed` | 绿灯 |
| `SessionStart` | ignored | 不改变 |

完成状态只接受：

- Codex `Stop` hook。
- Codex 明确取消授权或中断当前 turn。

手动 `Agent emit --state completed/done/idle/green` 会被忽略，不能把任务标记完成。

黄灯没有固定时长。收到 `PermissionRequest` 后，如果没有真实后续信号，就持续显示黄灯。用户授权通过后，收到 `PreToolUse`、`PostToolUse` 或明确 approval 记录才切回红灯。

UI 收到完成事件后有 0.8 秒绿灯确认窗口。如果窗口内出现新的红灯或黄灯事件，则取消绿灯显示。

## 4. 本地数据

默认数据目录：

```text
%LOCALAPPDATA%\SignalLight
```

主要文件：

- `snapshot.json`: 当前聚合快照。
- `sessions/*.json`: 每个任务或会话的最新状态。
- `events/*.json`: 事件历史。
- `diagnostics/latest-hook-context.json`: 最近一次 hook 输入、解析结果和映射状态。
- `diagnostics/latest-hook-error.json`: 最近一次 hook 内部错误。
- `diagnostics/latest-permission-watch.json`: 最近一次授权 watcher 结果。
- `diagnostics/hooks/*.json`: 历史 hook 诊断。
- `diagnostics/permission-watch/*.json`: 历史授权 watcher 诊断。

## 5. 启动与运行

推荐一键启动：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

脚本会执行：

1. 定位 `dist\SignalLight-Portable-win-x64.zip`。
2. 解压到 `dist\SignalLight-Portable-win-x64-run`。
3. 执行 `install-hooks.ps1` 写入 Codex hooks。
4. 结束已有 SignalLight App 实例。
5. 后台启动 `dotnet app\SignalLight.App.dll`。
6. 输出日志到 `.local\start-signal-light.out.log` 和 `.local\start-signal-light.err.log`。

启动后可以关闭 PowerShell 终端。退出 SignalLight 应使用系统托盘菜单 `Exit`。

## 6. hooks 安装逻辑

安装脚本写入：

```text
%USERPROFILE%\.codex\hooks.json
```

如果设置了 `CODEX_HOME`，则写入：

```text
%CODEX_HOME%\hooks.json
```

当前 Codex lifecycle hook 命令形态：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "UserPromptSubmit"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "PreToolUse"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "PostToolUse"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "PermissionRequest"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "Stop"
powershell -NoProfile -ExecutionPolicy Bypass -File "<portable-root>\hooks\codex-hook.ps1" -EventName "SessionStart"
```

安装后必须在 Codex 中执行：

```text
/hooks
```

然后信任 SignalLight 相关命令。未信任前，Codex 不会执行 hooks。

## 7. 已解决问题

| 问题 | 当前处理 |
|---|---|
| `hooks.json` 解析失败 | 安装脚本使用无 BOM UTF-8 写入，损坏文件会备份后重建 |
| 中文任务标题乱码 | 中文文档已重写为 UTF-8；hook 诊断保留原始 payload 便于排查 |
| `SignalLight.Agent.dll` 被 Smart App Control 拦截 | Codex lifecycle hooks 改为直接调用 PowerShell hook 脚本 |
| 任务未完成就绿灯 | `SessionStart` ignored；完成只接受 Codex `Stop` 或明确取消/中断 |
| 授权后一直黄灯 | 新增 `PreToolUse`、`PostToolUse` 和 watcher 诊断，但不使用不可靠进程检测 |
| 用户未点 Yes 却变红 | 已移除命令行进程兜底检测，避免误匹配 watcher 自身 |
| 完成后标题变成 `Codex` / `true` | Core 忽略占位标题并保留真实 prompt 节选 |
| 完成后耗时变成 `0s` | 使用 `StartedAt` 计算运行/完成耗时 |
| 多终端互相影响 | 优先使用 session/conversation/terminal/process id，避免随意完成其他活跃任务 |
| 启动必须保留终端 | 一键脚本后台启动 `dotnet app\SignalLight.App.dll`，退出靠托盘 |
| 抽屉信息冗余 | 抽屉只保留任务节选、状态、工作目录摘要和耗时 |

## 8. 构建与打包

构建：

```powershell
cd "B:\AI Traffic Signal"
dotnet build SignalLight.sln
```

打包：

```powershell
cd "B:\AI Traffic Signal"
.\tools\package-portable.ps1
```

便携包输出：

```text
dist\SignalLight-Portable-win-x64.zip
dist\SignalLight-Portable-win-x64\
```

便携包应包含：

```text
app\SignalLight.App.dll
app\SignalLight.App.deps.json
app\SignalLight.App.runtimeconfig.json
agent\SignalLight.Agent.dll
agent\SignalLight.Agent.deps.json
agent\SignalLight.Agent.runtimeconfig.json
hooks\codex-hook.ps1
install-hooks.ps1
uninstall-hooks.ps1
README.md
LICENSE
```

## 9. 验证

推荐验证：

```powershell
cd "B:\AI Traffic Signal"
dotnet test SignalLight.sln
.\tools\validate-phase1.ps1
.\tools\package-portable.ps1
```

当前已验证通过：

- `dotnet test SignalLight.sln`
- `tools\validate-phase1.ps1`
- `tools\package-portable.ps1`
- 手动 `Agent emit --state completed/done/idle/green` 不会把快照切为完成

真实 Codex 验证：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

然后在 Codex 中执行：

```text
/hooks
```

信任后提交真实 prompt，观察：

```text
提交 prompt -> 红灯闪烁
权限请求 -> 黄灯闪烁
授权通过并收到继续执行信号 -> 红灯闪烁
任务结束或明确取消 -> 绿灯闪烁
```

## 10. 诊断命令

查看最近 hook 输入：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
```

查看授权 watcher：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-permission-watch.json"
```

查看当前快照：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\snapshot.json"
```

查看 session 文件：

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight\sessions"
```

查看启动错误日志：

```powershell
Get-Content "B:\AI Traffic Signal\.local\start-signal-light.err.log"
```

查看 hooks 配置：

```powershell
Get-Content "$env:USERPROFILE\.codex\hooks.json"
```

## 11. 平台判断

当前可直接使用：

- Windows 10 / Windows 11 x64
- .NET 8 Desktop Runtime 或 .NET 8 SDK
- Codex CLI
- PowerShell

当前不能完整直接使用：

- macOS
- Ubuntu / 其他 Linux

原因：

- `SignalLight.App` 是 WPF，目标是 Windows 桌面。
- 当前便携包目标是 `win-x64`。
- 启动脚本、hook 安装脚本和托盘行为按 Windows / PowerShell 设计。

可迁移部分：

- `SignalLight.Core`
- `SignalLight.Storage`
- `SignalLight.Agent`

如果要跨平台，需要另行实现 Avalonia、MAUI、Electron、Tauri 或 Web UI，并改写 hook 安装脚本。

## 12. 文档维护规则

- 当前事实以代码、脚本和 `docs/04-protocol/state-completion-policy.md` 为准。
- 用户教程写“怎么用”。
- 本文档写“如何复现、如何排查、当前为什么这样做”。
- 历史进度文档可以保留过程，但如果与当前实现冲突，应在新的里程碑记录中明确更新。
- 所有中文文档必须保存为 UTF-8。
