# BorderLine V3 canonical processing source

> Đây là detector production hiện hành của v0.1. Xem [kiến trúc v0.1](architecture.md), [BorderPixel V1](borderpixel-detector-spec.md) và [shared Interior pipeline](interior-shared-pipeline-integration.md).

Raw Interior artwork is used only to identify, fingerprint, and normalize an input image. Once `.workspace/cache/<page-id>/normalized-source.png` exists, every later image-processing stage uses that one canonical artifact.

The normalizer creates an opaque-white square PNG. It uses nearest-neighbour resize, so a `1024×1024` source is upscaled to the configured `2048×2048` default and a `2048×2048` source still passes through the same cache contract. Classification, BorderLine, BorderPixel, preparation, framing, and page production therefore share one coordinate space.

BorderLine V3 reads the canonical source once. It evaluates pass 1 at the configured shallow depth (`200`), then evaluates the deeper pass (`320`) only when pass 1 has no coherent four-sided frame. Both passes use the same side/corner quality gates and classification evidence is identified as `borderline-v3`. BorderPixel V1 runs only when BorderLine is negative and keeps its exact perimeter-pixel algorithm.

Global settings contain `artworkSourceNormalization.normalizedSourceSize` and the `borderLineDetection` tuning group. Changing normalization size/version invalidates normalization and all downstream artifacts; changing BorderLine configuration invalidates classification and downstream artifacts. Clear Cache removes the page cache, including the canonical source.

The opt-in BorderLine corpus remains the final product-artwork review mechanism. Certification includes representative raw `1024×1024` and `2048×2048` BorderArt pipeline inputs. Do not tune crop behavior against miniature synthetic one-pixel fixtures: inspect corpus outputs before treating a residual border as a product defect.
