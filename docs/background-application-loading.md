# Background application loading boundaries

Printable Book keeps the WPF Dispatcher responsive by assigning each responsibility to a narrow owner.

- The UI Dispatcher owns WPF/WebView rendering, bridge receive/post work, navigation, and dialogs.
- `ApplicationLoadCoordinator` owns full snapshot background scheduling, first-load interrupted-processing recovery, and refresh coalescing.
- `BookAssetPreviewCoordinator` owns temporary bounded preview execution, with a maximum of two workers.
- Core `ApplicationSnapshotService` owns snapshot composition only; it does not schedule threads.
- Diagnostics owns operation timing, Dispatcher-stall observation, and a bounded in-memory event list.

An async method name does not guarantee background execution. The only approved background scheduling boundaries are the Desktop load coordinator and preview coordinator (plus the existing processing-session boundary).

## Preview delivery

Preview delivery currently remains Base64 over the bridge. This is temporary: a persistent thumbnail/local-URL architecture is the next step. Cache-clear semantics are deliberately deferred until that persistent preview cache exists.
