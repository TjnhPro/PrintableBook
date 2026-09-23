# Shared Interior pipeline integration

> Xem [kiến trúc v0.1](architecture.md), [BorderLine V3](borderline-detector-v3.md) và [Artwork Preparation V1](interior-artwork-preparation-v1.md).

## Status

The production shared Interior workflow and its deterministic certification are complete. Product-artwork workflow certification remains intentionally local until reviewed user images are supplied under `TestResults/InteriorWorkflowCorpus/`. The corpus, its temporary workspaces, and rendered outputs are ignored by Git and excluded from CI.

## Workflow and geometry

`DiskBackedInteriorPagePipeline` is an orchestration boundary. It does not contain detector or type-specific raster logic.

```text
raw source
  -> normalized-source.png (artwork-source-normalization-v1)
  -> classify (artwork-classification-v1)
  -> prepare by ArtworkType (artwork-preparation-v1)
  -> prepared.png: 2270 x 2270, opaque
  -> optional prepared-stage Brand frame
  -> framed.png: 2270 x 2270
  -> working-page.png: 2550 x 2550
  -> final interior PNG: 2588 x 2625
```

The working page centers the 2270-square artwork at `(140, 140)`. The final page centers the working page at `(19, 37)`; the unmatched pixels are placed on the right and bottom according to `floor(delta / 2)`.

## Brand-frame policy

Normal Interior exposes exactly two modes. `FrameMode.Enabled` runs detected preparation and requires a compatible Brand frame. `FrameMode.Disabled` is the default, skips the classifier, selects CropArt and suppresses framing.

```text
ShouldApplyFrame = (Enabled => true, Disabled => false)
```

Enabled can frame any detected artwork type. Disabled always prepares as CropArt and remains unframed; it does not preserve a detected BorderArt/FullArt preparation path. `AutoFrameRecommended` remains detector metadata only and is no longer a user mode.

An applied frame must already match the prepared artwork size. It is not silently resized. A Book run with any Frame page copies the Brand frame into an immutable run input, hashes that staged file once, and every Frame request uses that staged path/digest. Missing, unreadable or wrong-size frame input fails before any cache-success return. For No Frame, `framed.png` is an exact pass-through artifact so downstream stages have a stable input.

## Cache and recovery

Each page has these durable artifacts:

```text
.workspace/cache/<PageId>/classification.json
.workspace/cache/<PageId>/normalized-source.png
.workspace/cache/<PageId>/prepared.png
.workspace/cache/<PageId>/framed.png
.workspace/cache/<PageId>/working-page.png
.workspace/processed/interior/<PageId>.png
.workspace/cache/<PageId>/input-stamp.json
```

The v5 input stamp includes source identity, classification policy, detector/preparation versions, all three image sizes, density, `FrameMode`, and the staged frame content SHA-256. `Disabled ↔ Enabled` rebuilds from classification. BorderLine detector settings do not invalidate forced No Frame; the trim threshold remains a preparation dependency. Same-length/same-timestamp frame replacement still invalidates Frame output through its digest.

`classification.json` v2 persists effective type, origin (`detected`, `forced-no-frame`, `forced-intro`), detection status and optional detector evidence. Forced entries must have null evidence. Legacy cache schemas rebuild into the binary policy contract; corrupt, contradictory or unknown schemas fail closed. Classification metadata is atomically replaced and the v5 stamp is committed last, so cancellation retains a retryable workspace.

## Local product workflow certification

```text
TestResults/InteriorWorkflowCorpus/
  borderart/
  fullart/
  cropart/
  frame.png                 # optional 2270 x 2270 compatible frame
```

The opt-in test verifies each input's expected classification, prepared opacity and size, frame policy, and exact working/final sizes. It writes reviewable files under `results/{prepared,framed,working,final}/` and `results/interior-workflow-report.json`.

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~InteriorWorkflowLocalCorpusTests"
```

Visual review remains required: Frame output should remove the detected source border before Brand overlay where applicable, FullArt should retain an acceptable min-side crop, and No Frame should preserve all trimmed CropArt without a Brand frame.
