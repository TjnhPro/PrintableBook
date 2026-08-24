# Interior Artwork Preparation V1

## Status

The implementation and deterministic raster certification are complete. Product-artwork certification remains intentionally local: it passes only after reviewed user images are supplied under `TestResults/ArtworkPreparationCorpus/` and the opt-in corpus test succeeds. The corpus and its rendered output are ignored by Git, so they never affect a clean checkout or CI.

## Boundary

`IArtworkPreparationService` consumes an existing `ArtworkClassificationResult` and returns a `PreparedArtwork`. It does not classify the image or rerun either detector. The result contains the prepared PNG file, its `ArtworkType`, and whether the type is eligible for a Brand frame.

Every successful path produces an opaque-white, square PNG at the `PreparedArtworkSize` requested by the caller. The current product setting is `2270×2270`; low-level image processors do not embed that value.

## Locked type-specific behavior

| Type | Preparation | FrameAllowed |
| --- | --- | --- |
| BorderArt | Crop strictly inside the detected inclusive `BorderBounds`, center-crop with the smaller side, then resize. The detected border pixels are excluded; the immediately internal pixels remain. | `true` |
| FullArt | Trim with the existing artwork-detection threshold, center-crop with the smaller side, then resize. Long-axis content can be removed by the approved crop. | `true` |
| CropArt | Trim with the existing artwork-detection threshold, pad onto an opaque-white square using the larger side, then resize. Trimmed content is never discarded by square normalization. | `false` |

Center crop and padding use `floor(delta / 2)` for the left/top offset. Any odd extra pixel belongs to the right or bottom.

All paths normalize to a square before resizing and flatten transparency onto white before returning. Brand frame compositing is a later pipeline stage and is not baked into prepared artwork.

## Certification

Repository-owned deterministic tests use real Magick processors to verify exact strict-inside BorderArt coordinates, even and odd crop/pad centering, trim behavior, content preservation, request-size validation, opaque alpha, and the locked frame policy.

The opt-in product corpus applies the real classifier followed by the real preparation service. It expects this ignored local layout:

```text
TestResults/ArtworkPreparationCorpus/
  borderart/
  fullart/
  cropart/
  results/
    prepared/
    artwork-preparation-v1-report.json
```

Run only after copying reviewed artwork into those category folders:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ArtworkPreparationLocalCorpusTests"
```

The report records each expected and actual classification, frame policy, output dimensions, opacity result, elapsed time, and any failure. The test is deliberately skipped when `PRINTABLEBOOK_RUN_LOCAL_CORPUS` is not enabled.
