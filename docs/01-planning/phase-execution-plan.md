# SignalLight 阶段执行计划

## 1. 执行目标

本计划面向通用 AI / Agent 交通信号灯产品 `SignalLight`。产品必须保留红、黄、绿交通灯作为主 UI，同时具备低依赖、便携部署、通用适配和可诊断能力。

核心目标：

- 第一阶段优先支持 Codex hooks。
- 架构上不绑定 Codex，后续可扩展到 AI 网站、CLI Agent、自建 Agent、IDE 插件和浏览器插件。
- 默认使用 JSON 本地存储，不强依赖 SQLite、后台服务或额外运行时。
- 发布形态优先支持便携包。
- 一次配置后，任意项目或任意接入源都能方便使用。

## 2. 阶段总览

| 阶段 | 名称 | 目标 | 结果 |
|---|---|---|---|
| Phase 0 | 项目初始化 | 建立工程骨架和协议边界 | 可编译、可测试的基础项目 |
| Phase 1 | Codex MVP | 实现 Codex hooks 到交通灯状态 | 本地可用的最小闭环 |
| Phase 2 | 多会话和诊断 | 完成任务徽标抽屉、诊断导出、信任引导和托盘入口 | 可日常使用的本地工具 |
| Phase 3 | 通用适配器 | 支持 CLI、文件和通用事件上报 | 不再只是 Codex 工具 |
| Phase 4 | 浏览器适配 | 接入 AI 网站等待状态 | 覆盖网页 AI 场景 |
| Phase 5 | 发布质量 | 打包、安装器、文档、测试和诊断闭环 | 可分发版本 |

## 3. Phase 0：项目初始化

### 目标

建立新项目的基础结构，避免沿用旧项目命名和状态文件协议。

### 任务

- 创建解决方案和项目结构：

```text
SignalLight/
  src/
    SignalLight.App/
    SignalLight.Core/
    SignalLight.Agent/
    SignalLight.Adapters/
    SignalLight.Storage/
  tests/
    SignalLight.Core.Tests/
    SignalLight.Agent.Tests/
    SignalLight.Storage.Tests/
  docs/
  tools/
  installer/
```

- 统一 UTF-8 文档编码。
- 确定通用事件协议。
- 确定默认本地数据目录：

```text
%LOCALAPPDATA%\SignalLight\
```

- 建立基础 CI 和本地测试命令。

### 交付物

- 可打开的 solution。
- Core 项目基础模型。
- Agent 项目命令行入口。
- App 项目基础窗口。
- docs 中的协议文档。

### 验收标准

- `dotnet build` 通过。
- `dotnet test` 通过。
- 项目名、命名空间和文件名不绑定 Codex。
- 中文文档为 UTF-8 且无乱码。

## 4. Phase 1：Codex MVP

### 目标

完成第一条端到端链路：

```text
Codex hooks -> codex-hook.ps1 -> JSON state -> WPF traffic light UI
```

### 任务

- 实现 `SignalLight.Agent emit` 命令。
- 实现 Codex hook PowerShell 模板。
- 实现 hooks 安装脚本 `install-hooks.ps1`。
- 实现 JSON 主存储：

```text
snapshot.json
sessions\<session-id>.json
events\*.json
diagnostics\latest-hook-context.json
```

- 实现 Core 状态转换：

```text
TaskStarted -> Thinking -> 红灯
UserActionRequired -> Waiting -> 黄灯
TaskCompleted -> Completed / Idle -> 绿灯
TaskFailed -> Failed -> 异常状态
```

- 实现 WPF 交通灯主窗口。
- 提供自动化 Phase 1 验证脚本，模拟 Codex hook 输入并验证状态文件。

### 交付物

- 可通过 `dotnet` 运行的 `SignalLight.App.dll`。
- 可通过 `dotnet` 运行的 `SignalLight.Agent.dll`。
- Codex hook 模板。
- 安装 hooks 脚本。
- 红黄绿交通灯 UI。
- Phase 1 自动验证脚本。

### 验收标准

- 用户提交 Codex 请求后亮红灯。
- Codex 请求权限时亮黄灯。
- Codex 完成后亮绿灯。
- App 未启动时，Agent 仍能写入事件、会话和快照文件。
- App 启动后能读取最新状态。
- 不需要 SQLite。
- 不需要后台服务。
- 自动验证脚本能通过模拟 hook 链路证明代码路径可用。

## 5. Phase 2：多会话和诊断

### 目标

让产品达到日常可用状态，解决多会话、信任、路径和诊断问题，同时保持主窗口足够小。

### 任务

- 实现多会话 session 存储。
- 实现交通灯主状态聚合：

```text
Waiting > Failed > Thinking > Completed > Idle > Stale > Unknown
```

- 实现右下角任务计数徽标。
- 实现点击徽标展开的任务抽屉。
- 任务抽屉显示：

```text
任务 prompt 节选
状态徽标
工作目录摘要
运行中耗时或完成耗时
单行删除入口
```

- 实现 hook 信任引导。
- 将诊断信息放入托盘菜单和诊断导出，而不是常驻主窗口。
- 实现托盘菜单。
- 实现旧 session 自动过期。
- 实现已完成 session 清理。

### 交付物

- 小型悬浮交通灯窗口。
- 任务计数徽标和任务抽屉。
- 单任务删除能力。
- 诊断导出能力。
- 托盘菜单。
- hooks 重装入口。
- session 清理策略。

### 验收标准

