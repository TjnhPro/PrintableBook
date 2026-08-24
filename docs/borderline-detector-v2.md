# BorderLine detector V2 calibration

`MagickBorderLineDetector` identifies a persistent, coherent outer frame for the Interior Processing classifier. It does not classify `fullart` or `cropart`, crop pixels, or apply a brand frame.

## Decision rules

The detector decodes the source once and reads only four 101-pixel outer corridors plus four bounded 120 x 120 corner regions. Each corridor is converted once into a reusable per-scanline depth profile and fixed depth histogram, then divided into eight segments. A candidate is kept only when it has:

- at least 6 supported segments;
- at least 55% scanline support;
- at least 70% span across the usable side range;
- depth spread no greater than 12 pixels; and
- no missing run longer than two segments.

Candidate tracks use the request's direct RGBA threshold (the product default is RGB <= 20 and alpha >= 128), with a local depth tolerance of three pixels. All four sides must form valid rectangle geometry. A frame also needs compatible evidence in at least three of four 120 x 120 corner regions: both selected adjacent tracks must have ink near their expected position. Among frames satisfying those conditions, the lowest combined depth wins, which selects the outermost valid frame.

These are V2 acceptance semantics, not per-file exceptions. A newly supplied corpus must be evaluated through the local-corpus workflow before changing them.

## Calibration evidence

The initial calibration used the local product corpus with the default threshold:

| Category | Images | Expected `HasBorder` | Result |
| --- | ---: | --- | --- |
| `borderart` | 22 | `true` | 22 passed |
| `fullart` | 9 | `false` | 9 passed |
| `cropart` | 9 | `false` | 9 passed |

The deterministic suite additionally exercises interrupted and occluded tracks, rounded and moving tracks, stronger inner rectangles, bookshelf/window-like internal lines, edge-touching objects, disconnected corner tracks, threshold/alpha boundaries, and returned outer-frame geometry.

The corpus images, the generated measurement report, debug overlays, and the reviewed-frame manifest remain local-only. They are never committed or uploaded. Run the corpus with the command in the [README](../README.md#local-artwork-corpus), inspect the magenta overlays, and update `TestResults/BorderLineCorpus/expected-outer-frames.json` before accepting a new positive-artwork certification. The test requires one `left`/`right`/`top`/`bottom` entry for every `borderart` image and compares it exactly with the detected `BorderBounds`.
