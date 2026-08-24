# Shared Interior pipeline integration

## Status

The production shared Interior workflow and its deterministic certification are complete. Product-artwork workflow certification remains intentionally local until reviewed user images are supplied under `TestResults/InteriorWorkflowCorpus/`. The corpus, its temporary workspaces, and rendered outputs are ignored by Git and excluded from CI.

## Workflow and geometry

`DiskBackedInteriorPagePipeline` is an orchestration boundary. It does not contain detector or type-specific raster logic.

```text
original source
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

Frame availability, automatic recommendation, and user mode remain separate. `FrameMode.Auto` uses `AutoFrameRecommended`; `FrameMode.Enabled` forces a compatible available frame; `FrameMode.Disabled` suppresses it. The exact decision is:

```text
ShouldApplyFrame = FrameAvailable &&
  (Auto => AutoFrameRecommended, Enabled => true, Disabled => false)
```

Thus BorderArt and FullArt frame in Auto, CropArt stays unframed in Auto, CropArt can be framed with Enabled, and BorderArt/FullArt can be unframed with Disabled.

An applied frame must already match the prepared artwork size. It is not silently resized. If no frame applies, `framed.png` is an exact pass-through artifact so downstream stages have a stable input.

## Cache and recovery

Each page has these durable artifacts:

```text
.workspace/cache/<PageId>/classification.json
.workspace/cache/<PageId>/prepared.png
.workspace/cache/<PageId>/framed.png
.workspace/cache/<PageId>/working-page.png
.workspace/processed/interior/<PageId>.png
.workspace/processed/interior/<PageId>.input-stamp.json
```

The input stamp includes source identity, threshold, classification and preparation algorithm versions, all three image sizes, density, frame identity, and `FrameMode`. Cache invalidation starts at the earliest changed dependency: a FrameMode-only change reuses `classification.json` and `prepared.png`, then rebuilds `framed.png`, `working-page.png`, and the final page. Corrupt or incompatible stamps, corrupt metadata, and unreadable/wrong-size stage files are treated as stale and regenerated. `classification.json` persists canonical type strings: `borderart`, `fullart`, or `cropart`. Failure or cancellation retains the workspace for a later retry.

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
