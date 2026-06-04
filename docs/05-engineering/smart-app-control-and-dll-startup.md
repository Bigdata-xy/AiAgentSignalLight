# Smart App Control 拦截与 DLL 启动方案

Date: 2026-06-04

## 1. 问题现象

运行早期便携包中的 apphost exe 时，PowerShell 可能出现：

```text
程序 SignalLight.App.exe 无法运行：应用程序控制策略已阻止此文件。
```

同时，Codex hooks 仍可能已经成功安装到：

```text
C:\Users\<user>\.codex\hooks.json
```

这说明 hook 配置写入和 App 进程启动是两个问题。hooks 可以安装成功，但如果 App 或 Agent exe 被系统阻止，红黄绿状态链路仍无法闭环。

## 2. 排查结论

AppLocker 日志显示：

```text
AppLocker 组件在此 SKU 上不可用
```

这说明当前拦截不是由 AppLocker 规则直接导致。

Code Integrity 日志显示：

```text
ProviderName: Microsoft-Windows-CodeIntegrity
Id: 3118
Message: Smart App Control Block Details
```

因此根因归类为：

```text
Smart App Control / Code Integrity 拦截本地未签名 exe。
```

## 3. 为什么最开始 exe 可以运行

最开始能运行，不代表后续重新打包后的 exe 会继续被信任。Windows 的应用控制策略可能参考：

- 文件哈希。
- 文件签名。
- 文件来源标记。
- 文件信誉。
- 路径策略。
- Smart App Control 当前状态。

每次重新 `dotnet publish` 或重新打包后，`SignalLight.App.exe` 和 `SignalLight.Agent.exe` 的内容可能变化，文件哈希也会变化。即使文件名相同，系统也可能把它视为新的未知程序。

如果旧 run 目录被删除，再重新解压新包，原先可运行的旧 exe 信任状态也不会自动转移到新文件。

## 4. exe 方式与 DLL 方式区别

### exe 方式

```powershell
.\app\SignalLight.App.exe
.\agent\SignalLight.Agent.exe
```

优点：

- 像普通 Windows 程序一样直接运行。
- 可以双击启动。
- 适合已签名、已被系统信任的正式发行环境。

缺点：

- 当前 exe 是本地构建的未签名程序。
- Smart App Control 可能把它识别为未知程序并阻止。
- 重新打包后哈希变化，可能再次触发拦截。

### DLL + dotnet 方式

```powershell
dotnet .\app\SignalLight.App.dll
dotnet .\agent\SignalLight.Agent.dll
```

优点：

- 实际启动进程是系统已信任的 `dotnet.exe`。
- 可以避开 Smart App Control 对本地未签名 apphost exe 的拦截。
- App UI、Codex hooks、Agent 写入、任务抽屉、任务删除等功能保持不变。

缺点：

- 需要本机安装 .NET 8 Runtime 或 SDK。
- 不能直接双击 DLL 启动。
- 启动命令不如 exe 直观。

## 5. 当前采用方案

当前项目保留全部功能，但便携包改为 framework-dependent DLL 运行方式。

打包脚本使用：

```powershell
--self-contained false
-p:UseAppHost=false
```

生成：

```text
app\SignalLight.App.dll
app\SignalLight.App.runtimeconfig.json
app\SignalLight.App.deps.json

agent\SignalLight.Agent.dll
agent\SignalLight.Agent.runtimeconfig.json
agent\SignalLight.Agent.deps.json
```

不再依赖：

```text
app\SignalLight.App.exe
agent\SignalLight.Agent.exe
```

## 6. 当前启动链路

用户执行：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

脚本执行：

```text
1. 解压 dist\SignalLight-Portable-win-x64.zip
2. 对 zip 和解压文件执行 Unblock-File
3. 运行 install-hooks.ps1
4. 后台启动 dotnet app\SignalLight.App.dll
```

Codex hook 执行：

```text
Codex hook event
-> powershell hooks\codex-hook.ps1 -EventName <event>
-> 写入 %LOCALAPPDATA%\SignalLight
-> App 监听 snapshot / diagnostics 并刷新红黄绿 UI
```

当前本机环境中，直接加载便携包里的 `SignalLight.Agent.dll` 会被应用控制策略拦截，因此真实 Codex lifecycle hooks 使用纯 PowerShell hook 脚本直接写 JSON。Agent DLL 仍保留在包中用于开发、手工 emit 或后续环境兼容，但不是当前真实 Codex hook 的主入口。

App 由启动脚本作为独立后台进程启动。PowerShell 终端关闭后，SignalLight 应继续运行；退出由托盘菜单 `Exit` 控制。

## 7. 验证命令

构建：

```powershell
dotnet build SignalLight.sln
```

打包：

```powershell
tools\package-portable.ps1
```

阶段验证：

```powershell
tools\validate-phase1.ps1 -Configuration Release
```

检查便携包目录：

```powershell
Get-ChildItem ".\dist\SignalLight-Portable-win-x64\app"
Get-ChildItem ".\dist\SignalLight-Portable-win-x64\agent"
```

应看到 `.dll`、`.deps.json`、`.runtimeconfig.json`，不应依赖 `SignalLight.App.exe` 或 `SignalLight.Agent.exe`。

## 8. 后续可选方案

如果未来希望恢复 exe 方式，应优先考虑：

- 为 `SignalLight.App.exe` 和 `SignalLight.Agent.exe` 做代码签名。
- 使用安装器安装到受信任目录。
- 在 Windows 安全策略中允许发布目录。
- 建立稳定版本号和发布哈希，减少频繁未知文件变化。

在未签名前，DLL + dotnet 方式是当前本机 Smart App Control 环境下更稳的运行策略。
