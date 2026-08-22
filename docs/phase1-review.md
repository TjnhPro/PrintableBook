# Phase 1 review gate

Phase 1 is a foundation-only release. It is ready to be reviewed without claiming that image, PDF, metadata, or KDP processing exists.

## Included boundaries

- Book/source/asset and brand-profile primitives.
- Ordered immutable processing-settings snapshots.
- Core-owned file, image, PDF, metadata, and workspace adapter contracts.
- Processing context, result, progress, cancellation, application, and replaceable pipeline-stage contracts.
- WPF composition root and isolated, versioned WebView bridge.
- Architecture guards and a real-fixture-ready Infrastructure test project.

## Deliberately excluded

No production processor, KDP pixel rule, final configuration schema, brand folder layout, output naming convention, or third-party image/PDF library is selected by this phase.

## Reproducible verification

```powershell
dotnet restore PrintableBook.sln
dotnet build PrintableBook.sln --configuration Release --no-restore
dotnet test tests/PrintableBook.Core.Tests/PrintableBook.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Infrastructure.Tests/PrintableBook.Infrastructure.Tests.csproj --configuration Release --no-build
dotnet test tests/PrintableBook.Desktop.Tests/PrintableBook.Desktop.Tests.csproj --configuration Release --no-build
node --test tests/PrintableBook.Desktop.Bridge.Tests/app-bridge.test.mjs
```

Remote CI must run the same suite for the committed branch before Phase 1 is treated as closed.
