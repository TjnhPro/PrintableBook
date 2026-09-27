<!-- /autoplan restore point: C:\Users\admin\.gstack\projects\coloringbook\feat-pdf-library-card-ui-autoplan-restore-20260927-092346.md -->

# Book Information Save Validation Plan

## Status

- Phase: implemented.
- Implementation: completed on `feat/book-information-validation` in four reviewable commits.
- Product contract: approved.
- Review state: CEO, Design, Engineering, and DX reviews completed on 2026-09-27.
- Target: additive MVP; no workflow refactor, schema migration, or database change.

Verification completed after implementation:

- Frontend bridge tests: 100 passed.
- Frontend UI contract: 52 checks passed.
- Full Release solution: 991 passed, 5 local-corpus tests skipped, 0 failed.
- Release build: succeeded with 0 warnings and 0 errors.

## Outcome

Prevent malformed production metadata from being saved while keeping every Book Information property optional. A rejected save must explain every problem in the current draft, preserve the previously saved metadata and Brand assignment, and leave the user in the same drawer with focus on the first field to fix.

This phase treats the rules as an approved internal production convention. A successful save does not mean the Book is complete or globally “production ready”; blank fields remain valid.

## Fixed product contract

| Property | Empty after trim | Non-empty validation |
|---|---|---|
| `Title` | Valid | 2–3 terms, fewer than 120 characters, no duplicate terms |
| `Subtitle` | Valid | Fewer than 120 characters, no duplicate terms; no term-count restriction |
| `Subcover` | Valid | 4 to 6 terms and fewer than 100 characters |
| `Description` | Valid | Keep current multiline trim/blank-to-null normalization |
| `Author` | Valid | Keep current single-line trim/blank-to-null normalization |

Validation occurs only when the user invokes **Save Book Information**. The complete draft is validated atomically, including unchanged values in a legacy draft. Any error rejects the whole save.

## Canonical validation semantics

### Effective value and persistence

- Validate after the existing outer trim.
- Null, empty, or whitespace-only is valid and normalizes to `null`.
- Validation never rewrites casing, punctuation, a trailing `s`, or internal whitespace.
- Persistence retains the existing outer trim and blank-to-null normalization.
- `Title`, `Subtitle`, `Subcover`, and `Author` remain single-line. CR or LF produces `single_line`; `Description` remains multiline.

### Terms

- A term is a maximal non-empty run separated by Unicode whitespace.
- Canonical separators: `U+0009–U+000D`, `U+0020`, `U+0085`, `U+00A0`, `U+1680`, `U+2000–U+200A`, `U+2028`, `U+2029`, `U+202F`, `U+205F`, and `U+3000`.
- Repeated separators do not create empty terms.
- Punctuation stays inside a term: `friend` and `friend,` are different.
- No locale word segmentation, dictionary, stemming, or semantic comparison.

### Character count

- “Character” means an extended grapheme/text element after outer trim, matching the existing backend use of `.NET StringInfo`.
- Title and Subtitle: 119 graphemes pass; 120 fail.
- Subcover: 99 graphemes pass; 100 fail.
- The browser uses `Intl.Segmenter` with `granularity: "grapheme"` for immediate feedback.
- If `Intl.Segmenter` is unavailable, the client skips only the length verdict and lets the backend decide. It must not fall back to UTF-16 or code-point counting.
- Remove HTML `maxlength="99"`; HTML counts UTF-16 units and can reject valid grapheme input before validation runs.

### Duplicate terms

Duplicate detection runs independently inside `Title` and inside `Subtitle`; the two fields are never compared.

For an unordered pair, a duplicate exists when:

1. The terms are equal using backend `StringComparer.OrdinalIgnoreCase`; or
2. The longer term is exactly the shorter term plus one final ASCII `s` or `S`, using the same comparison.

