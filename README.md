# SignalLight

SignalLight is a local AI / Agent traffic signal. It keeps the traffic-light UI as the primary surface while using adapters to collect events from Codex, CLI agents, browser pages, and future automation systems.

## Principles

- Traffic-light UI is mandatory.
- Codex is only the first adapter, not the product boundary.
- MVP uses local JSON storage and self-contained Windows binaries.
- Core domain logic is independent from WPF, hooks, and browser integrations.
- Diagnostics are a first-class product surface.

## Layout

```text
src/
  SignalLight.Core/       Domain events, sessions, state engine, contracts
  SignalLight.Storage/    JSON storage and path resolution
  SignalLight.Agent/      Local event ingestion CLI
  SignalLight.Adapters/   Codex, browser, generic adapter contracts
  SignalLight.App/        WPF traffic-light UI
tests/
  SignalLight.Core.Tests/
  SignalLight.Storage.Tests/
  SignalLight.Agent.Tests/
docs/
  Product, protocol, architecture, and execution plans
tools/
  Build, test, packaging, and hook install helpers
hooks/
  Codex hook script templates
```

## MVP Command Shape

```powershell
SignalLight.Agent.exe emit --state running --source codex-cli --adapter codex-hooks --session demo --title "Demo task"
```
