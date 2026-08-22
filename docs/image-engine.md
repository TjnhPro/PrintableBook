# Image engine selection — Phase 2

Phase 2 uses `Magick.NET-Q8-AnyCPU` 14.16.0 only in `PrintableBook.Infrastructure`.

The project is Apache-2.0 licensed and provides real PNG decoding/encoding, crop, canvas composition, high-quality resizing, frame compositing, and image density handling. Its types remain fully inside Infrastructure; Core exposes only neutral file references, image dimensions, density, and processing contracts.

All processor correctness tests must create/open actual PNG files and inspect actual output. No mock image engine is used to assert trim, resize, canvas, or composition behavior.
