# Threading and lifetime model

## Owners

- The WPF dispatcher owns `App`, `HistoryWindow`, `OverlayHost`,
  `OverlayWindow`, `LiveOverlayPresenter`, and debug diagnostics.
- `PowerLogTailer` is the single writer to its bounded raw-line channel.
- `LiveTrackingCoordinator` is the single consumer and the only live caller of
  its `TrackingSession`.
- `InMemoryCardDatabase` publishes an atomically replaced immutable database
  state; readers do not lock or observe partial refreshes.
- `TelemetryRuntime` is the single reader of a bounded 16-item summary channel;
  `TelemetryOutbox` serializes file access with its private gate.
- `RecordingRuntime` (Debug only) receives observer callbacks on the live
  consumer thread, guards its state with one private gate, and is the single
  reader of a bounded two-slot completed-capture channel drained by one
  sequential persistence worker.
- `ProfileHistoryRuntime` accepts completed personal records through a bounded
  non-blocking handoff. Its single worker owns file access and publishes an
  immutable history snapshot only after a durable commit.

## Startup

`App.OnStartup` opens the normal history window, creates Debug diagnostics when
applicable, constructs `IceCrowRuntime`, and calls `Start`. The runtime starts
overlay presentation and then the data, telemetry, optional profile sync,
always-on local history, and live log pipelines. Optional card data never blocks
live tracking.

## Shutdown order

1. Stop accepting telemetry/profile/history work and cancel the one root token.
2. Await data, telemetry, profile, history-drain, and live background tasks.
3. Detach/dispose live log resources.
4. Dispose the recording runtime (Debug builds): discard any in-flight match
   capture as intentional, complete the capture queue, drain pending saves for
   a bounded grace period, then cancel — and, if a save ignores cancellation,
   abandon it with its fault observed rather than block WPF shutdown.
5. Dispose telemetry/profile/history storage and data HTTP resources.
6. Dispose the presenter and overlay on the WPF dispatcher.
7. Dispose product/debug presentation and the root cancellation source.

The product window's UI event handler awaits this path. WPF's `Application.OnExit`
is synchronous, so `App.xaml.cs` contains the only permitted blocking task wait
as an idempotent final fallback. Architecture tests reject such waits elsewhere.

## Backpressure and dispatch

- `PowerLogTailer`: bounded channel, `Wait`; no accepted lines are dropped.
- File watcher signal: capacity one, `DropOldest`; signals are coalesced because
  the reader re-checks current filesystem state.
- Live pre-detection events: bounded FIFO; oldest events are evicted and counted.
- Telemetry summaries: capacity 16, `DropOldest`; telemetry is optional and
  derived, while gameplay tracking must never wait for local telemetry IO.
- Completed match captures: capacity 2, `TryWrite` from the observer path; a
  full queue reports an explicit persistence error instead of blocking the
  tracking consumer or silently dropping evidence.
- Local match history: capacity 64, `TryWrite`; accepted records are appended
  and flushed by one worker, transient IO failures retry, and a full handoff or
  4096-item/64 MiB archive is reported instead of replacing accepted history.
- Overlay presentation: latest-only dispatcher scheduling; intermediate UI
  frames may coalesce without losing canonical state.

No `async void` is allowed outside WPF event handlers. Cancellation is expected
control flow and is caught only when the corresponding token is cancelled.
