# BorderPixel detector V1

**Status:** Implemented; awaiting local product-corpus certification
**Scope:** evidence used only after `IBorderLineDetector` returns `HasBorder=false`

BorderPixel answers a narrow question about the original, untrimmed source raster:

> Does any visible near-black ink pixel touch an exact outermost raster edge?

It is not a classifier and it does not trim, prepare, resize, or otherwise transform pixels. The later classifier maps `BorderLine=false + BorderPixel=true` to `fullart`, and maps no contact to `cropart`.

## Locked V1 rule

```text
source             = original untrimmed raster
perimeter thickness = exactly 1 pixel
qualifying ink      = A >= 128 and each RGB channel <= ArtworkDetectionThreshold
positive            = any qualifying pixel on any one of the four sides
negative            = no qualifying perimeter pixel on all four sides
```

One side and one pixel are sufficient. This is intentional: a fullart composition can have a thin stroke that reaches only one canvas boundary. Conversely, ink one or two pixels inside an edge is not contact and remains negative; no multi-pixel edge band is used.

The Core result retains `LeftHit`, `RightHit`, `TopHit`, and `BottomHit` for diagnostics while deriving `HasBorderPixel` as their logical OR.

## Implementation boundary

`MagickBorderPixelDetector` decodes the image once, exports each exact one-pixel edge as a bounded RGBA array, scans all four sides sequentially, and returns the side evidence. It does not use `GetPixel`, a full-image export, internal parallelism, or `Task.Run`. Decode/cancellation failures propagate as errors; `false` means a valid image was inspected with no qualifying contact.

## Certification workflow

The repository-owned suite uses generated fixtures for the exact-edge, threshold, alpha, corner, JPEG, dimensions, cancellation, corrupt input, and access-pattern cases. Real product images are intentionally separate:

```text
TestResults/BorderPixelCorpus/
  fullart/   # expected BorderPixel=true
  cropart/   # expected BorderPixel=false
  results/borderpixel-v1-report.json
```

The opt-in local test first verifies `BorderLine=false`. A BorderLine-positive input is reported as `PRECONDITION_FAIL`, not treated as a BorderPixel pass. Run it after adding local corpus files:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~BorderPixelLocalCorpusTests"
```

`TestResults/` and the report are ignored by Git; they must never be committed, uploaded, or made a CI dependency. Product certification is complete only when every supplied `fullart` and `cropart` file passes with no precondition failure.
