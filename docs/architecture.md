# Architecture baseline

## Purpose

Printable Book is a modular-monolith Windows application for preparing printable colouring-book assets. Phase 0 establishes execution boundaries only; it deliberately contains no scanning, image, PDF, or book-processing behaviour.

| Project | Responsibility |
| --- | --- |
| `PrintableBook.Core` | Platform-independent domain, application contracts, pipelines, configuration, and abstractions. |
| `PrintableBook.Infrastructure` | Technical adapters such as file system, image, PDF, and metadata implementations. |
| `PrintableBook.Desktop` | WPF composition root and WebView2 host. It presents UI and invokes application use cases. |
| `PrintableBook.Core.Tests` | Core-only tests that run without WPF, WebView2, a browser, or production assets. |

## Dependency direction

```text
PrintableBook.Infrastructure ─────► PrintableBook.Core
PrintableBook.Desktop ─────────────► PrintableBook.Core
PrintableBook.Desktop ─────────────► PrintableBook.Infrastructure
PrintableBook.Core.Tests ──────────► PrintableBook.Core

PrintableBook.Core ──X──► Infrastructure / Desktop / WPF / WebView2
```

Core must remain platform-independent. Infrastructure implements Core-owned contracts when those contracts are introduced in later phases; neither WPF nor WebView2 may leak into Core. Desktop owns dependency injection and is therefore the only composition root. This keeps Core and Infrastructure reusable by a future worker or CLI host.

## Application boundary

Presentation code depends on `IPrintableBookApplication`, not on low-level processors. The interface is intentionally a Phase 0 placeholder; later phases add typed use cases and return contracts without changing the host's dependency direction.

UI bridge request and response models remain separate from Core domain objects. The WebView2 health-check command is transport-only and does not represent a Book-processing use case.

## Desktop and frontend boundary

The WPF host loads local `Frontend/index.html` in WebView2. HTML, JavaScript, and the compiled CSS asset are presentation-only. JavaScript contains no page sizing, trimming, PDF, image, or business calculations.

The host accepts only a typed, versioned JSON envelope:

```json
{ "version": 1, "id": "request-id", "command": "app.ping" }
```

For the Phase 0 bridge proof, the only accepted command is `app.ping`; the host returns an envelope with `command: "app.pong"`. Invalid payloads and unsupported commands receive structured error values. The host does not expose arbitrary .NET objects or methods to JavaScript. Future bridge messages must remain versioned, correlated by `id`, and safely deserialized at the host boundary.

## Composition and technical adapters

`PrintableBook.Desktop` registers application services at startup. Infrastructure folders exist for File System, Image Processing, PDF Processing, and Metadata Processing, but their adapters and library choices are intentionally deferred to Phase 1. This prevents technical dependencies from defining the Core model prematurely.

## Quality baseline

Nullable reference types and implicit usings are enabled. Shared build settings treat compiler warnings as errors. The Core test project is isolated from Desktop and verifies that its referenced assembly graph excludes Windows and WebView2 dependencies. GitHub Actions restores, builds, and executes Core tests in Phase 0.
