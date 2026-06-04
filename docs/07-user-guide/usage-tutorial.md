# SignalLight 使用教程

本文档说明如何使用 SignalLight 便携包接入 Codex，并用桌面红黄绿交通灯查看 AI / Agent 的运行状态。

## 1. 当前可用状态

当前版本已经可以作为本地 MVP 使用。

已验证：

- 项目可以构建。
- 自动化测试通过。
- Phase 1 模拟 Codex hook 链路通过。
- 便携包可以生成。

仍需用户在真实环境手工完成：

- 在 Codex 中执行 `/hooks`。
- 信任 SignalLight hook 命令。
- 用真实 Codex 任务观察红、黄、绿状态变化。

## 2. 状态含义

| 灯色 | 内部状态 | 含义 |
|---|---|---|
| 红灯 | `Thinking` | Codex / AI / Agent 正在处理任务 |
| 黄灯 | `Waiting` | 正在等待用户输入、权限确认或人工操作 |
| 绿灯 | `Completed` / `Idle` | 任务完成或当前空闲 |
| 异常/过期 | `Failed` / `Stale` / `Unknown` | 任务失败、状态过久未更新或无法判断 |

当前完成判定比较严格：只有 Codex `Stop` hook，或 Codex 明确取消授权/中断当前 turn，才会让任务进入绿灯。手动或测试命令写入 `completed`、`done`、`idle`、`green` 不会把真实任务标记完成。

黄灯没有固定时长。Codex 请求授权后，如果用户还没有确认，SignalLight 会一直保持黄灯。用户授权通过后，只有收到 Codex 后续 `PreToolUse`、`PostToolUse` 或明确 approval 记录，才会切回红灯。

完整状态策略：

```text
docs/04-protocol/state-completion-policy.md
```

## 3. 推荐使用方式：便携包

便携包位置：

```text
B:\AI Traffic Signal\dist\SignalLight-Portable-win-x64.zip
```

推荐直接使用项目根目录的一键启动脚本：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

这个脚本会自动解压最新便携包、安装 hooks，并启动 SignalLight。

启动脚本会把 SignalLight 放到独立后台进程中运行。脚本返回后可以关闭 PowerShell 窗口，红绿灯会继续保留；需要退出时使用系统托盘中的 `Exit`。

### 3.1 解压便携包

如果需要手工解压，打开 PowerShell，执行：

```powershell
cd "B:\AI Traffic Signal"
Expand-Archive -LiteralPath ".\dist\SignalLight-Portable-win-x64.zip" -DestinationPath ".\dist\SignalLight-Portable-win-x64-run" -Force
cd ".\dist\SignalLight-Portable-win-x64-run"
```

解压后目录中应包含：

```text
app\SignalLight.App.dll
agent\SignalLight.Agent.dll
hooks\codex-hook.ps1
install-hooks.ps1
uninstall-hooks.ps1
README.md
LICENSE
```

## 4. 安装 Codex hooks

在便携包目录中执行：

```powershell
.\install-hooks.ps1
```

这个脚本会把 SignalLight 的 hook 命令写入 Codex hooks 配置：

```text
%USERPROFILE%\.codex\hooks.json
```

如果你设置了 `CODEX_HOME`，则写入：

```text
%CODEX_HOME%\hooks.json
```

脚本会注册这些 Codex 事件：

```text
UserPromptSubmit
PreToolUse
PostToolUse
PermissionRequest
Stop
SessionStart
```

这些事件触发时，Codex 会直接调用：

```text
powershell -NoProfile -ExecutionPolicy Bypass -File hooks\codex-hook.ps1 -EventName <event>
```

当前主链路由 `hooks\codex-hook.ps1` 直接读取 Codex stdin 并写入本地 JSON。这样可以避开本机 Windows Smart App Control / 应用控制策略对便携包中 `SignalLight.Agent.dll` 的拦截。

hook 最终把状态写入：

```text
%LOCALAPPDATA%\SignalLight\
```

## 5. 启动 SignalLight

在便携包目录中执行：

```powershell
dotnet .\app\SignalLight.App.dll
```

当前推荐使用 DLL 方式，因为它可以避开 Windows Smart App Control 对本地未签名 exe 的拦截。

