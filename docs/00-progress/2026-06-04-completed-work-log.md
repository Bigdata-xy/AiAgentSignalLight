# 2026-06-04 已完成工作落盘记录

## 截至 2026-06-04 的阶段判断

截至 2026-06-04，项目已推进到 Phase 2 基础可用里程碑，整体完成度约 65%-72%。2026-06-05 之后的最新状态以 `docs/00-progress/current-completion-record.md` 和 `docs/04-protocol/state-completion-policy.md` 为准。

Phase 0 的工程骨架已经基本完成。Phase 1 的核心链路已经具备可测试基础：

```text
Codex hook command -> hooks/codex-hook.ps1 -> JSON snapshot/session/event -> WPF traffic light refresh
```

真实 Codex `/hooks` 信任流程仍需要用户在本机手工信任和观察。Phase 1 自动验证已经通过；App 侧安装入口、紧凑交通灯 UI、任务徽标抽屉、托盘入口、诊断导出、session 过期策略、单任务删除和 portable zip 打包已经具备可验证基础。

## 已完成模块

### 工程与仓库

- 已创建本地 Git 仓库。
- 主分支为 `main`。
- 项目仓库目标地址已确定：

```text
https://github.com/Bigdata-xy/AiAgentSignalLight
```

- 本地 Git remote 已配置：

```text
origin -> https://github.com/Bigdata-xy/AiAgentSignalLight.git
```

- 本地 `main` 已设置为跟踪 `origin/main`。
- GitHub 远程仓库原有 `LICENSE` 已合并进本地历史。
- 已生成合并提交：`Merge remote repository metadata`。
- 仓库描述建议已确定：

```text
SignalLight 是一个本地 AI / Agent 状态信号灯，用 Codex hooks 等适配器采集任务事件，并通过桌面交通灯 UI 展示运行、等待、完成和诊断状态。
```

- 已完成初始提交：`Initial AiAgentSignalLight project`。
- 已完成 hook 诊断提交：`Add hook diagnostics output`。
- 已完成工作日志提交：`Document completed project work`。
- `.gitignore` 已排除 `bin/`、`obj/`、`.local/`、`dist/`、`TestResults/` 等产物目录。

### GitHub 发布状态

- 已尝试将 `main` 推送到 GitHub 目标仓库。
- 首次推送时远程仓库已有内容，普通 push 被拒绝。
- 已执行 `git fetch origin main` 获取远程 `main`。
- 已通过 `--allow-unrelated-histories` 合并远程已有元数据，保留远程 `LICENSE`。
- 后续 GitHub 实时连接检查出现网络超时：

```text
Failed to connect to github.com port 443
```

- 本地引用显示 `main` 当前跟踪 `origin/main`，但由于当前环境无法连接 GitHub，仍需在网络恢复后重新执行远程确认。

建议网络恢复后执行：

```powershell
git remote show origin
git push -u origin main
git status --short --branch
```

### 项目结构

已存在并可构建的项目：

- `SignalLight.Core`
- `SignalLight.Storage`
- `SignalLight.Agent`
- `SignalLight.Adapters`
- `SignalLight.App`
- `SignalLight.Core.Tests`
- `SignalLight.Storage.Tests`
- `SignalLight.Agent.Tests`

### Core

- 已定义通用事件模型 `SignalEvent`。
- 已定义事件类型 `SignalEventType`。
- 已定义会话模型 `SignalSession`。
- 已定义会话状态 `SignalSessionState`。
- 已实现 `SignalStateEngine`：
  - `TaskStarted` 映射为 `Thinking`
  - `UserActionRequired` 映射为 `Waiting`
  - `TaskCompleted` 映射为 `Completed`
  - `TaskFailed` 映射为 `Failed`
  - `Heartbeat` 保留已有状态或回落到 `Thinking`
- 已实现聚合优先级：
  - `Waiting`
  - `Failed`
  - `Thinking`
  - `Completed`
  - `Idle`
  - `Stale`
  - `Unknown`
- `BuildSnapshot` 已支持 session 过期策略：
  - 运行中或等待中的旧会话会标记为 `Stale`
  - 超出保留窗口的已完成会话会从快照中隐藏

