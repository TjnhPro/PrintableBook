# Background task manager architecture

Printable Book is a local WPF/WebView application. UI-affine work stays on the Dispatcher; substantial local discovery and Interior Processing run through one owned runtime:

```text
UI → semantic bridge command → facade → BackgroundTaskManager → keyed worker
```

The manager retains RAM-only task snapshots, typed result/view objects, cancellation ownership, lifecycle diagnostics, and bounded lanes:

| Worker kind | Lane | Limit | Duplicate policy |
| --- | --- | --- | --- |
| LibraryRefresh | Library | 1 | Join by kind |
| ProcessingSession | Processing | 1 | Return existing |

`app.refresh` and `process.start` return accepted task/session state; they never wait for their worker to finish. `process.get`, `task.get`, and `task.list` are RAM-only observation calls. Task runtime and task IDs are not persisted. Workspace state is persisted separately, and the first Library Refresh performs interrupted-workspace recovery after restart.

`ProcessSessionService` and `ApplicationLoadCoordinator` are facades only. They do not own a `Task.Run`, semaphore, or per-operation cancellation source. The sole production scheduler for heavy Desktop-triggered work is `BackgroundTaskManager`. `DispatcherStallMonitor` is the independent watchdog exception.

Book cover and Interior image display is intentionally outside this task runtime. A LibraryRefresh supplies each discovered asset with a canonical local `file://` URL; WebView2 loads and caches that URL directly with native lazy image loading. This avoids generating thumbnails, Base64 payloads, task polling, and retained preview results.

New worker kinds require an independent user-visible start/cancel/observe lifecycle. Do not introduce arbitrary bridge delegates or a separate PDF worker merely to move an internal processing step.

## Bridge command audit

| Command family | Execution boundary |
| --- | --- |
| `app.refresh` | LibraryRefresh task; result is fetched explicitly after completion |
| `process.start` | ProcessingSession task; semantic `process.get/cancel` remain facade calls |
| `task.get/list/cancel` | RAM-only TaskManager observation/control |
| `book.validate` | requests a fresh Library snapshot and is retained for the existing preflight flow |
| settings | small local persistence mutation |
| brand settings, cover/frame choice | validate against the newest completed LibraryRefresh snapshot held in RAM, persist the small mutation, then queue a normal LibraryRefresh; never call discovery or source scanning from the bridge |
| output open/reveal/copy | UI-affine shell/clipboard actions, no new worker kind |
| diagnostics | bounded in-memory diagnostics/task data |

The completed LibraryRefresh snapshot is the UI command authority for Book and Brand identity. If no completed snapshot is retained, a mutation command returns a safe `snapshot_unavailable` response rather than synchronously creating a refresh. `ProcessingSessionWorker` is the only consumer that deliberately requests a fresh snapshot, and it does so from its BackgroundTaskManager-owned worker execution.
