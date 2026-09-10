# IntroTemplate processing

> Xem [kiến trúc v0.1](architecture.md) và [shared Interior pipeline](interior-shared-pipeline-integration.md).

## Ownership and selection

Brand-owned source artwork lives directly under `brands/<BrandName>/IntroTemplate/`.  The folder is discovered as file references only; Library Refresh does not inspect image metadata.

A Book does not retain a Brand name or any absolute path for a custom Intro. Its workspace state stores only these values:

- `HasIntro = false`: automatic mode. All eligible `.png`, `.jpg`, and `.jpeg` files in the current Brand are used in filename order.
- `HasIntro = true`: custom mode. The ordered, Book-relative `SelectedIntroInteriorSourceKeys` list is used. Each key identifies a source under that Book's `Book interior` folder, and at least one key is required.

Changing Brand only changes automatic mode. A custom selection is resolved against the current Book's full Interior source set, so it is independent of the active Brand. Missing custom Book sources need review in the UI and reject processing; automatic mode recomputes its Brand list without changing Book state.

## Processing and assembly

The two source modes have intentionally different contracts:

- **Automatic Brand IntroTemplate.** Brand validation accepts a readable `1024x1024`, `2048x2048`, or exact current **Final Interior Page** raster (the default is `2588x2625`). A final-sized Brand image is already print artwork: the pipeline returns that source directly to PDF assembly. It does not normalize, classify, detect borders, prepare, frame, create a Working Area, write a final raster, or create a page cache. This is the path for borderless full-page Intro art.
- **Legacy or Custom IntroTemplate.** A `1024x1024` or `2048x2048` Brand template, and every custom Book-interior Intro, retain the existing forced-CropArt pipeline. Custom mode deliberately does not gain the direct final-art exception, so a Book-owned source cannot silently change its established processing contract.

```text
Automatic Brand IntroTemplate
  exact Final Interior Page size -> source added directly to final PDF
  1024x1024 or 2048x2048        -> normalized source -> forced CropArt -> working page -> final raster

Custom Book-interior IntroTemplate
  1024x1024 or 2048x2048        -> normalized source -> forced CropArt -> working page -> final raster
```

The legacy processing path does not call BorderLine or BorderPixel detection and never applies a frame. Its artifacts are Book-local, with final rasters under `.workspace/processed/intro/`. Direct final Brand artwork intentionally has no processing artifacts to clear.

The processor runs the ordered `intro-pages` batch before the bounded `interior-pages` batch, using the same per-Book concurrency controller. For custom mode it first removes the selected Book Interior keys from the normal source set, then applies the normal Active filter and shuffle. A custom Intro source is therefore never included in `InteriorShuffleMap` or processed twice.

Selecting a Book Interior page as custom Intro does not change its stored Active or Frame mode. Those settings are ignored while it is an Intro page (Intro is always unframed), and resume unchanged when it is removed from the custom selection. Custom Intro pages do not satisfy the requirement for at least one active normal Interior page.

Assembly places the complete Intro block before shuffled Interior artwork. Direct final Brand artwork and legacy processed Intro pages have identical order at this boundary. When a Brand background is enabled, it follows every Intro page and every shuffled Interior page, including the last page of each block.

```text
intro-1, background, intro-2, background,
interior-shuffled-1, background, interior-shuffled-2, background
```

The same ordering applies to both FullBook and InteriorOnly exports.

## Cache lifecycle

Clear Cache deletes heavy legacy Intro artifacts (normalized, prepared, working, and final rasters) together with regular Interior artifacts. It preserves `HasIntro` and the ordered selection keys. Direct final Brand artwork has no artifact cache; the next run reads its certified source again. No processed cache is shared between Books or Brands.

## UI and safety boundary

Book detail exposes Brand-template previews for automatic mode and Book Interior candidates for custom mode, with local previews, explicit ordering, and a single existing Interior settings save action. Selected custom cards are marked as Intro and their Active/Frame controls are disabled without mutating stored settings. Changing a Brand does not alter custom selection or readiness. The UI shows the three valid Brand dimensions, including the configured Final Interior Page size. It communicates selection problems, but backend Brand validation and pipeline checks remain the correctness boundary for existence, readability, and dimensions.