| Pair | Result | Reason |
|---|---|---|
| `friend` / `friend` | Invalid | Exact duplicate |
| `Friend` / `friend` | Invalid | Case-insensitive duplicate |
| `friend` / `friends` | Invalid | One trailing ASCII `s` |
| `friends` / `friend` | Invalid | Rule is symmetric |
| `class` / `classes` | Valid | Not exactly one appended `s` |
| `person` / `people` | Valid | No irregular-plural logic |
| `friend` / `friend,` | Valid | Punctuation is significant |
| `friend` / `friendly` | Valid | No stemming |

Return one error for each distinct original-token relationship in source order, not every positional combination. `friend friend friends` therefore returns one `friend`/`friend` error and one `friend`/`friends` error.

The backend is authoritative for Unicode case behavior. Client duplicate validation is conservative: it may catch exact and normal ASCII case variants, but defers uncertain non-ASCII comparison to the backend rather than blocking a potentially valid draft.

## Deterministic error contract

Order errors by:

1. Field: `title`, `subtitle`, `subcover`, `author`.
2. Rule: `single_line`, `term_count`, `character_limit`, `duplicate_terms`.
3. Duplicate relationship in first-occurrence/source order.

Messages:

```text
Title must contain 2 or 3 terms (currently 4).
Title must be under 120 characters (currently 126).
Title contains duplicate terms: "friend" and "friends".
Subtitle contains duplicate terms: "and" and "and".
Subtitle must be under 120 characters (currently 124).
Subcover must contain 4 to 6 terms (currently 3).
Subcover must be under 100 characters (currently 100).
Author must be a single line.
```

Keep the existing bridge error code and add detail through its existing `Payload` slot:

```json
{
  "version": 1,
  "id": "request-id",
  "ok": false,
  "command": null,
  "error": "invalid_book_metadata",
  "payload": {
    "policyVersion": 1,
    "validationErrors": [
      {
        "field": "title",
        "code": "duplicate_terms",
        "message": "Title contains duplicate terms: \"friend\" and \"friends\".",
        "tokens": ["friend", "friends"]
      }
    ]
  }
}
```

- `field`, `code`, and `message` are required; `tokens` is only for `duplicate_terms`.
- `policyVersion` versions transport semantics only; it is not persisted.
- Keep `error: "invalid_book_metadata"` so older frontends retain the generic fallback.
- Malformed request types may retain the generic error without details.
- Unknown/malformed payloads fall back to the existing catalog error.
- Insert messages as text/escaped content, never raw HTML.

## Architecture

New rules must not enter `BookProductionMetadata.Create()` or `Normalize()`. `JsonBookWorkspaceStateStore` calls `Normalize()` during load and every state save; adding rules there would break legacy load or unrelated assignment changes.

```text
Book Information draft
        |
        | Save click
        v
Best-effort JS aggregate validator
   | known invalid                 | no known error
   v                               v
Patch field errors             book.metadata.save
Focus first field                   |
No bridge request                   v
                          existing mutation gate
                                     |
                                     v
                         SaveBookMetadataAsync
                         ValidateForSave (authoritative)
                           | invalid        | valid
                           v                v
                    typed aggregate     Normalize only
                    validation error        |
                           |                v
                           |          load latest state
                           |          replace Metadata only
                           |          existing atomic SaveAsync
                           v
               bridge code + payload
                           |
                           v
               same field-error DOM patch

Legacy load/unrelated state save:
JSON -> existing Normalize only -> snapshot/UI
```

### Core/domain boundary

- Keep `BookProductionMetadata` and current storage normalization.
- Add pure aggregate `ValidateForSave()` plus a small validation-error record in the existing domain file; no generic validation framework/service.
- The router constructs a raw `BookProductionMetadata` record from the complete bridge draft instead of calling `Create()` before the service.
- `SaveBookMetadataAsync` validates before loading or writing state.
- On success, normalize, load latest state under the existing gate, and replace only `Metadata`; preserve `AssignedBrand` and all other state.
- On failure, extend the existing catalog exception with an optional ordered error collection. Perform zero state load, zero write, and zero Library Refresh.
- Reuse the existing atomic state-store write; add no transaction, lock, repository, DI registration, database, or schema.

