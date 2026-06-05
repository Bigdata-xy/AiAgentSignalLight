# SignalLight

## 项目描述

SignalLight 是一个本地 AI / Agent 状态信号灯，用 Codex hooks 等适配器采集任务事件，并通过桌面交通灯 UI 展示运行、等待、完成和诊断状态。

## 演示视频

<video controls width="100%" src="assets/Video%20Project%205.mp4" title="SignalLight 演示视频"></video>

## 当前功能

- 紧凑悬浮红、黄、绿交通灯 UI。
- 活跃灯支持呼吸和脉冲动画。
- Codex hooks 自动接入本地任务状态。
- 右下角任务计数徽标。
- 点击徽标可打开任务抽屉，查看任务 prompt 节选、状态、工作目录摘要和耗时。
- 抽屉支持删除单个任务行。
- 托盘菜单支持显示/隐藏、安装 hooks、卸载 hooks、导出诊断、打开数据目录、清理已完成任务和退出。
- 支持便携包运行：`dist\SignalLight-Portable-win-x64.zip`。
- 默认通过 `dotnet app\SignalLight.App.dll` 启动，降低 Windows Smart App Control / 应用控制策略拦截未签名 exe 的风险。

## 状态含义

| 灯色 | 含义 | 主要来源 |
|---|---|---|
| 红灯 | AI / Agent 正在执行任务 | 提交 prompt、工具执行前后、运行中事件 |
| 黄灯 | 正在等待人工授权 | Codex `PermissionRequest` |
| 绿灯 | 当前任务完成或空闲 | Codex `Stop` 或明确取消/中断 |
| 异常 | 失败、过期或无法判断 | 失败事件、长时间无更新或异常状态 |

当前完成判定比较严格：

- 只有 Codex `Stop` 或 Codex 明确取消/中断当前 turn，才会把任务显示为完成。
- 手动或测试命令写入 `completed`、`done`、`idle`、`green` 不会把真实任务标记完成。
- 黄灯没有固定超时。只要 Codex 仍在等待授权，就会保持黄灯。
- 绿灯有 `0.8 秒`确认窗口。如果收到完成事件后马上又有红灯或黄灯事件，UI 会保持真实执行状态，不闪一下绿灯。

## 使用要求

- Windows 10 / Windows 11 x64
- .NET 8 Desktop Runtime 或 .NET 8 SDK
- Codex CLI
- PowerShell

## 快速启动

在项目根目录执行：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

这个脚本会自动完成：

1. 解压最新便携包。
2. 安装 SignalLight hooks 到 Codex。
3. 后台启动 SignalLight 桌面程序。

脚本执行完成后可以关闭 PowerShell 窗口。SignalLight 会继续在后台运行，需要退出时使用系统托盘图标中的 `Exit`。

## 信任 Codex hooks

首次安装或 hooks 更新后，打开 Codex，执行：

```text
/hooks
```

看到 SignalLight 相关 hooks 后，按 `t` 信任全部。

未信任前，Codex 不会执行 SignalLight hooks，交通灯不会随任务变化。

## 典型状态变化

```text
提交 prompt -> 红灯
Codex 请求授权 -> 黄灯
授权通过并收到继续执行信号 -> 红灯
任务结束或明确取消 -> 绿灯
```

如果授权界面选择 `No`，Codex 会中断当前 turn。SignalLight 只在识别到 Codex 明确取消/中断信号后转为绿灯或空闲，不会因为普通文本中出现类似词语就误判完成。

## 在另一台电脑使用

每台电脑都需要单独安装并信任 hooks，因为 Codex hooks 保存在当前用户目录中。

### 方式 A：复制便携包

从原电脑复制：

```text
dist\SignalLight-Portable-win-x64.zip
```

在新电脑上进入存放 zip 的目录，执行：

```powershell
Expand-Archive -LiteralPath ".\SignalLight-Portable-win-x64.zip" -DestinationPath ".\SignalLight-Portable-win-x64-run" -Force; cd ".\SignalLight-Portable-win-x64-run"; .\install-hooks.ps1; dotnet .\app\SignalLight.App.dll
```

然后在 Codex 中执行 `/hooks`，按 `t` 信任 SignalLight hooks。

### 方式 B：下载源码项目

在项目根目录执行：

```powershell
dotnet build SignalLight.sln
.\tools\package-portable.ps1
.\start-signal-light.ps1
```

然后在 Codex 中执行 `/hooks`，按 `t` 信任 SignalLight hooks。

## 本地数据位置

Codex hooks 配置：

```text
%USERPROFILE%\.codex\hooks.json
```

SignalLight 状态、会话、事件和诊断数据：

```text
%LOCALAPPDATA%\SignalLight
```

常用诊断文件：

```text
%LOCALAPPDATA%\SignalLight\snapshot.json
%LOCALAPPDATA%\SignalLight\diagnostics\latest-hook-context.json
%LOCALAPPDATA%\SignalLight\diagnostics\latest-permission-watch.json
```

## 常见问题

### 交通灯没有变化

检查三件事：

1. 是否已经运行 `.\start-signal-light.ps1`。
2. 是否在 Codex 中执行过 `/hooks`。
3. 是否按 `t` 信任了 SignalLight hooks。

### 启动后看不到窗口

查看系统托盘是否有 SignalLight 图标。也可以重新执行：

```powershell
.\start-signal-light.ps1
```

### `SignalLight.App.exe` 被系统阻止

当前推荐通过 DLL 方式启动：

```powershell
dotnet .\app\SignalLight.App.dll
```

根目录的 `start-signal-light.ps1` 已按这个方式处理。

### 授权后没有立刻从黄灯变红

SignalLight 不会通过进程命令行猜测用户是否点击 `Yes`，因为这种方式可能误判。授权通过后，只有收到 Codex 后续执行信号或明确 approval 记录，才会切回红灯。

## 卸载 hooks

在便携包目录执行：

```powershell
.\uninstall-hooks.ps1
```

它只移除 SignalLight 自己写入的 hooks，不会删除其他用户 hooks。

## 开发验证

```powershell
dotnet test SignalLight.sln
.\tools\validate-phase1.ps1
.\tools\package-portable.ps1
```

这些命令用于验证核心状态逻辑、模拟 Codex hook 链路和便携包生成。
