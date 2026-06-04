# 通用 AI / Agent 交通信号灯产品设计方案

## 1. 产品定位

SignalLight 是一个纯本地运行的 AI / Agent 状态信号灯。它通过适配器采集 AI 工具、Agent、网站或 CLI 的运行事件，维护本地会话状态，并用红、黄、绿交通灯 UI 展示“处理中、等待用户、已完成”等状态。

Codex 只是第一阶段适配对象，不是产品边界。后续应能接入其他需要等待或确认的 AI 网站、Agent 工具、CLI、IDE 插件和自动化系统。

一句话定义：

```text
SignalLight 是一个本地 AI / Agent 交通信号灯：适配器采集事件，Core 维护状态机，JSON 保存实时状态，桌面交通灯 UI 展示状态，任务徽标抽屉展示当前任务信息，托盘菜单提供诊断和操作入口。
```

## 2. 设计目标

产品需要解决三个核心问题：

1. 让用户一眼知道某个 AI / Agent 是否正在处理任务。
2. 让用户及时发现某个 AI / Agent 是否正在等待输入、权限确认或人工接管。
3. 让多个工具、多个项目、多个会话同时运行时的状态更加清晰。

同时需要避免旧实现中已经暴露的问题：

- 中文文档编码损坏。
- PowerShell hook 脚本过长，并把业务逻辑混在脚本里。
- hook 脚本直接决定最终 UI 状态。
- 状态协议过于简单，缺少 schema version。
- 统计口径和多会话聚合口径混淆。
- 诊断能力隐藏在文件里，不够产品化。

## 3. 核心原则

### 3.1 交通灯 UI 必须保留

主视觉必须是红、黄、绿交通灯：

- 红灯：AI / Agent 正在处理。
- 黄灯：等待用户输入、权限确认或人工操作。
- 绿灯：已完成、空闲或无活跃任务。

可以增强材质、动画、信息密度和任务抽屉，但不能把主 UI 改成状态条、仪表盘或普通卡片列表。

### 3.2 适配器只采集事件

适配器不直接决定最终 UI 状态。推荐边界：

```text
Adapters = event collectors
Core state engine = state decision maker
UI = state renderer
```

Codex hooks、浏览器脚本、CLI wrapper 都只负责把外部事件转换成统一事件协议。

### 3.3 事件和状态分离

事件表示“发生了什么”：

```text
TaskStarted
UserActionRequired
TaskCompleted
TaskFailed
Heartbeat
SessionEnded
```

状态表示“当前应该如何展示”：

```text
Thinking
Waiting
Completed
Idle
Failed
Stale
Unknown
```

这样可以支持更多事件源，而不破坏 UI 逻辑。

### 3.4 本地优先

所有核心数据默认保存在本地，不上传用户数据，不依赖服务器。

默认数据目录：

```text
%LOCALAPPDATA%\SignalLight\
```

网络功能只允许用户主动触发，例如检查更新或打开 release 页面。

### 3.5 诊断能力产品化

hook 类工具常见问题包括：

- hook 没有被信任。
- hook 没有触发。
- `CODEX_HOME` 不一致。
- session id 不稳定。
- 工作目录识别失败。
- PowerShell 执行失败。
- Agent 路径错误。

因此诊断不是附属功能，而是产品可靠性的核心组成部分。

## 4. 总体架构

推荐链路：

```text
Adapters -> SignalLight.Agent -> SignalLight.Core -> SignalLight.Storage -> SignalLight.App
```

职责边界：

- Adapters：收集工具特定事件。
- Agent：归一化入站事件。
- Core：状态转换、聚合、过期策略。
- Storage：保存 JSON snapshot、sessions、events 和 diagnostics。
- App：展示交通灯 UI、任务徽标抽屉、诊断导出和操作入口。

MVP 不引入常驻后台服务。App 未启动时，Agent 仍然可以写入本地 JSON；App 下次启动后读取最新状态。

## 5. 工程结构

```text
SignalLight/
  src/
    SignalLight.App/        WPF 桌面 UI
    SignalLight.Core/       状态机、会话聚合、协议模型
    SignalLight.Agent/      本地事件写入器
    SignalLight.Adapters/   Codex、浏览器、CLI 等适配边界
    SignalLight.Storage/    JSON 存储和路径解析
  tests/
    SignalLight.Core.Tests/
    SignalLight.Agent.Tests/
    SignalLight.Storage.Tests/
  hooks/
    codex-hook.ps1
  tools/
    install-hooks.ps1
    uninstall-hooks.ps1
    package-portable.ps1
  docs/
```

## 6. 技术选型

| 模块 | 技术 | 说明 |
|---|---|---|
| 桌面 UI | .NET 8 WPF | Windows 本地桌面体验成熟 |
| 核心逻辑 | .NET class library | 状态机必须可单测 |
| Local Agent | .NET console exe | 接收 hooks、CLI 和通用事件 |
| Codex hook | PowerShell | 只负责转发和最小诊断 |
| 默认存储 | JSON 文件 | 零依赖，便于调试和迁移 |
| 可选增强 | SQLite | 后续用于历史和统计 |
| UI 通知 | FileSystemWatcher | MVP 简单可靠 |
| 发布 | Framework-dependent portable DLL zip | 通过 `dotnet` 启动，降低本地未签名 exe 被 Smart App Control 拦截的风险 |
| 测试 | xUnit | 覆盖 Core、Agent、Storage |

## 7. 事件协议

所有适配器应输出同一事件形态：

