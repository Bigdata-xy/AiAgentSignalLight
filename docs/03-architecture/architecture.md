# SignalLight Architecture

SignalLight is structured around adapters, a domain core, local storage, and a traffic-light UI.

```text
Adapters -> SignalLight.Agent -> SignalLight.Core -> SignalLight.Storage -> SignalLight.App
```

## Boundaries

- Adapters collect tool-specific events.
- Agent normalizes inbound events.
- Core owns state transitions and aggregation.
- Storage persists JSON snapshots, sessions, events, and diagnostics.
- App renders the mandatory traffic-light UI.

Codex is implemented as the first adapter. The product remains generic for future AI websites and agents.
