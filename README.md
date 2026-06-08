# SignalLight

SignalLight 是一个本地 AI / Agent 状态指示灯。它把 Codex hooks 等运行事件转换为本地状态文件，并通过 Windows 桌面红 / 黄 / 绿交通灯窗口显示当前任务状态。

## 功能

- 悬浮红、黄、绿交通灯 UI。
- 红灯表示 AI / Agent 正在执行任务。
- 黄灯表示 Codex 正在等待授权或人工操作。
- 绿灯表示当前任务完成或空闲。
- 支持本地 Codex hooks 自动接入。
- 支持远程 SSH Codex 任务通过 SSH 反向隧道点亮本地指示灯。
- 支持任务抽屉、任务计数、单条任务删除、托盘菜单、诊断导出。
- 支持便携包运行：`dist\SignalLight-Portable-win-x64.zip`。
- 默认通过 `dotnet app\SignalLight.App.dll` 启动，降低 Windows Smart App Control 拦截未签名 exe 的风险。

## 状态规则

| 灯色 | 含义 | 主要来源 |
|---|---|---|
| 红灯 | AI / Agent 正在执行任务 | `UserPromptSubmit`、`PreToolUse`、`PostToolUse` |
| 黄灯 | 等待用户授权或人工操作 | `PermissionRequest` |
| 绿灯 | 当前任务完成或空闲 | `Stop` 或明确取消 / 中断 |
| 异常 | 失败、过期或未知 | 失败事件、长期无更新或解析异常 |

完成判定比较严格：

- 只有 Codex `Stop` 或明确取消 / 中断当前 turn，才会把任务标记为完成。
- 普通手动 `completed`、`done`、`idle`、`green` 不会替代真实完成事件。
- 黄灯没有固定超时。只要 Codex 仍在等待授权，就保持黄灯。
- 绿灯有 0.8 秒确认窗口，避免中间状态短暂闪绿。

## 使用要求

- Windows 10 / Windows 11 x64
- .NET 8 Desktop Runtime 或 .NET 8 SDK
- Codex CLI
- PowerShell
- 远程 SSH 功能需要 Windows OpenSSH 客户端：`ssh`、`scp`、`ssh-keygen`

## 本地快速启动

在项目根目录执行：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

脚本会自动：

1. 解压最新便携包。
2. 安装本地 Codex hooks。
3. 启动 `SignalLight.App`。
4. 启动 `SignalLight.Bridge`，用于接收远程 SSH 转发回来的事件。

首次安装或 hooks 更新后，在 Codex 里执行：

```text
/hooks
```

看到 `SignalLight:*` 后信任 hooks。未信任前，Codex 不会执行 SignalLight hooks。

## 远程 SSH 指示灯

当前已为这台服务器提供一键脚本：

```text
Host my-172.16.11.106
HostName 172.16.11.106
User xiaoyao
Port 3368
```

运行：

```powershell
cd "B:\AI Traffic Signal"
.\start-remote-signal-my-172.16.11.106.ps1
```

也可以直接双击桌面快捷方式：

```text
SignalLight Remote 172.16.11.106
```

如果桌面快捷方式丢失，可以重新生成：

```powershell
cd "B:\AI Traffic Signal"
.\tools\create-remote-signal-shortcut.ps1
```

这个脚本会依次完成：

1. 启动本地 SignalLight 和 RemoteBridge。
2. 确认或创建本机专用 SSH key：`%USERPROFILE%\.ssh\id_ed25519_signal_light`。
3. 首次运行时提示输入一次远程服务器密码，把公钥写入远程 `~/.ssh/authorized_keys`。
4. 使用 SSH key 配置远程 `~/.signal-light/codex-remote-hook.sh`。
5. 写入远程 `~/.signal-light/env`。
6. 安装远程 `~/.codex/hooks.json`。
7. 打开 SSH 反向隧道：`ssh -R 37631:127.0.0.1:37631 ...`。

SSH 隧道窗口需要保持打开。远程 Codex 事件会通过这个隧道回传到本地 Bridge，然后驱动本地指示灯。

远程 Codex 首次配置后，进入远程 Codex 执行：

```text
/hooks
```

看到 `SignalLight Remote:*` 后信任 hooks。之后远程任务状态应能点亮本地指示灯。

### 远程手动测试

在远程服务器执行：

```bash
bash ~/.signal-light/codex-remote-hook.sh --event UserPromptSubmit <<'JSON'
{"prompt":"remote hook test","session_id":"manual-demo"}
JSON
```

本地灯应变红。

然后执行：

```bash
bash ~/.signal-light/codex-remote-hook.sh --event Stop <<'JSON'
{"session_id":"manual-demo"}
JSON
```

本地灯应转绿。

## 常用文件

本地 Codex hooks：

```text
%USERPROFILE%\.codex\hooks.json
```

本地 SignalLight 数据：

```text
%LOCALAPPDATA%\SignalLight
```

远程 Bridge 配置：

```text
%LOCALAPPDATA%\SignalLight\remote-bridge.json
```

远程 Bridge 诊断：

```text
%LOCALAPPDATA%\SignalLight\diagnostics\remote-bridge
```

远程服务器配置：

```text
~/.signal-light/env
~/.signal-light/codex-remote-hook.sh
~/.codex/hooks.json
```

## 常见问题

### 本地灯没有变化

检查：

1. 是否运行了 `.\start-signal-light.ps1`。
2. 是否在本地 Codex 执行过 `/hooks` 并信任 `SignalLight:*`。
3. 如果是远程任务，SSH 反向隧道窗口是否还打开。
4. 是否在远程 Codex 执行过 `/hooks` 并信任 `SignalLight Remote:*`。

### 远程任务一直红

通常是开始事件和结束事件没有使用同一个 session ID。当前远程 hook 会优先读取 Codex payload 里的 `session_id`。更新远程 hook 后重新配置：

```powershell
.\tools\configure-remote-signal.ps1 -HostName 172.16.11.106 -User xiaoyao -SshPort 3368 -RemoteLabel my-172.16.11.106 -IdentityFile "$env:USERPROFILE\.ssh\id_ed25519_signal_light"
```

### 仍然反复要求输入 SSH 密码

运行一键脚本时，首次会输入一次密码安装 SSH 公钥。之后应使用：

```text
%USERPROFILE%\.ssh\id_ed25519_signal_light
```

如果仍反复要求密码，说明远程公钥登录未生效。检查远程：

```bash
ls -ld ~/.ssh
ls -l ~/.ssh/authorized_keys
```

建议权限：

```text
~/.ssh               700
~/.ssh/authorized_keys 600
```

### 启动后看不到窗口

查看系统托盘是否有 SignalLight 图标。也可以重新运行：

```powershell
.\start-signal-light.ps1
```

### `SignalLight.App.exe` 被系统阻止

当前推荐通过 DLL 方式启动：

```powershell
dotnet .\app\SignalLight.App.dll
```

根目录的启动脚本已经按这个方式处理。

## 卸载 hooks

在便携包目录执行：

```powershell
.\uninstall-hooks.ps1
```

它只移除 SignalLight 自己写入的 hooks，不会删除其他用户 hooks。

## 开发验证

```powershell
dotnet build SignalLight.sln
dotnet test SignalLight.sln
.\tools\validate-phase1.ps1
.\tools\package-portable.ps1
```

更多文档见：

```text
docs\05-engineering\remote-ssh-local-signal-setup.md
docs\04-protocol\state-completion-policy.md
docs\05-engineering\reproducible-project-state-guide.md
```
