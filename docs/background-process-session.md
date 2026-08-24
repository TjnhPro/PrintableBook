# Background process session

Interior Processing is owned by `IProcessSessionService`, not by the WPF UI or WebView route that started it. `StartAsync` validates and snapshots the requested queue, then starts one owned worker task on the thread pool and returns the `Running` snapshot immediately. Only one session can be active at a time.

```text
WebView process.start
  -> IProcessSessionService.StartAsync
  -> immutable queue snapshot + session cancellation source
  -> Task.Run(ExecuteAsync)
  -> IPrintableBookApplication.ProcessBooksAsync
  -> terminal session snapshot + cancellation cleanup
```

`process.get` is a snapshot read: it does not start work and can be called from any page. The desktop shell polls it once a second while `IsActive` is true, so progress remains current when the Process page is not visible. `process.cancel` only requests cancellation and returns the `Cancelling` snapshot; it never waits for image or PDF work to finish.

## Shutdown behaviour

Closing the desktop window checks the current snapshot. If processing is active, the user can keep the application open or request a graceful stop. The coordinator calls `StopAndWaitAsync` with a five-second timeout. A timeout presents an explicit choice to keep waiting or force exit; no UI close path blocks the dispatcher synchronously.

Windows session ending uses the same five-second bounded stop as best effort and does not display UI or prevent the operating system from ending the session. Abrupt termination can leave a Book workspace marked `Running`; startup recovery changes only such stale states to `Interrupted`. Completed, failed, and cancelled workspaces are untouched. `Interrupted` is a terminal state that preserves resume metadata.

## Verification

Core tests prove execution runs outside the caller synchronization context, cancellation becomes observable before worker unwind, bounded wait times out for non-cooperative work, and a new session is possible only after terminal cleanup. Desktop tests cover the close decision coordinator. Bridge tests cover global polling and stopping controls.
