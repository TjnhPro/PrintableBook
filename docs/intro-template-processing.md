# IntroTemplate processing

## Ownership and selection

Brand-owned source artwork lives directly under `brands/<BrandName>/IntroTemplate/`.  The folder is discovered as file references only; Library Refresh does not inspect image metadata.

A Book does not retain a Brand name or any absolute path for a custom Intro. Its workspace state stores only these values:

- `HasIntro = false`: automatic mode. All eligible `.png`, `.jpg`, and `.jpeg` files in the current Brand are used in filename order.
- `HasIntro = true`: custom mode. The ordered, Book-relative `SelectedIntroInteriorSourceKeys` list is used. Each key identifies a source under that Book's `Book interior` folder, and at least one key is required.

Changing Brand only changes automatic mode. A custom selection is resolved against the current Book's full Interior source set, so it is independent of the active Brand. Missing custom Book sources need review in the UI and reject processing; automatic mode recomputes its Brand list without changing Book state.

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

The processor runs the ordered `intro-pages` batch before the bounded `interior-pages` batch, using the same per-Book concurrency controller. For custom mode it first removes the selected Book Interior keys from the normal source set, then applies the normal Active filter and shuffle. A custom Intro source is therefore never included in `InteriorShuffleMap` or processed twice.

Selecting a Book Interior page as custom Intro does not change its stored Active or Frame mode. Those settings are ignored while it is an Intro page (Intro is always unframed), and resume unchanged when it is removed from the custom selection. Custom Intro pages do not satisfy the requirement for at least one active normal Interior page.

Assembly places the complete Intro block before shuffled Interior artwork. When a Brand background is enabled, it follows every Intro page and every shuffled Interior page, including the last page of each block.

```text
intro-1, background, intro-2, background,
interior-shuffled-1, background, interior-shuffled-2, background
```

The same ordering applies to both FullBook and InteriorOnly exports.

## Cache lifecycle

Clear Cache deletes heavy Intro artifacts (normalized, prepared, working, and final rasters) together with regular Interior artifacts. It preserves `HasIntro` and the ordered selection keys. The next process run rebuilds the Book-local Intro artifacts; no processed cache is shared between Books or Brands.

## UI and safety boundary

Book detail exposes Brand-template previews for automatic mode and Book Interior candidates for custom mode, with local previews, explicit ordering, and a single existing Interior settings save action. Selected custom cards are marked as Intro and their Active/Frame controls are disabled without mutating stored settings. Changing a Brand does not alter custom selection or readiness. The UI communicates selection problems, but backend worker and pipeline checks remain the correctness boundary for existence, readability, and dimensions.
