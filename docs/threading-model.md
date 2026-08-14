# Threading and lifetime model

## Owners

- The WPF dispatcher owns `App`, `OverlayHost`, `OverlayWindow`,
  `LiveOverlayPresenter`, and debug diagnostics.
- `PowerLogTailer` is the single writer to its bounded raw-line channel.
- `LiveTrackingCoordinator` is the single consumer and the only live caller of
  its `TrackingSession`.
- `InMemoryCardDatabase` publishes an atomically replaced immutable database
  state; readers do not lock or observe partial refreshes.
- `TelemetryRuntime` is the single reader of a bounded 16-item summary channel;
  `TelemetryOutbox` serializes file access with its private gate.

## Startup

`App.OnStartup` creates diagnostics, constructs `IceCrowRuntime`, and calls
`Start`. The runtime starts overlay presentation and then three background
pipelines: data initialization/refresh, consent/outbox processing, and live log
tracking. Optional card data never blocks live tracking.

## Shutdown order

1. Stop accepting telemetry summaries and cancel the one root token.
2. Await data, telemetry, and live background tasks.
3. Detach/dispose live log resources.
4. Dispose telemetry storage and data HTTP resources.
5. Dispose the presenter and overlay on the WPF dispatcher.
6. Dispose debug presentation and the root cancellation source.

The Debug window's UI event handler awaits this path. WPF's `Application.OnExit`
is synchronous, so `App.xaml.cs` contains the only permitted blocking task wait
as an idempotent final fallback. Architecture tests reject such waits elsewhere.

## Backpressure and dispatch

- `PowerLogTailer`: bounded channel, `Wait`; no accepted lines are dropped.
- File watcher signal: capacity one, `DropOldest`; signals are coalesced because
  the reader re-checks current filesystem state.
- Live pre-detection events: bounded FIFO; oldest events are evicted and counted.
- Telemetry summaries: capacity 16, `DropOldest`; telemetry is optional and
  derived, while gameplay tracking must never wait for local telemetry IO.
- Overlay presentation: latest-only dispatcher scheduling; intermediate UI
  frames may coalesce without losing canonical state.

No `async void` is allowed outside WPF event handlers. Cancellation is expected
control flow and is caught only when the corresponding token is cancelled.