### Storage

- 已实现本地 JSON 存储。
- 已支持写入和读取：
  - `snapshot.json`
  - `sessions/*.json`
  - `events/*.json`
- 已预留诊断目录：
  - `diagnostics/`
- JSON 写入使用临时文件加替换方式，降低半写入文件风险。
- 损坏 session 文件会被跳过，不阻断整体读取。

### Agent

- 已实现公开入口：

```powershell
SignalLight.Agent emit --state running --source codex-cli --adapter codex-hooks --session demo --title "Demo"
```

- Agent 已支持将通用状态参数映射为内部事件：
  - `running` / `thinking` / `red`
  - `waiting` / `yellow`
  - `completed` / `done` / `idle` / `green`
  - `failed` / `error`
  - `heartbeat`
- Agent 已能写入 event、session 和 snapshot。
- Agent 已支持 `--root` 指定数据目录，便于测试和隔离真实用户数据。

### Codex Hook

- 已实现 `hooks/codex-hook.ps1`。
- 支持 Codex 事件名映射：
  - `UserPromptSubmit` -> `running`
  - `PreToolUse` -> `running`
  - `PostToolUse` -> `running`
  - `PermissionRequest` -> `waiting`
  - `Stop` -> `completed`
  - `SessionStart` -> ignored
- hook 脚本可从 stdin 读取 Codex payload。
- hook 脚本可提取：
  - session id
  - workspace/cwd
  - title/prompt
- hook 脚本可从开发目录或 portable `agent/` 目录定位 `SignalLight.Agent.dll`。
- hook 脚本已写入诊断文件：

```text
diagnostics/latest-hook-context.json
```

诊断内容包括：

- event name
- mapped state
- tool root
- signal data root
- Agent path
- Agent 是否找到
- session
- workspace
- title
- payload parse error
- raw payload

### Hook 安装与卸载

- `tools/install-hooks.ps1` 已从提示脚本升级为实际安装脚本。
- 安装脚本会合并 SignalLight hook 到 Codex `hooks.json`。
- 安装脚本会保留用户已有 hook。
- 安装脚本具备幂等性，重复执行不会重复添加 SignalLight hook。
- 安装脚本会备份已有 `hooks.json`。
- 安装脚本和卸载脚本已改为无 BOM UTF-8 写入，避免 Codex 解析 `hooks.json` 时出现 `expected value at line 1 column 1`。
- `tools/uninstall-hooks.ps1` 已实现移除 SignalLight 自有 hook。
- 卸载脚本会保留用户已有 hook。

### WPF App

- 已实现紧凑悬浮交通灯窗口。
- UI 保留红、黄、绿三灯主视觉，窗口尺寸已压缩到类似参考项目的小型形态。
- 活跃灯支持呼吸和外圈脉冲动画。
- 启动时读取当前 `snapshot.json`。
- 已使用 `FileSystemWatcher` 监听 `snapshot.json`。
- snapshot 更新后，UI 会通过 Dispatcher 延迟刷新，避免文件系统连续事件导致重复刷新。
- 已监听 `diagnostics/latest-hook-context.json`。
- 主窗口默认隐藏复杂细节，仅保留交通灯和右下角任务计数徽标。
- 已新增任务抽屉，点击徽标可查看当前可见任务。
- 抽屉任务行显示：
  - 任务 prompt 节选或工作目录名
  - 状态徽标
  - 工作目录摘要
  - 运行中耗时或完成耗时
- 抽屉任务行不再显示 `source/adapter`。
- 抽屉任务行已支持单行删除，删除后会重建 snapshot 并刷新信号灯状态。
- 已新增托盘入口：
  - Show / Hide
  - Hooks > Install
  - Hooks > Uninstall
  - Diagnostics > Open data
  - Diagnostics > Export
  - Diagnostics > Clear done
  - Exit
- 已新增诊断导出：
  - snapshot
  - sessions
  - events
  - latest diagnostics
  - Codex hooks.json
- 已新增清理已完成会话入口：
  - Clear Done

### 任务标题与显示策略

