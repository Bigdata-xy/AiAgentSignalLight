# 2026-06-04 已完成工作落盘记录

## 当前阶段判断

项目当前处于 Phase 1 中段，整体完成度约 45%-55%。

Phase 0 的工程骨架已经基本完成。Phase 1 的核心链路已经具备可测试基础：

```text
Codex hook script -> SignalLight.Agent -> JSON snapshot/session/event -> WPF traffic light refresh
```

真实 Codex `/hooks` 信任流程和 WPF 桌面窗口实时视觉验证仍需要手工执行。

## 已完成模块

### 工程与仓库

- 已创建本地 Git 仓库。
- 主分支为 `main`。
- 仓库描述已设置为 `AiAgentSignalLight`。
- 已完成初始提交：`Initial AiAgentSignalLight project`。
- 已完成 hook 诊断提交：`Add hook diagnostics output`。
- `.gitignore` 已排除 `bin/`、`obj/`、`.local/`、`dist/`、`TestResults/` 等产物目录。

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
  - `PermissionRequest` -> `waiting`
  - `Stop` -> `completed`
  - `SessionStart` -> `completed`
- hook 脚本可从 stdin 读取 Codex payload。
- hook 脚本可提取：
  - session id
  - workspace/cwd
  - title/prompt
- hook 脚本可从开发目录或 portable `agent/` 目录定位 `SignalLight.Agent.exe`。
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
- `tools/uninstall-hooks.ps1` 已实现移除 SignalLight 自有 hook。
- 卸载脚本会保留用户已有 hook。

### WPF App

- 已实现基础交通灯窗口。
- UI 保留红、黄、绿三灯主视觉。
- 启动时读取当前 `snapshot.json`。
- 已使用 `FileSystemWatcher` 监听 `snapshot.json`。
- snapshot 更新后，UI 会通过 Dispatcher 延迟刷新，避免文件系统连续事件导致重复刷新。

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
- 已记录中文长文档乱码风险。

## 已验证结果

2026-06-04 已重新验证：

```powershell
dotnet build SignalLight.sln
dotnet test
```

结果：

- `dotnet build SignalLight.sln` 通过。
- 构建结果：0 warning，0 error。
- `dotnet test` 通过。
- 测试总数：6。
- 失败测试：0。

测试覆盖当前包括：

- Core 状态映射。
- Core 聚合优先级。
- Storage event 文件写入。
- Agent 公开命令合同。
- hook 安装脚本幂等性和保留用户 hook。
- Codex hook 脚本调用 Agent 并写入 snapshot/diagnostics 的模拟端到端链路。

## 当前未完成事项

Phase 1 尚未完全闭环的事项：

- 尚未在真实 Codex 环境执行 `/hooks` 信任流程。
- 尚未用真实 Codex prompt 验证红灯触发。
- 尚未用真实 Codex permission request 验证黄灯触发。
- 尚未用真实 Codex Stop 验证绿灯触发。
- 尚未手工观察 WPF 窗口在真实事件下的实时刷新表现。
- App 内还没有 hook 信任/安装引导界面。

Phase 2 及后续未完成事项：

- 多会话任务抽屉。
- 托盘菜单。
- 诊断页。
- 诊断导出。
- session 过期和清理策略。
- 通用 file-drop adapter。
- 浏览器/userscript adapter。
- release-ready portable zip。
- 安装器。

## 风险与注意事项

- 中文长文档仍存在 mojibake 乱码，应恢复或重写为 UTF-8 后再作为正式依据。
- 当前自动化测试还没有覆盖 WPF 视觉行为。
- 当前 hook 端到端测试使用模拟 stdin，不等价于真实 Codex hook 信任流程。
- 当前产品还处于本地 MVP 阶段，不应视为可发布版本。

## 下一步建议

1. 按 `manual-phase1-validation-checklist.md` 完成真实 Codex 手工验证。
2. 将真实验证结果追加写入 `docs/00-progress`。
3. 若手工验证通过，再进入 Phase 2 的 UI/诊断能力开发。
4. 若手工验证失败，优先根据 `diagnostics/latest-hook-context.json` 修复 hook 路径、payload 或信任配置问题。