### Bridge boundary

- Preserve bridge protocol version `1`, command `book.metadata.save`, and all busy/processing/book guards.
- Catch typed metadata validation before generic `ArgumentException`.
- Return the additive payload; do not start refresh after validation failure.

## UI contract

### Initial render

- Keep the current Book Information card, field order, two-column layout, dirty badge, assignment warning, and footer.
- Do not add a panel or long persistent helper text. A prior helper caused layout drift; precise post-Save errors are enough for this MVP.
- Give Title, Subtitle, Subcover, and Author stable control/error-container IDs.
- Link each control to its container with `aria-describedby`; set `aria-invalid` only while invalid.
- Reuse `field-error`, `control-invalid`, and catalog feedback styles; no CSS redesign.

### Save behavior

- Save is enabled whenever dirty unless the existing mutation/processing guard is active.
- A known validation error does not disable Save; Save is the explicit trigger.
- Validate the complete draft and show all errors together.
- For client-known errors, send no bridge request. Footer: `Book Information was not saved. Fix N issues in M fields, starting with <Field>.`
- Focus the first invalid control in UI order and call `scrollIntoView({ block: "nearest" })`; do not select text/reset caret.
- A repeated Save focuses the first remaining invalid field.

### Editing after failed Save

- Replace `subcoverTouchedBooks` with per-Book transient state containing `attempted` and ordered errors by field.
- Before first Save, typing shows no errors. Afterwards, input revalidates only the edited field and updates the aggregate footer.
- Never move focus during input.
- Patch only the affected control class/ARIA, error container, footer, dirty indicator, assignment warning, and Save state.
- Never call `refreshBookDrawerBody()` or full `render()` to show/change/clear validation errors.
- Returning exactly to persisted values clears transient validation and disables Save.

### Backend rejection and success

- Structured backend rejection uses the same state/presentation/focus path as client validation.
- Rejection retains draft, Unsaved state, saved metadata, Brand assignment, and warning; it requests no snapshot.
- Generic rejection uses existing catalog feedback and retains the draft.
- Preserve current `Saving…`/busy flow. Clear draft/validation only after refreshed snapshot confirms success.

## Compatibility and rollback

- Persisted JSON is unchanged; no migration or metadata schema/version field.
- Newly invalid legacy values still load and display normally.
- Unrelated state saves, Brand assignment, processing, and production do not invoke new validation.
- Saving Book Information validates the complete draft. Invalid legacy fields must be corrected or cleared first: deliberate migration-on-edit behavior for MVP.
- Rejection cannot alter saved metadata or `AssignedBrand`.
- Backend-first remains compatible because the top-level error is unchanged; frontend-first handles generic-only backend errors.
- Rollback is data-safe because no persisted format changes.

## Implementation phases and commits

### Phase 1 — Domain and service

Commit: `feat(core): validate book information on save`

Files:

- `src/PrintableBook.Core/Domain/Books/BookProductionMetadata.cs`
- `src/PrintableBook.Core/Application/Desktop/IBookCatalogMetadataService.cs`
- `tests/PrintableBook.Core.Tests/Application/Desktop/BookCatalogMetadataServiceTests.cs`
- `tests/PrintableBook.Infrastructure.Tests/JsonBookWorkspaceStateStoreTests.cs`

Tasks:

1. Add canonical aggregate validator and stable error record/codes.
2. Keep new rules out of `Create()`/`Normalize()`.
3. Validate raw draft before state access; normalize/save only when valid.
4. Extend catalog exception without affecting Brand errors.
5. Prove aggregate ordering, zero-write rejection, assignment preservation, normalization, legacy load, and unrelated-save compatibility.
6. Make incidental successful-save fixtures valid; retain intentional invalid fixtures in compatibility tests.

