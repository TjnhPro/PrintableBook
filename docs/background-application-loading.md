# Background task manager architecture

Printable Book is a local WPF/WebView application. UI-affine work stays on the Dispatcher; substantial local discovery, image preview, and Interior Processing run through one owned runtime:

```text
UI → semantic bridge command → facade → BackgroundTaskManager → keyed worker
```

The manager retains RAM-only task snapshots, typed result/view objects, cancellation ownership, lifecycle diagnostics, and bounded lanes:

| Worker kind | Lane | Limit | Duplicate policy |
| --- | --- | --- | --- |
| LibraryRefresh | Library | 1 | Join by kind |
| ProcessingSession | Processing | 1 | Return existing |
| AssetPreview | Preview | 2 | Join by opaque asset key |

`app.refresh`, `process.start`, and `book.asset.preview.get` return accepted task/session state; they never wait for their worker to finish. `process.get`, `task.get`, and `task.list` are RAM-only observation calls. Task runtime and task IDs are not persisted. Workspace state is persisted separately, and the first Library Refresh performs interrupted-workspace recovery after restart.

`ProcessSessionService`, `ApplicationLoadCoordinator`, and `BookAssetPreviewCoordinator` are facades only. They do not own a `Task.Run`, semaphore, or per-operation cancellation source. The sole production scheduler for heavy Desktop-triggered work is `BackgroundTaskManager`. `DispatcherStallMonitor` is the independent watchdog exception.

New worker kinds require an independent user-visible start/cancel/observe lifecycle. Do not introduce arbitrary bridge delegates or a separate PDF worker merely to move an internal processing step.