- 多个 Codex 会话同时运行时，主灯按优先级聚合。
- 任务抽屉能解释主灯为什么是当前状态。
- 任务行顶部显示任务 prompt 节选，完成后仍保留该节选。
- 任务行不显示 `source/adapter` 这类适配器内部细节。
- 用户可以从抽屉删除某个当前任务行。
- 未信任 hooks 或 hook 异常时，可通过诊断导出定位问题。
- 已完成会话能自动隐藏或手动清理。

## 6. Phase 3：通用适配器

### 目标

把产品从 Codex 专用工具扩展为通用 AI / Agent 信号灯。

### 任务

- 强化通用 `emit` 命令：

```text
signal-agent emit --source cli --state running --title "AutoAgent"
signal-agent emit --source generic --state waiting --title "Need approval"
signal-agent emit --source generic --state completed --title "Done"
```

- 实现通用 JSON drop-in 事件目录。
- 实现 source / adapter 字段展示。
- 支持非 Codex session id。
- 增加 adapter registry：

```text
codex-hooks
generic-cli
file-drop
```

### 交付物

- 通用 `emit` 合同。
- 通用事件协议文档。
- 非 Codex 示例脚本。
- adapter registry。

### 验收标准

- 不运行 Codex，也能通过命令触发红黄绿状态。
- 任意外部工具能通过写 JSON 或调用 Agent 接入。
- UI 中能区分事件来源。
- Core 不依赖 Codex 类型。

## 7. Phase 4：浏览器适配

### 目标

支持部署到需要等待的 AI 网站或 Web Agent。

### 任务

- 先实现 userscript 原型。
- 后实现浏览器扩展。
- 定义网页状态识别接口：

```text
generating -> TaskStarted
needs confirmation -> UserActionRequired
finished -> TaskCompleted
error -> TaskFailed
```

- 支持 tab id、URL、title 作为 session 上下文。
- 通过本地 companion bridge 或文件上报给 Agent。

### 交付物

- userscript 原型。
- 浏览器扩展原型。
- ChatGPT / Claude / Gemini 等站点适配说明。
- Web Agent 接入文档。

### 验收标准

- 在至少一个 AI 网站生成回答时，交通灯进入红灯。
- 网站等待用户确认或输入时，交通灯进入黄灯。
- 生成完成后，交通灯进入绿灯。
- 不影响网页正常使用。

## 8. Phase 5：发布质量

### 目标

让产品达到可分发、可排查、可升级的质量。

### 任务

- 生成便携包：

```text
SignalLight-Portable.zip
  app\SignalLight.App.dll
  agent\SignalLight.Agent.dll
  install-hooks.ps1
  uninstall-hooks.ps1
  hooks\codex-hook.ps1
  README.md
```

- 可选生成安装器。
- 增加版本检查。
- 增加诊断包导出。
- 增加完整用户文档：

```text
快速开始
Codex 接入
通用 CLI 接入
浏览器接入
故障排查
卸载说明
```

- 增加端到端验证清单。

### 交付物

- Portable zip。
- 安装器。
- 用户文档。
- 诊断导出。
- 发布检查清单。

### 验收标准

- 新机器解压后可按文档完成配置。
- 不安装额外运行时即可运行。
- 卸载 hooks 后不残留无效命令。
- 诊断包能帮助定位 hook 未触发、路径错误、Agent 缺失等问题。

## 9. 测试计划

### Core 测试

- 状态机转换。
- 聚合优先级。
- session 去重。
- session 过期。
- JSON 读写。
- 事件协议版本兼容。

### Agent 测试

- `emit` 命令参数解析。
- stdin JSON 解析。
- 缺失字段处理。
- fallback 写入。
- 诊断文件写入。

### Adapter 测试

- Codex hook 事件映射。
- 通用 CLI 事件映射。
- file-drop 事件导入。
- 浏览器状态事件映射。

### App 测试

- 启动 smoke test。
- 状态文件变化后 UI 刷新。
- 任务徽标和抽屉显示。
- 抽屉任务行删除。
- 托盘诊断导出。
- 托盘退出。

## 10. 阶段优先级建议

优先顺序：

```text
Phase 0 -> Phase 1 -> Phase 2
```

这三个阶段完成后，产品已经能作为 Codex 的低依赖本地交通灯使用。

随后推进：

```text
Phase 3
```

这一阶段是产品从 Codex 工具变成通用 AI / Agent 信号灯的关键。

最后推进：

```text
Phase 4 -> Phase 5
```

浏览器适配和发布质量可以并行推进，但不应早于 Core 协议稳定之前大规模开发。

## 11. 关键风险和控制方式

| 风险 | 影响 | 控制方式 |
|---|---|---|
| Codex hooks 输入变化 | Codex 适配失效 | 保留 raw payload，适配器单独维护 |
| 浏览器网站 DOM 变化 | 网页适配不稳定 | 每个站点独立 adapter，先 userscript 验证 |
| 过度依赖后台服务 | 部署复杂 | MVP 不做后台服务 |
| SQLite 过早引入 | 增加依赖和迁移成本 | MVP 使用 JSON |
| 产品名绑定 Codex | 后续扩展受限 | 使用 SignalLight / AI Traffic Signal |
| UI 变成非交通灯 | 偏离产品约束 | 主 UI 严格保留红黄绿交通灯 |

## 12. 当前推荐下一步

当前已完成 Phase 2 紧凑可用里程碑。下一步建议：

1. 完成真实 Codex `/hooks` 信任和红黄绿状态手工验收。
2. 将验收结果写入 `docs/00-progress`。
3. 若通过，再启动 Phase 3 通用 adapter 开发。
4. 若失败，优先根据 `diagnostics/latest-hook-context.json` 和诊断导出包修复 hook 路径、payload 或信任问题。