启动后会出现一个小型桌面交通灯窗口。窗口可以拖动，也会显示托盘图标。通过 `start-signal-light.ps1` 启动时，App 不依赖当前 PowerShell 窗口，关闭终端不会关闭红绿灯。

主窗口默认只显示红、黄、绿三盏灯和任务计数徽标。其他操作隐藏在托盘菜单和任务抽屉中，避免窗口占用太多屏幕空间。

托盘菜单的第一层只保留常用入口：

- Show / Hide
- Hooks
- Diagnostics
- Exit

其中 `Hooks` 子菜单包含 `Install` 和 `Uninstall`，用于接入或移除 Codex hooks。

`Diagnostics` 子菜单包含 `Open data`、`Export` 和 `Clear done`，用于查看本地数据、导出排查包和清理已完成会话。

## 6. 在 Codex 中信任 hooks

打开 Codex，在任意会话中输入：

```text
/hooks
```

看到 SignalLight 相关 hook 命令后，选择信任。

这是必须步骤。没有信任前，Codex 不会执行 hook，SignalLight 也不会收到真实 Codex 事件。

## 7. 正常使用 Codex

完成 hook 安装和信任后，正常使用 Codex 即可。

典型状态变化：

```text
提交 prompt -> 红灯
Codex 请求权限 -> 黄灯
授权通过并收到继续执行信号 -> 红灯
任务完成 -> 绿灯
```

如果在授权界面选择 `No`，Codex 会中断当前 turn。SignalLight 只识别 Codex 明确取消/中断信号，并把该任务转为空闲/完成显示；不会因为普通文本里出现“denied/canceled”等词就误判完成。

收到 `Stop` 后，UI 会先等待 0.8 秒确认窗口。如果这段时间内又来了新的红灯或黄灯事件，绿灯不会显示，避免执行过程中短暂闪绿。

SignalLight 会用右下角徽标显示多会话完成数量，例如 `2/3` 表示 3 个可见会话中已有 2 个完成。

点击右下角徽标可以打开任务抽屉。抽屉中每行任务会显示：

- 任务 prompt 节选或工作目录名
- 当前状态
- 工作目录摘要
- 运行中耗时或完成耗时

抽屉不显示 `source/adapter`。任务完成后仍保留原始任务节选，不会变成 `Codex`。

每行右侧的 `x` 可以删除该任务记录。删除后 SignalLight 会刷新任务列表、计数徽标和主灯状态。

## 8. 验证是否接通

### 8.1 在窗口中查看

如果接通成功，SignalLight 主窗口的灯色会随 Codex 状态变化：

```text
提交 prompt -> 红灯闪烁
请求权限 -> 黄灯闪烁
授权通过并收到继续执行信号 -> 红灯闪烁
任务完成 -> 绿灯闪烁
```

如果需要查看详细 hook 诊断，请使用托盘菜单的 `Diagnostics > Open data` 或 `Diagnostics > Export`。

如果需要查看当前任务细节，请点击窗口右下角的任务计数徽标打开抽屉。

### 8.2 在 PowerShell 中查看

查看本地数据目录：

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight"
```

查看事件文件：

```powershell
Get-ChildItem "$env:LOCALAPPDATA\SignalLight\events"
```

查看当前快照：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\snapshot.json"
```

