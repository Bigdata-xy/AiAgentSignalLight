# Codex Traffic Light 项目分析报告

## 1. 项目定位

`codex-traffic-light` 是一个纯本地 Windows 桌面小工具，用悬浮红黄绿交通灯窗口显示 Codex 的运行状态。它不直接接入 Codex 内部进程，也不运行服务端，而是通过 Codex hooks 执行 PowerShell 脚本，把 hook 事件写入本地 JSON 文件，再由 WPF 程序通过 `FileSystemWatcher` 监听这些文件并刷新界面。

核心目标可以概括为：

- 红灯表示 Codex 正在处理用户请求。
- 黄灯表示 Codex 正在等待权限确认。
- 绿灯表示 Codex 当前任务结束或处于空闲状态。
- 支持多个 Codex CLI 或 VS Code 插件会话同时运行时的聚合状态和任务列表。
- 保持本地运行，不上传用户数据。

该项目更像一个 Codex hooks 状态可视化器，不是 Codex 的替代入口，也不是后台守护服务。

## 2. 技术栈和工程结构

旧项目使用 .NET 8，主要包含桌面应用、核心库、测试、安装脚本和发布脚本。

典型结构：

```text
src/
  CodexTrafficLight.App/       WPF 桌面窗口、托盘菜单、文件监听、视觉效果
  CodexTrafficLight.Core/      路径、hook 安装、状态文件、会话、设置、统计
tests/
  CodexTrafficLight.Tests/     xUnit 单元测试和结构断言测试
installer/
  CodexTrafficLight.iss        Inno Setup 安装脚本
tools/
  codex-light.ps1              Codex 启动包装脚本
  publish-installer.ps1        发布并构建安装包
```

优点是 Core 与 WPF 有一定分离，路径、JSON、hook、统计逻辑可以单独测试。

## 3. 核心实现思路

旧项目关键链路：

```text
Codex hook event
  -> PowerShell hook script
  -> local JSON state files
  -> WPF FileSystemWatcher
  -> Red / Yellow / Green UI
```

状态文件通常包括：

- 主状态文件。
- session 状态文件。
- 诊断文件。
- 设置和统计文件。

这种方式的优势是低依赖、纯本地、容易排查，也不需要长期运行后台服务。

## 4. 已有设计优点

### 4.1 本地优先

所有状态保存在本地 JSON 文件中，不需要服务器、数据库或网络连接，符合隐私和低依赖目标。

### 4.2 交通灯语义直观

红、黄、绿和 Codex 状态映射清晰，用户不需要阅读复杂面板就能判断是否需要关注。

### 4.3 hooks 接入简单

Codex hooks 能在关键生命周期事件中触发脚本，适合做轻量状态采集。

### 4.4 有诊断意识

旧项目已经意识到 hook 信任、路径、事件触发和 JSON 写入可能失败，并开始保存诊断信息。

### 4.5 可打包分发

Windows 桌面工具可以做成安装器或便携包，适合个人工作流。

## 5. 主要问题

### 5.1 产品边界过度绑定 Codex

旧项目的命名、文件名和协议都倾向于 Codex 专用。这样会限制后续接入其他 AI 网站、Agent 或 CLI 工具。

改进方向：

- 产品名使用 SignalLight。
- Core 协议使用通用 `TaskStarted`、`UserActionRequired` 等事件。
- Codex 只是 adapter，不是领域模型。

### 5.2 hook 脚本承担过多业务逻辑

PowerShell 应只负责采集和转发。复杂业务判断应放在 Agent 和 Core 中，否则脚本会难以测试、难以维护，也容易受执行环境影响。

改进方向：

```text
Codex hook command -> SignalLight.Agent -> Core state engine
```

### 5.3 状态文件协议不够通用

旧文件名和字段容易把产品锁定在 Codex 语境中，也缺少足够清晰的 schema version 和 source/adapter 字段。

改进方向：

```json
{
  "schemaVersion": 1,
  "eventId": "...",
  "eventType": "TaskStarted",
  "sessionId": "...",
  "source": "codex-cli",
  "adapter": "codex-hooks",
  "workspace": "...",
  "title": "...",
  "createdAt": "...",
  "payload": {}
}
```

### 5.4 UI 和诊断层级不足

单个交通灯能表达主状态，但不能解释为什么是这个状态。日常使用需要至少三层信息：

1. 迷你交通灯。
2. 活跃会话列表。
3. 诊断和导出页面。

### 5.5 中文文档编码损坏

旧项目或迁移过程中出现 mojibake，影响文档可信度。正式文档必须恢复或重写为 UTF-8。

## 6. SignalLight 的改进方向

SignalLight 应保留旧项目的直观交通灯体验，但在架构上做清晰拆分：

```text
Adapters -> Agent -> Core -> Storage -> App
```

模块职责：

- Adapters：Codex hooks、CLI、浏览器、file-drop。
- Agent：命令行事件入口和本地写入器。
- Core：事件到状态转换、聚合、过期策略。
- Storage：JSON 文件读写。
- App：交通灯、会话列表、诊断、托盘、安装入口。

这样可以保留红黄绿体验，同时避免产品只能服务 Codex。

## 7. 当前 SignalLight 状态

当前新项目已经实现：

- 通用事件模型。
- Core 状态机。
- JSON 存储。
- SignalLight.Agent `emit` 命令。
- Codex hook 脚本。
- hooks 安装和卸载脚本。
- WPF 交通灯 UI。
- 多会话列表。
- 诊断摘要和导出。
- 托盘入口。
- session 过期策略。
- portable zip 打包。

仍需完成：

- 真实 Codex `/hooks` 信任流程验证。
- 真实红黄绿触发验证。
- WPF 桌面视觉验证。
- 通用 file-drop adapter。
- 浏览器/userscript adapter。
- 安装器和完整用户文档。

## 8. 风险分析

| 风险 | 影响 | 建议 |
|---|---|---|
| Codex hook 输入变化 | Codex adapter 失效 | 保存 raw payload，诊断显示原始输入 |
| 用户未信任 hooks | 状态永远不更新 | App 显示信任步骤和最近 hook 时间 |
| Agent 路径错误 | hook 不写入状态 | hook 诊断记录 agentPath 和 agentFound |
| 多会话状态混乱 | 主灯误导用户 | Core 统一聚合优先级 |
| JSON 文件损坏 | UI 读取失败 | 损坏文件跳过并保留诊断 |
| 文档乱码 | 后续开发依据不可靠 | 正式文档全部 UTF-8 重写 |

## 9. 结论

旧项目证明了“用交通灯展示 AI 工具状态”这个方向是成立的，但它更像 Codex 专用可视化器。SignalLight 的价值在于把这个方向产品化：

- 保留交通灯主视觉。
- 把 Codex 降级为第一个 adapter。
- 用通用事件协议承载更多 AI / Agent。
- 用本地 JSON 保持低依赖。
- 把诊断能力变成一等功能。
- 用 portable zip 降低分发门槛。

推荐路线是先完成真实 Codex 验收，然后进入通用 adapter 阶段。

