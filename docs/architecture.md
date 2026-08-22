# Architecture foundation

## Purpose

Printable Book is a modular-monolith Windows application for preparing printable colouring-book assets. Phase 1 establishes the shared language and boundaries needed before real image, PDF, or metadata processors are introduced. It deliberately does not implement trimming, resizing, framing, PDF export, or KDP rules.

| Project | Responsibility |
| --- | --- |
| `PrintableBook.Core` | Platform-independent domain, processing contracts, configuration snapshot, application use case, pipeline, and technical adapter contracts. |
| `PrintableBook.Infrastructure` | Future concrete file system, image, PDF, metadata, workspace, and brand-profile adapters. |
| `PrintableBook.Desktop` | WPF composition root and WebView2 presentation host. It invokes application use cases and owns no processing logic. |
| `PrintableBook.Core.Tests` | Unit and architecture tests for Core contracts. |
| `PrintableBook.Infrastructure.Tests` | Integration-test foundation and future small real PNG/PDF fixtures. |

## Dependency direction

```text
PrintableBook.Infrastructure ─────► PrintableBook.Core
PrintableBook.Desktop ─────────────► PrintableBook.Core
PrintableBook.Desktop ─────────────► PrintableBook.Infrastructure
PrintableBook.Core.Tests ──────────► PrintableBook.Core
PrintableBook.Infrastructure.Tests ► PrintableBook.Infrastructure

PrintableBook.Core ──X──► Infrastructure / Desktop / WPF / WebView2 / Windows APIs
PrintableBook.Infrastructure ──X──► Desktop
```

Core owns the interfaces and technical primitives needed by processing; Infrastructure implements them. Core architecture tests reject direct references to Infrastructure, Desktop, WPF, WebView2, and Windows assemblies. Infrastructure tests reject a Desktop reference. This retains reuse by a future worker or CLI host.

## Domain and configuration

A `Book` holds a `BookId` and a general `BookSource`, whose assets are explicitly classified as Cover, Intro, Interior, or Colored. The model is deliberately small and uses no inheritance tree.

A `BrandProfile` is a resolved profile selected independently of the book. It can optionally carry an effective settings snapshot and opaque resource references. `IBrandProfileResolver` is a Core-owned adapter contract: Core never reads a brand folder, file, JSON, YAML, or environment value directly. Folder layout, extensions, resource names, and automation rules remain changeable.

Processing settings are opaque key/value values from ordered `IProcessingSettingsSource` instances. A later source overrides an earlier source and `ProcessingSettingsResolver` creates an immutable `EffectiveProcessingSettings` snapshot before processing begins. This supports configuration, environment, and runtime overrides without declaring a final schema or embedding KDP pixel values in code.

## Processing execution

The runtime flow is:

```text
Select Brand + Select Book
        ↓
Resolve Brand Profile + Effective Settings
        ↓
Create ProcessingContext and BookWorkspace
        ↓
IPrintableBookApplication → IBookProcessingPipeline → replaceable stages
```

`ProcessingContext` contains only the resolved book, brand, settings, workspace, and per-run options. `ProcessingResult` communicates Success, Warning, Failure, or Cancelled with small structured issues. `ProcessingProgress` is generic and application APIs expose `CancellationToken` boundaries. Stage order and business processing are intentionally not fixed in Phase 1.

Core represents file/directory references, image size/point/bounds, PDF document facts, metadata cleaning, and per-book workspaces with neutral contracts. No third-party image or PDF types can leak through those contracts.

## Desktop and bridge boundary

Desktop is the composition root through `AddPrintableBookCore` and `AddPrintableBookInfrastructure`. The WPF `MainWindow` receives a WebView2 message, passes it to `WebViewBridgeRouter`, and serializes the response. The router owns request/response envelopes, version validation, correlation IDs, and the narrow `app.ping` route. It exposes no arbitrary .NET object to JavaScript.

Frontend code remains presentation-only and contains no processing/business calculations.

## Testing policy

Mocks and fakes are allowed for pipeline orchestration or boundary tests. They do not prove an image/PDF implementation correct. Repository-owned integration tests must use a small, deterministic, redistributable real PNG/PDF input; open and inspect the actual output for its relevant dimensions, page count, content properties, or metadata. The `Infrastructure.Tests/TestData` directory is reserved for those fixtures and is required by CI.

User-supplied image corpora are a separate `TestScope=LocalCorpus`. They live under ignored `TestResults/` paths, require explicit local opt-in, and must be excluded from CI. A clean checkout can only run repository-owned tests.

## Deliberately unconfirmed decisions

The following are not contracts yet: KDP processing values, configuration field names/schema, brand folder convention, configuration file format, output naming, and brand automation. They must be chosen alongside a concrete implementation and real-input tests rather than inferred by this foundation.
