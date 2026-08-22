# Printable Book

Printable Book is a Windows desktop application for preparing printable colouring-book assets. It uses a modular monolith architecture with a platform-independent Core, technical Infrastructure adapters, and a WPF/WebView2 presentation host.

## Development status

Phase 2 Core Processing MVP is complete on the `phase-2` branch. The repository now processes one or more Book folders through persistent workspaces, bounded per-page image processing, deterministic shuffle maps, real PNG/PDF output validation, atomic versioned publishing, and retry-safe disk cache reuse. Cover and Interior PDFs have independent physical page sizes; state, processed PNGs, intermediate cache, and other workspace diagnostics are retained after processing. Workspace cleanup is a future explicit user action. The WPF/WebView2 host remains a thin presentation boundary.

See [Phase 2 processing](docs/phase-2-core-processing.md), [image-engine.md](docs/image-engine.md), and [pdf-engine.md](docs/pdf-engine.md) for the implemented boundaries and engine decisions.

## Development prerequisites

- .NET 10 SDK
- Windows, for the WPF desktop host

## Test commands

```powershell
dotnet restore PrintableBook.sln
dotnet build PrintableBook.sln --configuration Release --no-restore
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "TestScope!=LocalCorpus"
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --configuration Release --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
```

### Local artwork corpus

The regular and CI suite runs repository-owned tests only: deterministic fixtures tracked in `tests/**/TestData/`. User-supplied artwork is a separate `LocalCorpus` scope. It stays in `TestResults/InteriorProcessing/trim/custom/`, is ignored by Git, and writes its review `report.json` and rendered files to its local `output/` folder.

Run that corpus only after placing real images in the folder:

```powershell
$env:PRINTABLEBOOK_RUN_LOCAL_CORPUS = "true"
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build --filter "TestScope=LocalCorpus"
```

Without the environment variable, local-corpus tests are reported as skipped. This is deliberate: an empty clean checkout must never depend on user artwork.