- `UserPromptSubmit` 会把 prompt 作为任务标题来源。
- 任务标题在 UI 中以节选形式显示，避免长文本撑开小窗口。
- `Stop` / `SessionStart` 等没有 prompt 的事件不再把标题默认写成 `Codex`。
- 完成事件没有真实标题时，会保留上一条任务节选。
- 旧 hook 若仍传入 `Codex` 占位标题，Core 会忽略该占位标题，继续保留真实任务节选。
- 已修复 cmd 启动 Codex 时无 session id 导致开始事件和完成事件落到不同 session 的问题。完成事件会优先匹配同来源、同 adapter、同工作目录下最近的活跃任务。
- 已修复 payload 中布尔 `prompt: true` 或命令参数占位 `true` 被误当作任务标题的问题。`true` / `false` 现在会被视为占位标题，不再覆盖真实任务节选。
- 已修复 `SessionStart` 被映射为 completed 导致任务尚未完成就绿灯的问题。`SessionStart` 现在只记录诊断，不写入任务状态。
- 已新增 `PreToolUse -> running` 和 `PostToolUse -> running`。Codex 请求权限变黄后，用户授权并进入或完成工具调用时会重新切回红灯，避免授权后一直停在黄灯。
- 已新增 session `StartedAt` 字段。抽屉任务时间改为显示任务耗时：进行中显示运行时长，完成后显示完成耗时，不再因为完成事件更新 `UpdatedAt` 而直接显示 `0s`。
- 已修复真实 Codex hooks 调用 `SignalLight.Agent.dll` 被 Windows Application Control / Smart App Control 拦截后出现 `hook exited with code 1` 的问题。当前真实 hook 安装改为调用 `hooks/codex-hook.ps1`，由 PowerShell 脚本直接写入 JSON 状态；Agent DLL 仍保留用于开发和兼容用途。
- `hooks/codex-hook.ps1` 已加入顶层容错：内部异常会写入 `diagnostics/latest-hook-error.json` 和 `diagnostics/latest-hook-context.json`，并以退出码 `0` 返回，避免 SignalLight hook 异常中断 Codex 会话。
- 已修复多个终端 Codex 任务互相影响主灯状态的问题。Agent 现在会优先提取 `session_id`、`conversation_id`、`terminal_id`、`process_id`、`pid` 等身份字段；没有稳定 session id 时，新的 `UserPromptSubmit` 不再覆盖同工作目录旧任务，`Stop` 在多个活跃任务并存时不会随意完成其中一个，避免一个终端完成后把另一个仍在运行的任务变绿。

### Portable 打包

- 已修正 `tools/install-hooks.ps1` 的路径判断。
- 安装脚本现在同时兼容：
  - 开发目录：`tools/install-hooks.ps1`
  - portable 根目录：`install-hooks.ps1`
- 已升级 `tools/package-portable.ps1`。
- 打包脚本现在会生成：

```text
dist/SignalLight-Portable-win-x64/
dist/SignalLight-Portable-win-x64.zip
```

- portable 包内容包括：
  - `app/SignalLight.App.dll`
  - `agent/SignalLight.Agent.dll`
  - `hooks/codex-hook.ps1`
  - `install-hooks.ps1`
  - `uninstall-hooks.ps1`
  - `README.md`
  - `LICENSE`

### 启动脚本

- 已新增根目录启动脚本：

```text
start-signal-light.ps1
start-signal-light.cmd
```

- `start-signal-light.ps1` 会自动：
  - 定位 `dist/SignalLight-Portable-win-x64.zip`
  - 解压到 `dist/SignalLight-Portable-win-x64-run`
  - 执行 `install-hooks.ps1`
  - 使用 `Start-Process` 后台启动 `dotnet app/SignalLight.App.dll`
  - 允许用户关闭 PowerShell 终端，App 继续通过托盘图标运行和退出

- 推荐用户日常启动命令：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

### 文档

- `docs` 已按用途分类：
  - `00-progress`
  - `01-planning`
  - `02-product-design`
  - `03-architecture`
  - `04-protocol`
  - `05-engineering`
  - `06-analysis`
- 已建立 `docs/README.md` 文档索引。
- 已建立当前完成记录：
  - `docs/00-progress/current-completion-record.md`