```json
{
  "schemaVersion": 1,
  "eventId": "550e8400-e29b-41d4-a716-446655440000",
  "eventType": "TaskStarted",
  "sessionId": "codex-session-id",
  "source": "codex-cli",
  "adapter": "codex-hooks",
  "workspace": "B:\\project",
  "title": "Task title",
  "createdAt": "2026-06-04T15:00:00+08:00",
  "payload": {}
}
```

Codex hook 映射：

| Codex 事件 | 通用事件 | 默认状态 |
|---|---|---|
| `UserPromptSubmit` | `TaskStarted` | `Thinking` |
| `PermissionRequest` | `UserActionRequired` | `Waiting` |
| `PreToolUse` | `TaskStarted` | `Thinking` |
| `PostToolUse` | `TaskStarted` | `Thinking` |
| `Stop` | `TaskCompleted` | `Completed` |
| `SessionStart` | ignored | 不写入任务状态 |

## 8. 本地存储

默认目录：

```text
%LOCALAPPDATA%\SignalLight\
  snapshot.json
  settings.json
  sessions\
    <session-id>.json
  events\
    <timestamp>-<event-id>.json
  diagnostics\
    latest-hook-context.json
```

说明：

- `snapshot.json` 保存聚合状态。
- `sessions/*.json` 保存每个会话的最新状态。
- `events/*.json` 保存事件流水。
- `diagnostics/latest-hook-context.json` 保存最近一次 hook 诊断信息。

写入应尽量使用临时文件再替换，降低半写入风险。

## 9. 状态聚合

主灯状态按用户注意力优先级聚合：

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

策略：

- 只要有等待用户操作的会话，主灯显示黄灯。
- 有失败会话且没有等待会话时，显示异常状态。
- 有运行中会话且没有更高优先级会话时，显示红灯。
- 只有已完成或空闲会话时，显示绿灯。
- 运行中或等待中过久未更新的会话标记为 `Stale`。
- 已完成会话超出保留窗口后从快照隐藏。

## 10. UI 设计

主窗口必须保留交通灯本体：

```text
SignalLight
  红灯
  黄灯
  绿灯
  completed / total
```

当前实现采用小型悬浮窗口，默认只显示交通灯和右下角任务计数徽标。活跃灯应有呼吸或脉冲反馈，便于用户在余光中判断状态变化。

点击任务计数徽标后打开任务抽屉。抽屉任务行显示：

- 任务 prompt 节选或工作目录名。
- 当前状态徽标。
- 工作目录摘要。
- 运行中耗时或完成耗时。
- 单行删除入口。

任务行不显示 `source/adapter`，避免把适配器细节暴露成主要用户信息。完成事件没有真实标题时，任务名应保留上一条 prompt 节选，不应回退成 `Codex`。

托盘菜单提供：

- Show / Hide。
- Hooks > Install。
- Hooks > Uninstall。
- Diagnostics > Open data。
- Diagnostics > Export。
- Diagnostics > Clear done。
- Exit。

诊断细节不常驻主窗口。需要排查时通过托盘打开数据目录或导出诊断包。

## 11. 分发方案

优先提供便携包：

```text
SignalLight-Portable-win-x64.zip
  app\SignalLight.App.dll
  agent\SignalLight.Agent.dll
  hooks\codex-hook.ps1
  install-hooks.ps1
  uninstall-hooks.ps1
  README.md
  LICENSE
```

推荐用户流程：

```powershell
cd "B:\AI Traffic Signal"
.\start-signal-light.ps1
```

手工流程：

```powershell
Expand-Archive SignalLight-Portable-win-x64.zip
.\install-hooks.ps1
dotnet .\app\SignalLight.App.dll
```

然后在 Codex 中运行：

```text
/hooks
```

信任 SignalLight hook 命令后，任意项目目录运行 Codex 都能上报状态。

## 12. MVP 范围

Phase 1 MVP 包括：

- WPF 交通灯窗口。
- SignalLight.Agent。
- Codex hook PowerShell 模板。
- hooks 安装和卸载脚本。
- JSON 存储。
- 基础诊断文件。
- 自动化 Phase 1 模拟验证。

Phase 2 基础可用包括：

- 小型悬浮交通灯 UI。
- 活跃灯呼吸和脉冲动画。
- 任务计数徽标。
- 当前任务抽屉。
- 单任务删除。
- hook 安装和卸载入口。
- 诊断导出。
- 托盘入口。
- session 过期策略。
- completed session 清理。

Phase 3 以后再做：

- 通用 file-drop adapter。
- 浏览器 userscript adapter。
- 浏览器扩展。
- 安装器。
- 更新机制。

## 13. 关键风险

| 风险 | 控制方式 |
|---|---|
| Codex hooks 输入变化 | 保留 raw payload，诊断显示原始输入 |
| 用户未信任 hooks | App 显示安装状态和信任提示 |
| Agent 路径错误 | hook 写入 `agentFound` 和 `agentPath` |
| session id 不稳定 | 优先使用外部 session id，缺失时生成 fallback |
| 文件监听丢事件 | App 启动时重新读取 snapshot 和 sessions |
| 中文文档乱码 | 所有正式文档使用 UTF-8 重写和保存 |

## 14. 推荐路线

短期：

1. 完成真实 Codex `/hooks` 信任验证。
2. 验证真实红黄绿状态。
3. 完成 Phase 1 验收落盘。

中期：

1. 强化 Agent emit 合同。
2. 增加 file-drop adapter。
3. 增加非 Codex 接入示例。

长期：

1. 浏览器 userscript。
2. 浏览器扩展。
3. 安装器和自动更新。