Exit: focused Core/Infrastructure tests pass; UI/bridge behavior unchanged.

### Phase 2 — Additive bridge payload

Commit: `feat(desktop): return book metadata validation details`

Files:

- `src/PrintableBook.Desktop/Bridge/WebViewBridgeRouter.cs`
- `tests/PrintableBook.Desktop.Tests/BridgeMessageContractTests.cs`

Tasks:

1. Pass raw complete record to the service.
2. Map typed failure to stable error plus ordered payload.
3. Preserve malformed-request/catalog error behavior.
4. Test camelCase serialization, ordering, zero refresh on rejection, and valid refresh.

Exit: bridge tests pass; old-client fallback remains valid.

### Phase 3 — Inline UI without redraw

Commit: `feat(ui): show book information validation errors`

Files:

- `src/PrintableBook.Desktop/Frontend/js/app.js`
- `tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs`
- `tests/PrintableBook.Desktop.Tests/BookWorkspaceLayoutContractTests.cs`

Tasks:

1. Replace Subcover-only state/helper with aggregate per-Book validation state.
2. Add best-effort client validator with canonical rules/order/messages.
3. Remove `maxlength`; keep dirty Save enabled independent of errors.
4. Render stable field errors/ARIA.
5. Implement Save validation, focus, edit-time field revalidation, and backend payload handling.
6. Remove validation-triggered drawer redraws; patch local DOM only.
7. Clear transient state on discard, exact revert, and confirmed success.
8. Test focus, ARIA, aggregation, retained draft, no client bridge request, backend/fallback handling, and zero redraw.

Exit: bridge/UI contract tests pass without focus/caret regression.

### Phase 4 — Docs and regression

Commit: `docs: document book information validation`

File: `docs/user-guide.md`

Tasks:

1. Replace the statement that Subcover count is not enforced.
2. Document optional fields, Save-time rules, limits, duplicate/punctuation examples, and legacy-on-next-save behavior.
3. Run full regression.

Exit: docs match executable contract; all tests/build pass.

## Test matrix

### Domain/service

- All properties null/empty/whitespace save.
- Title 2/3 terms pass; 1/4 fail.
- Subtitle has no term-count constraint.
- Subcover 4, 5, or 6 terms passes; 3/7 fail.
- Title/Subtitle 119 graphemes pass, 120 fail; Subcover 99 passes, 100 fails.
- Combining marks and emoji-ZWJ count as graphemes.
- Declared whitespace set, repeats, CR/LF, NBSP, narrow NBSP, and ideographic space behave canonically.
- CR/LF errors each single-line field; Description stays multiline.
- Exact, ASCII case-only, trailing-`s` pairs fail in both orders.
- Punctuation, irregular plurals, and non-stem variants pass.
- Title/Subtitle are never cross-compared.
- Mixed errors return completely and deterministically with original spelling.
- Invalid save performs zero state access/write and preserves metadata/assignment.
- Valid save preserves assignment and normalization.

### Compatibility/bridge

- Legacy invalid metadata loads/projects and survives unrelated saves.
- Saving the complete invalid draft rejects it.
- Top-level error remains `invalid_book_metadata`.
- Version/errors serialize camelCase in deterministic order.
- Rejection starts no refresh and makes no persistence call.
- Malformed types retain generic handling; valid save retains current refresh flow.

### Frontend

- Dirty invalid draft keeps Save enabled; busy/processing/no-dirty still disable.
- Before Save, typing shows no errors.
- Known-invalid Save sends no bridge message, shows all errors, focuses first invalid.
- Repeated Save focuses first remaining invalid.
- Post-failure editing revalidates only its field without focus/caret loss.
- Exact revert clears errors/disables Save.
- Structured backend errors use identical UI and retain draft.
- Bad/missing payload falls back to catalog error.
- Messages are escaped.
- Missing `Intl.Segmenter` defers length to backend.
- Showing/clearing errors increments neither full-render nor drawer-body-render count.
- Success clears transient state only after snapshot.