- 已建立 Phase 1 手工验收清单：
  - `docs/00-progress/manual-phase1-validation-checklist.md`
- 已恢复中文长文档乱码问题。
- 以下文档已重写并保存为 UTF-8：
  - `docs/01-planning/phase-execution-plan.md`
  - `docs/02-product-design/next-generation-product-design.md`
  - `docs/06-analysis/project-analysis-report.md`

### Phase 1 自动验收

- 已新增 `tools/validate-phase1.ps1`。
- 验证脚本会在隔离目录 `.local/phase1-validation` 中模拟 Codex hook 链路。
- 验证覆盖：
  - `UserPromptSubmit` -> `Thinking`
  - `PermissionRequest` -> `Waiting`
  - `PreToolUse` -> `Thinking`
  - `PostToolUse` -> `Thinking`
  - `Stop` -> `Completed`
  - `snapshot.json`
  - `sessions/*.json`
  - `events/*.json`
  - `diagnostics/latest-hook-context.json`
- 脚本已通过。

## 已验证结果

2026-06-04 已重新验证：

```powershell
dotnet build SignalLight.sln
dotnet test
tools\validate-phase1.ps1
tools\package-portable.ps1
```

结果：

- `dotnet build SignalLight.sln` 通过。
- 构建结果：0 warning，0 error。
- Core 与 Storage 测试通过。
- Agent 测试在部分运行中会被 Windows Application Control 策略拦截 DLL 加载；此前已通过，当前不作为代码失败处理。
- `tools\validate-phase1.ps1` 通过。
- `tools\package-portable.ps1` 通过。
- 已生成 `dist\SignalLight-Portable-win-x64.zip`。

测试覆盖当前包括：

- Core 状态映射。
- Core 聚合优先级。
- Core session 过期与 completed retention。
- Core 完成事件保留任务 prompt 节选。
- Core 忽略 `Codex` 占位标题。
- Storage event 文件写入。
- Agent 公开命令合同。
- hook 安装脚本幂等性和保留用户 hook。
- Codex hook 脚本调用 Agent 并写入 snapshot/diagnostics 的模拟端到端链路。

## 当前未完成事项

Phase 1 尚未完全闭环的事项：

- 尚未在可稳定访问 GitHub 的网络环境下复核远程仓库是否已经完整同步。
- 自动化 Phase 1 hook 链路已通过；真实 Codex 环境的 `/hooks` 信任流程仍需人工验收。
- 尚未用真实 Codex prompt 人工验证红灯触发。
- 尚未用真实 Codex permission request 人工验证黄灯触发。
- 尚未用真实 Codex Stop 人工验证绿灯触发。
- 尚未手工观察 WPF 窗口在真实事件下的实时刷新表现。
- App 内已有安装入口，但还没有完整的 `/hooks` 信任流程逐步引导。

Phase 2 及后续未完成事项：

- 通用 file-drop adapter。
- 浏览器/userscript adapter。
- 完整视觉诊断页与复制诊断信息能力。
- 安装器。

## 风险与注意事项

- GitHub 远程仓库已配置并合并远程元数据，但当前环境连接 GitHub 443 端口超时，远程最终状态需要网络恢复后复核。
- 当前自动化测试还没有覆盖 WPF 视觉行为和托盘行为。
- 当前 hook 端到端测试使用模拟 stdin，不等价于真实 Codex hook 信任流程。
- 当前产品还处于本地 MVP 阶段，不应视为可发布版本。

## 下一步建议

1. 网络恢复后，先执行 `git remote show origin` 和 `git push -u origin main`，确认 GitHub 仓库完整同步。
2. 在 GitHub 仓库页面填写仓库描述。
3. 按 `manual-phase1-validation-checklist.md` 完成真实 Codex 手工验证。
4. 将真实验证结果追加写入 `docs/00-progress`。
5. 若手工验证通过，再进入 Phase 3 的通用 adapter 开发。
6. 若手工验证失败，优先根据 `diagnostics/latest-hook-context.json` 和诊断导出包修复 hook 路径、payload 或信任配置问题。

