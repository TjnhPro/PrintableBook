# Phase 2 — Core Processing MVP

The processing entry point accepts an ordered queue of Book folders. One session owns the queue at a time; Books are processed sequentially while only Interior pages of the active Book may run concurrently (clamped to 1–12 workers).

For each Book, the application creates `.workspace`, scans PNG sources, validates Cover dimensions, processes Interior artwork through disk-backed `trim → square canvas → resize → frame → final PNG` stages, persists a shuffle map, assembles the logical page order, writes real Cover and Interior PDFs, reopens/validates them, then moves the complete output set into one versioned final-output directory.

State, logs, errors, configuration fingerprint, cache stamps, shuffle mapping, and published artifact paths persist below `.workspace`. Retry reuses compatible stage files; changing source facts, processing settings, or frame content invalidates the affected page cache. A re-shuffle changes only the persisted mapping and need not change source assets.

Image processing uses Magick.NET in Infrastructure. Magick.NET's PDF encoder writes output; PDFsharp reopens documents for inspection. Their implementation types do not leak into Core or Desktop.

The integration fixture uses real temporary PNGs and checks completed state, 300 DPI final PNG output, persisted shuffle state, reopened PDF page counts, and physical page dimensions. The Phase 2 scope intentionally defers Intro generation/normalization, metadata cleaning, rich Desktop processing UI, and production naming rules.
