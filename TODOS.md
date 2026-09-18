# Deferred work

## Interior artwork treatment preview

- Status: deferred from the No Frame → CropArt patch.
- Why: users would benefit from seeing trim/pad/overlay results before starting a batch, but this requires a new UI interaction and visual design review.
- Revisit when: artwork-treatment settings next gain UI scope or support reports show unexpected print composition.
- Acceptance: preview uses the same processing policy/recipe as production and clearly distinguishes source border from Brand frame.

## Published rendering recipe history

- Status: deferred from the No Frame → CropArt patch.
- Why: page-cache provenance is sufficient for the current fix; attaching a complete recipe/version manifest to published PDFs is a broader reproducibility feature.
- Revisit when: published-edition comparison, audit, or exact historical regeneration becomes a product requirement.
- Acceptance: every published artifact can identify source facts, effective classification origin, algorithm/policy versions, geometry, and frame recipe.

## PageId path containment

- Status: deferred security hardening.
- Evidence: `DiskBackedInteriorPagePipeline` currently checks only that `PageId` is non-blank, then combines it into cache/output paths used by write and invalidation operations. Production currently generates safe IDs internally.
- Why separate: the No Frame patch introduces no new caller or external input path; changing the public request validation deserves its own compatibility/security review.
- Revisit when: any caller can supply or mutate `InteriorPagePipelineRequest.PageId`, or before exposing the pipeline outside the current internal processing flow.
- Acceptance: reject rooted paths, separators, `.`/`..`, and invalid filename components; verify resolved cache/final paths stay under their intended roots before writes/deletes; cover reparse-point behavior.
