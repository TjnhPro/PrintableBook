# BorderPixel detector specification

**Status:** Proposed — requires semantic approval before implementation  
**Scope:** evidence used only after `IBorderLineDetector` returns `HasBorder=false`  
**Out of scope:** artwork type selection, trimming, square preparation, and pipeline orchestration

## Purpose

`IBorderPixelDetector` distinguishes the two non-frame categories:

```text
BorderLine=false + BorderPixel=true  -> fullart
BorderLine=false + BorderPixel=false -> cropart
```

It answers one narrow question:

> Does meaningful dark artwork touch the source raster boundary?

This is deliberately different from BorderLine's coherent outer-frame question. A positive BorderPixel result is not evidence of a frame and must not return an artwork type.

## Proposed contract

The Core contract is intentionally minimal:

```csharp
public interface IBorderPixelDetector
{
    ValueTask<BorderPixelDetectionResult> DetectAsync(
        BorderPixelDetectionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record BorderPixelDetectionRequest(
    FileReference Source,
    ArtworkDetectionThreshold Threshold);

public sealed record BorderPixelDetectionResult(bool HasBorderPixel);
```

No side-specific or score evidence is part of the public contract until product measurements demonstrate a downstream need.

## Proposed measurement rule

The detector samples only a three-pixel band on each raw source edge. A qualifying pixel has:

```text
R <= request.Threshold
G <= request.Threshold
B <= request.Threshold
A >= 128
```

The default threshold remains the application's current `ArtworkDetectionThreshold` value (20); robustness must not come from silently increasing it.

For each side, count qualifying pixels in its band, excluding a 10% corner zone at either end to reduce accidental corner marks. A side is contacted when it contains at least eight qualifying pixels. The proposed decision is:

```text
HasBorderPixel = at least two independently contacted sides
```

This rejects a single compression speck or a small object grazing one edge while recognizing full-page artwork that genuinely reaches the canvas. It uses raw source geometry, not trim bounds: using trim bounds would make every trimmed artwork touch an edge and erase the distinction.

## Performance and errors

- Decode the image once per invocation.
- Read four bounded edge bands through `ToByteArray(..., PixelMapping.RGBA)`.
- Do not use `GetPixel`, a full-raster scan, `Task.Run`, or internal parallelism.
- Check cancellation before decode, between ROI reads, and between side scans — not per pixel.
- Propagate unreadable/corrupt image errors. Do not convert them to `HasBorderPixel=false`.

## Deterministic test matrix

The implementation must include real generated raster tests for:

1. two contacted sides produces `true`;
2. one contacted side produces `false`;
3. exactly eight versus seven qualifying pixels at the side threshold;
4. pixels at RGB threshold 20 and just above it;
5. alpha 127 versus 128;
6. marks only in excluded corner zones;
7. black pixels beyond the three-pixel edge band;
8. portrait, landscape, and small clamped inputs;
9. corrupt input and cancellation propagation; and
10. one decode/bounded RGBA/no-`GetPixel`/no-parallelism source checks.

## Local corpus and calibration

Real user artwork remains local-only. The eventual corpus is:

```text
TestResults/InteriorClassificationCorpus/
  fullart/   # expected BorderPixel=true after BorderLine=false
  cropart/   # expected BorderPixel=false after BorderLine=false
  results/
```

Before locking the proposed numbers (three-pixel band, ten-percent corner exclusion, eight hits, two sides), run the corpus and compare false positives and negatives. The report must record the threshold, contacted-side counts, expectation, actual result, and elapsed time. Never add filename, hash, or product-specific exceptions.

## Approval required

The proposed rule deliberately makes four policy choices that affect classification and therefore final raster output:

| Decision | Proposed value |
| --- | --- |
| Sampling basis | Raw source raster boundary |
| Edge band | 3 px |
| Side contact | >= 8 qualifying pixels, outside corner zones |
| Positive result | >= 2 contacted sides |

These values must be approved before the detector, classifier, preparation strategies, or main pipeline are implemented. Once approved, this document becomes the versioned `borderpixel-v1` semantic source for cache invalidation.
