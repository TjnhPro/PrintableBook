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

Frame availability, automatic recommendation, and user mode remain separate at the overlay stage. `FrameMode.Auto` uses `AutoFrameRecommended`; `FrameMode.Enabled` forces a compatible available frame; `FrameMode.Disabled` suppresses it. Before preparation, however, Disabled is also an explicit user override: it skips the classifier and selects CropArt.

```text
ShouldApplyFrame = FrameAvailable &&
  (Auto => AutoFrameRecommended, Enabled => true, Disabled => false)
```

Thus BorderArt and FullArt frame in Auto, CropArt stays unframed in Auto, and any detected type can be framed with Enabled. Disabled always prepares as CropArt and remains unframed; it no longer preserves a detected BorderArt/FullArt preparation path.

An applied frame must already match the prepared artwork size. It is not silently resized. If no frame applies, `framed.png` is an exact pass-through artifact so downstream stages have a stable input.

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

The v4 input stamp includes source identity, classification policy, threshold, detector/preparation versions, all three image sizes, density, frame identity, and `FrameMode`. Cache invalidation follows policy-specific dependencies: `Auto ↔ Enabled` reuses classification/prepared; `Disabled ↔ Auto|Enabled` rebuilds from classification; detector settings do not invalidate forced No Frame; trim threshold changes invalidate its preparation.

`classification.json` v2 persists effective type, origin (`detected`, `forced-no-frame`, `forced-intro`), detection status and optional detector evidence. Forced entries must have null evidence. Recognized v3 Auto/Enabled metadata is upgraded without rewriting compatible preparation, while legacy Interior Disabled is rebuilt. Corrupt, contradictory or unknown schemas fail closed. Classification metadata is atomically replaced and the v4 stamp is committed last, so cancellation retains a retryable workspace.

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

Visual review remains required: BorderArt should have its source border removed before the Brand frame overlay, FullArt should retain an acceptable min-side crop, and CropArt in Auto should preserve all trimmed artwork without a Brand frame.