查看最近 hook 诊断：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
```

## 9. 导出诊断

在 SignalLight 托盘菜单中点击：

```text
Diagnostics > Export
```

诊断包会生成在：

```text
%LOCALAPPDATA%\SignalLight\diagnostics\
```

诊断包通常包含：

- `snapshot.json`
- `sessions/*.json`
- `events/*.json`
- `diagnostics/latest-hook-context.json`
- Codex `hooks.json`

## 10. 清理已完成会话

在 SignalLight 托盘菜单中点击：

```text
Diagnostics > Clear done
```

它会删除本地已经完成或空闲的 session 文件，并重建当前 snapshot。

不会删除事件历史文件。

## 11. 卸载 SignalLight hooks

如果不想继续让 Codex 接入 SignalLight，在便携包目录执行：

```powershell
.\uninstall-hooks.ps1
```

它只会移除 SignalLight 自己写入的 hook 项，保留用户已有的其他 hooks。

## 12. 日常使用命令

首次使用推荐：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

首次使用手工方式：

```powershell
cd "B:\AI Traffic Signal"
Expand-Archive -LiteralPath ".\dist\SignalLight-Portable-win-x64.zip" -DestinationPath ".\dist\SignalLight-Portable-win-x64-run" -Force
cd ".\dist\SignalLight-Portable-win-x64-run"
.\install-hooks.ps1
dotnet .\app\SignalLight.App.dll
```

然后在 Codex 中执行：

```text
/hooks
```

信任 SignalLight hook 命令。

以后日常启动通常只需要：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

命令执行完成后可以关闭终端窗口。退出 SignalLight 请使用托盘菜单的 `Exit`。

只有 hooks 被修改、删除或需要重新接入时，才再次执行：

```powershell
.\install-hooks.ps1
```

## 13. 开发目录运行方式

如果不使用便携包，也可以直接从源码目录运行：

```powershell
cd "B:\AI Traffic Signal"
.\tools\install-hooks.ps1
dotnet run --project .\src\SignalLight.App\SignalLight.App.csproj
```

然后在 Codex 中执行：

```text
/hooks
```

## 14. 自动验证 Phase 1 链路

项目内置了模拟 Codex hook 的自动验证脚本：

```powershell
cd "B:\AI Traffic Signal"
.\tools\validate-phase1.ps1
```

它会验证：

- `UserPromptSubmit` -> `Thinking`
- `PermissionRequest` -> `Waiting`
- `PreToolUse` -> `Thinking`
- `PostToolUse` -> `Thinking`
- `Stop` -> `Completed`
- 手动 `completed/done/idle/green` 不作为真实完成来源
- `snapshot.json`
- `sessions/*.json`
- `events/*.json`
- `diagnostics/latest-hook-context.json`

注意：这个脚本验证的是模拟 hook 链路，不等价于真实 Codex `/hooks` 信任流程。

## 15. 常见问题

### 15.1 窗口没有变化

检查：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
```

如果文件不存在，通常说明 Codex hook 没有触发。

处理：

1. 确认已经执行 `.\install-hooks.ps1`。
2. 在 Codex 中执行 `/hooks`。
3. 信任 SignalLight hook 命令。
4. 再提交一个 Codex prompt。

### 15.2 Codex 提示 hooks.json parse failed

如果 Codex 显示：

```text
failed to parse hooks config ... expected value at line 1 column 1
```

通常是旧版 `hooks.json` 写入了 BOM 或文件内容损坏。使用当前版本重新安装 hooks：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

当前安装脚本会用无 BOM UTF-8 写入 `hooks.json`。

### 15.3 SignalLight.App.exe 被应用程序控制策略阻止

如果 PowerShell 显示：

```text
应用程序控制策略已阻止此文件
```

当前版本已经改为 DLL 启动方案。先使用最新版启动脚本：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

如果仍被阻止，通常说明系统连 `dotnet` 加载本地 DLL 也限制了。此时需要把 `B:\AI Traffic Signal` 或便携包目录加入系统允许列表。

### 15.4 Hooks 显示未安装

重新执行：

```powershell
.\install-hooks.ps1
```

然后在 Codex 中执行：

```text
/hooks
```

### 15.5 Agent missing

如果诊断里显示 Agent 找不到，确认便携包目录结构没有被改乱：

```text
agent\SignalLight.Agent.dll
hooks\codex-hook.ps1
```

不要只复制 `app` 目录运行，hook 还需要 `agent` 和 `hooks` 目录。

### 15.6 想完全停止接入 Codex

执行：

```powershell
.\uninstall-hooks.ps1
```

然后关闭 SignalLight 窗口或通过托盘菜单退出。

### 15.7 授权后没有立刻从黄灯变红

这通常不是 UI 卡住，而是 Codex 没有在授权通过的瞬间写入 SignalLight 可以可靠识别的 approval 记录。

SignalLight 当前不会用进程命令行做兜底判断，因为这种方式可能误匹配 watcher 自身命令行，导致用户还没点 `Yes` 就提前变红。

当前可靠规则是：

```text
PermissionRequest -> 黄灯
PreToolUse/PostToolUse/明确 approval -> 红灯
Stop 或 Codex 明确取消/中断 -> 绿灯
```

如果需要排查真实事件顺序，查看：

```powershell
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-hook-context.json"
Get-Content "$env:LOCALAPPDATA\SignalLight\diagnostics\latest-permission-watch.json"
```