## Verification

```powershell
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --filter "FullyQualifiedName~BookCatalogMetadataServiceTests"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --filter "FullyQualifiedName~JsonBookWorkspaceStateStoreTests&TestScope!=LocalCorpus"
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --filter "FullyQualifiedName~BridgeMessageContractTests|FullyQualifiedName~BookWorkspaceLayoutContractTests"
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
npm --prefix src/PrintableBook.Desktop/Frontend run test:ui
dotnet test PrintableBook.sln -c Release
dotnet build PrintableBook.sln -c Release
```

## Out of scope

- Required metadata fields or cross-field duplicate comparison.
- Linguistic stemming/dictionaries/irregular plurals/semantic similarity/punctuation stripping.
- Auto-rewrite, prompt versioning, ChatGPT integration, or metadata import.
- Configurable profiles, feature flags, telemetry, or generic readiness system.
- Metadata migration, persisted validation version, or schema change.
- Author/Brand/processing/production/output workflow changes.
- Generic validation framework, database, repository, transaction layer, dependency, or UI redesign.

## Review decisions

| Concern | Decision | Reason |
|---|---|---|
| Rules may be arbitrary editorial constraints | Keep | Explicitly approved MVP convention; not presented as universal readiness. |
| Save-time blocking is strict | Keep | Explicit contract; atomic failure and clear recovery limit damage. |
| Legacy metadata can block later metadata Save | Keep/document | Deliberate migration-on-edit; load/unrelated operations remain compatible. |
| Author correction can be blocked by another invalid field | Keep | Complete-form atomic Save is approved; offending optional field can be corrected/cleared. |
| Add profiles/versioned metadata/telemetry | Reject | Outside additive MVP; only transport `policyVersion` remains. |
| Add persistent helper text | Reject | Prior helper caused layout drift; post-Save inline errors suffice. |
| Put rules in `Create()`/`Normalize()` | Reject | Breaks legacy load/unrelated state saves. |
| New validator service/JS module/Unicode package | Reject | Pure helpers in existing files are lower-maintenance. |
| Every positional duplicate pair | Refine | Every distinct token relationship avoids duplicate noise. |
| HTML `maxlength` | Reject | UTF-16 count conflicts with grapheme contract. |

## GSTACK REVIEW REPORT

| Phase | Result | Main plan change |
|---|---|---|
| CEO/Product | Completed with concerns | Framed rules as approved internal convention; documented migration-on-edit; rejected broader readiness/profile system. |
| Design/UX | Completed | Save stays available; aggregate errors/focus/ARIA; remove `maxlength`; no validation redraw. |
| Engineering | Completed | Separate Save validation from normalization; reuse bridge payload/atomic store; exact semantics/tests. |
| Developer Experience | Completed | Phase/commit/file boundaries, rollback notes, and focused/full verification. |

Automatic decisions used completeness, user value, implementability, lower risk, smaller maintenance surface, and existing repository patterns. Fixed user-approved behavior took precedence over suggestions to alter the product contract.

Scope check:

- Four existing production files; no new service, database, schema, dependency, or production file.
- Existing tests/docs span layers because the feature crosses domain, persistence compatibility, bridge, and WebView UI.
- Validation cost is negligible for fields bounded by short character limits; no performance infrastructure is needed.
- No implementation code changed during planning.

Tooling notes:

- `DESIGN.md` is absent; current Book card/form/feedback/error patterns are authoritative.
- Local gstack helper binaries and `jq` were unavailable on this Windows installation. Review JSONL/dashboard artifacts were skipped rather than hand-written; this terminal report captures the review.

**Final status: READY FOR USER REVIEW — NO UNRESOLVED DECISIONS.**
