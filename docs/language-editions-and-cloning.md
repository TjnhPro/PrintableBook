# Language editions and clone boundaries

## Scope

Printable Book treats Language as persisted Book and Brand data. A suffix such as `_de` is only a naming convention for a cloned edition; discovery, assignment, and processing never infer Language from a folder name.

Supported codes are `en`, `de`, `fr`, `es`, `it`, `pt`, and `nl`. The UI shows the corresponding display name and does not provide a direct Language editor. Create another language edition through **Clone Brand** or **Clone Book**.

This feature does not add a bulk migration, rename existing folders, change the S3 key format, or add a persisted `asin_language` field.

## Compatibility rules

| Persisted value | Effective result | Write on read |
|---|---|---|
| Missing or `null` Language | English (`en`) | No |
| Supported code with different casing or surrounding whitespace | Canonical catalog code | Only when another operation explicitly saves that record |
| Unsupported persisted code | Metadata/state is invalid or unavailable | No fallback |
| Folder name ending in `_de` but missing persisted Language | English (`en`) | No |

Saving a Brand Author loads the current metadata and changes only Author. It preserves the persisted Language instead of resetting it to English.

## Edition naming

Brand and Book clone use the same naming policy:

- `AnimalBook` cloned to German becomes `AnimalBook_de`.
- `AnimalBook_de` cloned to French becomes `AnimalBook_fr`.
- At most one recognized language suffix at the end is replaced.
- Destination collision checks are case-insensitive.

The suffix is not an authority for Language. For example, a legacy folder named `AnimalBook_de` without `LanguageCode` remains an English edition until it is cloned into another persisted edition.

## Brand clone

Brand clone copies the Brand content into staging and publishes the destination with one atomic directory move. Before publish, `brand.metadata.json` contains the target canonical Language and the source Author. A source without metadata produces a clone with an empty Author and the requested Language.

The root validation certificate is not copied. Invalid source metadata, invalid Language, collision, cancellation, copy failure, or any reparse point fails without publishing the destination; staging is cleaned up.

## Book clone

Book clone supports both the legacy flat Book layout and the `Main book/Clone book` layout. It copies source content independently, excluding root `.workspace/` and `Output/`, then creates a fresh workspace for the destination.

### Preserved

- Book Information: Title, Subtitle, Subcover, Description, Author, and Publishing ASIN.
- Selected Cover when its reference can be remapped inside the copied Book tree.
- Interior active/inactive state, Frame overrides, Background, Intro mode, and custom Intro selection.
- Interior shuffle/order when every entry can be remapped safely.
- The requested canonical Language.

### Reset or excluded

- Assigned Brand: the clone starts `Unassigned`.
- Keyword Builder, including Book Keywords, generated Keyword 1-7, Ads Keyword, Ads ASIN, seed, fingerprint, and digest.
- Processing status, resume state, current/last step, failures, logs, and published output references.
- Cache, previews, processed pages, runtime templates/generator state, and published artifacts.
- Production state and `final_cover.png`, `interior_cover.png`, and `interior_book_owner.png`.

Relative references remain relative. Absolute Selected Cover and shuffle references must be inside the source Book root and are remapped to the destination root. A corrupt state/shuffle file or an escaping reference returns `book_clone_state_invalid`; no partial destination is published. A Book without state starts from a clean English default before applying the target Language.

Clone runs under the shared processing mutation gate. Interior processing, a Production action, or Clear Cache prevents the clone from starting. Concurrent duplicate requests are serialized, and collision is checked both before copying and before atomic publish.

## Publishing ASIN and S3

The logical edition identity is the pair of normalized Publishing ASIN and effective Book Language. Cloning keeps the Publishing ASIN while changing Language, so English and German editions can represent distinct catalog editions even when the ASIN string is temporarily the same. Ads ASIN is Keyword Builder data and is not cloned.

S3 behavior is intentionally unchanged in this phase: the destination prefix still uses only the current Book ASIN, and the existing global duplicate-ASIN safety check still prevents two discovered Books from publishing to the same prefix. Assign unique final ASINs before S3 Check or Upload. No S3 schema, receipt, key, or remote migration is performed by clone.

## Assignment and processing boundary

A Brand assignment is valid only when both persisted facts match:

1. The Brand exists and its metadata is readable.
2. Book Language equals Brand Language.
3. Both Book and Brand have an Author.
4. Authors match under the existing normalization policy.

The desktop selector first filters Brands by Language, then by Author. An existing assignment with a different Language remains visible as `Language mismatch` so it can be reviewed or unassigned, but it is not offered as a valid selection.

The backend repeats these checks when assigning. A mismatch returns `book_brand_language_mismatch`. Snapshot refresh reevaluates older assignments, and processing/template-copy/Production policies accept only `Valid`; UI filtering is not the business boundary.

## Desktop recovery behavior

**Clone Book** opens an inline fieldset. Choose a Language and review the read-only destination name. The action stays disabled for an empty Language, destination collision, active writer, or pending request.

After clone and refresh, the desktop opens the new Book on Settings, focuses its title, and shows its persisted Language, `Unassigned` status, empty Keyword state, and empty Production state. If the clone succeeded but refresh failed, the destination name and an inline error remain visible; use **Refresh** to load the already-created edition. Do not submit the clone again unless the destination is confirmed absent.

## Acceptance smoke

Automated checks:

```powershell
dotnet test PrintableBook.sln -c Release --no-restore -m:1
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
node src/PrintableBook.Desktop/Frontend/test-ui.mjs
npm --prefix src/PrintableBook.Desktop/Frontend run verify:css
```

Manual artifact smoke:

1. Open an English Brand with an Author and clone it to German. Refresh and confirm German plus the preserved Author.
2. Open an English Book with Publishing ASIN and reusable Interior settings; clone it to German.
3. Confirm the German Book opens automatically, is `Unassigned`, keeps Publishing ASIN and reusable settings, and has no Keyword/Production output from the source.
4. Confirm only German Brands with the same Author appear in Brand Assignment.
5. Assign a German Brand and run Interior preflight. Confirm an English Brand is rejected before Author comparison.
6. Verify the source Book, source Brand, existing PDFs, and S3 configuration were not modified.
