# Printable Book

Printable Book is a Windows desktop application for preparing printable colouring-book assets. It uses a modular monolith architecture with a platform-independent Core, technical Infrastructure adapters, and a WPF/WebView2 presentation host.

## Development status

Phase 2 Core Processing MVP is complete on the `phase-2` branch. The repository now processes one or more Book folders through persistent workspaces, bounded per-page image processing, deterministic shuffle maps, real PNG/PDF output validation, atomic versioned publishing, and retry-safe disk cache reuse. Cover and Interior PDFs have independent physical page sizes; state, processed PNGs, intermediate cache, and other workspace diagnostics are retained after processing. Workspace cleanup is a future explicit user action. The WPF/WebView2 host remains a thin presentation boundary.

See [Phase 2 processing](docs/phase-2-core-processing.md), [image-engine.md](docs/image-engine.md), and [pdf-engine.md](docs/pdf-engine.md) for the implemented boundaries and engine decisions.

## Development prerequisites

- .NET 10 SDK
- Windows, for the WPF desktop host

## Test commands

```powershell
dotnet restore PrintableBook.sln
dotnet build PrintableBook.sln --configuration Release --no-restore
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "TestScope!=LocalCorpus"
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --configuration Release --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
```

## Background processing

Interior Processing runs independently of the visible WebView page. Start returns immediately, cancellation is a non-blocking request, and the desktop keeps polling active work while the user visits other pages. Closing the desktop uses a five-second graceful-stop flow; a stale `Running` workspace after an abrupt end is recovered as `Interrupted` on the next startup. See [background process session](docs/background-process-session.md).

### Local artwork corpus

The regular and CI suite runs repository-owned tests only: deterministic fixtures tracked in `tests/**/TestData/` or generated deterministically by the test for compact pixel-geometry cases. Either form must be redistributable and must not depend on user artwork. User-supplied artwork is a separate `LocalCorpus` scope and is ignored by Git.

The trim corpus stays in `TestResults/InteriorProcessing/trim/custom/` and writes its review `report.json` plus rendered files to its local `output/` folder. The BorderLine detector corpus uses the following layout. Its V2 measurement report is written to `TestResults/BorderLineCorpus/results/borderline-v2-measurement-report.json`; for every positive detection it also writes a magenta frame overlay below `TestResults/BorderLineCorpus/results/debug/` so that crop-target geometry can be reviewed locally:

```text
TestResults/BorderLineCorpus/
  borderart/  # expected HasBorder=true
  fullart/    # expected HasBorder=false
  cropart/    # expected HasBorder=false
  expected-outer-frames.json  # local reviewed geometry for every borderart input
```

`expected-outer-frames.json` maps each `borderart/...` relative path to its reviewed `left`, `right`, `top`, and `bottom` coordinates. A positive image without an entry, or an entry without a matching positive image, fails the local certification; this prevents a presence-only result from being accepted as a crop-target pass.

Run that corpus only after placing real images in the folder:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "TestScope=LocalCorpus"
```

To run only the BorderLine detector corpus (without requiring another local corpus), use:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~BorderLineLocalCorpusTests"
```

Without the environment variable, local-corpus tests are reported as skipped. This is deliberate: an empty clean checkout must never depend on user artwork.

The V2 detector evaluates a persistent four-sided outer frame rather than requiring four perfectly continuous scanlines. Its final decision requires broad, consistent side tracks in the 100-pixel outer corridors and selects the outermost coherent four-side candidate. The local report is the source of evidence for validating those semantics against product artwork; it is ignored by Git together with the input images and debug overlays. The calibrated decision rules are recorded in [BorderLine detector V2 calibration](docs/borderline-detector-v2.md).

BorderPixel V1 certifies non-frame artwork separately. Place locally reviewed inputs under `TestResults/BorderPixelCorpus/fullart/` and `TestResults/BorderPixelCorpus/cropart/`; its opt-in test first verifies `BorderLine=false`, then writes `TestResults/BorderPixelCorpus/results/borderpixel-v1-report.json`. It is also ignored by Git and excluded from CI.

Interior Artwork Preparation V1 certifies the complete classification-to-preparation boundary separately. Place locally reviewed inputs in the category that they are expected to classify as:

```text
TestResults/ArtworkPreparationCorpus/
  borderart/  # expected BorderArt, prepared output recommends a Brand frame in Auto mode
  fullart/    # expected FullArt, prepared output recommends a Brand frame in Auto mode
  cropart/    # expected CropArt, prepared output does not recommend a Brand frame in Auto mode
```

Its opt-in test runs the real BorderLine detector, BorderPixel detector, classifier, trim/crop/pad processors, and preparation service. It writes prepared PNGs to `TestResults/ArtworkPreparationCorpus/results/prepared/` and an auditable result for every input to `TestResults/ArtworkPreparationCorpus/results/artwork-preparation-v1-report.json`. A test pass requires the expected classification, the locked frame policy, a `2270×2270` output, and fully opaque pixels:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~ArtworkPreparationLocalCorpusTests"
```

The complete shared Interior workflow has a separate local corpus. It runs the production disk-backed pipeline from original source through classification, type-specific preparation, optional prepared-stage framing, working-page centering, and final-page centering. Place only reviewed artwork in the matching category; optionally add a `2270×2270` compatible `frame.png`. CropArt remains unframed in Auto mode; users can choose Auto, Frame, or No Frame per Interior source image.

```text
TestResults/InteriorWorkflowCorpus/
  borderart/
  fullart/
  cropart/
  frame.png                  # optional, prepared-stage Brand frame
  results/
    prepared/
    framed/
    working/
    final/
    interior-workflow-report.json
```

The report records expected/actual classification, automatic frame recommendation, frame mode/availability/application, all three geometry gates, opacity, timing, and failures. All inputs, temporary workspaces, rendered outputs, and the report remain local and ignored by Git:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~InteriorWorkflowLocalCorpusTests"
```

See [Interior Artwork Preparation V1](docs/interior-artwork-preparation-v1.md) and [shared Interior pipeline integration](docs/interior-shared-pipeline-integration.md) for the locked processing semantics and certification status.
