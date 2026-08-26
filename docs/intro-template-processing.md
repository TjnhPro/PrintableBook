# IntroTemplate processing

## Ownership and selection

Brand-owned source artwork lives directly under `brands/<BrandName>/IntroTemplate/`.  The folder is discovered as file references only; Library Refresh does not inspect image metadata.

A Book does not retain a Brand name or any absolute Brand path.  Its workspace state stores only these values:

- `HasIntro = false`: automatic mode. All eligible `.png`, `.jpg`, and `.jpeg` files in the current Brand are used in filename order.
- `HasIntro = true`: custom mode. The ordered, Brand-relative `SelectedIntroTemplateKeys` list is used. At least one key is required.

Changing Brand resolves those stored keys against the newly selected Brand. Missing custom keys need review in the UI and reject processing; automatic mode recomputes its list without changing Book state.

## Processing and assembly

Every selected source must be a readable square raster of exactly `1024x1024` or `2048x2048`. The processing worker validates this at session startup, and the page pipeline repeats the check for direct callers.

```text
raw IntroTemplate (1024 or 2048)
  -> normalized-source.png (canonical 2048 by default)
  -> forced CropArt preparation
  -> working page
  -> final Interior-size raster
```

IntroTemplate pages do not call BorderLine or BorderPixel detection and never apply a frame. They use Book-local cache directories, with final rasters under `.workspace/processed/intro/`.

The processor runs the ordered `intro-pages` batch before the bounded `interior-pages` batch, using the same per-Book concurrency controller. Intro pages are never included in `InteriorShuffleMap`.

Assembly places the complete Intro block before shuffled Interior artwork. When a Brand background is enabled, it follows every Intro page and every shuffled Interior page, including the last page of each block.

```text
intro-1, background, intro-2, background,
interior-shuffled-1, background, interior-shuffled-2, background
```

The same ordering applies to both FullBook and InteriorOnly exports.

## Cache lifecycle

Clear Cache deletes heavy Intro artifacts (normalized, prepared, working, and final rasters) together with regular Interior artifacts. It preserves `HasIntro` and the ordered selection keys. The next process run rebuilds the Book-local Intro artifacts; no processed cache is shared between Books or Brands.

## UI and safety boundary

Book detail exposes automatic/custom selection, local previews, explicit ordering, and a single existing Interior settings save action. Changing a Brand refreshes Intro readiness without mutating saved selection. The UI communicates selection problems, but backend worker and pipeline checks remain the correctness boundary for existence, readability, and dimensions.
